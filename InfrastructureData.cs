using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    public static class InfrastructureData
    {
        private static readonly Dictionary<(Type, string), MemberInfo> members = new Dictionary<(Type, string), MemberInfo>();
        private static readonly object gate = new object();
        private static Task<string>? cached;
        private static DateTime completedAt;
        private static TaskCompletionSource<Snapshot>? pending;

        // Only plain managed data crosses to the HTTP worker, never Unity objects.
        private sealed class Snapshot
        {
            public long sampledAt;
            public bool worldLoaded;
            public string signalsStatus = "world-unavailable";
            public List<object> junctions = new List<object>();
            public List<object> signals = new List<object>();
            public AiTrafficData.State aiTraffic = new AiTrafficData.State();
            public MultiplayerData.State multiplayer = new MultiplayerData.State();
            public double captureMainThreadMs, maxSliceMs, captureWallMs;
        }

        public static Task<string> GetJson()
        {
            lock (gate)
            {
                if (cached == null || cached.IsFaulted || cached.IsCanceled ||
                    (cached.IsCompleted && (DateTime.UtcNow - completedAt).TotalMilliseconds >= 500))
                    cached = BuildJson();
                return cached;
            }
        }

        private static async Task<string> BuildJson()
        {
            var completion = new TaskCompletionSource<Snapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Updater.RunOnMainThread(() => {
                pending = completion;
                Updater.RunCoroutine(Capture(completion));
            }).ConfigureAwait(false);
            var snapshot = await completion.Task.ConfigureAwait(false);
            var json = await Task.Run(() => JsonConvert.SerializeObject(snapshot)).ConfigureAwait(false);
            lock (gate) completedAt = DateTime.UtcNow;
            return json;
        }

        public static void Reset()
        {
            pending?.TrySetCanceled();
            pending = null;
            lock (gate) { cached = null; completedAt = default; }
            members.Clear();
            AiTrafficData.Reset();
        }

        private static object? Read(object target, string name)
        {
            var type = target as Type ?? target.GetType();
            var key = (type, name);
            if (!members.TryGetValue(key, out var member))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                member = (MemberInfo?)type.GetProperty(name, flags) ?? type.GetField(name, flags)
                    ?? throw new MissingMemberException(type.FullName, name);
                members[key] = member;
            }
            var instance = target is Type ? null : target;
            return member is PropertyInfo property ? property.GetValue(instance) : ((FieldInfo)member).GetValue(instance);
        }

        private static float[] Position(Vector3 position)
        {
            var p = new World.Position(position - WorldMover.currentMove).ToLatLon();
            return new[] { p.latitude, p.longitude };
        }

        // One row per MoveNext lets the coroutine yield between Unity reads.
        private static IEnumerable<object?> ReadRows(Snapshot result)
        {
            result.worldLoaded = WorldStreamingInit.Instance && WorldStreamingInit.IsLoaded;
            if (!result.worldLoaded) yield break;
            var ordered = RailTrackRegistry.Instance.OrderedJunctions;
            for (var id = 0; id < ordered.Length; id++)
            {
                var junction = ordered[id];
                var branches = new List<string>();
                foreach (var branch in junction.outBranches) branches.Add(TrackIdentity.Id(branch.track));
                result.junctions.Add(new { id, position = Position(junction.position), branches, selectedBranch = junction.selectedBranch });
                yield return null;
            }
            result.signalsStatus = "unavailable";
            var mod = UnityModManager.FindMod("DVSignals");
            if (mod?.Active != true || mod.Assembly == null) yield break;
            result.signalsStatus = "initializing";
            var type = mod.Assembly.GetType("Signals.Game.SignalManager", true)!;
            if (!(Read(type, "Running") is bool running) || !running) yield break;
            var manager = Read(type, "Instance");
            if (manager == null) yield break;
            // The live registry can change while this coroutine is yielded.
            var controllers = new List<object>();
            foreach (var controller in (IEnumerable)Read(manager, "AllControllers")!) controllers.Add(controller);
            var seen = new HashSet<int>();
            foreach (var controller in controllers)
            {
                if (!Convert.ToBoolean(Read(controller, "Exists"))) continue;
                foreach (var root in (IEnumerable)Read(controller, "AllSignals")!)
                for (object? signal = root; signal != null; signal = Read(signal, "DistantSignal"))
                {
                    var id = Convert.ToInt32(Read(signal, "Id"));
                    if (!seen.Add(id)) break;
                    var definition = (Component)Read(signal, "Definition")!;
                    if (!definition) break;
                    var aspect = Read(signal, "CurrentAspect");
                    result.signals.Add(new {
                        id, name = Convert.ToString(Read(signal, "Name")),
                        position = Position(definition.transform.position), heading = definition.transform.eulerAngles.y,
                        aspect = aspect == null ? null : Convert.ToString(Read(aspect, "Id")),
                        disallowPassing = aspect == null ? (bool?)null : Convert.ToBoolean(Read(aspect, "DisallowPassing")),
                        isOff = Convert.ToBoolean(Read(signal, "IsOff")),
                        operation = Convert.ToString(Read(signal, "Operation")),
                        shunting = Convert.ToBoolean(Read(signal, "IsShunting"))
                    });
                    yield return null;
                }
            }
            result.signalsStatus = "ready";
        }

        private static IEnumerator Capture(TaskCompletionSource<Snapshot> completion)
        {
            var result = new Snapshot { sampledAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            var wall = Stopwatch.StartNew();
            var slice = Stopwatch.StartNew();
            using var rows = ReadAllRows(result).GetEnumerator();
            while (true)
            {
                bool more;
                try { more = rows.MoveNext(); }
                catch (Exception e)
                {
                    result.signals.Clear();
                    result.signalsStatus = "incompatible";
                    Main.DebugLog(() => "Infrastructure capture failed: " + e.Message);
                    more = false;
                }
                if (!more) break;
                if (slice.Elapsed.TotalMilliseconds >= 1)
                {
                    RecordSlice(result, slice);
                    yield return null;
                    if (completion.Task.IsCompleted) yield break;
                    slice.Restart();
                }
            }
            RecordSlice(result, slice);
            result.captureWallMs = wall.Elapsed.TotalMilliseconds;
            completion.TrySetResult(result);
        }

        private static void RecordSlice(Snapshot result, Stopwatch slice)
        {
            var ms = slice.Elapsed.TotalMilliseconds;
            result.captureMainThreadMs += ms;
            result.maxSliceMs = Math.Max(result.maxSliceMs, ms);
        }

        private static IEnumerable<object?> SafeRows(IEnumerable<object?> source, Action<Exception> failed)
        {
            using var rows = source.GetEnumerator();
            while (true)
            {
                bool more;
                try { more = rows.MoveNext(); }
                catch (Exception e) { failed(e); more = false; }
                if (!more) yield break;
                yield return null;
            }
        }

        private static IEnumerable<object?> ReadAllRows(Snapshot result)
        {
            foreach (var row in SafeRows(ReadRows(result), e => {
                result.signals.Clear(); result.signalsStatus = "incompatible";
                Main.DebugLog(() => "Infrastructure capture failed: " + e.Message);
            })) yield return row;
            if (!result.worldLoaded) yield break;
            foreach (var row in SafeRows(AiTrafficData.ReadRows(result.aiTraffic), e => {
                result.aiTraffic = new AiTrafficData.State { status = "incompatible" };
                Main.DebugLog(() => "AITraffic capture failed: " + e.Message);
            })) yield return row;
            foreach (var row in SafeRows(MultiplayerData.ReadRows(result.multiplayer), e => {
                result.multiplayer = new MultiplayerData.State { status = "incompatible" };
                Main.DebugLog(() => "Multiplayer capture failed: " + e.Message);
            })) yield return row;
        }
    }
}

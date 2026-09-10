using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityModManagerNet;

namespace DvMod.RemoteDispatch
{
    // Commands deliberately isolated from the read-only, periodically sampled adapter.
    // This contract is verified against AITraffic 0.2.1; unknown versions fail closed.
    public static class AiTrafficCommands
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private sealed class Policy { public object path = null!; }
        private static ConditionalWeakTable<object, Policy> dispatched = new ConditionalWeakTable<object, Policy>();
        public static void Reset() => dispatched = new ConditionalWeakTable<object, Policy>();
        private static object? Read(object target, string name) => AiTrafficData.Read(target, name);
        private static MethodInfo Method(object target, string name, params Type[] args) =>
            (target as Type ?? target.GetType()).GetMethod(name, Flags, null, args, null)
            ?? throw new MissingMethodException(name);
        private static object? Call(object target, string name, params object[] args) =>
            Method(target, name, args.Select(a => a.GetType()).ToArray()).Invoke(target is Type ? null : target, args);
        private static Assembly Assembly()
        {
            var mod = UnityModManager.FindMod("AITraffic");
            if (mod?.Active != true || mod.Assembly == null) throw new InvalidOperationException("AITraffic is unavailable.");
            if (mod.Info.Version != "0.2.1") throw new InvalidOperationException("AI commands: unsupported AITraffic version (0.2.1 required).");
            return mod.Assembly;
        }
        public static object? FindEngineer(TrainCar car)
        {
            var mod = UnityModManager.FindMod("AITraffic");
            if (mod?.Active != true || mod.Assembly == null) return null;
            var manager = Read(mod.Assembly.GetType("AITraffic.Core.TrafficManager", true)!, "s_instance");
            if (manager == null) return null;
            var matches = AiTrafficData.CopyList(Read(manager, "ActiveEngineers")).Where(e => {
                var lead = Read(e, "TrainCar") as TrainCar;
                return lead != null && (lead == car || (car.trainset != null && lead.trainset == car.trainset));
            }).ToList();
            if (matches.Count > 1) throw new InvalidOperationException("Multiple AI drivers are present in this train. Separate their assignments before issuing commands.");
            return matches.SingleOrDefault();
        }
        public static TrainCar DrivingCar(TrainCar car)
        {
            var engineer = FindEngineer(car);
            return engineer == null ? car : (TrainCar)Read(engineer, "TrainCar")!;
        }
        private static StationController? Station(RailTrack track) => StationController.allStations?
            .FirstOrDefault(s => s != null && s.AllStationTracks != null && s.AllStationTracks.Contains(track));
        private static object? WorkerTask(object engineer)
        {
            if (!Convert.ToBoolean(Read(engineer, "IsWorkerDriven"))) return null;
            var manager = Read(Assembly().GetType("AITraffic.Workers.WorkerManager", true)!, "s_instance");
            var tasks = manager == null ? new List<object>() : AiTrafficData.CopyList(Read(manager, "ActiveTasks"));
            var matching = tasks.Where(t => ReferenceEquals(Read(t, "Engineer"), engineer)).ToList();
            if (matching.Count != 1 || Convert.ToString(Read(matching[0], "Status")) != "EnRoute")
                throw new InvalidOperationException("The hired driver's assignment is no longer active.");
            return matching[0];
        }
        public static string? BlockReason(TrainCar car, List<RailTrack> tracks)
        {
            try
            {
                Assembly();
                var engineer = FindEngineer(car) ?? throw new InvalidOperationException("No active AI driver is assigned to this train.");
                if (tracks.Count < 2) throw new InvalidOperationException("Choose a different arrival track for the AI driver.");
                var task = WorkerTask(engineer);
                if (task != null && Station(tracks.Last()) == null)
                    throw new InvalidOperationException("A hired driver must arrive on a station track.");
                float length = car.trainset?.cars.Sum(c => c.InterCouplerDistance) ?? car.InterCouplerDistance;
                if (tracks.Last().curve.length < length + 25)
                    throw new InvalidOperationException($"The arrival track is too short for the AI train ({Math.Ceiling(length + 25)} m required, including margin).");
                var graph = Read(Assembly().GetType("AITraffic.Navigation.RailGraph", true)!, "s_instance");
                if (graph == null || !Convert.ToBoolean(Read(graph, "IsInitialized")))
                    throw new InvalidOperationException("The AITraffic network is not initialized.");
                return null;
            }
            catch (Exception e) { return "AI assignment: " + e.Message; }
        }
        public static object Control(TrainCar car, string action)
        {
            Assembly();
            var engineer = FindEngineer(car) ?? throw new InvalidOperationException("No active AI driver is assigned to this train.");
            if (action != "stop" && action != "resume") throw new ArgumentException("Unknown AI command.");
            if (action == "resume" && Read(engineer, "CurrentPath") == null)
                throw new InvalidOperationException("There is no active route to resume.");
            Call(engineer, action == "stop" ? "EmergencyStop" : "Resume");
            return new { message = action == "stop" ? "AI braking requested. Wait until the train stops, then preview again." : "The AI driver was asked to resume its current route." };
        }
        // Pre-resolved writes allow contract validation before any game mutation.
        private sealed class Change
        {
            private readonly object target;
            private readonly MemberInfo member;
            private readonly object? before, after;
            public Change(object target, string name, object? after)
            {
                this.target = target; this.after = after;
                member = (MemberInfo?)target.GetType().GetProperty(name, Flags) ?? target.GetType().GetField(name, Flags)
                    ?? throw new MissingMemberException(name);
                if (member is PropertyInfo p && p.GetSetMethod(true) == null) throw new MissingMethodException(name + " setter");
                before = Read(target, name);
            }
            private void Set(object? value)
            {
                if (member is PropertyInfo p) p.SetValue(target, value); else ((FieldInfo)member).SetValue(target, value);
            }
            public void Apply() => Set(after);
            public void Undo() => Set(before);
        }
        public static bool KeepDispatchedPath(object __instance) =>
            !dispatched.TryGetValue(__instance, out var policy) || !ReferenceEquals(Read(__instance, "CurrentPath"), policy.path);
        private static void EnablePathPolicy(Type engineerType)
        {
            var method = Method(engineerType, "TryAdoptPlayerAlignedPassingRoute", typeof(RailTrack));
            const string owner = "RemoteDispatchLive";
            if (Harmony.GetPatchInfo(method)?.Owners.Contains(owner) != true)
                new Harmony(owner).Patch(method, prefix: new HarmonyMethod(typeof(AiTrafficCommands), nameof(KeepDispatchedPath)));
        }
        public static object Assign(TrainCar car, List<RailTrack> tracks, RouteGraph.Path preview, object expectedEngineer)
        {
            var reason = BlockReason(car, tracks);
            if (reason != null) throw new InvalidOperationException(reason);
            var assembly = Assembly();
            var engineer = FindEngineer(car)!;
            if (!ReferenceEquals(engineer, expectedEngineer)) throw new InvalidOperationException("The driver changed. Recalculate the route.");
            var graph = Read(assembly.GetType("AITraffic.Navigation.RailGraph", true)!, "s_instance")!;
            var builder = Activator.CreateInstance(assembly.GetType("AITraffic.Navigation.Pathfinder", true)!, graph)!;
            var path = Call(builder, "BuildPathFromTracks", tracks) ?? throw new InvalidOperationException("AITraffic refused this route.");
            if (!Convert.ToBoolean(Read(path, "IsValid")) || !AiTrafficData.CopyList(Read(path, "Tracks")).SequenceEqual(tracks.Cast<object>()))
                throw new InvalidOperationException("AITraffic does not recognize every track in this route.");
            var switches = (IDictionary)Read(path, "JunctionSwitches")!;
            var ordered = RailTrackRegistry.Instance.OrderedJunctions;
            if (switches.Count != preview.steps.Where(s => s.junction >= 0).Select(s => s.junction).Distinct().Count())
                throw new InvalidOperationException("AITraffic added switches that were absent from the preview.");
            foreach (var step in preview.steps.Where(s => s.junction >= 0))
                if (!switches.Contains(ordered[step.junction]) || Convert.ToByte(switches[ordered[step.junction]]) != step.branch)
                    throw new InvalidOperationException("The AITraffic route diverges from the preview at one or more switches.");
            var task = WorkerTask(engineer);
            var station = Station(tracks.Last());
            string stationName = station?.stationInfo?.Name ?? tracks.Last().LogicTrack().ID.ToString();
            var changes = new List<Change> {
                new Change(engineer, "CurrentPath", path), new Change(engineer, "CurrentPathTrackIndex", 0),
                new Change(engineer, "TargetDirection", preview.steps[0].forward ? 1f : -1f),
                new Change(engineer, "DistanceToDestination", Convert.ToSingle(Read(path, "TotalDistance"))),
                new Change(engineer, "DestinationStationName", stationName), new Change(engineer, "DestinationTrackName", tracks.Last().name),
                new Change(engineer, "IsStationDestination", false), new Change(engineer, "IsTerminusDestination", true),
                new Change(engineer, "DwellTimeRemaining", 0f), new Change(engineer, "StationaryTimer", 0f),
                new Change(engineer, "_heavySensorUpdateCooldown", 0f), new Change(engineer, "_pathUpdateCooldown", 0f),
                new Change(engineer, "_speedProfileUpdateCooldown", 0f)
            };
            if (task != null)
                changes.AddRange(new[] { new Change(task, "DestinationStation", station), new Change(task, "DestinationTrack", tracks.Last()),
                    new Change(task, "IsAutoSelectedTrack", false), new Change(task, "RouteDistance", Convert.ToSingle(Read(path, "TotalDistance"))),
                    new Change(task, "StatusMessage", "En route to " + stationName + " (Remote Dispatch)") });
            var upcoming = (IList)Read(engineer, "UpcomingTracks")!;
            var oldUpcoming = AiTrafficData.CopyList(upcoming);
            var blocks = (IList)Read(engineer, "_upcomingSignalBlocks")!;
            var signals = (IList)Read(engineer, "_upcomingSignals")!;
            var reserved = (HashSet<RailTrack>)Read(engineer, "_reservedTracks")!;
            var stop = Method(engineer, "EmergencyStop");
            var resume = Method(engineer, "Resume");
            var clearSignals = Method(engineer, "ReleaseAllSignalReservations");
            var release = Method(graph, "ReleaseTrackReservation", typeof(RailTrack), typeof(object));
            // Capture only this driver's actual reservations; preserve tracks under its consist.
            var ownCars = car.trainset?.cars ?? new List<TrainCar> { car };
            var occupied = new HashSet<RailTrack>(ownCars.SelectMany(c => new[] { c.FrontBogie?.track, c.RearBogie?.track }).Where(t => t != null)!);
            var owned = AiTrafficData.CopyRegistry(graph, "_trackReservations").Where(e => ReferenceEquals(e.Value, engineer) || ReferenceEquals(e.Value, car)).ToList();
            var controller = Read(assembly.GetType("AITraffic.Navigation.JunctionController", true)!, "_instance");
            var unlocked = new List<(Junction junction, object requester)>();
            MethodInfo? cancelPassing = null;
            if (controller != null)
            {
                cancelPassing = Method(controller, "CancelTrainPassing", typeof(object), typeof(Junction));
                var allCars = TrainCarRegistry.Instance.logicCarToTrainCar.Values.Where(c => c != null).ToArray();
                foreach (var entry in AiTrafficData.CopyRegistry(controller, "_activeLocks"))
                {
                    var requester = Read(entry.Value!, "Requester");
                    if (!ReferenceEquals(requester, engineer) && !ReferenceEquals(requester, car)) continue;
                    var junction = (Junction)entry.Key;
                    if (preview.steps.Any(s => s.junction >= 0 && ordered[s.junction] == junction)) continue;
                    bool protectedByTrain = allCars.Any(c => {
                        var front = c.FrontBogie?.track; var rear = c.RearBogie?.track;
                        float radius = c.InterCouplerDistance * 0.5f + 10;
                        return front == junction.inBranch.track || rear == junction.inBranch.track || junction.outBranches.Any(b => b.track == front || b.track == rear) ||
                            (c.transform.TransformPoint(c.Bounds.center) - junction.position).sqrMagnitude < radius * radius;
                    });
                    if (!protectedByTrain) unlocked.Add((junction, requester!));
                }
            }
            EnablePathPolicy(engineer.GetType());
            dispatched.TryGetValue(engineer, out var previousPolicy);
            stop.Invoke(engineer, null);
            int applied = 0;
            try
            {
                foreach (var change in changes) { change.Apply(); applied++; }
                upcoming.Clear(); foreach (var track in tracks) upcoming.Add(track);
                blocks.Clear(); signals.Clear();
                clearSignals.Invoke(engineer, null);
                foreach (var entry in owned)
                    if (!occupied.Contains((RailTrack)entry.Key)) release.Invoke(graph, new[] { entry.Key, entry.Value });
                reserved.Clear();
                foreach (var entry in owned)
                    if (occupied.Contains((RailTrack)entry.Key) && ReferenceEquals(entry.Value, engineer)) reserved.Add((RailTrack)entry.Key);
                foreach (var item in unlocked) cancelPassing!.Invoke(controller, new[] { item.requester, item.junction });
                dispatched.Remove(engineer); dispatched.Add(engineer, new Policy { path = path });
                resume.Invoke(engineer, null);
            }
            catch (Exception e)
            {
                stop.Invoke(engineer, null);
                for (int i = applied - 1; i >= 0; i--) { try { changes[i].Undo(); } catch { } }
                upcoming.Clear(); foreach (var track in oldUpcoming) upcoming.Add(track);
                dispatched.Remove(engineer);
                if (previousPolicy != null) dispatched.Add(engineer, previousPolicy);
                // Never reacquire a released reservation blindly; the stopped driver's sensors rebuild them.
                throw new InvalidOperationException("Assignment interrupted; the driver remains stopped. Recalculate before resuming. " + e.GetBaseException().Message);
            }
            return new { changed = 0, message = "Route assigned and AI departure requested toward " + stationName +
                ". AITraffic controls switches and signals. " + (task == null ? "" : "The hired driver's assignment was updated without an additional payment.") };
        }
    }
}

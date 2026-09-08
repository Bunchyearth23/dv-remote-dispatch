using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using DvMod.RemoteDispatch;

class Program
{
    public static int MainThread;
    public static void OnMain() { if (Environment.CurrentManagedThreadId != MainThread) throw new Exception("Unity accessed off thread"); }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void PumpUntil(Task task)
    {
        var stop = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < stop) { Updater.Pump(); Thread.Sleep(1); }
        Check(task.IsCompleted, "Capture hung");
    }
    static void Main()
    {
        MainThread = Environment.CurrentManagedThreadId;
        var first = InfrastructureData.GetJson();
        var second = InfrastructureData.GetJson();
        Check(ReferenceEquals(first, second), "Concurrent clients must share capture");
        PumpUntil(first);
        var snapshot = JObject.Parse(first.GetAwaiter().GetResult());
        Check((string?)snapshot["signalsStatus"] == "ready", "Reflection adapter failed");
        Check(snapshot["signals"]!.Count() == 100, "Signals missing");
        Check(Updater.Yields > 1, "Capture did not yield between rows");
        Check(ReferenceEquals(first, InfrastructureData.GetJson()), "Completed cache not reused");
        Thread.Sleep(510);
        var refreshed = InfrastructureData.GetJson();
        Check(!ReferenceEquals(first, refreshed), "Expired cache not refreshed");
        PumpUntil(refreshed);
        Check(refreshed.IsCompletedSuccessfully, "Refresh failed");
        InfrastructureData.Reset();
        var pending = InfrastructureData.GetJson();
        Updater.Pump();
        InfrastructureData.Reset();
        PumpUntil(pending);
        Check(pending.IsCanceled, "Unload did not cancel active capture");
        Updater.Pump();
        UnityModManagerNet.UnityModManager.Mod.Active = false;
        var absent = InfrastructureData.GetJson();
        PumpUntil(absent);
        Check((string?)JObject.Parse(absent.Result)["signalsStatus"] == "unavailable", "Missing optional mod not handled");
        Console.WriteLine("PASS: main-thread access, shared capture, frame yields, cache expiry, unload cancellation, optional mod absence.");
        CheckAiTraffic();
    }

    static AiTrafficData.State ReadAi()
    {
        var state = new AiTrafficData.State();
        foreach (var unused in AiTrafficData.ReadRows(state)) { }
        return state;
    }
    static void CheckAiTraffic()
    {
        UnityModManagerNet.UnityModManager.AiMod.Active = true;
        var engineer = AITraffic.Core.TrafficManager.Engineer;
        var state = ReadAi();
        var json = JObject.FromObject(state);
        Check(state.status == "ready" && state.trains.Count == 1, "AI not captured");
        Check(state.trains[0].worker, "Worker driver omitted");
        Check(state.trains[0].distanceToSignal == null, "Infinity leaked into JSON");
        Check(state.trains[0].routeTracks.SequenceEqual(new[]{"planned-only","reserved"}), "Planned route lost order");
        Check(json["reservations"]!.Count() == 1 && (string?)json["reservations"]![0]!["trackId"] == "reserved", "Planned/attempted reservation reported as effective");
        Check((string?)json["reservations"]![0]!["ownerId"] == "train-guid", "Reservation owner lost");
        var junction = RailTrackRegistry.Instance.OrderedJunctions[0];
        Check(state.junctionLocks.Count == 1 && AiTrafficData.JunctionBlockReason(junction) != null, "Live AI lock not respected");
        Check(AiTrafficData.JunctionBlockReason(junction, new HashSet<string>{state.trains[0].id}) == null, "Own AI lock blocks assignment");
        Check(AiTrafficData.JunctionBlockReason(junction, new HashSet<string>{"other"}) != null, "Foreign AI lock allowed");
        AITraffic.Navigation.JunctionController.Info.IsExpired = true;
        Check(ReadAi().junctionLocks.Count == 0 && AiTrafficData.JunctionBlockReason(junction) == null, "Expired lock still blocks");
        // Optional DVSignals failure/absence must not prevent AITraffic capture.
        InfrastructureData.Reset();
        var independent = InfrastructureData.GetJson(); PumpUntil(independent);
        Check((string?)JObject.Parse(independent.Result)["aiTraffic"]!["status"] == "ready", "AI depends on DVSignals snapshot availability");
        // A private API mismatch clears partial AI data instead of returning misleading reservations.
        AITraffic.Core.TrafficManager.Engineers.Add(new object());
        InfrastructureData.Reset();
        var mismatch = InfrastructureData.GetJson(); PumpUntil(mismatch);
        var failed = JObject.Parse(mismatch.Result)["aiTraffic"]!;
        Check((string?)failed["status"] == "incompatible" && !failed["trains"]!.Any(), "Partial AI data leaked on adapter error");
        AITraffic.Core.TrafficManager.Engineers.Clear();
        Check(ReadAi().trains.Count == 0, "Despawned train retained");
        UnityModManagerNet.UnityModManager.AiMod.Active = false;
        Check(ReadAi().status == "unavailable", "Disabled AI mod still read");
        Console.WriteLine("PASS: AI workers, route order, finite distances, effective ownership, active/expired locks, independent optional mods, adapter failure and despawn.");
    }
}
namespace DvMod.RemoteDispatch
{
    public static class Main { public static void DebugLog(Func<string> message) => Console.WriteLine(message()); }
    public static class Updater
    {
        static readonly ConcurrentQueue<Action> queue = new();
        static readonly List<IEnumerator> routines = new();
        public static int Yields;
        public static Task RunOnMainThread(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() => { try { action(); tcs.SetResult(true); } catch (Exception e) { tcs.SetException(e); } });
            return tcs.Task;
        }
        public static void RunCoroutine(IEnumerator routine) { Program.OnMain(); routines.Add(routine); }
        public static void Pump()
        {
            while (queue.TryDequeue(out var action)) action();
            for (int i = routines.Count - 1; i >= 0; i--) if (!routines[i].MoveNext()) routines.RemoveAt(i); else Yields++;
        }
    }
    public static class World
    {
        public struct Position { UnityEngine.Vector3 p; public Position(UnityEngine.Vector3 p) { this.p = p; } public LatLon ToLatLon() => new() { latitude = p.z, longitude = p.x }; }
        public struct LatLon { public float latitude, longitude; }
    }
}
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public static Vector3 operator -(Vector3 a, Vector3 b) => a; }
    public class Transform { public Vector3 position, eulerAngles; }
    public class Component { public Transform transform { get { Program.OnMain(); return new(); } } public static implicit operator bool(Component? c) => c != null; }
}
public static class WorldStreamingInit { public static bool Instance => true; public static bool IsLoaded => true; }
public static class WorldMover { public static UnityEngine.Vector3 currentMove => new(); }
public class RailTrackRegistry { public static RailTrackRegistry Instance = new(); public Junction[] OrderedJunctions = new[] { new Junction() }; }
public class Junction { public UnityEngine.Vector3 position; public byte selectedBranch; public Branch[] outBranches = new[] { new Branch() }; }
public class Branch { public Track track = new(); }
public class Track : RailTrack { }
public class RailTrack { public RailTrack LogicTrack() => this; public string ID = "test"; }
public class TrainCar : UnityEngine.Component { public string ID = "L-001", CarGUID = "train-guid"; }
namespace UnityModManagerNet
{
    public static class UnityModManager
    {
        public static Entry Mod = new();
        public static Entry AiMod = new() {Active = false};
        public static Entry FindMod(string id) { Program.OnMain(); return id == "AITraffic" ? AiMod : Mod; }
        public class Entry { public bool Active = true; public Assembly Assembly => Assembly.GetExecutingAssembly(); public Info Info = new(); }
        public class Info { public string Version = "0.2.1"; }
    }
}
namespace Signals.Game
{
    public class SignalManager
    {
        public static SignalManager Instance = new();
        public static bool Running => true;
        public List<Controller> AllControllers = new() { new Controller() };
    }
    public class Controller { public bool Exists => true; public Signal[] AllSignals = Enumerable.Range(0, 100).Select(i => new Signal { Id = i }).ToArray(); }
    public class Signal
    {
        public int Id;
        public Signal? DistantSignal => null;
        public UnityEngine.Component Definition => new();
        public string Name { get { Program.OnMain(); Thread.SpinWait(20000); return "test"; } }
        public Aspect CurrentAspect => new();
        public bool IsOff => false;
        public bool IsShunting => false;
        public string Operation => "Automatic";
    }
    public class Aspect { public string Id => "Stop"; public bool DisallowPassing => true; }
}

namespace AITraffic.Core
{
    public class TrafficManager
    {
        private static TrafficManager s_instance = new();
        public static TrafficManager Instance => throw new Exception("Must not invoke creating singleton getter");
        public static bool IsRunning => true;
        public static Engineer Engineer = new();
        public static List<object> Engineers = new() { Engineer };
        public List<object> ActiveEngineers => Engineers;
    }
    public class Engineer
    {
        public TrainCar TrainCar = new();
        public string State = "Driving", OriginStationName = "Origin", DestinationStationName = "Destination", DestinationTrackName = "reserved";
        public float CurrentSpeedKmh = 30, TargetSpeedKmh = 40, DistanceToSignal = float.PositiveInfinity, DistanceToDestination = 1200;
        public object? ApproachingSignal = null;
        public bool IsWorkerDriven = true;
        public List<RailTrack> UpcomingTracks = new() { new RailTrack {ID="planned-only"}, new RailTrack {ID="reserved"} };
    }
}
namespace AITraffic.Navigation
{
    public class RailGraph
    {
        private static RailGraph s_instance = new();
        private readonly object _lock = new();
        private Dictionary<RailTrack,object> _trackReservations = new() { [new RailTrack{ID="reserved"}] = Core.TrafficManager.Engineer };
        public bool IsInitialized = true;
    }
    public class JunctionController
    {
        public static LockInfo Info = new();
        private static JunctionController _instance = new();
        private readonly object _lock = new();
        private Dictionary<Junction,LockInfo> _activeLocks = new() { [RailTrackRegistry.Instance.OrderedJunctions[0]] = Info };
    }
    public class LockInfo { public bool IsExpired; public object Requester = Core.TrafficManager.Engineer; }
}

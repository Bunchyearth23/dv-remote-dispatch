using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DvMod.RemoteDispatch;

class Program
{
    public static int MainThread;
    public static void OnMain() { if (Environment.CurrentManagedThreadId != MainThread) throw new Exception("Unity accessed on worker"); }
    static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    public static T Await<T>(Task<T> task)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < end) { Updater.Pump(); Thread.Sleep(1); }
        Check(task.IsCompleted, "Request hung");
        return task.GetAwaiter().GetResult();
    }
    static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    static void Refuses(Func<object> action, string contains)
    {
        try { action(); throw new Exception("Expected refusal: " + contains); }
        catch (Exception e) { Check(e.Message.Contains(contains), "Wrong refusal: " + e.Message); }
    }
    static void Main()
    {
        MainThread = Environment.CurrentManagedThreadId;
        var graph = new RouteGraph();
        foreach (string id in new[] {"A", "B", "C", "D", "E"}) graph.tracks[id] = new RouteGraph.Track { id = id, length = 100 };
        graph.tracks["A"].forward.Add(new RouteGraph.Link { track="B", forward=true });
        graph.tracks["B"].forward.Add(new RouteGraph.Link { track="D", forward=true, junction=0, branch=0 });
        graph.tracks["B"].forward.Add(new RouteGraph.Link { track="C", forward=false, junction=0, branch=1 });
        graph.tracks["C"].backward.Add(new RouteGraph.Link { track="D", forward=true });
        var shortest = graph.Find("A", "D", null, true);
        Check(shortest.steps.Select(s=>s.track).SequenceEqual(new[]{"A","B","D"}) && shortest.distance==300, "Shortest path incorrect");
        var via = graph.Find("A", "D", "C", null);
        Check(via.steps.Select(s=>s.track).SequenceEqual(new[]{"A","B","C","D"}) && !via.steps[2].forward, "Via lost track-end orientation");
        Refuses(()=>graph.Find("A","D",null,false), "Aucun parcours");
        Refuses(()=>graph.Find("A","E",null,null), "Aucun parcours");
        Refuses(()=>graph.Find("missing","D",null,null), "introuvable");
        Check(graph.Find("A","A",null,null).steps.Count==1,"Same-track route");
        graph.tracks["C"].backward[0].junction=0; graph.tracks["C"].backward[0].branch=0;
        Refuses(()=>graph.Find("A","D","C",null), "deux positions");
        Console.WriteLine("PASS graph: shortest path, via, orientation, unreachable, invalid tracks, same track, incompatible switch positions.");

        RailTrack a = new RailTrack("A"), b = new RailTrack("B"), c = new RailTrack("C"), d = new RailTrack("D");
        UnityEngine.Object.Tracks = new[]{a,b,c,d};
        a.Out.Add(new Junction.Branch(b,true)); b.In.Add(new Junction.Branch(a,false));
        var j = new Junction { inBranch=new Junction.Branch(b,false), outBranches=new(){new Junction.Branch(c,true),new Junction.Branch(d,true)}, selectedBranch=0 };
        b.outJunction=j; c.inJunction=j; d.inJunction=j;
        b.Out.AddRange(j.outBranches); c.In.Add(j.inBranch); d.In.Add(j.inBranch);
        RailTrackRegistry.Instance.OrderedJunctions=new[]{j};
        var car = new TrainCar("player",a); TrainCarRegistry.Instance.logicCarToTrainCar["player"]=car;
        var duplicate = new RailTrack("A");
        UnityEngine.Object.Tracks = new[]{a,b,c,d,duplicate};
        Check(TrackIdentity.Id(a) != TrackIdentity.Id(duplicate), "Duplicate display IDs merged distinct tracks");
        Check(TrackIdentity.All.Count == 5, "Duplicate track disappeared");
        AiTrafficData.Unavailable = true;
        var degradedCatalog = Await(RoutePlanner.Catalog());
        Check(Property<Array>(degradedCatalog,"trains").Length == 1, "Optional AI blocked locomotive loading");
        Check(Property<string>(degradedCatalog,"warning").Contains("AI"), "Missing degraded AI warning");
        AiTrafficData.Unavailable = false;
        var duplicatePlan = Await(RoutePlanner.Preview("dispatcher","player","D",null));
        Check(Property<string>(duplicatePlan,"origin") == TrackIdentity.Id(a), "Preview lost native origin with duplicate display name");
        Check(Property<List<string>>(duplicatePlan,"conflicts").Count == 0, "Duplicate name blocked valid route");
        UnityEngine.Object.Tracks = new[]{a,b,c,d};
        TrackIdentity.Reset();
        RoutePlanner.Reset();
        var catalog = Await(RoutePlanner.Catalog());
        Check(Property<Array>(catalog,"trains").Length==1,"Catalog missed locomotive");
        var plan = Await(RoutePlanner.Preview("dispatcher","player","D",null));
        Check(Property<List<string>>(plan,"conflicts").Count==0,"Unexpected conflict");
        var token = Property<string>(plan,"token");
        Refuses(()=>Await(RoutePlanner.Apply("other",token)),"expirée");
        DvMod.RemoteDispatch.Main.settings.permissions.Allowed=false;
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"Permission");
        DvMod.RemoteDispatch.Main.settings.permissions.Allowed=true;
        Check(j.selectedBranch==0,"Unauthorized write");
        var applied=Await(RoutePlanner.Apply("dispatcher",token));
        Check(j.selectedBranch==1 && Property<int>(applied,"changed")==1,"Switch not set");
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"expirée");

        string Prepare() => Property<string>(Await(RoutePlanner.Preview("dispatcher","player","D",null)),"token");
        token=Prepare(); car.Speed=1;
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"Immobilisez"); car.Speed=0;
        token=Prepare(); car.FrontBogie.track=b;
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"changé de voie"); car.FrontBogie.track=a;
        token=Prepare(); AiTrafficData.Reservations[d]="other";
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"réservée"); AiTrafficData.Reservations.Clear();
        token=Prepare(); AiTrafficData.Locked=true;
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"lock"); AiTrafficData.Locked=false;
        token=Prepare(); TrainCarRegistry.Instance.logicCarToTrainCar["other"]=new TrainCar("other",d);
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"occupée"); TrainCarRegistry.Instance.logicCarToTrainCar.Remove("other");
        j.selectedBranch=0; token=Prepare(); TrainCarRegistry.Instance.logicCarToTrainCar["other"]=new TrainCar("other",c);
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"adjacente"); TrainCarRegistry.Instance.logicCarToTrainCar.Remove("other");
        token=Prepare(); AiTrafficData.Engineers.Add("player");
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"affectation"); AiTrafficData.Engineers.Clear();
        var secondLoco = new TrainCar("second",a);
        car.trainset = secondLoco.trainset = new Trainset { cars = new() { car, secondLoco } };
        token=Prepare(); AiTrafficData.Engineers.Add("second");
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"affectation"); AiTrafficData.Engineers.Clear();
        token=Prepare(); b.Out.RemoveAll(branch=>branch.track==d);
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"connexions"); b.Out.Add(new Junction.Branch(d,true));
        token=Prepare();
        var planStore=(IDictionary)typeof(RoutePlanner).GetField("plans",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var stored=planStore[token]!; stored.GetType().GetField("expires")!.SetValue(stored,DateTime.UtcNow.AddSeconds(-1));
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"expirée");
        token=Prepare(); RoutePlanner.Reset();
        Refuses(()=>Await(RoutePlanner.Apply("dispatcher",token)),"expirée");
        Console.WriteLine("PASS commands: main-thread capture, catalog, permission, token ownership, single use, expiry, apply, moving/stale train, reservations, locks, occupation, adjacent protection, AI/consist isolation, changed connectivity, unload.");
        AiCommandChecks.Run();
        MultiplayerChecks.Run();
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static RailTrack[] Tracks=Array.Empty<RailTrack>();
        public static T[] FindObjectsOfType<T>() { Program.OnMain(); return (T[])(object)Tracks; }
    }
    public struct Vector3
    {
        public float x,z;
        public float sqrMagnitude=>x*x;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(){x=a.x-b.x,z=a.z-b.z};
    }
    public class Transform { public Vector3 TransformPoint(Vector3 v)=>new(){x=10000}; }
    public struct Bounds { public Vector3 center; }
}
public class WorldStreamingInit { public static bool Instance=true, IsLoaded=true; }
public class Curve { public double length=100; }
public class LogicTrack { public string ID=""; }
public class RailTrack
{
    private static int nextId;
    private readonly int instanceId = ++nextId;
    public int GetInstanceID() => instanceId;
    public readonly string Name;
    public string name => Name;
    public RailTrack(string name) { Name=name; }
    public Curve curve=new();
    public Junction? inJunction,outJunction;
    public List<Junction.Branch> In=new(),Out=new();
    public List<Junction.Branch> GetAllInBranches() {Program.OnMain();return In;}
    public List<Junction.Branch> GetAllOutBranches() {Program.OnMain();return Out;}
    public LogicTrack LogicTrack() {Program.OnMain();return new(){ID=Name};}
}
public class Junction
{
    public Dictionary<Type,object> Components=new();
    public object? GetComponent(Type type)=>Components.GetValueOrDefault(type);
    public enum SwitchMode {REGULAR}
    public class Branch {public RailTrack track;public bool first;public Branch(RailTrack t,bool f){track=t;first=f;}}
    public Branch inBranch=null!;
    public List<Branch> outBranches=new();
    public byte selectedBranch;
    public UnityEngine.Vector3 position;
    public void Switch(SwitchMode mode,byte branch){Program.OnMain();selectedBranch=branch;}
}
public class RailTrackRegistry {public static RailTrackRegistry Instance=new();public Junction[] OrderedJunctions=Array.Empty<Junction>();}
public class Bogie {public RailTrack? track;}
public class Trainset {public List<TrainCar> cars=new();}
public class TrainCar
{
    public Dictionary<Type,object> Components=new();
    public object? GetComponent(Type type)=>Components.GetValueOrDefault(type);
    public string CarGUID,ID;
    public bool IsLoco=true;
    public Bogie FrontBogie=new(),RearBogie=new();
    public Trainset? trainset;
    public float Speed,InterCouplerDistance=20;
    public UnityEngine.Transform transform=new();
    public UnityEngine.Bounds Bounds;
    public TrainCar(string id,RailTrack track){CarGUID=ID=id;FrontBogie.track=RearBogie.track=track;}
    public float GetForwardSpeed(){Program.OnMain();return Speed;}
}
public class TrainCarRegistry
{
    public static TrainCarRegistry Instance=new();
    public Dictionary<string,TrainCar> logicCarToTrainCar=new();
    public TrainCar? GetTrainCarByCarGuid(string id){Program.OnMain();return logicCarToTrainCar.GetValueOrDefault(id);}
}
namespace DvMod.RemoteDispatch
{
    public static class Main {public static Settings settings=new();}
    public class Settings {public Permissions permissions=new();}
    public class Permissions {public bool Allowed=true;public bool HasJunctionPermission(string owner)=>Allowed;public bool HasLocoControlPermission(string owner)=>Allowed;}
    public static class AiTrafficData
    {
        public static Dictionary<RailTrack,string> Reservations=new();
        public static HashSet<string> Engineers=new();
        public static bool Locked;
        public static bool Unavailable;
        public static Dictionary<RailTrack,string> RouteReservations(out HashSet<string> engineers){Program.OnMain(); if(Unavailable) throw new Exception("AI initialization"); engineers=Engineers;return Reservations;}
        public static string? JunctionBlockReason(Junction junction,HashSet<string>? allowedOwners=null)=>Locked?"lock AI":null;
        internal static object? Read(object target,string name)
        {
            Program.OnMain();
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            var type=target as Type??target.GetType();var instance=target is Type?null:target;
            if(type.GetProperty(name,flags) is {} p)return p.GetValue(instance);
            return (type.GetField(name,flags)??throw new MissingMemberException(name)).GetValue(instance);
        }
        internal static List<object> CopyList(object? source)=>source is IEnumerable items?items.Cast<object>().ToList():new();
        internal static List<DictionaryEntry> CopyRegistry(object source,string name){var result=new List<DictionaryEntry>();foreach(DictionaryEntry entry in (IDictionary)Read(source,name)!)result.Add(entry);return result;}
    }
    public static class Updater
    {
        static ConcurrentQueue<Action> queue=new();
        static List<IEnumerator> routines=new();
        public static Task<T> RunOnMainThread<T>(Func<T> f)
        {
            var t=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(()=>{try{t.SetResult(f());}catch(Exception e){t.SetException(e);}});return t.Task;
        }
        public static void RunCoroutine(IEnumerator routine){Program.OnMain();routines.Add(routine);}
        public static void Pump(){while(queue.TryDequeue(out var a))a();foreach(var r in routines.ToArray())if(!r.MoveNext())routines.Remove(r);}
    }
}

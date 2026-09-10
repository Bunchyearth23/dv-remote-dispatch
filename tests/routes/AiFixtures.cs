using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DvMod.RemoteDispatch;

public class StationInfo { public string Name="Test station"; }
public class StationController
{
    public static List<StationController> allStations=new();
    public List<RailTrack> AllStationTracks=new();
    public StationInfo stationInfo=new();
}
namespace UnityModManagerNet
{
    public static class UnityModManager
    {
        public class Info { public string Version="0.2.1"; }
        public class Mod {public bool Active;public Assembly Assembly=typeof(Mod).Assembly;public Info Info=new();}
        public static Mod AiMod=new();
        public static Mod MpMod=new();
        public static Mod FindMod(string id)=>id=="Multiplayer"?MpMod:AiMod;
    }
}
namespace AITraffic.Core
{
    public class TrafficManager
    {
        public static TrafficManager s_instance=new();
        public List<object> ActiveEngineers=new();
    }
}
namespace AITraffic.Navigation
{
    public class RailGraph
    {
        public static RailGraph s_instance=new();
        public bool IsInitialized=true;
        public Dictionary<RailTrack,object> _trackReservations=new();
        public bool FailRelease;
        public void ReleaseTrackReservation(RailTrack track,object requester)
        {
            Program.OnMain();if(FailRelease)throw new Exception("injected release failure");
            if(_trackReservations.TryGetValue(track,out var owner)&&ReferenceEquals(owner,requester))_trackReservations.Remove(track);
        }
    }
    public class RailPath
    {
        public List<RailTrack> Tracks=new();public bool IsValid=>Tracks.Count>0;
        public Dictionary<Junction,byte> JunctionSwitches=new();
        public float TotalDistance=>Tracks.Sum(t=>(float)t.curve.length);
    }
    public class Pathfinder
    {
        public Pathfinder(RailGraph graph){}
        public RailPath BuildPathFromTracks(List<RailTrack> tracks)=>new(){Tracks=tracks.ToList()};
    }
    public class LockInfo {public object Requester=null!;}
    public class JunctionController
    {
        public static JunctionController _instance=new();
        public Dictionary<Junction,LockInfo> _activeLocks=new();
        public void CancelTrainPassing(object requester,Junction junction){Program.OnMain();if(_activeLocks.TryGetValue(junction,out var v)&&v.Requester==requester)_activeLocks.Remove(junction);}
    }
}
namespace AITraffic.Workers
{
    public class WorkerManager {public static WorkerManager s_instance=new();public List<object> ActiveTasks=new();}
    public class AtoBHaulTask
    {
        public object Engineer=null!;public string Status="EnRoute";
        public StationController? DestinationStation {get;private set;}
        public RailTrack? DestinationTrack {get;private set;}
        public bool IsAutoSelectedTrack {get;private set;}=true;
        public float RouteDistance {get;private set;}=5;
        public string StatusMessage {get;private set;}="old";
        public double HiringFee {get;private set;}=100;
    }
}
namespace AITraffic.Driver
{
    public class AIEngineer
    {
        public TrainCar TrainCar=null!;
        public bool IsWorkerDriven;
        public object CurrentPath=null!;
        public int CurrentPathTrackIndex {get;private set;}=5;
        public float TargetDirection,DistanceToDestination;
        public string DestinationStationName="old",DestinationTrackName="old";
        public bool IsStationDestination=true,IsTerminusDestination;
        public float DwellTimeRemaining {get;private set;}=20;
        public float StationaryTimer {get;private set;}=10;
        public float _heavySensorUpdateCooldown=10,_pathUpdateCooldown=10,_speedProfileUpdateCooldown=10;
        public List<RailTrack> UpcomingTracks=new();
        public ArrayList _upcomingSignalBlocks=new(),_upcomingSignals=new();
        public HashSet<RailTrack> _reservedTracks=new();
        public bool Held,SignalsReleased;
        public void EmergencyStop(){Program.OnMain();Held=true;}
        public void Resume(){Program.OnMain();Held=false;}
        public void ReleaseAllSignalReservations(){Program.OnMain();SignalsReleased=true;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool TryAdoptPlayerAlignedPassingRoute(RailTrack track)=>true;
    }
}
static class AiCommandChecks
{
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Refuses(Action action,string text){try{action();throw new Exception("Expected refusal");}catch(Exception e){Check(e.Message.Contains(text),e.ToString());}}
    public static void Run()
    {
        var mod=UnityModManagerNet.UnityModManager.AiMod;mod.Active=true;
        var a=new RailTrack("ai-start");var b=new RailTrack("ai-dest");b.curve.length=500;
        var oldTrack=new RailTrack("old");var otherTrack=new RailTrack("other");
        var car=new TrainCar("driver",a);
        TrainCarRegistry.Instance.logicCarToTrainCar.Clear();TrainCarRegistry.Instance.logicCarToTrainCar[car.CarGUID]=car;
        var driver=new AITraffic.Driver.AIEngineer{TrainCar=car,IsWorkerDriven=true,CurrentPath=new AITraffic.Navigation.RailPath{Tracks=new(){a,oldTrack}}};
        driver.UpcomingTracks.Add(oldTrack);
        AITraffic.Core.TrafficManager.s_instance.ActiveEngineers.Add(driver);
        var task=new AITraffic.Workers.AtoBHaulTask{Engineer=driver};
        AITraffic.Workers.WorkerManager.s_instance.ActiveTasks.Add(task);
        var station=new StationController{AllStationTracks=new(){b}};StationController.allStations.Add(station);
        var graph=AITraffic.Navigation.RailGraph.s_instance;
        graph._trackReservations[a]=driver;graph._trackReservations[oldTrack]=driver;var foreign=new object();graph._trackReservations[otherTrack]=foreign;
        var locks=AITraffic.Navigation.JunctionController._instance._activeLocks;
        var obsoleteLock=new Junction{inBranch=new Junction.Branch(oldTrack,true)};
        var occupiedLock=new Junction{inBranch=new Junction.Branch(a,true)};
        var foreignLock=new Junction{inBranch=new Junction.Branch(otherTrack,true)};
        locks[obsoleteLock]=new(){Requester=driver};locks[occupiedLock]=new(){Requester=driver};locks[foreignLock]=new(){Requester=foreign};
        var preview=new RouteGraph.Path{steps=new(){new(){track="ai-start",forward=true},new(){track="ai-dest",forward=true}},distance=600};
        var native=new List<RailTrack>{a,b};
        Check(AiTrafficCommands.BlockReason(car,native)==null,"Valid AI route rejected");
        mod.Info.Version="9.9";Check(AiTrafficCommands.BlockReason(car,native)!.Contains("version"),"Unknown version accepted");mod.Info.Version="0.2.1";
        b.curve.length=20;Check(AiTrafficCommands.BlockReason(car,native)!.Contains("too short"),"Short arrival accepted");b.curve.length=500;
        StationController.allStations.Clear();Check(AiTrafficCommands.BlockReason(car,native)!.Contains("station track"),"Worker sent outside station");StationController.allStations.Add(station);
        Refuses(()=>AiTrafficCommands.Assign(car,native,preview,new object()),"driver changed");
        AiTrafficCommands.Control(car,"stop");Check(driver.Held,"Stop not held");AiTrafficCommands.Control(car,"resume");Check(!driver.Held,"Resume failed");
        Refuses(()=>AiTrafficCommands.Control(car,"delete"),"Unknown");
        AiTrafficCommands.Assign(car,native,preview,driver);
        Check(((AITraffic.Navigation.RailPath)driver.CurrentPath).Tracks.SequenceEqual(native),"Wrong assigned path");
        Check(task.DestinationTrack==b && task.DestinationStation==station && task.HiringFee==100,"Worker task inconsistent or charged");
        Check(driver.CurrentPathTrackIndex==0 && driver.UpcomingTracks.SequenceEqual(native) && !driver.Held && driver.SignalsReleased,"Driver not reset/restarted");
        Check(graph._trackReservations.ContainsKey(a)&&!graph._trackReservations.ContainsKey(oldTrack)&&graph._trackReservations[otherTrack]==foreign,"Reservation ownership broken");
        Check(!locks.ContainsKey(obsoleteLock)&&locks.ContainsKey(occupiedLock)&&locks.ContainsKey(foreignLock),"Junction lock ownership/protection broken");
        Check(!driver.TryAdoptPlayerAlignedPassingRoute(a),"Dispatched via can be silently replaced");
        var unrelated=new AITraffic.Driver.AIEngineer();Check(unrelated.TryAdoptPlayerAlignedPassingRoute(a),"Unrelated AI behavior changed");
        var previous=driver.CurrentPath;var previousDestination=task.DestinationTrack;
        graph._trackReservations[oldTrack]=driver;graph.FailRelease=true;
        Refuses(()=>AiTrafficCommands.Assign(car,native,preview,driver),"remains stopped");graph.FailRelease=false;
        Check(driver.Held&&driver.CurrentPath==previous&&task.DestinationTrack==previousDestination,"Failure did not restore mission and hold brakes");
        Check(!driver.TryAdoptPlayerAlignedPassingRoute(a),"Rollback lost existing dispatch policy");
        var alias=new TrainCar("alias",a);alias.trainset=car.trainset=new Trainset{cars=new(){car,alias}};
        Check(AiTrafficCommands.DrivingCar(alias)==car,"Consist lead not resolved");
        var duplicate=new AITraffic.Driver.AIEngineer{TrainCar=alias};AITraffic.Core.TrafficManager.s_instance.ActiveEngineers.Add(duplicate);
        Refuses(()=>AiTrafficCommands.DrivingCar(car),"Multiple AI drivers");
        AITraffic.Core.TrafficManager.s_instance.ActiveEngineers.Remove(duplicate);
        // Exercise the real token/permission service through to the reflection adapter.
        UnityEngine.Object.Tracks=new[]{a,b};TrackIdentity.Reset();a.Out.Add(new Junction.Branch(b,true));b.In.Add(new Junction.Branch(a,false));
        TrainCarRegistry.Instance.logicCarToTrainCar.Clear();TrainCarRegistry.Instance.logicCarToTrainCar[car.CarGUID]=car;
        RailTrackRegistry.Instance.OrderedJunctions=Array.Empty<Junction>();RoutePlanner.Reset();AiTrafficData.Engineers.Add(car.CarGUID);
        var pending=Program.Await(RoutePlanner.Preview("dispatcher",car.CarGUID,"ai-dest",null));
        var token=(string)pending.GetType().GetProperty("token")!.GetValue(pending)!;
        DvMod.RemoteDispatch.Main.settings.permissions.Allowed=false;
        Refuses(()=>Program.Await(RoutePlanner.AssignAi("dispatcher",token)),"permissions");
        Refuses(()=>Program.Await(RoutePlanner.ControlAi("dispatcher",car.CarGUID,"stop")),"permission");
        DvMod.RemoteDispatch.Main.settings.permissions.Allowed=true;
        Refuses(()=>Program.Await(RoutePlanner.AssignAi("other",token)),"expired");
        Program.Await(RoutePlanner.AssignAi("dispatcher",token));
        Refuses(()=>Program.Await(RoutePlanner.AssignAi("dispatcher",token)),"expired");
        task.Status="Arrived";Check(AiTrafficCommands.BlockReason(car,native)!.Contains("active"),"Completed worker reused");
        Console.WriteLine("PASS AI: contract/version, worker destination and fee, route length, stop/resume, engineer identity, actual ownership, occupied reservations, path policy isolation, rollback with brakes, lead resolution, multiple drivers, completed mission.");
    }
}

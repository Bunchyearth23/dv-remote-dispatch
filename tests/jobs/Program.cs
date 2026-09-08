using DV.Logic.Job;
using DvMod.RemoteDispatch;
using Newtonsoft.Json.Linq;

static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        var manager = DV.Utils.SingletonBehaviour<JobsManager>.Instance;
        var load = new WarehouseTask { warehouseMachine = new() { WarehouseTrack = new() { ID = new() { FullDisplayID="HB-A1L" } } } };
        var unload = new WarehouseTask { warehouseMachine = new() { WarehouseTrack = new() { ID = new() { FullDisplayID="SM-A1L" } } } };
        var job = new Job { ID="HB-DH-01", jobType=(JobType)5, tasks=new(){load,unload} };
        // Yard Master exposes unassigned definitions, before registering any cars.
        SelfShunt.StaticDirectJobDefinition.jobDefinitions[job.ID] = new() { job=job };
        manager.jobToJobCars[new Job { ID="unsupported", jobType=(JobType)42 }] = new();
        var data = JobData.GetAllJobData();
        Check(data[job.ID]["yardMaster"]!.Value<bool>(), "Direct haul misclassified as passenger");
        Check(data[job.ID]["tasks"]![0]!["startTrack"]!.ToString()=="HB-A1L", "Loading destination lost");
        Check(data[job.ID]["tasks"]![0]!["destinationTrack"]!.ToString()=="SM-A1L", "Unloading destination lost");
        Check(!data[job.ID]["carsAssigned"]!.Value<bool>(), "Invented preassigned cars");
        Check(data.ContainsKey("unsupported") && data["unsupported"]["error"] != null, "Unsupported job broke snapshot");
        var car = new Car { ID="C-01", train=new TrainCar(), length=15 };
        load.Data.cars.Add(car); unload.Data.cars.Add(car);
        manager.jobToJobCars[job] = new(){car};
        JobData.JobPatches.CarPlatePatch.Postfix(car.train,job.ID);
        Check(JobData.JobIdForCar(car.train)==job.ID,"Late wagon assignment not reflected");
        data=JobData.GetAllJobData();
        Check(data[job.ID]["carsAssigned"]!.Value<bool>() && data[job.ID]["length"]!.Value<float>()==15,"Assigned consist missing");
        JobData.JobPatches.CarPlatePatch.Postfix(car.train,"");
        Check(JobData.JobIdForCar(car.train)==null,"Completed job still assigned to wagon");
        JobData.Reset();
        manager.jobToJobCars[new Job {ID="duplicate"}]=new(){car};
        Check(JobData.JobIdForCar(car.train)!=null,"Duplicate assignment poisoned static initialization");
        JobData.Reset(); manager.jobToJobCars.Clear(); SelfShunt.StaticDirectJobDefinition.jobDefinitions.Clear();
        Check(JobData.GetAllJobData().Count==0,"Reload retained jobs");
        Console.WriteLine("PASS Yard Master: unassigned DirectHaul, load/unload tracks, late assignment, plate clearing, duplicate car association, unsupported job isolation, reload.");
    }
}

public class TrainCar { public void UpdateJobIdOnCarPlates(string jobId){} }
public class StaticJobDefinition {public Job job=null!;}
public class JobChainController { public List<Car> carsForJobChain=new(); public void UpdateTrainCarPlatesOfCarsOnJob(string jobId){} }
namespace SelfShunt { public class StaticDirectJobDefinition : StaticJobDefinition {public static Dictionary<string,StaticDirectJobDefinition> jobDefinitions=new();} }
namespace DV.Utils {public class SingletonBehaviour<T> where T:new() {public static T Instance=new();} }
namespace DV.ThingTypes
{
    public enum CargoType {None}
    public class Cargo {public float massPerUnit;}
}
namespace DV.ThingTypes.TransitionHelpers
{
    public static class Helpers {public static DV.ThingTypes.Cargo ToV2(this DV.ThingTypes.CargoType c)=>new();public static TrainCar TrainCar(this Car car)=>car.train;}
}
namespace DV.Logic.Job
{
    [Flags] public enum JobLicenses {Basic=0,Hazmat1=1,Hazmat2=2,Hazmat3=4,Military1=8,Military2=16,Military3=32,TrainLength1=64,TrainLength2=128}
    public enum JobType {ShuntingLoad,ComplexTransport}
    public enum JobState {Available,InProgress}
    public enum TaskType {Transport,Warehouse,Sequence}
    public enum WarehouseTaskType {Loading,Unloading}
    public class TrackID {public string FullDisplayID="";}
    public class Track {public TrackID ID=new();}
    public class ParentType {public float mass;}
    public class CarType {public ParentType parentType=new();}
    public class Car {public string ID="";public TrainCar train=new();public float length,capacity;public CarType carType=new();}
    public class TaskData {public TaskType type;public List<Task> nestedTasks=null!;public Track startTrack=null!,destinationTrack=null!;public List<Car> cars=new();public List<DV.ThingTypes.CargoType> cargoTypePerCar=null!;public WarehouseTaskType warehouseTaskType;}
    public class Task {public TaskData Data=new();public TaskData GetTaskData()=>Data;}
    public class WarehouseMachine {public Track WarehouseTrack=new();}
    public class WarehouseTask:Task {public WarehouseMachine warehouseMachine=new();}
    public class ChainData {public string chainOriginYardId="HB",chainDestinationYardId="SM";}
    public class Job {public string ID="";public JobType jobType;public List<Task> tasks=new();public JobLicenses requiredLicenses;public ChainData chainData=new();public JobState State;public float GetBasePaymentForTheJob()=>100;public void TakeJob(bool takenViaLoadGame){} }
    public class JobsManager {public Dictionary<Job,HashSet<Car>> jobToJobCars=new();}
}
namespace UnityModManagerNet
{
    public static class UnityModManager {public static Entry FindMod(string id)=>new();public class Entry {public bool Active=true;public System.Reflection.Assembly Assembly=>typeof(Program).Assembly;} }
}
namespace DvMod.RemoteDispatch {public static class Main {public static void DebugLog(Func<string> text){} } public static class Sessions {public static void AddTag(string tag){} } }

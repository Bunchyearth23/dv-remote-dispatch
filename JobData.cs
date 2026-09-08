using DV.Logic.Job;
using DV.ThingTypes.TransitionHelpers;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System;

namespace DvMod.RemoteDispatch
{
    public static class JobData
    {
        private static readonly Dictionary<TrainCar, string> jobIdForCar = new Dictionary<TrainCar, string>();
        private static bool initialized;
        public static void Reset() { initialized = false; jobIdForCar.Clear(); jobForId.Clear(); }
        private static Dictionary<string, Job> jobForId = new Dictionary<string, Job>();

        private const JobLicenses LicensesToExport =
          JobLicenses.Hazmat1 | JobLicenses.Hazmat2 | JobLicenses.Hazmat3 |
          JobLicenses.Military1 | JobLicenses.Military2 | JobLicenses.Military3 |
          JobLicenses.TrainLength1 | JobLicenses.TrainLength2;

        public static string? JobIdForCar(TrainCar car)
        {
            if (!initialized) InitializeJobIdForCar();
            jobIdForCar.TryGetValue(car, out var jobId);
            return jobId;
        }

        public static Job? JobForCar(TrainCar car)
        {
            var jobId = JobIdForCar(car);
            if (jobId == null)
                return null;
            return JobForId(jobId);
        }

        private static void InitializeJobIdForCar()
        {
            var manager = SingletonBehaviour<JobsManager>.Instance;
            if (manager == null) return;
            foreach (var pair in manager.jobToJobCars)
            {
                if (pair.Key == null || pair.Value == null) continue;
                foreach (var car in pair.Value)
                {
                    var train = car?.TrainCar();
                    if (train != null) jobIdForCar[train] = pair.Key.ID;
                }
            }
            initialized = true;
        }

        public static Job? JobForId(string jobId)
        {
            if (jobForId.TryGetValue(jobId, out var job))
                return job;
            jobForId = SingletonBehaviour<JobsManager>.Instance.jobToJobCars.Keys
                .Where(j => j != null && !string.IsNullOrEmpty(j.ID)).GroupBy(j => j.ID).ToDictionary(g => g.Key, g => g.First());
            foreach (var definition in YardMasterDefinitions())
                if (definition.job != null) jobForId[definition.job.ID] = definition.job;
            jobForId.TryGetValue(jobId, out job);
            return job;
        }

        public static Dictionary<string, JObject> GetAllJobData()
        {
            static IEnumerable<JObject> PassengerJson(TaskData sequenceTask)
            {
                var sequence = sequenceTask.nestedTasks.Select(task => task.GetTaskData()).ToList();

                string startTrackId = sequence[0].destinationTrack.ID.FullDisplayID;

                for (int i = 1; i < sequence.Count; i++)
                {
                    var task = sequence[i];
                    bool isRuralTask = task.type == (TaskType)42;

                    bool isRuralUnload = isRuralTask && !((dynamic)task).isLoading;
                    if ((task.warehouseTaskType != WarehouseTaskType.Unloading) && !isRuralUnload)
                    {
                        // skip everything but unload tasks
                        continue;
                    }

                    string destTrackId;

                    if (isRuralTask)
                    {
                        destTrackId = ((dynamic)task).stationId;
                    }
                    else
                    {
                        destTrackId = task.destinationTrack.ID.FullDisplayID;
                    }

                    yield return new JObject()
                    {
                        { "startTrack", startTrackId },
                        { "destinationTrack", destTrackId },
                        { "cars", new JArray(task.cars.Select(car => car.ID)) }
                    };

                    startTrackId = destTrackId;
                }
            }
            static IEnumerable<TaskData> FlattenToTransport(TaskData data)
            {
                if (data.type == TaskType.Transport)
                {
                    yield return data;
                }
                else if (data.nestedTasks != null)
                {
                    foreach (var nested in data.nestedTasks)
                    {
                        foreach (var task in FlattenToTransport(nested.GetTaskData()))
                            yield return task;
                    }
                }
            }
            static IEnumerable<TaskData> FlattenMany(IEnumerable<TaskData> data) => data.SelectMany(FlattenToTransport);
            static JObject TaskToJson(TaskData data) => new JObject(
                new JProperty("startTrack", data.startTrack?.ID?.FullDisplayID),
                new JProperty("destinationTrack", data.destinationTrack?.ID?.FullDisplayID),
                new JProperty("cars", (data.cars ?? new List<Car>()).Select(car => car.ID))
            );
            static JArray RequiredLicenses(Job job) => JArray.FromObject(
                Enum.GetValues(typeof(JobLicenses))
                    .OfType<JobLicenses>()
                    .Where(v => (job.requiredLicenses & LicensesToExport & v) != JobLicenses.Basic)
                    .Select(v => Enum.GetName(typeof(JobLicenses), v))
            );
            static float TotalLength(TaskData task) => task.cars?.Sum(car => car.length) ?? 0;
            static float TotalMass(TaskData task) => (task.cars?.Sum(car => car.carType.parentType.mass) ?? 0)
                + ((task.cargoTypePerCar == null)
                ? 0f
                : (task.cars ?? new List<Car>()).Zip(task.cargoTypePerCar, (car, cargoType) => car.capacity * cargoType.ToV2().massPerUnit).Sum());

            static JObject JobToJson(Job job)
            {
                IEnumerable<JObject> taskJson;
                TaskData mainTask;
                var warehouses = job.tasks.OfType<WarehouseTask>().ToArray();
                bool directHaul = (int)job.jobType == 5 && warehouses.Length == 2;

                if (directHaul)
                {
                    // SelfShunt 1.0.0: two WarehouseTasks, no TransportTask or
                    // passenger sequence. Cars are assigned only at loading time.
                    mainTask = warehouses[0].GetTaskData();
                    taskJson = new[] { new JObject(
                        new JProperty("startTrack", warehouses[0].warehouseMachine.WarehouseTrack.ID.FullDisplayID),
                        new JProperty("destinationTrack", warehouses[1].warehouseMachine.WarehouseTrack.ID.FullDisplayID),
                        new JProperty("cars", new JArray((mainTask.cars ?? new List<Car>()).Select(c => c.ID)))) };
                }
                else if (job.jobType <= JobType.ComplexTransport)
                {
                    // normal job
                    var flattenedTasks = FlattenMany(job.tasks.Select(task => task.GetTaskData())).ToArray();
                    mainTask = job.jobType == JobType.ShuntingLoad ? flattenedTasks.Last() : flattenedTasks.First();

                    taskJson = flattenedTasks.Select(TaskToJson);
                }
                else
                {
                    // passenger
                    var sequenceTask = job.tasks[0].GetTaskData();
                    mainTask = sequenceTask.nestedTasks[0].GetTaskData();
                    taskJson = PassengerJson(sequenceTask);
                }

                return new JObject(
                    new JProperty("originYardId", job.chainData.chainOriginYardId),
                    new JProperty("destinationYardId", job.chainData.chainDestinationYardId),
                    new JProperty("tasks", taskJson),
                    new JProperty("yardMaster", directHaul),
                    new JProperty("carsAssigned", mainTask.cars?.Count > 0),
                    new JProperty("requiredLicenses", RequiredLicenses(job)),
                    new JProperty("length", TotalLength(mainTask)),
                    new JProperty("mass", TotalMass(mainTask) / 1000),
                    new JProperty("basePayment", job.GetBasePaymentForTheJob()),
                    new JProperty("isActive", job.State == JobState.InProgress));
            }

            // ensure cache is updated
            JobForId("");
            var result = new Dictionary<string, JObject>();
            foreach (var pair in jobForId)
            {
                try { result[pair.Key] = JobToJson(pair.Value); }
                catch (Exception e)
                {
                    // One unsupported job must not break positions or the other jobs.
                    result[pair.Key] = new JObject(
                        new JProperty("tasks", new JArray()), new JProperty("requiredLicenses", new JArray()),
                        new JProperty("length", 0), new JProperty("mass", 0), new JProperty("basePayment", 0),
                        new JProperty("isActive", pair.Value.State == JobState.InProgress),
                        new JProperty("error", "Mission non lisible : " + e.GetType().Name));
                }
            }
            return result;
        }

        private static IEnumerable<StaticJobDefinition> YardMasterDefinitions()
        {
            var mod = UnityModManagerNet.UnityModManager.FindMod("SelfShunt");
            if (mod?.Active != true || mod.Assembly == null) yield break;
            var field = mod.Assembly.GetType("SelfShunt.StaticDirectJobDefinition")?.GetField("jobDefinitions");
            if (!(field?.GetValue(null) is System.Collections.IDictionary definitions)) yield break;
            foreach (var value in definitions.Values)
                if (value is StaticJobDefinition definition && definition != null) yield return definition;
        }

        public static string GetAllJobDataJson()
        {
            return JsonConvert.SerializeObject(GetAllJobData());
        }

        public static class JobPatches
        {
            [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.UpdateJobIdOnCarPlates))]
            public static class CarPlatePatch
            {
                public static void Postfix(TrainCar __instance, [HarmonyArgument(0)] string jobId)
                {
                    if (string.IsNullOrEmpty(jobId)) jobIdForCar.Remove(__instance);
                    else jobIdForCar[__instance] = jobId;
                    Sessions.AddTag("jobs");
                }
            }
            [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.UpdateTrainCarPlatesOfCarsOnJob))]
            public static class UpdateTrainCarPlatesOfCarsOnJobPatch
            {
                public static void Postfix(JobChainController __instance, string jobId)
                {
                    foreach (Car car in __instance.carsForJobChain)
                    {
                        var trainCar = car.TrainCar();
                        if (trainCar == null) continue;

                        if (jobId.Length == 0)
                            jobIdForCar.Remove(trainCar);
                        else
                            jobIdForCar[trainCar] = jobId;
                        Sessions.AddTag("jobs");
                    }
                }
            }
            public static void UpdateJobsFromPersistentJobs(Job job)
            {
                Main.DebugLog(() => "Persistent Jobs sent update for job " + job.ID);
                Sessions.AddTag("jobs");
            }
            [HarmonyPatch(typeof(Job))]
            public static class UpdateJobStatePatches
            {
                [HarmonyPostfix]
                [HarmonyPatch(nameof(Job.TakeJob))]
                public static void TakeJobPostfix(Job __instance, bool takenViaLoadGame)
                {
                    if (!takenViaLoadGame)
                    {
                        Sessions.AddTag("jobs");
                    }
                }
            }
        }
    }
}

using DV.LocoRestoration;
using DV.ThingTypes;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DvMod.RemoteDispatch
{
    public class CarData
    {
        public readonly string guid;
        public readonly float length;
        public readonly World.LatLon latlon;
        public readonly float rotation;
        public readonly string? jobId;
        public readonly string? destinationYardId;
        public readonly TrainCarType carType;
        public string? cargoId;
        public float? loadedAmount;
        public float? cargoCapacity;
        // Unity-owned index, populated by existing captures; no fleet scan on cargo events.
        private static readonly Dictionary<DV.Logic.Job.Car, string> cargoCarGuids = new Dictionary<DV.Logic.Job.Car, string>();
        internal static void MarkCargoChanged(DV.Logic.Job.Car car)
        {
            if (car != null && cargoCarGuids.TryGetValue(car, out var id) && Sessions.HasActiveSessions()) Sessions.AddTag("carguid-" + id);
        }
        internal static void ForgetCargoCar(TrainCar car)
        {
            if (car?.logicCar != null) cargoCarGuids.Remove(car.logicCar);
        }

        protected CarData(string guid, float length, World.LatLon latlon, float rotation, string? jobId, string? destinationYardId, TrainCarType carType)
        {
            this.guid = guid;
            this.latlon = latlon;
            this.rotation = rotation;
            this.length = length;
            this.jobId = jobId;
            this.destinationYardId = destinationYardId;
            this.carType = carType;
        }

        public static CarData From(TrainCar trainCar)
        {
            if (LocoControl.CanBeControlled(trainCar))
                return new ControllableLocoData(trainCar);

            var data = new CarData(
                trainCar.CarGUID,
                trainCar.InterCouplerDistance,
                latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
                rotation: trainCar.transform.eulerAngles.y,
                jobId: JobData.JobIdForCar(trainCar),
                destinationYardId: JobData.JobForCar(trainCar)?.chainData?.chainDestinationYardId,
                carType: trainCar.carType);
            if (trainCar.logicCar != null)
            {
                cargoCarGuids[trainCar.logicCar] = trainCar.CarGUID;
                data.loadedAmount = trainCar.logicCar.LoadedCargoAmount;
                data.cargoCapacity = trainCar.logicCar.capacity;
                data.cargoId = data.loadedAmount > 0.01f ? trainCar.logicCar.CurrentCargoTypeInCar.ToString() : null;
            }
            return data;
        }

        public virtual JObject ToJson()
        {
            return new JObject(
                new JProperty("guid", guid),
                new JProperty("length", (int)length),
                new JProperty("position", latlon.ToJson()),
                new JProperty("rotation", Math.Round(rotation, 2)),
                new JProperty("cargoId", cargoId), new JProperty("loadedAmount", loadedAmount),
                new JProperty("cargoCapacity", cargoCapacity)
            );
        }

        public static async Task<JObject> GetAllCarDataJsonAsync()
        {
            var cars = await GetAllCarDataAsync().ConfigureAwait(false);
            return JObject.FromObject(cars.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson()));
        }

        public static async Task<JObject?> GetCarGuidDataJsonAsync(string guid)
        {
            var result = await Updater.RunOnMainThread(() =>
            {
                var car = TrainCarRegistry.Instance.GetTrainCarByCarGuid(guid);
                if (car == null || !ShouldReturnTrainCar(car))
                    return default;
                return (car.ID, From(car));
            }).ConfigureAwait(false);
            var (carId, carData) = result;
            if (carId == default)
                return null;
            var obj = carData.ToJson();
            obj.Add("id", carId);
            return obj;
        }

        public static Task<Dictionary<string, CarData>> GetAllCarDataAsync()
        {
            lock (captureGate)
            {
                if (allCarsCapture == null || allCarsCapture.IsCompleted) allCarsCapture = GetAllCarDataWorkerAsync();
                return allCarsCapture;
            }
        }

        private static readonly object captureGate = new object();
        private static Task<Dictionary<string, CarData>>? allCarsCapture;
        private static readonly Dictionary<int, Task<Dictionary<string, JObject>>> trainsetCaptures = new Dictionary<int, Task<Dictionary<string, JObject>>>();

        public static void Reset()
        {
            cargoCarGuids.Clear();
            lock (captureGate) { allCarsCapture = null; trainsetCaptures.Clear(); }
        }

        private static System.Collections.IEnumerator CaptureCars(IEnumerable<TrainCar> source,
            TaskCompletionSource<List<(string id, CarData data)>> completion)
        {
            var cars = source.ToArray();
            var result = new List<(string id, CarData data)>(cars.Length);
            var slice = System.Diagnostics.Stopwatch.StartNew();
            var count = 0;
            foreach (var car in cars)
            {
                if (car != null && ShouldReturnTrainCar(car)) result.Add((car.ID, From(car)));
                if (++count >= 16 || slice.Elapsed.TotalMilliseconds >= 0.5d)
                { yield return null; count = 0; slice.Restart(); }
            }
            completion.TrySetResult(result);
        }

        private static async Task<Dictionary<string, CarData>> GetAllCarDataWorkerAsync()
        {
            var captured = await UnityCapture.Run<List<(string id, CarData data)>>(completion =>
                CaptureCars(TrainCarRegistry.Instance.logicCarToTrainCar.Values, completion)).ConfigureAwait(false);
            return await Task.Run(() => captured.ToDictionary(value => value.id, value => value.data)).ConfigureAwait(false);
        }

        public static Task<Dictionary<string, JObject>> GetTrainsetDataAsync(int id)
        {
            lock (captureGate)
            {
                if (trainsetCaptures.TryGetValue(id, out var pending)) return pending;
                var capture = CaptureTrainset(id);
                trainsetCaptures.Add(id, capture);
                _ = capture.ContinueWith(_ => { lock (captureGate) {
                    if (trainsetCaptures.TryGetValue(id, out var owner) && ReferenceEquals(owner, capture)) trainsetCaptures.Remove(id);
                } }, TaskScheduler.Default);
                return capture;
            }
        }

        private static async Task<Dictionary<string, JObject>> CaptureTrainset(int id)
        {
            var captured = await UnityCapture.Run<List<(string id, CarData data)>>(completion =>
            {
                var trainset = Trainset.allSets.Find(set => set.id == id);
                return CaptureCars(trainset == null ? Enumerable.Empty<TrainCar>() : trainset.cars, completion);
            }).ConfigureAwait(false);
            return await Task.Run(() => captured.ToDictionary(value => value.id, value => value.data.ToJson())).ConfigureAwait(false);
        }

        public static async Task<JObject> GetTrainsetDataJsonAsync(int id)
        {
            return JObject.FromObject(await GetTrainsetDataAsync(id).ConfigureAwait(false));
        }

        public static bool ShouldReturnTrainCar(TrainCar trainCar)
        {
            if (Main.settings.showUndiscoveredLocomotives)
                return true;
            var state = LocoRestorationController.GetForTrainCar(trainCar)?.State;
            return !state.HasValue
                || state.Value >= LocoRestorationController.RestorationState.S3_RerailedCars;
        }
    }

    public class ControllableLocoData : CarData
    {
        public readonly bool canCouple;
        public readonly bool isSlipping;
        public readonly int carsInFront;
        public readonly int carsInRear;
        public readonly float brakePipe;
        public readonly float forwardSpeed;
        public readonly float independentBrake;
        public readonly float reverser;
        public readonly float throttle;
        public readonly float trainBrake;

        public ControllableLocoData(TrainCar trainCar)
        : base(
            trainCar.CarGUID,
            trainCar.InterCouplerDistance,
            latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
            rotation: trainCar.transform.eulerAngles.y,
            jobId: JobData.JobIdForCar(trainCar),
            destinationYardId: JobData.JobForCar(trainCar)?.chainData?.chainDestinationYardId,
            carType: trainCar.carType)
        {
            ILocomotiveRemoteControl controller = trainCar.GetComponent<ILocomotiveRemoteControl>();
            canCouple = controller.IsCouplerInRange(ExternalCouplingHandler.COUPLING_RANGE);
            isSlipping = controller.IsWheelslipping();
            carsInFront = controller.GetNumberOfCarsInFront();
            carsInRear = controller.GetNumberOfCarsInRear();
            forwardSpeed = trainCar.GetForwardSpeed();
            independentBrake = controller.GetTargetIndependentBrake();
            trainBrake = controller.GetTargetBrake();
            reverser = controller.GetReverserValue();
            throttle = controller.GetTargetThrottle();
            brakePipe = trainCar.brakeSystem.brakePipePressure;
        }

        override public JObject ToJson()
        {
            var carObj = base.ToJson();
            carObj.Add("canBeControlled", true);
            carObj.Add("canCouple", canCouple);
            carObj.Add("isSlipping", isSlipping);
            carObj.Add("carsInFront", carsInFront);
            carObj.Add("carsInRear", carsInRear);
            carObj.Add("forwardSpeed", forwardSpeed * 60 * 60 / 1000);
            carObj.Add("reverser", reverser);
            carObj.Add("independentBrake", independentBrake);
            carObj.Add("trainBrake", trainBrake);
            carObj.Add("throttle", throttle);
            carObj.Add("brakePipe", brakePipe);
            return carObj;
        }
    }
}

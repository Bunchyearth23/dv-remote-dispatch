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

            return new CarData(
                trainCar.CarGUID,
                trainCar.InterCouplerDistance,
                latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
                rotation: trainCar.transform.eulerAngles.y,
                jobId: JobData.JobIdForCar(trainCar),
                destinationYardId: JobData.JobForCar(trainCar)?.chainData?.chainDestinationYardId,
                carType: trainCar.carType);
        }

        public virtual JObject ToJson()
        {
            return new JObject(
                new JProperty("guid", guid),
                new JProperty("length", (int)length),
                new JProperty("position", latlon.ToJson()),
                new JProperty("rotation", Math.Round(rotation, 2))
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
            return Updater.RunOnMainThread(() =>
            {
                return TrainCarRegistry.Instance
                    .logicCarToTrainCar
                    .Values
                    .Where(ShouldReturnTrainCar)
                    .ToDictionary(car => car.ID, car => From(car));
            });
        }

        public static async Task<Dictionary<string, JObject>> GetTrainsetDataAsync(int id)
        {
            var cars = await Updater.RunOnMainThread(() =>
            {
                var trainset = Trainset.allSets.Find(set => set.id == id);
                if (trainset == null)
                    return new Dictionary<string, CarData>();
                return trainset.cars
                    .Where(ShouldReturnTrainCar)
                    .ToDictionary(car => car.ID, car => From(car));
            }).ConfigureAwait(false);
            return cars.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson());
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

using DV.LocoRestoration;
using DV.RemoteControls;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class CarUpdater
    {
        [HarmonyPatch(typeof(DV.Logic.Job.Car), "LoadCargo")]
        public static class CargoLoadedPatch
        {
            public static void Postfix(DV.Logic.Job.Car __instance) => CarData.MarkCargoChanged(__instance);
        }
        [HarmonyPatch(typeof(DV.Logic.Job.Car), "UnloadCargo")]
        public static class CargoUnloadedPatch
        {
            public static void Postfix(DV.Logic.Job.Car __instance) => CarData.MarkCargoChanged(__instance);
        }
        public static void ForceCarRefresh()
        {
            if (!Sessions.HasActiveSessions()) return;
            Sessions.AddTag("cars");
        }

        public static void MarkCarAsDirty(TrainCar car)
        {
            if (!Sessions.HasActiveSessions()) return;
            Sessions.AddTag($"carguid-{car.CarGUID}");
        }

        [HarmonyPatch(typeof(RemoteControllerModule), nameof(RemoteControllerModule.Init))]
        public static class RemoteControllerModuleInitPatch
        {
            public static void Postfix(RemoteControllerModule __instance)
            {
                var trainCar = TrainCar.Resolve(__instance.gameObject);
                var overrider = __instance.controlsOverrider;
                var controls = new OverridableBaseControl[]
                {
                    overrider.Brake,
                    overrider.IndependentBrake,
                    overrider.Reverser,
                    overrider.Throttle,
                };

                foreach (var control in controls.Where(c => c != null))
                {
                    control.ControlUpdated += _ => MarkCarAsDirty(trainCar);
                }
            }
        }

        public static void MarkTrainsetAsDirty(Trainset trainset, bool activeSession = false)
        {
            if (!activeSession && !Sessions.HasActiveSessions()) return;
            if (trainset.cars.Find(CarData.ShouldReturnTrainCar) != null)
                Sessions.AddTag($"trainset-{trainset.id}");
        }

        public static void Start()
        {
            CarSpawner carSpawner = SingletonBehaviour<CarSpawner>.Instance;
            if (carSpawner == null)
            {
                Main.DebugLog(() => $"Tried to start {nameof(CarUpdater)} before {nameof(CarSpawner)} was initialized!");
                return;
            }

            carSpawner.CarSpawned += OnCarsChanged;
            carSpawner.CarAboutToBeDeleted += OnCarDeleted;

            foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
                controller.StateChanged += OnRestorationStateChanged;
        }

        public static void Stop()
        {
            CarSpawner carSpawner = SingletonBehaviour<CarSpawner>.Instance;
            if (carSpawner == null)
                return;
            carSpawner.CarSpawned -= OnCarsChanged;
            carSpawner.CarAboutToBeDeleted -= OnCarDeleted;

            foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
                controller.StateChanged -= OnRestorationStateChanged;
        }

        private static void OnCarsChanged(TrainCar trainCar)
        {
            Sessions.AddTag("cars");
        }

        private static void OnCarDeleted(TrainCar trainCar)
        {
            CarData.ForgetCargoCar(trainCar);
            OnCarsChanged(trainCar);
        }

        private static void OnRestorationStateChanged(LocoRestorationController controller, TrainCarLivery livery, LocoRestorationController.RestorationState newState)
        {
            if (Main.settings.showUndiscoveredLocomotives)
                return; // already sent to client during initialization

            if (newState == LocoRestorationController.RestorationState.S3_RerailedCars)
                OnCarsChanged(controller.loco);
        }
    }
}

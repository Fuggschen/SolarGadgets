using DV.Simulation.Cars;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace SolarGadgets.Game
{
    // Spawn order: AwakeForPooledCar, then save data is loaded, then Start.
    [HarmonyPatch(typeof(TrainCar))]
    public static class TrainCarPatches
    {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        private static void Start_Postfix(TrainCar __instance)
        {
            SolarPowerNetwork.AttachLoco(__instance, resetCharge: false);
        }

        // A pooled car keeps its components, so its battery belongs to the previous car until reset.
        [HarmonyPatch(nameof(TrainCar.AwakeForPooledCar))]
        [HarmonyPostfix]
        private static void AwakeForPooledCar_Postfix(TrainCar __instance)
        {
            SolarPowerNetwork.AttachLoco(__instance, resetCharge: true);
        }
    }

    [HarmonyPatch(typeof(SimCarStateSave))]
    public static class SimCarStateSavePatches
    {
        private const string CHARGE_KEY = "solarGadgetsBattery";

        [HarmonyPatch(nameof(SimCarStateSave.GetStateSaveData))]
        [HarmonyPostfix]
        private static void GetStateSaveData_Postfix(SimCarStateSave __instance, JObject __result)
        {
            var car = __instance.GetComponent<TrainCar>();
            var loco = car != null ? SolarPowerNetwork.GetLoco(car) : null;
            if (loco != null)
            {
                __result[CHARGE_KEY] = loco.Charge;
            }
        }

        [HarmonyPatch(nameof(SimCarStateSave.SetStateSaveData))]
        [HarmonyPostfix]
        private static void SetStateSaveData_Postfix(SimCarStateSave __instance, JObject data)
        {
            var charge = data[CHARGE_KEY];
            var car = __instance.GetComponent<TrainCar>();
            if (charge == null || car == null)
            {
                return;
            }

            var loco = SolarPowerNetwork.AttachLoco(car, resetCharge: false);
            if (loco != null)
            {
                loco.Charge = charge.Value<float>();
            }
        }
    }
}

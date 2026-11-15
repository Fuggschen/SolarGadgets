using HarmonyLib;
using LocoSim.Implementations;

namespace SolarGadgets.Game
{
    // All diesel engine sims read the starter from ignitionExtIn during Tick, so it is checked just before.
    [HarmonyPatch]
    public static class StarterPatches
    {
        [HarmonyPatch(typeof(DieselEngineDirect), nameof(DieselEngineDirect.Tick))]
        [HarmonyPrefix]
        private static void DieselEngineDirect_Tick_Prefix(DieselEngineDirect __instance)
        {
            LocoElectrics.ProcessIgnition(__instance, __instance.ignitionExtIn, __instance.engineStarterFuseRef);
        }

        [HarmonyPatch(typeof(DieselEngineDirectDrive), nameof(DieselEngineDirectDrive.Tick))]
        [HarmonyPrefix]
        private static void DieselEngineDirectDrive_Tick_Prefix(DieselEngineDirectDrive __instance)
        {
            LocoElectrics.ProcessIgnition(__instance, __instance.ignitionExtIn, __instance.engineStarterFuseRef);
        }

        [HarmonyPatch(typeof(DieselEnginePowerSource), nameof(DieselEnginePowerSource.Tick))]
        [HarmonyPrefix]
        private static void DieselEnginePowerSource_Tick_Prefix(DieselEnginePowerSource __instance)
        {
            LocoElectrics.ProcessIgnition(__instance, __instance.ignitionExtIn, __instance.engineStarterFuseRef);
        }
    }
}

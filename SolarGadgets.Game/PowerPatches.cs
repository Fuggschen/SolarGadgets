using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;

namespace SolarGadgets.Game
{
    [HarmonyPatch(typeof(TrainCarCustomization.TrainCarCustomizerBase), nameof(TrainCarCustomization.TrainCarCustomizerBase.PowerState), MethodType.Getter)]
    public static class PowerStatePatch
    {
        // Solar power only fills in when the car has no fuse, never overrides a switched-off gadget or car fuse.
        // On a diesel loco, a working fuse delivers nothing once the loco battery and every other source are empty.
        // On a caboose with the setting on, the fuse is only a switch and the solar network must supply the power.
        private static void Postfix(TrainCarCustomization.TrainCarCustomizerBase __instance, ref bool __result)
        {
            if (__result)
            {
                var car = __instance.CustomTrainCar;
                if (__instance.fuse != null && __instance.fuse.State
                    && (SolarPowerNetwork.IsLocoDead(car) || (SolarPowerNetwork.FuseNeedsSupply(car) && !SolarPowerNetwork.IsPowered(car))))
                {
                    __result = false;
                }
                return;
            }

            if (!__instance.powerSwitch || !__instance.IsSolderable || !__instance.IsSoldered || __instance.fuse != null)
            {
                return;
            }

            __result = SolarPowerNetwork.IsPowered(__instance.CustomTrainCar);
        }
    }

    // Lights, beacons and oscillating lights take their brightness from ValueOfPower.
    // Fuse-powered gadgets only dim where the fuse has no real source: diesel locos (simulated battery) and,
    // with the setting on, cabooses (solar network).
    [HarmonyPatch(typeof(GadgetSwitch), nameof(GadgetSwitch.ValueOfPower))]
    public static class BrightnessPatch
    {
        private static void Postfix(GadgetBase gadget, ref float __result)
        {
            bool onCarFuse = gadget.fuse != null && gadget.fuse.State;
            var car = gadget.CustomTrainCar;
            if (__result <= 0f || (onCarFuse && !SolarPowerNetwork.HasLoco(car) && !SolarPowerNetwork.FuseNeedsSupply(car)))
            {
                return;
            }

            __result *= SolarPowerNetwork.GetBrightness(gadget.CustomTrainCar);
        }
    }

    // Lets gadgets that require the electrics fuse mount on cars without one, so solar can power them.
    [HarmonyPatch(typeof(TrainCarCustomization.TrainCarCustomizerBase), nameof(TrainCarCustomization.TrainCarCustomizerBase.IsValidTargetSelf))]
    public static class FuseRequirementPatch
    {
        private static void Postfix(
            TrainCarCustomization.TrainCarCustomizerBase __instance,
            Customization target,
            UnityEngine.Collider hitCollider,
            ref bool __result)
        {
            if (__result
                || !__instance.requirements.electricsFuse
                || !(target is TrainCarCustomization car)
                || car.electronicsFuse != null)
            {
                return;
            }

            // Re-run the other checks with the fuse requirement off; the flag stops this postfix from recursing.
            __instance.requirements.electricsFuse = false;
            try
            {
                __result = __instance.IsValidTargetSelf(target, hitCollider);
            }
            finally
            {
                __instance.requirements.electricsFuse = true;
            }
        }
    }

    // Without a car fuse, SetFuse(null) is a no-op, so the game sends no power change when soldering finishes or is removed.
    [HarmonyPatch(typeof(TrainCarCustomization.TrainCarCustomizerBase))]
    public static class SolderingPatches
    {
        [HarmonyPatch(nameof(TrainCarCustomization.TrainCarCustomizerBase.MakeSoldered))]
        [HarmonyPostfix]
        private static void MakeSoldered_Postfix(TrainCarCustomization.TrainCarCustomizerBase __instance)
        {
            RaiseIfFuseless(__instance);
        }

        [HarmonyPatch(nameof(TrainCarCustomization.TrainCarCustomizerBase.RemoveSoldering))]
        [HarmonyPostfix]
        private static void RemoveSoldering_Postfix(TrainCarCustomization.TrainCarCustomizerBase __instance)
        {
            RaiseIfFuseless(__instance);
        }

        private static void RaiseIfFuseless(TrainCarCustomization.TrainCarCustomizerBase customizer)
        {
            if (customizer.fuse == null && customizer.CustomTrainCar != null)
            {
                customizer.RaisePowerStateChanged();
            }
        }
    }
}

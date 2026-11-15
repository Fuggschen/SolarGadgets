using System.Collections.Generic;
using DV.Customization.Gadgets;
using HarmonyLib;
using UnityEngine;

namespace SolarGadgets.Game
{
    // custom_item_mod's item proxy has no placing fields, and Awake copies them into the placing context,
    // so they are set just before. The charge meter item also gets its cloned vanilla model here.
    [HarmonyPatch(typeof(GadgetItem), nameof(GadgetItem.Awake))]
    public static class PlacementPatches
    {
        private static void Prefix(GadgetItem __instance)
        {
            if (__instance.gadgetPrefab is GadgetChargeMeter meterPrefab)
            {
                VanillaLedBar.SetUpGadgetPrefab(meterPrefab);
                VanillaLedBar.SetUpItem(__instance);
            }

            if (__instance.gadgetPrefab is GadgetBattery)
            {
                // The surface normal must point along the battery's +Y, so only its bottom can touch it.
                __instance.onlyPlaceInOneAxis = true;
                __instance.placingAxis = Vector3.up;
            }
        }

        // The held item has its own copy of the battery model. Its LEDs follow the item's gadget, which keeps
        // the battery's charge while the item is off the car.
        private static void Postfix(GadgetItem __instance)
        {
            if (__instance.Gadget is GadgetBattery gadget && gadget.battery != null)
            {
                BatteryIndicator.Attach(__instance.gameObject, gadget.battery);
            }
        }
    }

    // The game's one-axis filter for mounts assumes the gadget's Z is its contact axis, so with a Y placing axis
    // it keeps rotations spinning around the battery's Z. Instead, generate all rotations and keep the ones
    // with the bottom (-Y) pointing into the mount (+Z of the attachment option), i.e. spinning around Y.
    [HarmonyPatch(typeof(FlatSurfaceAttachmentOption), nameof(FlatSurfaceAttachmentOption.GetPossibleAttachmentPositions))]
    public static class MountRotationPatches
    {
        private static void Prefix(GadgetBase gadget, List<AttachmentPosition> destination, out int __state)
        {
            __state = -1;
            if (gadget is GadgetBattery && gadget.GadgetItem != null && gadget.GadgetItem.onlyPlaceInOneAxis)
            {
                __state = destination.Count;
                gadget.GadgetItem.onlyPlaceInOneAxis = false;
            }
        }

        private static void Postfix(FlatSurfaceAttachmentOption __instance, GadgetBase gadget, List<AttachmentPosition> destination, int __state)
        {
            if (__state < 0)
            {
                return;
            }
            gadget.GadgetItem.onlyPlaceInOneAxis = true;

            // Stored rotations include the option's offset from the mount root; undo it to compare in option space.
            Quaternion optionToRoot = Quaternion.Inverse(__instance.Owner.transform.rotation) * __instance.transform.rotation;
            Quaternion rootToOption = Quaternion.Inverse(optionToRoot);
            for (int i = destination.Count - 1; i >= __state; i--)
            {
                Vector3 up = rootToOption * destination[i].rotation * Vector3.up;
                if (Vector3.Dot(up, Vector3.back) < 0.99f)
                {
                    destination.RemoveAt(i);
                }
            }
        }
    }
}

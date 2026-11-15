using System.Reflection;
using HarmonyLib;
using SolarGadgets.Unity;
using UnityEngine;

namespace SolarGadgets.Game
{
    // custom_item_mod builds the shelf sample in CustomItem.AddShelfSample as a plain Instantiate of the bundle
    // item, before any gadget patching, so the charge meter's empty stub needs the vanilla model added here.
    [HarmonyPatch]
    public static class ShelfPatches
    {
        private static MethodBase? TargetMethod()
        {
            var type = AccessTools.TypeByName("custom_item_mod.CustomItem") ?? AccessTools.TypeByName("CustomItem");
            return type != null ? AccessTools.Method(type, "AddShelfSample") : null;
        }

        // Skips the patch instead of failing the mod load if custom_item_mod changes.
        private static bool Prepare()
        {
            if (TargetMethod() != null)
            {
                return true;
            }
            Main.Warning("custom_item_mod's AddShelfSample not found, the charge meter shelf stays empty");
            return false;
        }

        // Arguments by position: shelfObject, itemInfo, providedItemPrefab, providedShelfPrefab.
        private static void Postfix(GameObject __0, GameObject __2)
        {
            var itemProxy = __2 != null ? __2.GetComponent<custom_item_components.GadgetItem>() : null;
            if (itemProxy == null || !(itemProxy.gadget is GadgetChargeMeterProxy))
            {
                return;
            }

            // The sample is the child that AddShelfSample just added.
            var shelf = __0.transform;
            if (shelf.childCount > 0)
            {
                VanillaLedBar.SetUpShelfSample(shelf.GetChild(shelf.childCount - 1).gameObject);
            }
        }
    }
}

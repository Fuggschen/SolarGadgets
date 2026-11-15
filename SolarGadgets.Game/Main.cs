using System.Reflection;
using custom_item_mod;
using DV.Customization;
using HarmonyLib;
using SolarGadgets.Unity;
using UnityEngine;
using UnityModManagerNet;

namespace SolarGadgets.Game
{
    public static class Main
    {
        private static UnityModManager.ModEntry Instance { get; set; } = null!;

        public static Settings Settings { get; private set; } = null!;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Instance = modEntry;

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnGUI = entry => Settings.Draw(entry);
            modEntry.OnSaveGUI = entry => Settings.Save(entry);
            DebugOverlay.Create();

            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            RegisterSolarPanelGadget();
            RegisterBatteryGadget();
            RegisterChargeMeterGadget();
            RegisterBoostConverterGadget();

            Log("SolarGadgets mod loaded successfully!");

            return true;
        }

        private static void RegisterSolarPanelGadget()
        {
            try
            {
                CustomGadgetBaseMap.RegisterGadgetImplementation(
                    typeof(GadgetSolarPanelProxy),
                    typeof(GadgetSolarPanel),
                    (custom_item_components.GadgetBase source, ref DV.Customization.Gadgets.GadgetBase target) =>
                    {
                        if (source is GadgetSolarPanelProxy proxy && target is GadgetSolarPanel replacement)
                        {
                            CopySolarPanelProxyFields(proxy, replacement);
                        }
                    }
                );
            }
            catch (System.Exception ex)
            {
                Error($"Failed to register solar panel gadget: {ex}");
            }
        }

        private static void RegisterBatteryGadget()
        {
            try
            {
                CustomGadgetBaseMap.RegisterGadgetImplementation(
                    typeof(GadgetBatteryProxy),
                    typeof(GadgetBattery),
                    (custom_item_components.GadgetBase source, ref DV.Customization.Gadgets.GadgetBase target) =>
                    {
                        if (source is GadgetBatteryProxy proxy && target is GadgetBattery replacement)
                        {
                            CopyBatteryProxyFields(proxy, replacement);
                        }
                    }
                );
            }
            catch (System.Exception ex)
            {
                Error($"Failed to register battery gadget: {ex}");
            }
        }

        private static void RegisterChargeMeterGadget()
        {
            try
            {
                CustomGadgetBaseMap.RegisterGadgetImplementation(
                    typeof(GadgetChargeMeterProxy),
                    typeof(GadgetChargeMeter),
                    (custom_item_components.GadgetBase source, ref DV.Customization.Gadgets.GadgetBase target) =>
                    {
                        if (target is GadgetChargeMeter replacement)
                        {
                            RequireSolderedOnTrainCar(replacement);
                            VanillaLedBar.SetUpGadgetPrefab(replacement);
                        }
                    }
                );
            }
            catch (System.Exception ex)
            {
                Error($"Failed to register charge meter gadget: {ex}");
            }
        }

        private static void RegisterBoostConverterGadget()
        {
            try
            {
                CustomGadgetBaseMap.RegisterGadgetImplementation(
                    typeof(GadgetBoostConverterProxy),
                    typeof(GadgetBoostConverter),
                    (custom_item_components.GadgetBase source, ref DV.Customization.Gadgets.GadgetBase target) =>
                    {
                        if (source is GadgetBoostConverterProxy proxy && target is GadgetBoostConverter replacement)
                        {
                            RequireSolderedOnTrainCar(replacement);
                            replacement.ratedAmps = proxy.ratedAmps;
                            replacement.inputVolts = proxy.inputVolts;
                            replacement.efficiency = proxy.efficiency;
                        }
                    }
                );
            }
            catch (System.Exception ex)
            {
                Error($"Failed to register boost converter gadget: {ex}");
            }
        }

        private static void RequireSolderedOnTrainCar(DV.Customization.Gadgets.GadgetBase gadget)
        {
            gadget.requirements.trainCarPresence =
                TrainCarCustomization.TrainCarCustomizerBase.CustomizerTrainCarRequirements.RequireTrainCar;
            gadget.requireSoldering = true;
        }

        private static void CopyBatteryProxyFields(GadgetBatteryProxy proxy, GadgetBattery replacement)
        {
            RequireSolderedOnTrainCar(replacement);

            var ledBar = replacement.GetComponent<HiddenLedBar>();
            if (ledBar == null)
            {
                ledBar = replacement.gameObject.AddComponent<HiddenLedBar>();
            }
            // With 0 LEDs, near-zero rounding lights 1 LED and the FILLING animation loops forever without yielding.
            ledBar.ledsCount = 1;

            var battery = replacement.GetComponent<Battery>();
            if (battery == null)
            {
                battery = replacement.gameObject.AddComponent<Battery>();
            }
            battery.ledBarDriver = ledBar;
            BatteryIndicator.Attach(replacement.gameObject, battery);

            replacement.battery = battery;
            replacement.capacityWh = proxy.capacityWh;
        }

        private static void CopySolarPanelProxyFields(GadgetSolarPanelProxy proxy, GadgetSolarPanel replacement)
        {
            RequireSolderedOnTrainCar(replacement);

            var solarPanel = replacement.GetComponent<SolarPanel>();
            if (solarPanel == null)
            {
                solarPanel = replacement.gameObject.AddComponent<SolarPanel>();
            }

            // SolarPanel measures sunlight along solarPanelTransform.up, so point a child's up along the chosen axis.
            var surface = new GameObject("SolarSurface").transform;
            surface.SetParent(replacement.transform, false);
            surface.localRotation = GetRotationForAxis(proxy.upAxis);

            solarPanel.solarPanelTransform = surface;
            solarPanel.updateInterval = proxy.updateInterval;
            solarPanel.useCurve = proxy.useCurve;
            if (proxy.timeToIntensityCurve != null)
            {
                solarPanel.timeToIntensityCurve = proxy.timeToIntensityCurve;
            }

            replacement.solarPanel = solarPanel;
            replacement.peakWatts = proxy.peakWatts;
        }

        private static Quaternion GetRotationForAxis(SolarPanelAxis axis)
        {
            return axis switch
            {
                SolarPanelAxis.PositiveX => Quaternion.Euler(0, 0, -90),
                SolarPanelAxis.NegativeX => Quaternion.Euler(0, 0, 90),
                SolarPanelAxis.PositiveY => Quaternion.identity,
                SolarPanelAxis.NegativeY => Quaternion.Euler(180, 0, 0),
                SolarPanelAxis.PositiveZ => Quaternion.Euler(90, 0, 0),
                SolarPanelAxis.NegativeZ => Quaternion.Euler(-90, 0, 0),
                _ => Quaternion.identity
            };
        }

        public static void Log(string message)
        {
            Instance.Logger.Log(message);
        }

        public static void Warning(string message)
        {
            Instance.Logger.Warning(message);
        }

        public static void Error(string message)
        {
            Instance.Logger.Error(message);
        }
    }
}

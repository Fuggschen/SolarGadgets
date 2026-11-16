using System.Collections.Generic;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Real-world power values. Power is in W, energy in Wh of game time.
    /// </summary>
    public static class PowerSpec
    {
        public const float LIGHT_W = 10f;
        public const float OSCILLATING_LIGHT_W = 15f;
        public const float BEACON_W = 25f;
        public const float DISPLAY_W = 3f;
        public const float CONTROLLER_W = 5f;
        public const float DEFAULT_GADGET_W = 5f;

        // Diesel loco: 64 V 450 Ah lead-acid and the loads it feeds with the engine off.
        public const float DIESEL_BATTERY_WH = 64f * 450f;
        public const float DIESEL_BASE_W = 150f;
        public const float DIESEL_HEADLIGHT_END_W = 250f;
        public const float DIESEL_CAB_LIGHTS_W = 60f;
        public const float DIESEL_DASH_LIGHT_W = 20f;

        // A running engine's alternator output. It covers the loco's own loads and the gadgets first; with only the
        // loco's own loads the rest refills the battery in about 7 hours.
        public const float ALTERNATOR_W = 5000f;

        // A steam loco's dynamo at full flow charges the external batteries with a tenth of the alternator output.
        public const float DYNAMO_SURPLUS_W = ALTERNATOR_W / 10f;

        // Most the locos on one MU cable move between each other to even out their charge.
        public const float MU_EQUALIZE_W = 2000f;

        // The sim Battery consumes 4 * power[W] * dt[s] / 1e6 container units, so one unit is 250 kJ.
        public const float WH_PER_SIM_UNIT = 1e6f / 4f / 3600f;

        // Gadgets that only show something have one of these LOD displays.
        private static readonly HashSet<string> DisplayLods = new HashSet<string>
        {
            "GadgetDigitalClockLOD",
            "GadgetDigitalSpeedometerLOD",
            "GadgetBrakeLEDBarLOD",
            "GadgetBrakesThermometerLOD",
            "GadgetProximityScreenLOD",
            "GadgetDefectDetectorLOD",
        };

        public static float WattsFor(GadgetBase gadget)
        {
            switch (gadget)
            {
                case GadgetOscillatingLight _:
                    return OSCILLATING_LIGHT_W;
                case GadgetLight _:
                    return LIGHT_W;
                case GadgetBeacon _:
                    return BEACON_W;
                case GadgetDPU _:
                case GadgetATS _:
                case GadgetAmpLimiter _:
                case GadgetAntiSlip _:
                case GadgetOverheatProtection _:
                case GadgetRoadrunner _:
                case GadgetControlPanel _:
                    return CONTROLLER_W;
            }

            foreach (var lod in gadget.LODObjects)
            {
                if (lod != null && DisplayLods.Contains(lod.GetType().Name))
                {
                    return DISPLAY_W;
                }
            }
            return DEFAULT_GADGET_W;
        }
    }
}

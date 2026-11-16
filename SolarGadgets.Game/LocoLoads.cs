using DV.Simulation.Cars;

namespace SolarGadgets.Game
{
    /// <summary>
    /// The loco's own consumers a loco battery feeds besides gadgets, in W, split so other mods can list them.
    /// </summary>
    public struct LocoLoads
    {
        private const string DASH_LAMP_STATE_PORT = "dashLamp.LAMP_STATE";

        public float Electronics;
        public float Headlights;
        public float CabLights;
        public float DashLight;

        public float Total => Electronics + Headlights + CabLights + DashLight;

        // Ported from dv-batterydischargesystem BatteryManager.CountActiveConsumers, without gadgets.
        // Also used by battery-electric locos, whose sim draws nothing from its traction battery for these.
        public static LocoLoads Read(SimController sim)
        {
            var loads = new LocoLoads { Electronics = PowerSpec.DIESEL_BASE_W };

            // The controller's current setup per end already folds in the switch notch, a blown fuse and broken
            // lamps, so this works for any switch layout, including the BE2's single 4-notch switch.
            // An end without setups has no lights; its current setup is only the struct default.
            var headlights = sim.headlightsController;
            if (headlights != null)
            {
                if (headlights.headlightSetupsFront.Length > 0 && !headlights.currentSetupFront.mainOffSetup)
                {
                    loads.Headlights += PowerSpec.DIESEL_HEADLIGHT_END_W;
                }
                if (headlights.headlightSetupsRear.Length > 0 && !headlights.currentSetupRear.mainOffSetup)
                {
                    loads.Headlights += PowerSpec.DIESEL_HEADLIGHT_END_W;
                }
            }

            // isOn is the game's own state: switch at or above the cab light step, fuse on, lamps intact.
            var cabLights = sim.cabLightsController;
            if (cabLights != null && cabLights.isOn)
            {
                loads.CabLights = PowerSpec.DIESEL_CAB_LIGHTS_W;
            }

            // The vanilla diesels light their gauges through this lamp, whatever switch feeds it. 0 is off, higher
            // values are on or blinking. Locos without it, like the BE2, light their gauges with the cab lights.
            // Looked up directly: TryGetPort logs an error for every loco without the port.
            if (sim.simFlow != null && sim.simFlow.fullPortIdToPort.TryGetValue(DASH_LAMP_STATE_PORT, out var dashLamp) && dashLamp.Value > 0f)
            {
                loads.DashLight = PowerSpec.DIESEL_DASH_LIGHT_W;
            }

            return loads;
        }
    }
}

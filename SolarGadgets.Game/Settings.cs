using UnityModManagerNet;

namespace SolarGadgets.Game
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Simulate diesel loco batteries (drain, dimming, failed starts)")]
        public bool locoBatterySimulation = true;

        [Draw("Gadgets on cabooses and other non-loco cars need solar or battery power")]
        public bool nonLocoFuseNeedsPower = true;

        [Draw("Consumption rate multiplier", Min = 0.1, Max = 10, Precision = 2)]
        public float consumptionMultiplier = 1f;

        [Draw("Charging rate multiplier", Min = 0.1, Max = 10, Precision = 2)]
        public float chargingMultiplier = 1f;

        [Draw("Battery capacity multiplier", Min = 0.1, Max = 10, Precision = 2)]
        public float capacityMultiplier = 1f;

        [Draw("Show debug overlay")]
        public bool debugOverlay;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void OnChange()
        {
        }
    }
}

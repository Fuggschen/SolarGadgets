using DV.Customization.Gadgets;
using Newtonsoft.Json.Linq;

namespace SolarGadgets.Game
{
    public class GadgetBattery : GadgetBase
    {
        public Battery battery = null!;
        public float capacityWh = 840f;

        public override void Awake()
        {
            base.Awake();
            if (battery != null)
            {
                battery.Initialize();
            }
        }

        public override void OnAfterLinked()
        {
            base.OnAfterLinked();
            // Awake can run before custom_item_mod attaches the Battery.
            if (!battery.initialized)
            {
                battery.Initialize();
            }
            if (CustomTrainCar != null)
            {
                SolarPowerNetwork.For(CustomTrainCar).Register(this);
            }
        }

        public override void OnBeforeUnlinked()
        {
            if (CustomTrainCar != null)
            {
                SolarPowerNetwork.For(CustomTrainCar).Unregister(this);
            }
            base.OnBeforeUnlinked();
        }

        public override void SaveDataRequested(JObject dst)
        {
            battery.SaveState(dst);
            base.SaveDataRequested(dst);
        }

        public override void SaveDataLoaded(JObject src)
        {
            base.SaveDataLoaded(src);
            battery.LoadSavedState(src);
        }
    }
}

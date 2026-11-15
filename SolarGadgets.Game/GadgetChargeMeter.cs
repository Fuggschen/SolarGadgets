using DV.Customization.Gadgets;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Shows the combined charge of all batteries on its car. Runs on a "coin cell", so it ignores car power
    /// and only needs to be soldered.
    /// </summary>
    public class GadgetChargeMeter : GadgetBase
    {
        public override void Awake()
        {
            base.Awake();
            VanillaLedBar.SetUpGadgetInstance(this);
        }

        public override void OnAfterLinked()
        {
            base.OnAfterLinked();
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
    }
}

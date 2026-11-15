using DV.Customization.Gadgets;

namespace SolarGadgets.Game
{
    /// <summary>
    /// DC-DC boost converter from the 12 V side (solar panels, external batteries) to the loco battery.
    /// Without one, solar and external batteries cannot charge a loco battery.
    /// </summary>
    public class GadgetBoostConverter : GadgetBase
    {
        public float ratedAmps = 140f;
        public float inputVolts = 12f;
        public float efficiency = 0.92f;

        public float MaxInputWatts => ratedAmps * inputVolts;

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

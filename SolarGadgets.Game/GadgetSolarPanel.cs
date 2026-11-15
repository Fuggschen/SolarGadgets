using DV.Customization.Gadgets;
using UnityEngine;

namespace SolarGadgets.Game
{
    public class GadgetSolarPanel : GadgetBase
    {
        public SolarPanel solarPanel = null!;
        public float peakWatts = 150f;

        public override void OnAfterLinked()
        {
            base.OnAfterLinked();
            // The shade raycast would otherwise hit the panel's own collider, like FlashlightItem avoids.
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                solarPanel.IgnoreSunBlocking(collider);
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
    }
}

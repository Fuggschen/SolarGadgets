using custom_item_components;
using UnityEngine;

namespace SolarGadgets.Unity
{
    public enum SolarPanelAxis
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }

    public class GadgetSolarPanelProxy : GadgetBase
    {
        [Tooltip("Which local axis points up/away from the solar panel surface")]
        public SolarPanelAxis upAxis = SolarPanelAxis.PositiveY;

        [Tooltip("How often to update power generation (in seconds)")]
        public float updateInterval = 0.1f;

        [Tooltip("Output in full sun, in watts")]
        public float peakWatts = 150f;

        [Tooltip("Use animation curve for time-based intensity")]
        public bool useCurve = true;

        [Tooltip("Animation curve mapping time of day to light intensity")]
        public AnimationCurve timeToIntensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
}

using custom_item_components;
using UnityEngine;

namespace SolarGadgets.Unity
{
    public class GadgetBoostConverterProxy : GadgetBase
    {
        [Tooltip("Maximum current drawn on the low-voltage side, from solar surplus and the external batteries")]
        public float ratedAmps = 140f;

        [Tooltip("Voltage of the low-voltage side: the solar panels and external batteries")]
        public float inputVolts = 12f;

        [Tooltip("Share of the input power that reaches the loco battery")]
        public float efficiency = 0.92f;
    }
}

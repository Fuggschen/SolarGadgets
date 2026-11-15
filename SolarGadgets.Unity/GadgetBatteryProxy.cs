using custom_item_components;
using UnityEngine;

namespace SolarGadgets.Unity
{
    public class GadgetBatteryProxy : GadgetBase
    {
        [Tooltip("Stored energy in watt-hours: 840 is a 12 V 70 Ah lead-acid battery")]
        public float capacityWh = 840f;
    }
}

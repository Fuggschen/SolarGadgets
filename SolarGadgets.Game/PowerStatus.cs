using System.Collections.Generic;

namespace SolarGadgets.Game
{
    /// <summary>
    /// One car's power network at one moment, from <see cref="SolarPowerNetwork.GetStatus"/>; a steam loco and its
    /// coupled tender report the same network. Other mods
    /// (dv-RAT) read it through reflection, so the names of these classes and fields must stay.
    /// Power is in W, energy in Wh of game time, charge in percent.
    /// </summary>
    public class PowerStatus
    {
        /// <summary>Solar panels plus the alternator of a running engine or a running steam dynamo.</summary>
        public float Supply;

        public float Demand;

        /// <summary>Capacity-weighted charge of the loco battery and the soldered batteries; null without any.</summary>
        public float? Charge;

        /// <summary>Capacity of the batteries counted in <see cref="Charge"/>.</summary>
        public float Capacity;

        /// <summary>
        /// What flows into the batteries (+) or out of them (-): supply minus demand, or 0 when no battery can
        /// take or give it. The loco battery only takes charge through a boost converter or a running alternator, and
        /// only gives it while the loco's electrics are on.
        /// </summary>
        public float Flow;

        /// <summary>Energy the batteries that take part can still give.</summary>
        public float Stored;

        /// <summary>Energy the batteries that take part can still take.</summary>
        public float Room;

        public List<BatteryStatus> Batteries = new List<BatteryStatus>();

        /// <summary>What the network supplies right now; gadgets that are off or on another supply are left out.</summary>
        public List<ConsumerStatus> Consumers = new List<ConsumerStatus>();

        public class BatteryStatus
        {
            public string Name = "";

            /// <summary>The loco's own battery rather than a battery gadget.</summary>
            public bool Internal;

            /// <summary>A battery gadget that is not soldered is not part of the network.</summary>
            public bool Soldered;

            public float Charge;
            public float Capacity;
        }

        public class ConsumerStatus
        {
            public string Name = "";
            public float Draw;
        }
    }
}

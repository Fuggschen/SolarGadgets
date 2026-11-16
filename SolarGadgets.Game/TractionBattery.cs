using LocoSim.Implementations;
using LocoSim.Resources;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// The BE2's real traction battery: solar trickles into its charge container and gadgets on its fuse draw from it.
    /// The game saves the container and pops the loco's fuse when it runs empty.
    /// </summary>
    public class TractionBattery : IInternalBattery
    {
        // The JNR DE10 mod runs its own electrics on this port and counts its battery in Ah.
        private const string DE10_PORT = "de10ChargeRequested.EXT_IN";

        // The DE10 mod's battery voltage, for a sim Battery that reports no usable range.
        private const float FALLBACK_VOLTS = 24f;

        private readonly ResourceContainer container;
        private readonly Fuse fuse;

        // Wh in one container unit: the game's sim units, or Ah times volts where a mod counts in Ah.
        private readonly float whPerUnit;

        // Container units this mod wrote since the last sample, so they can be told apart from the game's own.
        private double ownUnits;
        private float sampledAmount;
        private float sampledTime = -1f;
        private float gameWatts;

        public TrainCar Car { get; }

        public string Kind => "Traction battery";

        public bool Present => true;

        public float Charge => container.normalizedReadOutPort.Value * 100f;

        // A real battery, so the capacity setting does not apply.
        public float CapacityWh => container.capacity * whPerUnit;

        public bool IsEmpty => container.amountReadOut.Value <= 0f;

        public bool ElectricsOn => fuse.State;

        public bool EngineOn => false;

        public bool HasStarter => false;

        // Measured from the charge itself rather than the sim Battery's power port, so it also covers mods that
        // refill the container directly, like the DE10's generator.
        public float SampleGameWatts()
        {
            float amount = container.amountReadOut.Value;
            float now = Time.time;
            if (now <= sampledTime)
            {
                return gameWatts;
            }
            if (sampledTime >= 0f)
            {
                double gameUnits = amount - sampledAmount - ownUnits;
                gameWatts = (float)(-gameUnits * whPerUnit / ((now - sampledTime) / 3600.0));
            }
            sampledAmount = amount;
            sampledTime = now;
            ownUnits = 0.0;
            return gameWatts;
        }

        private TractionBattery(TrainCar car, ResourceContainer container, Fuse fuse, float whPerUnit)
        {
            Car = car;
            this.container = container;
            this.fuse = fuse;
            this.whPerUnit = whPerUnit;
        }

        // Battery-electric locos: a sim Battery fed by an electric charge container.
        public static TractionBattery? TryCreate(TrainCar car)
        {
            var sim = car.SimController;
            var fuse = car.Customization?.electronicsFuse;
            if (sim == null || sim.simFlow == null || fuse == null)
            {
                return null;
            }

            LocoSim.Implementations.Battery? battery = null;
            ResourceContainer? charge = null;
            foreach (var component in sim.simFlow.OrderedSimComps)
            {
                if (component is LocoSim.Implementations.Battery simBattery)
                {
                    battery = simBattery;
                }
                else if (component is ResourceContainer container && container.resourceType == ResourceContainerType.ELECTRIC_CHARGE)
                {
                    charge = container;
                }
            }
            if (battery == null || charge == null)
            {
                return null;
            }
            float whPerUnit = sim.simFlow.TryGetPort(DE10_PORT, out _) ? NominalVolts(battery) : PowerSpec.WH_PER_SIM_UNIT;
            return new TractionBattery(car, charge, fuse, whPerUnit);
        }

        // The middle of the sim Battery's voltage range, which follows from its chemistry and cell count.
        private static float NominalVolts(LocoSim.Implementations.Battery battery)
        {
            float volts = (battery.minVoltage + battery.maxVoltage) / 2f;
            return volts > 0f && !float.IsInfinity(volts) ? volts : FALLBACK_VOLTS;
        }

        public LocoLoads Loads()
        {
            return LocoLoads.Read(Car.SimController);
        }

        // Writing to the external ports makes the container apply the amount and reset the port to 0. Adding to the
        // port keeps a second write in the same tick from replacing the first.
        public double Add(double wattHours)
        {
            float units = Mathf.Min((float)(wattHours / whPerUnit), container.capacity - container.amountReadOut.Value);
            if (units <= 0f)
            {
                return 0.0;
            }
            container.refillExtIn.Value += units;
            ownUnits += units;
            return units * whPerUnit;
        }

        public double Take(double wattHours)
        {
            float units = Mathf.Min((float)(wattHours / whPerUnit), container.amountReadOut.Value);
            if (units <= 0f)
            {
                return 0.0;
            }
            container.consumeExtIn.Value += units;
            ownUnits -= units;
            return units * whPerUnit;
        }

        public void ApplyBrightness(float brightness)
        {
        }
    }
}

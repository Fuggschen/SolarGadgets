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
        private readonly ResourceContainer container;
        private readonly LocoSim.Implementations.Battery simBattery;
        private readonly Fuse fuse;

        public TrainCar Car { get; }

        public string Kind => "Traction battery";

        public bool Present => true;

        public float Charge => container.normalizedReadOutPort.Value * 100f;

        // A real battery, so the capacity setting does not apply.
        public float CapacityWh => container.capacity * PowerSpec.WH_PER_SIM_UNIT;

        public bool IsEmpty => container.amountReadOut.Value <= 0f;

        public bool ElectricsOn => fuse.State;

        public bool EngineOn => false;

        public bool HasStarter => false;

        // The sim Battery drains its charge from this port, which sums traction motors and every other loco system.
        public float GameWatts => simBattery.powerReader.Value;

        private TractionBattery(TrainCar car, LocoSim.Implementations.Battery simBattery, ResourceContainer container, Fuse fuse)
        {
            Car = car;
            this.simBattery = simBattery;
            this.container = container;
            this.fuse = fuse;
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
            return battery != null && charge != null ? new TractionBattery(car, battery, charge, fuse) : null;
        }

        public LocoLoads Loads()
        {
            return LocoLoads.Read(Car.SimController);
        }

        // Writing to the external ports makes the container apply the amount and reset the port to 0.
        public double Add(double wattHours)
        {
            float units = Mathf.Min((float)(wattHours / PowerSpec.WH_PER_SIM_UNIT), container.capacity - container.amountReadOut.Value);
            if (units <= 0f)
            {
                return 0.0;
            }
            container.refillExtIn.Value = units;
            return units * PowerSpec.WH_PER_SIM_UNIT;
        }

        public double Take(double wattHours)
        {
            float units = Mathf.Min((float)(wattHours / PowerSpec.WH_PER_SIM_UNIT), container.amountReadOut.Value);
            if (units <= 0f)
            {
                return 0.0;
            }
            container.consumeExtIn.Value = units;
            return units * PowerSpec.WH_PER_SIM_UNIT;
        }

        public void ApplyBrightness(float brightness)
        {
        }
    }
}

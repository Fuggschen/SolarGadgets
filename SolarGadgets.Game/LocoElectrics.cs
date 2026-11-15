using System.Runtime.CompilerServices;
using DV.Simulation.Cars;
using LocoSim.Implementations;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Simulated internal battery of a diesel loco: drains with electrics on and the engine off,
    /// charges while the engine runs, and can refuse to start the engine when low.
    /// </summary>
    public class LocoElectrics : IInternalBattery
    {
        // Gameplay cost per start; a real crank would cost well under 1 % of the battery.
        public const float STARTER_COST_MIN = 5f;
        public const float STARTER_COST_MAX = 10f;

        // Below this charge a start can fail, with certainty at 0%.
        public const float START_FAIL_CHARGE = 50f;

        private static readonly ConditionalWeakTable<SimComponent, LocoElectrics> ByEngine =
            new ConditionalWeakTable<SimComponent, LocoElectrics>();

        private readonly SolarPowerNetwork network;
        private readonly SimController sim;
        private readonly Fuse fuse;
        private readonly LocoLightDimmer? dimmer;

        // The log showed re-sends roughly every 0.3-0.7 s while held, so keep a margin above that.
        private const float PRESS_GAP_SECONDS = 1f;

        private float lastStarterInput = float.NegativeInfinity;
        private bool blockPress;
        private bool dimmed;

        public TrainCar Car { get; }

        public string Kind => "Loco battery";

        // The battery stays attached (and saved) when the setting is off; it is just ignored.
        public bool Present => Main.Settings.locoBatterySimulation;

        public float Charge { get; set; }

        public float CapacityWh => PowerSpec.DIESEL_BATTERY_WH * Main.Settings.capacityMultiplier;

        // Partial drains leave float residue, so treat anything below 0.01% as empty.
        public bool IsEmpty => Charge < 0.01f;

        public bool ElectricsOn => fuse.State;

        public bool EngineOn => sim.controlsOverrider?.EngineOnReader?.IsOn ?? false;

        public bool HasStarter => true;

        // The game does not simulate this battery, so its sim draws nothing from it.
        public float SampleGameWatts() => 0f;

        public double Add(double wattHours)
        {
            double stored = System.Math.Min(wattHours, (100.0 - Charge) / 100.0 * CapacityWh);
            Charge = Mathf.Clamp(Charge + (float)(stored * 100.0 / CapacityWh), 0f, 100f);
            return stored;
        }

        public double Take(double wattHours)
        {
            double taken = System.Math.Min(wattHours, Charge / 100.0 * CapacityWh);
            Charge = Mathf.Clamp(Charge - (float)(taken * 100.0 / CapacityWh), 0f, 100f);
            return taken;
        }

        private LocoElectrics(TrainCar car, SolarPowerNetwork network, SimController sim, Fuse fuse, SimComponent engine)
        {
            Car = car;
            this.network = network;
            this.sim = sim;
            this.fuse = fuse;
            dimmer = LocoLightDimmer.Create(sim);
            ResetCharge();
            ByEngine.Remove(engine);
            ByEngine.Add(engine, this);
        }

        public static bool IsEligible(TrainCar car)
        {
            return FindEngine(car) != null;
        }

        public static LocoElectrics? TryCreate(TrainCar car, SolarPowerNetwork network)
        {
            var engine = FindEngine(car);
            return engine == null
                ? null
                : new LocoElectrics(car, network, car.SimController, car.Customization.electronicsFuse, engine);
        }

        private static SimComponent? FindEngine(TrainCar car)
        {
            var sim = car.SimController;
            if (sim == null || sim.simFlow == null || car.Customization?.electronicsFuse == null)
            {
                return null;
            }
            foreach (var component in sim.simFlow.OrderedSimComps)
            {
                if (component is DieselEngineDirect || component is DieselEngineDirectDrive || component is DieselEnginePowerSource)
                {
                    return component;
                }
            }
            return null;
        }

        // New locos start nearly full, like dv-batterydischargesystem.
        public void ResetCharge()
        {
            Charge = Random.Range(90f, 100f);
            lastStarterInput = float.NegativeInfinity;
            blockPress = false;
            if (dimmed)
            {
                ApplyBrightness(1f);
            }
        }

        // Lights are left alone until the battery first dims them, so idle locos keep the game's own settings.
        public void ApplyBrightness(float brightness)
        {
            if (dimmer == null || (!dimmed && brightness >= 1f))
            {
                return;
            }
            dimmer.Apply(brightness);
            dimmed = brightness < 1f;
        }

        public string FuseId => fuse.id;

        public LocoLoads Loads()
        {
            return LocoLoads.Read(sim);
        }

        // Called before each engine sim tick. Zeroing a failed press hides the release, and the control
        // re-sends its value while held, so inputs closer together than PRESS_GAP_SECONDS are one press.
        public static void ProcessIgnition(SimComponent engine, Port ignition, FuseReference starterFuse)
        {
            if (!Main.Settings.locoBatterySimulation || !ByEngine.TryGetValue(engine, out var loco))
            {
                return;
            }

            if (ignition.Value <= 0f || !starterFuse.State || loco.EngineOn)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now - loco.lastStarterInput > PRESS_GAP_SECONDS)
            {
                loco.blockPress = !loco.network.TryStart(loco);
            }
            loco.lastStarterInput = now;

            if (loco.blockPress)
            {
                ignition.Value = 0f;
            }
        }
    }
}

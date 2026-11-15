namespace SolarGadgets.Game
{
    /// <summary>
    /// A loco's own battery as the solar network uses it: the simulated diesel battery or the BE2's traction battery.
    /// </summary>
    public interface IInternalBattery
    {
        TrainCar Car { get; }

        // Name for the log, overlay and other mods.
        string Kind { get; }

        // Whether the network uses it at all; the diesel battery has a setting.
        bool Present { get; }

        float Charge { get; }

        float CapacityWh { get; }

        bool IsEmpty { get; }

        bool ElectricsOn { get; }

        bool EngineOn { get; }

        // Diesels can fail to start; the BE2 has no engine.
        bool HasStarter { get; }

        // The loco's own consumers this battery feeds, besides gadgets.
        LocoLoads Loads();

        // What the loco's own sim takes from this battery, in W; the game drains it, so the network only reports it.
        float GameWatts { get; }

        // Returns the Wh actually stored or taken.
        double Add(double wattHours);

        double Take(double wattHours);

        void ApplyBrightness(float brightness);
    }
}

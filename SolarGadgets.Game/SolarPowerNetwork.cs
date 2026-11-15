using System;
using System.Collections.Generic;
using System.Text;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.ThingTypes;
using DV.Utils;
using DV.WeatherSystem;
using LocoSim.Implementations;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Per-car power from soldered solar panels, external batteries and the loco's own battery
    /// (simulated on diesels, the real traction battery on the BE2). A steam loco's network also runs the tender
    /// coupled behind it. Power is in W, energy in Wh of game time.
    /// </summary>
    public class SolarPowerNetwork : MonoBehaviour
    {
        private const float TICK_INTERVAL = 0.5f;

        // Same daylight window and minimum output as SolarPanel.
        private const float DAY_START_TIME = 0.25f;
        private const float DAY_END_TIME = 0.83f;
        private const float MINIMUM_LIGHT_INTENSITY = 0.2f;

        // On battery, gadgets run at full brightness down to this charge and fade linearly to 0%.
        private const float DIMMING_START_CHARGE = 75f;
        private const float BRIGHTNESS_STEP = 0.01f;

        // Ticks spanning more in-game time than this are time skips (sleeping, time menu).
        private const double SKIP_THRESHOLD_HOURS = 0.25;
        private const double SKIP_SAMPLE_HOURS = 0.1;

        // The gadgets soldered to this car.
        private readonly List<GadgetSolarPanel> ownPanels = new List<GadgetSolarPanel>();
        private readonly List<GadgetBattery> ownBatteries = new List<GadgetBattery>();
        private readonly List<GadgetChargeMeter> ownMeters = new List<GadgetChargeMeter>();
        private readonly List<GadgetBoostConverter> ownConverters = new List<GadgetBoostConverter>();

        // The gadgets this network runs: its own plus those of a tender it leads, refreshed every tick.
        private readonly List<GadgetSolarPanel> panels = new List<GadgetSolarPanel>();
        private readonly List<GadgetBattery> batteries = new List<GadgetBattery>();
        private readonly List<GadgetChargeMeter> meters = new List<GadgetChargeMeter>();
        private readonly List<GadgetBoostConverter> converters = new List<GadgetBoostConverter>();
        private readonly List<GadgetWiringModule.WireLinkPort> linksBuffer = new List<GadgetWiringModule.WireLinkPort>();

        private TrainCarCustomization customization = null!;

        // A steam loco and the tender coupled behind it run as one network, led by the loco's.
        private SolarPowerNetwork? tender;
        private SolarPowerNetwork? leader;
        private IInternalBattery? loco;
        private float tickTimer;
        private DateTime? previousTime;
        private float previousRealTime;

        // Real seconds per game second over the last tick. The loco's own sim drains in real time while the network
        // works in game time, so its draw is scaled by this to compare. 0 until measured.
        private double realPerGameTime;
        private bool isDay;
        private bool locoActive;
        private float production;
        private float supply;
        private float demand;
        // The two parts of demand: the loco's own loads run on the loco battery only, gadgets on the 12 V side.
        private float locoLoad;
        private float gadgetDemand;
        // What the loco's own sim and other mods take from its battery (traction motors etc.); they drain it
        // themselves. Negative while regenerative braking or a modded generator charges the battery.
        private float gameDraw;

        // Last tick's boost converter power: drawn on the 12 V side and delivered into the loco battery.
        private float converterInputW;
        private float converterOutputW;

        // Last tick's diesel alternator output, 0 while the engine is off.
        private float alternatorW;

        // Last tick's steam dynamo power into the external batteries.
        private float dynamoW;

        public bool Powered { get; private set; }

        public float Brightness { get; private set; } = 1f;

        // Loco electrics are on but neither the engine, the panels nor any battery can supply them.
        public bool LocoDead { get; private set; }

        public static SolarPowerNetwork For(TrainCarCustomization customization)
        {
            if (!customization.TryGetComponent(out SolarPowerNetwork network))
            {
                network = customization.gameObject.AddComponent<SolarPowerNetwork>();
            }
            return network;
        }

        // The network that powers the car: a coupled tender answers with its loco's.
        private static SolarPowerNetwork? Resolve(TrainCarCustomization? customization)
        {
            return customization != null && customization.TryGetComponent(out SolarPowerNetwork network) ? network.Lead : null;
        }

        private SolarPowerNetwork Lead => leader != null && leader.tender == this ? leader : this;

        private bool IsFollower => Lead != this;

        // Battery-electric locos (the BE2 and modded ones) get their real traction battery, diesels the simulated one.
        // The traction check runs first so a loco with both a traction battery and an engine keeps its real battery.
        // Returns the diesel one for saving.
        public static LocoElectrics? AttachLoco(TrainCar car, bool resetCharge)
        {
            if (car.Customization == null)
            {
                return null;
            }

            var traction = TractionBattery.TryCreate(car);
            if (traction != null)
            {
                var network = For(car.Customization);
                if (!(network.loco is TractionBattery) || network.loco.Car != car)
                {
                    network.loco = traction;
                }
                return null;
            }

            if (LocoElectrics.IsEligible(car))
            {
                var network = For(car.Customization);
                if (!(network.loco is LocoElectrics diesel) || diesel.Car != car)
                {
                    diesel = LocoElectrics.TryCreate(car, network)!;
                    network.loco = diesel;
                }
                else if (resetCharge)
                {
                    diesel.ResetCharge();
                }
                return diesel;
            }
            return null;
        }

        public static SolarPowerNetwork? Get(TrainCarCustomization? customization)
        {
            return Resolve(customization);
        }

        public static LocoElectrics? GetLoco(TrainCar car)
        {
            return car.Customization != null && car.Customization.TryGetComponent(out SolarPowerNetwork network)
                ? network.loco as LocoElectrics
                : null;
        }

        public static bool HasLoco(TrainCarCustomization? customization)
        {
            var network = Resolve(customization);
            return network != null && network.LocoSimulated;
        }

        private bool LocoSimulated => loco != null && loco.Present;

        public bool HasContent => !IsFollower && (panels.Count > 0 || batteries.Count > 0 || meters.Count > 0 || converters.Count > 0 || LocoSimulated);

        // Capacity-weighted charge of the simulated loco battery and all soldered batteries; null without any.
        public float? CombinedCharge
        {
            get
            {
                double energy = 0.0;
                double capacity = 0.0;
                if (LocoSimulated)
                {
                    energy += loco!.Charge / 100.0 * loco.CapacityWh;
                    capacity += loco.CapacityWh;
                }
                foreach (var battery in batteries)
                {
                    if (battery.IsSoldered)
                    {
                        double batteryCapacity = BatteryCapacity(battery);
                        energy += battery.battery.CurrentPower / 100.0 * batteryCapacity;
                        capacity += batteryCapacity;
                    }
                }
                return capacity > 0.0 ? (float)(energy / capacity * 100.0) : (float?)null;
            }
        }

        // Into the batteries (+) or out of them (-), including what the loco's own sim draws. Follows RouteEnergy:
        // solar only reaches the gadgets, the external batteries and the converters, so loco loads always come out
        // of the loco battery unless the alternator runs, and the converters lose part of what they move.
        private float NetWatts
        {
            get
            {
                // Negative when the alternator is off or too weak: the loco battery covers the rest of its loads.
                float alternatorAfterLoco = alternatorW - locoLoad;
                float alternatorSurplus = Mathf.Max(alternatorAfterLoco, 0f) - gadgetDemand;
                float solar = production + Mathf.Min(alternatorSurplus, 0f);
                if (solar > 0f && FindEmptiestBattery() == null && !CanChargeLocoFromSolar)
                {
                    solar = 0f;
                }
                float loco = Mathf.Max(alternatorSurplus, 0f) + Mathf.Min(alternatorAfterLoco, 0f);
                float dynamo = FindEmptiestBattery() != null ? dynamoW : 0f;
                return solar + loco + dynamo - (converterInputW - converterOutputW) - GameDrawInGameTime;
            }
        }

        // The loco sim's draw as the game-time W that drain the battery as fast; at about 12 game seconds per real
        // second, 8 kW of traction motors count as about 680 W.
        private float GameDrawInGameTime => (float)(gameDraw * realPerGameTime);

        // +1 while the batteries gain charge, -1 while they lose it, 0 when balanced, full or empty.
        public int ChargeTrend
        {
            get
            {
                float? charge = CombinedCharge;
                float net = NetWatts;
                if (!charge.HasValue)
                {
                    return 0;
                }
                if (net > 0.01f && charge.Value < 99.9f)
                {
                    return 1;
                }
                return net < -0.01f && charge.Value > 0.01f ? -1 : 0;
            }
        }

        public static bool IsPowered(TrainCarCustomization? customization)
        {
            var network = Resolve(customization);
            return network != null && network.Powered;
        }

        public static bool IsLocoDead(TrainCarCustomization? customization)
        {
            var network = Resolve(customization);
            return network != null && network.LocoDead;
        }

        // A caboose has an electrics fuse but nothing generating power, so with the setting on its fuse is only a switch.
        // A tender's fuse follows its loco's dynamo instead.
        public static bool FuseNeedsSupply(TrainCarCustomization? customization)
        {
            return Main.Settings.nonLocoFuseNeedsPower
                && customization != null
                && customization.TrainCar != null
                && !customization.TrainCar.IsLoco
                && !CarTypes.IsTender(customization.TrainCar.carLivery);
        }

        // Steam locos and tenders switch their electronics fuse with the dynamo's output, so an off fuse there only
        // means the dynamo is not running, not that the player cut the power.
        public static bool FuseFollowsDynamo(TrainCarCustomization? customization)
        {
            var livery = customization != null && customization.TrainCar != null ? customization.TrainCar.carLivery : null;
            return livery != null && (CarTypes.IsSteamLocomotive(livery) || CarTypes.IsTender(livery));
        }

        public static float GetBrightness(TrainCarCustomization? customization)
        {
            var network = Resolve(customization);
            return network != null ? network.Brightness : 1f;
        }

        private static float BatteryCapacity(GadgetBattery battery)
        {
            return battery.capacityWh * Main.Settings.capacityMultiplier;
        }

        private static readonly HashSet<SolarPowerNetwork> all = new HashSet<SolarPowerNetwork>();

        public static IReadOnlyCollection<SolarPowerNetwork> All => all;

        private void Awake()
        {
            customization = GetComponent<TrainCarCustomization>();
            all.Add(this);
        }

        private void OnDestroy()
        {
            all.Remove(this);
        }

        // logicCar is null during pool setup, and TrainCar.ID reads through it.
        public string CarLabel
        {
            get
            {
                var car = customization.TrainCar;
                return car == null ? name : car.logicCar != null ? car.ID : car.name;
            }
        }

        private string HeaderText()
        {
            string time = previousTime.HasValue ? previousTime.Value.ToString("HH:mm") : "--:--";
            return $"{(isDay ? "Day" : "Night")} {time}, powered: {Powered}, brightness: {Brightness:P0}, " +
                $"supply: {supply:F0} W / demand: {demand:F0} W" +
                (gameDraw != 0f ? $", loco sim: {GameDrawInGameTime:F0} W" : "") +
                (LocoDead ? ", loco dead" : "");
        }

        private string LocoText()
        {
            string engine = loco!.HasStarter ? (loco.EngineOn ? "engine on, " : "engine off, ") : "";
            return $"{loco.Kind}: {loco.Charge:F1}% of {loco.CapacityWh / 1000f:F1} kWh, {engine}" +
                (loco.ElectricsOn ? "electrics on" : "electrics off");
        }

        // Instantaneous state for the debug overlay; the net value is the charge (+) or drain (-) in W.
        public void AppendStatus(StringBuilder text)
        {
            float net = NetWatts;
            text.Append(CarLabel).Append("  ").Append(HeaderText()).Append($", net {net:+0;-0;0} W").AppendLine();

            float? combined = CombinedCharge;
            if (combined.HasValue)
            {
                string trend = ChargeTrend > 0 ? "charging" : ChargeTrend < 0 ? "discharging" : "steady";
                text.AppendLine($"    Combined charge: {combined.Value:F1}%, {trend}{(meters.Count > 0 ? $", {meters.Count} meter(s)" : "")}");
            }

            if (LocoSimulated)
            {
                text.Append("    ").AppendLine(LocoText());
            }

            if (converters.Count > 0)
            {
                text.Append("    ").AppendLine(ConverterText());
            }

            bool hasTarget = CanChargeLocoFromSolar || FindEmptiestBattery() != null;
            foreach (var panel in panels)
            {
                text.Append("    Solar panel: ").AppendLine(DescribePanel(panel, hasTarget));
            }

            foreach (var battery in batteries)
            {
                text.Append("    Battery: ")
                    .Append(battery.IsSoldered ? $"{battery.battery.CurrentPower:F1}%" : "not soldered")
                    .AppendLine(battery.battery.Depleted ? " (depleted)" : "");
            }

            foreach (var customizer in AllCustomizers())
            {
                if (IsPotentialConsumer(customizer, out var gadget) && gadget.GetComponent<Mount>() == null)
                {
                    string? reason = ExclusionReason(gadget, ConsumerFuseFilter());
                    text.Append("    Gadget ").Append(gadget.name.Replace("(Clone)", "")).Append($" ({PowerSpec.WattsFor(gadget):F0} W): ")
                        .AppendLine(reason == null ? "counted" : $"not counted, {reason}");
                }
            }
        }

        /// <summary>The network as it is now, for other mods; see <see cref="PowerStatus"/>.</summary>
        public PowerStatus GetStatus()
        {
            if (IsFollower)
            {
                return Lead.GetStatus();
            }
            float consumption = Main.Settings.consumptionMultiplier;
            var status = new PowerStatus
            {
                Supply = supply + Mathf.Max(-GameDrawInGameTime, 0f),
                Demand = demand + Mathf.Max(GameDrawInGameTime, 0f),
                Charge = CombinedCharge,
            };
            if (LocoSimulated)
            {
                status.Batteries.Add(new PowerStatus.BatteryStatus
                {
                    Name = loco!.Kind,
                    Internal = true,
                    Soldered = true,
                    Charge = loco.Charge,
                    Capacity = loco.CapacityWh,
                });
                status.Capacity += loco.CapacityWh;
                // Only a boost converter, a running alternator or the game itself (regenerative braking, a modded
                // generator) charges the loco battery; DrawEnergy only takes from it while its electrics are on.
                if (ConverterBudgetWatts() > 0f || AlternatorRunning || GameDrawInGameTime < 0f)
                {
                    status.Room += (100f - loco.Charge) / 100f * loco.CapacityWh;
                }
                if (locoActive)
                {
                    status.Stored += loco.Charge / 100f * loco.CapacityWh;
                }
            }
            foreach (var battery in batteries)
            {
                float capacity = BatteryCapacity(battery);
                status.Batteries.Add(new PowerStatus.BatteryStatus
                {
                    Name = GadgetName(battery),
                    Soldered = battery.IsSoldered,
                    Charge = battery.battery.CurrentPower,
                    Capacity = capacity,
                });
                if (battery.IsSoldered)
                {
                    status.Capacity += capacity;
                    AddEnergy(status, battery.battery.CurrentPower, capacity);
                }
            }
            float balance = NetWatts;
            status.Flow = balance > 0f ? (status.Room > 0.001f ? balance : 0f) : (status.Stored > 0.001f ? balance : 0f);

            if (locoActive)
            {
                var loads = loco!.Loads();
                AddConsumer(status, "Loco electronics", loads.Electronics * consumption);
                AddConsumer(status, "Headlights", loads.Headlights * consumption);
                AddConsumer(status, "Cab lights", loads.CabLights * consumption);
                AddConsumer(status, "Dash lights", loads.DashLight * consumption);
            }
            AddConsumer(status, "Traction and loco systems", GameDrawInGameTime);
            // Moves energy between batteries rather than using it, so it is listed but not part of Demand.
            AddConsumer(status, "Boost converter (to loco battery)", converterInputW);
            bool? filter = ConsumerFuseFilter();
            foreach (var customizer in AllCustomizers())
            {
                if (IsPotentialConsumer(customizer, out var gadget) && ExclusionReason(gadget, filter) == null)
                {
                    status.Consumers.Add(new PowerStatus.ConsumerStatus { Name = GadgetName(gadget), Draw = PowerSpec.WattsFor(gadget) * consumption });
                }
            }
            return status;
        }

        private static void AddConsumer(PowerStatus status, string name, float draw)
        {
            if (draw > 0f)
            {
                status.Consumers.Add(new PowerStatus.ConsumerStatus { Name = name, Draw = draw });
            }
        }

        private static void AddEnergy(PowerStatus status, float charge, float capacity)
        {
            status.Stored += charge / 100f * capacity;
            status.Room += (100f - charge) / 100f * capacity;
        }

        // The name of the gadget's item in the game's language, else the prefab's name.
        private static string GadgetName(GadgetBase gadget)
        {
            var spec = gadget.GadgetItem != null ? gadget.GadgetItem.GetComponent<InventoryItemSpec>() : null;
            return spec != null ? spec.LocalizedName : gadget.name.Replace("(Clone)", "");
        }

        public void Register(GadgetSolarPanel panel)
        {
            if (!ownPanels.Contains(panel))
            {
                ownPanels.Add(panel);
            }
            Tick();
        }

        public void Unregister(GadgetSolarPanel panel)
        {
            ownPanels.Remove(panel);
            Tick();
        }

        public void Register(GadgetBattery battery)
        {
            if (!ownBatteries.Contains(battery))
            {
                ownBatteries.Add(battery);
            }
            Tick();
        }

        public void Register(GadgetChargeMeter meter)
        {
            if (!ownMeters.Contains(meter))
            {
                ownMeters.Add(meter);
            }
        }

        public void Unregister(GadgetChargeMeter meter)
        {
            ownMeters.Remove(meter);
        }

        public void Register(GadgetBoostConverter converter)
        {
            if (!ownConverters.Contains(converter))
            {
                ownConverters.Add(converter);
            }
            Tick();
        }

        public void Unregister(GadgetBoostConverter converter)
        {
            ownConverters.Remove(converter);
            Tick();
        }

        public void Unregister(GadgetBattery battery)
        {
            ownBatteries.Remove(battery);
            Tick();
        }

        private void Update()
        {
            tickTimer += Time.deltaTime;
            if (tickTimer < TICK_INTERVAL)
            {
                return;
            }
            tickTimer = 0f;
            Tick();
        }

        // Links a steam loco with the tender coupled at its rear, the way the game finds it, from either side.
        private void UpdateTenderLink()
        {
            var car = customization.TrainCar;
            if (car == null)
            {
                return;
            }
            if (CarTypes.IsTender(car.carLivery))
            {
                var locoCar = CoupledSteamLoco(car);
                leader = locoCar != null && locoCar.Customization != null ? For(locoCar.Customization) : null;
                if (leader != null)
                {
                    leader.tender = this;
                }
                return;
            }
            var tenderCar = CoupledTender(car);
            tender = tenderCar != null && tenderCar.Customization != null ? For(tenderCar.Customization) : null;
            if (tender != null)
            {
                tender.leader = this;
            }
        }

        private static TrainCar? CoupledTender(TrainCar loco)
        {
            if (!CarTypes.IsSteamLocomotive(loco.carLivery))
            {
                return null;
            }
            var other = loco.rearCoupler.coupledTo != null ? loco.rearCoupler.coupledTo.train : null;
            return other != null && CarTypes.IsTender(other.carLivery) ? other : null;
        }

        private static TrainCar? CoupledSteamLoco(TrainCar tender)
        {
            foreach (var coupler in tender.couplers)
            {
                var other = coupler.coupledTo != null ? coupler.coupledTo.train : null;
                if (other != null && CoupledTender(other) == tender)
                {
                    return other;
                }
            }
            return null;
        }

        private void GatherGadgets()
        {
            var follower = tender != null && tender.leader == this ? tender : null;
            Gather(panels, ownPanels, follower?.ownPanels);
            Gather(batteries, ownBatteries, follower?.ownBatteries);
            Gather(meters, ownMeters, follower?.ownMeters);
            Gather(converters, ownConverters, follower?.ownConverters);
        }

        private static void Gather<T>(List<T> into, List<T> own, List<T>? other)
        {
            into.Clear();
            into.AddRange(own);
            if (other != null)
            {
                into.AddRange(other);
            }
        }

        private IEnumerable<Customization.CustomizerBase> AllCustomizers()
        {
            foreach (var customizer in customization.Customizers)
            {
                yield return customizer;
            }
            if (tender != null && tender.leader == this)
            {
                foreach (var customizer in tender.customization.Customizers)
                {
                    yield return customizer;
                }
            }
        }

        // Also polls soldering of panels and batteries, which joins or leaves them from the network.
        private void Tick()
        {
            UpdateTenderLink();
            if (IsFollower)
            {
                // The leader credits the time in between, so this network starts measuring afresh once uncoupled.
                previousTime = null;
                Lead.Tick();
                return;
            }
            GatherGadgets();

            var weather = SingletonBehaviour<WeatherDriver>.Instance;
            if (weather != null && weather.manager != null)
            {
                DateTime now = weather.manager.DateTime;
                isDay = IsDayTime(now);
                locoActive = LocoSimulated && loco!.ElectricsOn;
                if (loco != null && !LocoSimulated)
                {
                    // Turning the setting off mid-game must not leave the lights dimmed.
                    loco.ApplyBrightness(1f);
                }

                float consumption = Main.Settings.consumptionMultiplier;
                production = isDay ? CurrentProduction() : 0f;
                locoLoad = (locoActive ? loco!.Loads().Total : 0f) * consumption;
                gadgetDemand = ConsumerWatts(ConsumerFuseFilter()) * consumption;
                demand = locoLoad + gadgetDemand;
                alternatorW = AlternatorRunning ? PowerSpec.ALTERNATOR_W * Main.Settings.chargingMultiplier : 0f;
                dynamoW = DynamoFlow() * PowerSpec.DYNAMO_SURPLUS_W * Main.Settings.chargingMultiplier;
                supply = production + alternatorW + dynamoW;
                gameDraw = LocoSimulated ? loco!.SampleGameWatts() : 0f;

                // A backwards jump means a load or a time reset; start measuring again from here.
                if (previousTime.HasValue && now > previousTime.Value)
                {
                    DateTime from = previousTime.Value;
                    double hours = (now - from).TotalHours;
                    realPerGameTime = (Time.time - previousRealTime) / 3600.0 / hours;
                    double produced = hours > SKIP_THRESHOLD_HOURS
                        ? SkippedProduction(from, now)
                        : production * DayHoursBetween(from, now);
                    RouteEnergy(produced, hours);
                    if (dynamoW > 0f)
                    {
                        ShareExternal(dynamoW * hours, charging: true);
                    }
                }
                else
                {
                    // Nothing moves while the game clock stands still, so the last tick's converter power would be stale.
                    converterInputW = 0f;
                    converterOutputW = 0f;
                }
                previousTime = now;
                previousRealTime = Time.time;
            }

            SetPowerState(ComputePowered(), ComputeBrightness(), ComputeLocoDead());
        }

        private static bool IsDayTime(DateTime time)
        {
            double timeOfDay = time.TimeOfDay.TotalDays;
            return timeOfDay >= DAY_START_TIME && timeOfDay <= DAY_END_TIME;
        }

        private static double DayHoursBetween(DateTime from, DateTime to)
        {
            return (DaylightDaysUntil(to) - DaylightDaysUntil(from)) * 24.0;
        }

        // Daylight days elapsed from DateTime.MinValue up to the given time.
        private static double DaylightDaysUntil(DateTime time)
        {
            double fullDays = Math.Floor(time.Ticks / (double)TimeSpan.TicksPerDay);
            double timeOfDay = time.TimeOfDay.TotalDays;
            double today = Math.Min(Math.Max(timeOfDay - DAY_START_TIME, 0.0), DAY_END_TIME - DAY_START_TIME);
            return fullDays * (DAY_END_TIME - DAY_START_TIME) + today;
        }

        // Solar only reaches the gadgets directly, so loco loads are only covered by a running alternator. What the
        // alternator has left after them goes to the gadgets together with solar.
        private bool SupplyCoversDemand => (alternatorW > 0f || production > 0f)
            && alternatorW >= locoLoad
            && alternatorW - locoLoad + production >= gadgetDemand;

        // Without a charged battery the panels must cover the whole demand, otherwise everything browns out.
        // A loco battery counts while its electrics are on.
        private bool ComputePowered()
        {
            return SupplyCoversDemand || FirstChargedBattery() != null || (locoActive && !loco!.IsEmpty);
        }

        // Brightness follows the capacity-weighted charge of every battery that can cover the deficit, so it fades
        // once over all of them instead of once per battery as they empty one after another.
        private float ComputeBrightness()
        {
            if (SupplyCoversDemand || demand <= 0f)
            {
                return 1f;
            }

            double energy = 0.0;
            double capacity = 0.0;
            foreach (var battery in batteries)
            {
                if (battery.IsSoldered)
                {
                    double batteryCapacity = BatteryCapacity(battery);
                    energy += battery.battery.CurrentPower / 100.0 * batteryCapacity;
                    capacity += batteryCapacity;
                }
            }
            if (locoActive)
            {
                energy += loco!.Charge / 100.0 * loco.CapacityWh;
                capacity += loco.CapacityWh;
            }

            if (capacity <= 0.0)
            {
                return batteries.Count == 0 ? 1f : 0f;
            }
            return Mathf.Clamp01((float)(energy / capacity * 100.0) / DIMMING_START_CHARGE);
        }

        private bool ComputeLocoDead()
        {
            // On the BE2 the game pops the fuse itself when the traction battery runs empty.
            return locoActive
                && loco!.HasStarter
                && !loco.EngineOn
                && loco.IsEmpty
                && converterOutputW < locoLoad;
        }

        private GadgetBattery? FirstChargedBattery()
        {
            foreach (var battery in batteries)
            {
                if (battery.IsSoldered && !battery.battery.Depleted)
                {
                    return battery;
                }
            }
            return null;
        }

        private GadgetBattery? FindEmptiestBattery()
        {
            GadgetBattery? emptiest = null;
            foreach (var battery in batteries)
            {
                if (battery.IsSoldered && (emptiest == null || battery.battery.CurrentPower < emptiest.battery.CurrentPower))
                {
                    emptiest = battery;
                }
            }
            return emptiest;
        }

        // Panels are rated at full sun (intensity 1).
        private float CurrentProduction()
        {
            float total = 0f;
            foreach (var panel in panels)
            {
                if (panel.IsSoldered && panel.solarPanel.Initialized)
                {
                    total += panel.peakWatts * EffectiveIntensity(panel.solarPanel);
                }
            }
            return total * Main.Settings.chargingMultiplier;
        }

        private double SkippedProduction(DateTime from, DateTime to)
        {
            double total = 0.0;
            foreach (var panel in panels)
            {
                if (panel.IsSoldered && panel.solarPanel.Initialized)
                {
                    total += panel.peakWatts * CurveIntensityHours(panel.solarPanel, from, to);
                }
            }
            return total * Main.Settings.chargingMultiplier;
        }

        private bool AlternatorRunning => locoActive && loco!.EngineOn;

        // Steam locos: the dynamo's smoothed output from 0 to 1, which already drops without steam pressure.
        private float DynamoFlow()
        {
            var car = customization.TrainCar;
            var sim = car != null ? car.SimController : null;
            if (sim == null || sim.simFlow == null)
            {
                return 0f;
            }
            foreach (var component in sim.simFlow.OrderedSimComps)
            {
                if (component is Dynamo dynamo)
                {
                    return Mathf.Clamp01(dynamo.dynamoFlowNormalizedReadOut?.Value ?? 0f);
                }
            }
            return 0f;
        }

        // The loco battery only takes solar and external-battery power through a boost converter.
        private bool CanChargeLocoFromSolar => LocoSimulated && ConverterBudgetWatts() > 0f;

        // Summed input limit of the soldered boost converters, in W on the 12 V side.
        private float ConverterBudgetWatts()
        {
            float watts = 0f;
            foreach (var converter in converters)
            {
                if (converter.IsSoldered)
                {
                    watts += converter.MaxInputWatts;
                }
            }
            return watts;
        }

        // Output per input W, weighted by each soldered converter's rating.
        private float ConverterEfficiency()
        {
            float input = 0f;
            float output = 0f;
            foreach (var converter in converters)
            {
                if (converter.IsSoldered)
                {
                    input += converter.MaxInputWatts;
                    output += converter.MaxInputWatts * converter.efficiency;
                }
            }
            return input > 0f ? output / input : 0f;
        }

        // The network charges instead of SolarPanel (its battery stays null), so skipped daylight is credited too.
        // Solar and the external batteries are on the 12 V side: solar covers the gadgets, then feeds the boost
        // converters up to their rating, and charges the external batteries with the rest. The loco's own loads run
        // on the loco battery, which only the converters or a running engine's alternator charge. The alternator
        // covers the loco's own loads, then the gadgets, and its surplus fills the loco battery first and the external
        // batteries with what it cannot store. A gadget deficit draws from the external batteries first, then from
        // the loco battery while its electrics are on.
        private void RouteEnergy(double solarWattHours, double hours)
        {
            converterInputW = 0f;
            converterOutputW = 0f;

            double alternatorWattHours = alternatorW * hours;
            double locoWattHours = locoLoad * hours;
            double locoFromAlternator = Math.Min(alternatorWattHours, locoWattHours);
            if (locoWattHours > locoFromAlternator)
            {
                loco!.Take(locoWattHours - locoFromAlternator);
            }

            double alternatorSurplus = alternatorWattHours - locoFromAlternator - gadgetDemand * hours;
            if (alternatorSurplus > 0.0)
            {
                // The alternator covers all demand, so every solar Wh is surplus.
                double overflow = alternatorSurplus - loco!.Add(alternatorSurplus);
                RouteSolarSurplus(solarWattHours, hours);
                if (overflow > 0.0)
                {
                    ShareExternal(overflow, charging: true);
                }
                return;
            }

            double net = solarWattHours + alternatorSurplus;
            if (net < 0.0)
            {
                DrawEnergy(-net, includeInternal: locoActive);
                RunConverters(0.0, hours);
                return;
            }
            RouteSolarSurplus(net, hours);
        }

        private void RouteSolarSurplus(double surplusWattHours, double hours)
        {
            double surplusWatts = hours > 0.0 ? surplusWattHours / hours : 0.0;
            double solarIntoConverters = RunConverters(surplusWatts, hours);
            double leftover = surplusWattHours - solarIntoConverters * hours;
            if (leftover > 0.0)
            {
                ShareExternal(leftover, charging: true);
            }
        }

        // Feeds the loco battery through the converters, from solar surplus first and the external batteries for the
        // rest, stopping when the loco battery is full. Returns the solar W it used.
        private double RunConverters(double solarWatts, double hours)
        {
            float budget = ConverterBudgetWatts();
            float efficiency = ConverterEfficiency();
            if (!LocoSimulated || budget <= 0f || efficiency <= 0f || hours <= 0.0)
            {
                return 0.0;
            }
            double roomWattHours = (100.0 - loco!.Charge) / 100.0 * loco.CapacityWh;
            double inputWatts = Math.Min(budget, roomWattHours / hours / efficiency);
            if (inputWatts <= 0.0)
            {
                return 0.0;
            }

            double solarUsed = Math.Min(solarWatts, inputWatts);
            double fromBank = (inputWatts - solarUsed) * hours;
            double bankGave = fromBank - ShareExternal(fromBank, charging: false);
            double inputWattHours = solarUsed * hours + bankGave;
            double stored = loco.Add(inputWattHours * efficiency);

            converterInputW = (float)(inputWattHours / hours);
            converterOutputW = (float)(stored / hours);
            return solarUsed;
        }

        private string ConverterText()
        {
            int soldered = 0;
            float amps = 0f;
            foreach (var converter in converters)
            {
                if (converter.IsSoldered)
                {
                    soldered++;
                    amps += converter.ratedAmps;
                }
            }
            if (soldered == 0)
            {
                return "Boost converter: not soldered";
            }
            if (!LocoSimulated)
            {
                return $"Boost converter: {soldered} x, {amps:F0} A, no loco battery";
            }
            return converterInputW > 0f
                ? $"Boost converter: {soldered} x, {amps:F0} A, in {converterInputW:F0} W, to loco {converterOutputW:F0} W"
                : $"Boost converter: {soldered} x, {amps:F0} A, idle";
        }

        // Takes energy from the external batteries, then the internal loco battery. Returns what was not covered.
        private double DrawEnergy(double wattHours, bool includeInternal)
        {
            wattHours = ShareExternal(wattHours, charging: false);

            if (includeInternal && loco != null && wattHours > 0.0)
            {
                wattHours -= loco.Take(wattHours);
            }
            return Math.Max(wattHours, 0.0);
        }

        // Soldered external batteries act as one bank wired in parallel: charging fills the emptiest first and
        // discharging empties the fullest first until their charges meet, then they all move together.
        // Finds that common level by bisection. Returns the Wh the bank could not take or give.
        private double ShareExternal(double wattHours, bool charging)
        {
            double available = 0.0;
            foreach (var battery in batteries)
            {
                if (battery.IsSoldered)
                {
                    float charge = battery.battery.CurrentPower;
                    available += (charging ? 100.0 - charge : charge) / 100.0 * BatteryCapacity(battery);
                }
            }
            if (available <= 0.0)
            {
                return wattHours;
            }

            double moved = Math.Min(wattHours, available);
            double low = 0.0;
            double high = 100.0;
            for (int i = 0; i < 40; i++)
            {
                double level = (low + high) / 2.0;
                double atLevel = EnergyToLevel(level, charging);
                if (charging ? atLevel < moved : atLevel > moved)
                {
                    low = level;
                }
                else
                {
                    high = level;
                }
            }

            double target = (low + high) / 2.0;
            foreach (var battery in batteries)
            {
                if (!battery.IsSoldered)
                {
                    continue;
                }
                float charge = battery.battery.CurrentPower;
                if (charging && target > charge)
                {
                    battery.battery.Charge((float)(target - charge));
                }
                else if (!charging && target < charge)
                {
                    battery.battery.Drain((float)(charge - target));
                }
            }
            return wattHours - moved;
        }

        // Wh needed to bring every soldered external battery up (charging) or down to the given charge level.
        private double EnergyToLevel(double level, bool charging)
        {
            double energy = 0.0;
            foreach (var battery in batteries)
            {
                if (battery.IsSoldered)
                {
                    double delta = charging ? level - battery.battery.CurrentPower : battery.battery.CurrentPower - level;
                    energy += Math.Max(delta, 0.0) / 100.0 * BatteryCapacity(battery);
                }
            }
            return energy;
        }

        /// <summary>
        /// Pays for one starter press and rolls whether the engine turns over.
        /// The battery that pays decides the failure chance: the external bank when it holds the whole cost.
        /// </summary>
        public bool TryStart(LocoElectrics starting)
        {
            float costPercent = UnityEngine.Random.Range(LocoElectrics.STARTER_COST_MIN, LocoElectrics.STARTER_COST_MAX);
            double cost = costPercent / 100.0 * starting.CapacityWh;

            double externalStored = 0.0;
            double externalCapacity = 0.0;
            foreach (var battery in batteries)
            {
                if (battery.IsSoldered)
                {
                    double capacity = BatteryCapacity(battery);
                    externalStored += battery.battery.CurrentPower / 100.0 * capacity;
                    externalCapacity += capacity;
                }
            }
            bool externalPays = externalStored >= cost && externalStored > 0.0;
            float charge = externalPays ? (float)(externalStored / externalCapacity * 100.0) : starting.Charge;
            float failChance = !externalPays && starting.IsEmpty ? 1f : Mathf.Clamp01((LocoElectrics.START_FAIL_CHARGE - charge) / LocoElectrics.START_FAIL_CHARGE);

            DrawEnergy(cost, includeInternal: true);

            return UnityEngine.Random.value >= failChance;
        }

        // Mirrors SolarPanel.GenerateBatteryPower: output in shade or low light is floored at 0.2.
        // The game's sun light exceeds 1 at midday, but peakWatts is the panel's maximum, so cap at 1.
        private static float EffectiveIntensity(SolarPanel solar)
        {
            float sun = solar.averageLightIntensity;
            if (sun < MINIMUM_LIGHT_INTENSITY || solar.SurfaceInShade(solar.solarPanelTransform.position))
            {
                return MINIMUM_LIGHT_INTENSITY;
            }
            return Mathf.Min(sun, 1f);
        }

        // Live sun data does not exist for skipped time, so integrate the panel's time-of-day curve instead.
        private static double CurveIntensityHours(SolarPanel solar, DateTime from, DateTime to)
        {
            double total = 0.0;
            double spanHours = (to - from).TotalHours;
            for (double t = 0.0; t < spanHours; t += SKIP_SAMPLE_HOURS)
            {
                double step = Math.Min(SKIP_SAMPLE_HOURS, spanHours - t);
                DateTime sample = from.AddHours(t + step / 2.0);
                if (!IsDayTime(sample))
                {
                    continue;
                }

                float dayProgress = (float)((sample.TimeOfDay.TotalDays - DAY_START_TIME) / (DAY_END_TIME - DAY_START_TIME));
                float intensity = solar.timeToIntensityCurve != null ? solar.timeToIntensityCurve.Evaluate(dayProgress) : 0.5f;
                total += Mathf.Clamp(intensity, MINIMUM_LIGHT_INTENSITY, 1f) * step;
            }
            return total;
        }

        // Ported from dv-batterydischargesystem BatteryManager.CountActiveGadgets.
        // Uses PowerSwitch, not PowerState: PowerState depends on Powered, and counting it would oscillate.
        // Which gadgets this network supplies: all of them (null) on a loco with electrics on, since its battery feeds
        // gadgets on and off the fuse, and on a car whose fuse is only a switch; otherwise those on cars without a fuse.
        private bool? ConsumerFuseFilter()
        {
            return locoActive || FuseNeedsSupply(customization) ? (bool?)null : false;
        }

        private float ConsumerWatts(bool? onCarFuse)
        {
            float watts = 0f;
            foreach (var customizer in AllCustomizers())
            {
                if (IsPotentialConsumer(customizer, out var gadget) && ExclusionReason(gadget, onCarFuse) == null)
                {
                    watts += PowerSpec.WattsFor(gadget);
                }
            }
            return watts;
        }

        private static bool IsPotentialConsumer(Customization.CustomizerBase customizer, out GadgetBase gadget)
        {
            gadget = (customizer as GadgetBase)!;
            return gadget != null
                && !(gadget is GadgetSwitch)
                && !(gadget is GadgetSolarPanel)
                && !(gadget is GadgetBattery)
                && !(gadget is GadgetChargeMeter)
                && !(gadget is GadgetBoostConverter);
        }

        // Why a gadget is not counted as demand, or null when it is.
        private string? ExclusionReason(GadgetBase gadget, bool? onCarFuse)
        {
            if (!gadget.IsSolderable)
            {
                return "not solderable (always powered by the game)";
            }
            if (!gadget.IsSoldered)
            {
                return "not soldered";
            }
            if (!gadget.PowerSwitch)
            {
                return "switched off";
            }

            // A turned-off car fuse cuts the gadget off from every source, solar included, except where the fuse only
            // follows the dynamo: there the gadget is left to the network like on a car without a fuse.
            bool fused = gadget.fuse != null;
            if (fused && !gadget.fuse!.State)
            {
                if (!FuseFollowsDynamo(customization))
                {
                    return "car fuse off";
                }
                fused = false;
            }
            if (onCarFuse.HasValue && fused != onCarFuse.Value)
            {
                return "runs on the car fuse";
            }

            return IsSwitchedOff(gadget) ? "wired switch off" : null;
        }

        private bool IsSwitchedOff(GadgetBase gadget)
        {
            foreach (var port in gadget.WireLinkPorts)
            {
                linksBuffer.Clear();
                port.GetLinks(linksBuffer);
                foreach (var linked in linksBuffer)
                {
                    if (linked.owner is GadgetSwitch sw && sw.OutputValueOf(gadget) <= 0f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Gadgets only re-read brightness on a power change notification, so raise one when it moves.
        private void SetPowerState(bool powered, float brightness, bool locoDead)
        {
            bool brightnessChanged = Mathf.Abs(Brightness - brightness) >= BRIGHTNESS_STEP
                || (brightness != Brightness && (brightness == 1f || brightness == 0f));
            if (Powered == powered && LocoDead == locoDead && !brightnessChanged)
            {
                return;
            }
            Powered = powered;
            LocoDead = locoDead;
            if (brightnessChanged)
            {
                Brightness = brightness;
                if (LocoSimulated)
                {
                    loco!.ApplyBrightness(brightness);
                }
            }

            foreach (var customizer in AllCustomizers())
            {
                if (customizer is TrainCarCustomization.TrainCarCustomizerBase carCustomizer)
                {
                    carCustomizer.RaisePowerStateChanged();
                }
            }
        }

        private static string DescribePanel(GadgetSolarPanel panel, bool hasTarget)
        {
            var solar = panel.solarPanel;
            if (!panel.IsSoldered)
            {
                return "not soldered";
            }
            if (!solar.Initialized)
            {
                return "initializing";
            }
            if (!solar.isDay)
            {
                return "night, 0 W";
            }

            float sun = solar.averageLightIntensity;
            float effective = EffectiveIntensity(solar);
            bool shaded = effective == MINIMUM_LIGHT_INTENSITY && sun >= MINIMUM_LIGHT_INTENSITY;
            float watts = panel.peakWatts * effective * Main.Settings.chargingMultiplier;

            return $"sun {sun:F2}{(shaded ? " (shaded)" : "")}, {watts:F0} of {panel.peakWatts:F0} W, {(hasTarget ? "surplus to battery" : "no battery")}";
        }
    }
}

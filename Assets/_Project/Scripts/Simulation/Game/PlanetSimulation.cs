using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// A working stand-in for the real simulation so Presentation can be built against <see cref="IPlanetSimulation"/> now.
    /// The star's motion, panel output and previews use the real solar model, and power flows through the
    /// colony grid (<see cref="PowerGrid"/>: battery, lander RTGs, base demand). K accounting is a placeholder
    /// until the K meter (#10).
    /// </summary>
    public sealed partial class PlanetSimulation : IPlanetSimulation
    {
        const double MaxStepHours = 0.25;

        /// <summary>Placeholder farm size for one placed panel, m².</summary>
        public double PanelAreaM2 { get; set; } = 10_000.0;
        public double PanelEfficiency { get; set; } = 0.20;
        /// <summary>Energy to build 1 m² of panel, Wh (modern mono-Si ≈ 266 kWh/m²; see Economics).</summary>
        public double EmbodiedWhPerM2 { get; set; } = Economics.EmbodiedWhPerM2(PanelTechnology.ModernMonoSi);
        public double PanelLifetimeYears { get; set; } = Economics.LifetimeYears;
        /// <summary>Placeholder colony demand, W.</summary>
        public double BaseDemandW { get => Grid.DemandW; set => Grid.DemandW = value; }

        public SkyNotebook Notebook { get; } = new SkyNotebook();

        /// <summary>Which planet this is (the id in planets.json); saves remember it.</summary>
        public string PlanetId { get; set; } = "";
        /// <summary>The noise seed of this planet's map: its continents and its deposits (saved, version 28).</summary>
        public int TerrainSeed { get; set; } = DefaultTerrainSeed;   // settable so tests can vary the weather

        /// <summary>The seed a map uses when nothing else sets one.</summary>
        public const int DefaultTerrainSeed = 7;

        /// <summary>
        /// Star's effective temperature, K, for the spectral match f_spec of the panels' cells (P0 item 5).
        /// 0 means "treat as the Sun" (f_spec = 1).
        /// </summary>
        public double StarTemperatureK { get; set; }

        /// <summary>Cell material of every panel; its f_spec depends on the star's temperature.</summary>
        public CellMaterial PanelMaterial { get; set; } = CellMaterial.Silicon;

        /// <summary>Weather on the panels (P1 item 13): Earth's cloud climatology on Earth-like planets, else clear sky.</summary>
        public CloudModel Clouds { get; set; } = CloudModel.None;

        /// <summary>f_spec of the panels under this star (1 for a Sun-like star).</summary>
        public double SpectralFactor =>
            StarTemperatureK > 0.0 ? SpectralMatch.FSpec(PanelMaterial, StarTemperatureK) : 1.0;

        readonly PlanetSurface surface;
        readonly PlanetSolarParams planet;
        readonly double planetRadiusM;
        readonly double tileAreaM2;
        readonly double subStellarLongitudeAtStart;
        readonly Dictionary<(int Tile, int Slot), (double Tilt, double Azimuth, double Area)> panels = new Dictionary<(int, int), (double, double, double)>();
        readonly Dictionary<int, int> panelsPerTile = new Dictionary<int, int>();
        bool targetReached;

        /// <summary>The colony's grid: battery, RTGs and demand (tune it here). Demand comes from <see cref="Colony"/>.</summary>
        public PowerGrid Grid { get; } = PowerGrid.Lander();

        /// <summary>How the colony's demand grows with its power supply.</summary>
        public ColonyDemand Colony { get; private set; }

        /// <summary>Build parts and construction sites: what buildings cost and how spare power pays for them.</summary>
        public Construction Build { get; } = new Construction();

        /// <summary>The colony's robots and groups; they work on construction sites and salvage orders (phase 1).</summary>
        public Workforce Workforce { get; } = new Workforce();

        /// <summary>Robots charging, wearing and being repaired (phase 2).</summary>
        public RobotUpkeep Upkeep { get; } = new RobotUpkeep();

        /// <summary>Power going into robot batteries now, W.</summary>
        public double RobotChargingW { get; private set; }

        /// <summary>True around a flaring red dwarf (Proxima Cen, TRAPPIST-1): robots wear faster (design D13).</summary>
        public bool IsFlaringStar => StarTemperatureK > 0.0 && StarTemperatureK < RobotSpecs.FlaringStarBelowK;

        /// <summary>Where robots charge and are repaired: the lander, and every finished battery yard (design D10, D12).</summary>
        public List<RobotFacility> RobotFacilities()
        {
            var list = new List<RobotFacility> { new RobotFacility(BaseTileId, -1, ChargingCapability.Lander) };
            foreach (var b in batteries)
                if (!Build.IsQueued(b.Tile, b.Slot) && WiredOrOff(b))
                    list.Add(new RobotFacility(b.Tile, b.Slot, ChargingCapability.BatteryYard));
            foreach (var c in stations)
                if (!Build.IsQueued(c.Tile, c.Slot) && WiredOrOff(c) && StationEnabled(c))
                    list.Add(new RobotFacility(c.Tile, c.Slot, ChargingCapability.Station));
            return list;
        }

        /// <summary>Finished buildings the player asked a robot to take down, in the order asked.</summary>
        readonly List<(int Tile, int Slot)> salvageOrders = new List<(int, int)>();
        readonly List<(int Tile, int Slot, double Tilt, double Azimuth)> adjustOrders = new List<(int, int, double, double)>();

        readonly HashSet<(int Tile, int Slot)> batteries = new HashSet<(int, int)>();
        readonly HashSet<(int Tile, int Slot)> workshops = new HashSet<(int, int)>();
        readonly HashSet<(int Tile, int Slot)> stations = new HashSet<(int, int)>();
        readonly HashSet<(int Tile, int Slot)> relays = new HashSet<(int, int)>();
        public int BaseTileId { get; }
        public double TimeHours { get; private set; }
        public Vector3 StarDirection { get; private set; }
        public bool IsDayAtBase { get; private set; }
        public double GenerationW { get; private set; }
        public double DemandW { get; private set; }
        public double RtgW { get; private set; }
        public double BatteryPowerW { get; private set; }
        public double CurtailedW { get; private set; }
        public double KardashevPowerW { get; private set; }
        public double KardashevK => Kardashev.K(KardashevPowerW);
        public double PlanetK => Kardashev.PlanetK(KardashevPowerW, TypeITargetW);
        public double TypeITargetW { get; }

        public TileInfo GetTile(int tileId) =>
            new TileInfo(tileId, Sphere.Latitude(tileId), Sphere.Longitude(tileId),
                         surface.Terrain[tileId], surface.Elevation[tileId], tileAreaM2);

        public void Tick(double hours)
        {
            if (hours <= 0.0)
                return;
            while (hours > 0.0)
            {
                double dt = Math.Min(hours, MaxStepHours);
                Step(dt);
                hours -= dt;
            }
        }

        /// <summary>True inside <see cref="Step"/>: a building finished there starts counting from the next step.</summary>
        bool stepping;

        void Step(double dt)
        {
            stepping = true;
            try { StepCore(dt); }
            finally { stepping = false; }
        }

        void StepCore(double dt)
        {
            ApplyDebugOptions();
            TimeHours += dt;
            UpdateStar();

            // Robots at the lander charge from its RTGs first (D10); elsewhere, robots below 20 % charge right after the
            // colony and the rest top up from spare power (D6).
            StepCircuits(dt);   // a circuit in a charging station decides whether the station charges
            Movement moving = CurrentMovement();
            Workforce.Movement = moving;
            Upkeep.Plan(Workforce, RobotFacilities(), dt, moving);
            // The network hub and relays draw as a priority load: they stay up until the colony itself browns out.
            Grid.Battery.UsableShare = BatteryUsableShare;   // a margin until the surface temperature is known
            GridStep grid = Grid.Step(ComputeGeneration(), dt, TimeHours / Rtg.HoursPerYear,
                                      Upkeep.CriticalRequestW + Network.PowerW + ComputingLoadW() + WorkshopPowerW + CircuitLoadW, Upkeep.DockRequestW);
            GenerationW = grid.GenerationW;
            RtgW = grid.RtgW;
            DemandW = grid.DemandW;
            BatteryPowerW = grid.ChargeW - grid.DischargeW;
            // Spare power (battery full or at its limit) tops up robots; the rest is curtailed. Buildings are made from
            // materials, not power (director, 2026-11-14).
            double topUpW = Math.Min(grid.CurtailedW, Upkeep.TopUpRequestW);
            CurtailedW = grid.CurtailedW - topUpW;
            // The network's hub and relays are served first from the priority power; robots' critical charging gets the rest.
            double networkServedW = Math.Min(Network.PowerW, grid.PriorityServedW);
            double meanAnomaly = CurrentMeanAnomaly();
            var shelter = ShelterTiles();
            Upkeep.Apply(Workforce, dt, grid.DockServedW, grid.PriorityServedW - networkServedW, topUpW, IsFlaringStar, moving,
                         Materials, LiveAirTemperatureAt(BaseTileId), LiveAirTemperatureAt, shelter.Contains);
            HandleRobotWeather();
            RobotChargingW = Upkeep.ChargingW;
            Network.SetPowered(!Grid.IsBrownout);
            Workforce.UpdateConnections(dt);
            DeliverHeldFindings();
            haulLedger.RecordEnergy(Upkeep.HaulDriveWh);   // what driving with cargo aboard cost
            Upkeep.HaulDriveWh = 0.0;
            AdvanceFieldWork(dt);
            EvaluateRules();   // step 7a: rules evaluate (mission operations 5.3)
            PlanHauls();
            Settle(null);
            // Used power: what reached the loads (the Workshop's included) or went into the battery; curtailed power is not used.
            // What the priority loads took beyond the network and the robots' critical charging is the Workshop's, the refineries'
            // and the colony computing's: power used all the same (found by the 60-day energy-closure runs, mission operations P5).
            double industrialServedW = Math.Max(0.0, grid.PriorityServedW - networkServedW - Upkeep.CriticalRequestW);
            KardashevPowerW = grid.ServedW + grid.ChargeW + RobotChargingW + networkServedW + industrialServedW;
            // Milestones are judged on a whole day's average, so a sunny noon doesn't count.
            dayEnergyWh += KardashevPowerW * dt;
            dayHours += dt;
            if (dayHours >= MilestoneDayHours - 1e-9)
            {
                DayAveragePowerW = dayEnergyWh / dayHours;
                dayEnergyWh = 0.0;
                dayHours = 0.0;
                CheckMilestones();
            }
            Colony.Step(dt, grid.GenerationW, grid.ServedW, grid.IsBrownout);
            Grid.DemandW = Colony.DemandW;
            double soc = Grid.Battery.CapacityWh > 0.0 ? Grid.Battery.StoredWh / Grid.Battery.CapacityWh : 0.0;
            History.Add(dt, TimeHours, grid.GenerationW, grid.DemandW, grid.ServedW, WorkshopPowerW, soc, grid.IsBrownout);

            bool day = SunAt(BaseTileId).Up > 0.0;
            if (day != IsDayAtBase)
            {
                IsDayAtBase = day;
                DayNightChanged?.Invoke(day);
            }
        }

        /// <summary>Used power averaged over the last full day (see <see cref="MilestoneDayHours"/>), W.</summary>
        public double DayAveragePowerW { get; private set; }

        /// <summary>Highest milestone reached so far (0–10; 10 is this planet's Type I target).</summary>
        public int MilestonesReached { get; private set; }

        void CheckMilestones()
        {
            int now = Kardashev.MilestoneFor(Kardashev.PlanetK(DayAveragePowerW, TypeITargetW));
            while (MilestonesReached < now)
            {
                MilestonesReached++;
                // Technology (D8): milestones raise how many robots a group can hold.
                Workforce.GroupCapacity = Math.Max(Workforce.GroupCapacity, RobotTech.GroupCapacity(MilestonesReached));
                MilestoneReached?.Invoke(MilestonesReached);
            }
            if (!targetReached && DayAveragePowerW >= TypeITargetW)
            {
                targetReached = true;
                TargetReached?.Invoke();
            }
        }
        public PowerHistory History { get; }

        Double3 SunAt(int tileId) => PlanetFrame.ToLocal(StarDirection, Sphere.Latitude(tileId), Sphere.Longitude(tileId));

        int PickBaseTile()
        {
            // Flat open ground (lowland, desert or a dry basin; tundra only if there is nothing else) closest to
            // 20° N, longitude 0: a sunny start, and under the star on a tidally locked planet.
            Vector3 goal = PlanetFrame.FromLatLon(20.0 * Math.PI / 180.0, 0.0);
            int best = 0, bestRank = 0;
            float bestDot = float.NegativeInfinity;
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                int rank = Terrains.BaseSiteRank(surface.Terrain[t]);
                if (rank == 0 || rank < bestRank)
                    continue;
                float d = Vector3.Dot(Sphere.Centers[t], goal);
                if (rank > bestRank || d > bestDot) { bestRank = rank; bestDot = d; best = t; }
            }
            return best;
        }
    }
}

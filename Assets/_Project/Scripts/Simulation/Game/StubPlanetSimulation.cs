using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// A working stand-in for the real simulation so Presentation can be built against <see cref="IPlanetSimulation"/> now.
    /// The star's motion, panel output and previews use the real solar model, and power flows through the
    /// colony grid (<see cref="PowerGrid"/>: battery, lander RTGs, base demand). K accounting is a placeholder
    /// until the K meter (#10).
    /// </summary>
    public sealed class StubPlanetSimulation : IPlanetSimulation
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

        /// <summary>Which planet this is (the id in planets.json); saves remember it.</summary>
        public string PlanetId { get; set; } = "";

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
        readonly Dictionary<(int Tile, int Slot), (double Tilt, double Azimuth)> panels = new Dictionary<(int, int), (double, double)>();
        readonly Dictionary<int, int> panelsPerTile = new Dictionary<int, int>();
        bool targetReached;

        /// <summary>The colony's grid: battery, RTGs and demand (tune it here). Demand comes from <see cref="Colony"/>.</summary>
        public PowerGrid Grid { get; } = PowerGrid.Lander();

        /// <summary>How the colony's demand grows with its power supply.</summary>
        public ColonyDemand Colony { get; private set; }

        /// <summary>Build parts and construction sites: what buildings cost and how spare power pays for them.</summary>
        public Construction Build { get; } = new Construction();

        readonly HashSet<(int Tile, int Slot)> batteries = new HashSet<(int, int)>();
        readonly Dictionary<int, int> batteriesPerTile = new Dictionary<int, int>();

        public StubPlanetSimulation(HexSphere sphere, PlanetSurface surface, PlanetSolarParams planet,
                                    double planetRadiusM = Kardashev.EarthRadiusM, int baseTileId = -1)
        {
            Sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            this.planet = planet;
            this.planetRadiusM = planetRadiusM;
            tileAreaM2 = 4.0 * Math.PI * planetRadiusM * planetRadiusM / sphere.TileCount;
            BaseTileId = baseTileId >= 0 ? baseTileId : PickBaseTile();

            // Start mid-morning at the base: hour angle −0.4π. A tidally locked planet has the star fixed over
            // longitude 0, as in LightPotential and AnnualYield, so the base (picked near longitude 0) is in daylight.
            subStellarLongitudeAtStart = planet.TidallyLocked ? 0.0 : Sphere.Longitude(BaseTileId) + 0.4 * Math.PI;
            Grid.BrownoutChanged += on => BrownoutChanged?.Invoke(on);
            Colony = new ColonyDemand(Grid.DemandW, planet.DayLengthHours);
            // One sample per planet "hour" (a 24th of the day), a week of them.
            History = new PowerHistory(planet.DayLengthHours > 0.0 ? planet.DayLengthHours / 24.0 : 1.0, 24 * 7);

            double g0 = Orbit.ExtraterrestrialIrradiance(planet.Luminosity, planet.SemiMajorAxis);
            TypeITargetW = Kardashev.TypeITargetW(g0, planetRadiusM);

            UpdateStar();
            IsDayAtBase = SunAt(BaseTileId).Up > 0.0;
            GenerationW = ComputeGeneration() + Grid.RtgPowerW(0.0);
            RtgW = Grid.RtgPowerW(0.0);
            DemandW = BaseDemandW;
            KardashevPowerW = Math.Min(GenerationW, DemandW);
        }

        /// <summary>A colony on a planet from planets.json: its orbit and star, size, star colour and weather.</summary>
        public StubPlanetSimulation(HexSphere sphere, PlanetSurface surface, PlanetRecord record, int baseTileId = -1)
            : this(sphere, surface, (record ?? throw new ArgumentNullException(nameof(record))).ToSolarParams(),
                   record.planet.radiusM > 0.0 ? record.planet.radiusM : Kardashev.EarthRadiusM, baseTileId)
        {
            PlanetId = record.id ?? "";
            StarTemperatureK = record.star.teffK;
            Clouds = record.Clouds;
        }

        public HexSphere Sphere { get; }
        public int TileCount => Sphere.TileCount;
        public int BaseTileId { get; }
        public double TimeHours { get; private set; }
        public double DayLengthHours => planet.DayLengthHours;
        public double YearLengthHours => planet.YearLengthHours;
        public Vector3 StarDirection { get; private set; }
        public bool IsDayAtBase { get; private set; }
        public double GenerationW { get; private set; }
        public double DemandW { get; private set; }
        public double RtgW { get; private set; }
        public double BatteryStoredWh => Grid.Battery.StoredWh;
        public double BatteryCapacityWh => Grid.Battery.CapacityWh;
        public double BatteryPowerW { get; private set; }
        public double CurtailedW { get; private set; }
        public bool IsBrownout => Grid.IsBrownout;
        public double KardashevPowerW { get; private set; }
        public double KardashevK => Kardashev.K(KardashevPowerW);
        public double PlanetK => Kardashev.PlanetK(KardashevPowerW, TypeITargetW);
        public double TypeITargetW { get; }

        public event Action<bool> BrownoutChanged;
        public event Action<bool> DayNightChanged;
        public event Action TargetReached;
        public event Action<int, int> BuildingCompleted;

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

        void Step(double dt)
        {
            TimeHours += dt;
            UpdateStar();

            GridStep grid = Grid.Step(ComputeGeneration(), dt, TimeHours / Rtg.HoursPerYear);
            GenerationW = grid.GenerationW;
            RtgW = grid.RtgW;
            DemandW = grid.DemandW;
            BatteryPowerW = grid.ChargeW - grid.DischargeW;
            // Spare power (battery full or at its limit) is turned into build parts, which then pay the sites.
            BuildPowerW = grid.CurtailedW;
            CurtailedW = grid.CurtailedW - BuildPowerW;
            foreach (var done in Build.Step(BuildPowerW, dt))
                Finish(done.Tile, done.Slot);
            // Used power: what reached the loads, went into the battery or made build parts; curtailed power is not used.
            KardashevPowerW = grid.ServedW + grid.ChargeW + BuildPowerW;
            Colony.Step(dt, grid.GenerationW, grid.ServedW, grid.IsBrownout);
            Grid.DemandW = Colony.DemandW;
            double soc = Grid.Battery.CapacityWh > 0.0 ? Grid.Battery.StoredWh / Grid.Battery.CapacityWh : 0.0;
            History.Add(dt, TimeHours, grid.GenerationW, grid.DemandW, grid.ServedW, BuildPowerW, soc, grid.IsBrownout);

            bool day = SunAt(BaseTileId).Up > 0.0;
            if (day != IsDayAtBase)
            {
                IsDayAtBase = day;
                DayNightChanged?.Invoke(day);
            }

            if (!targetReached && GenerationW >= TypeITargetW)
            {
                targetReached = true;
                TargetReached?.Invoke();
            }
        }

        public PanelPreview PreviewPanel(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount)
                return new PanelPreview(false, "No such tile.", 0, 0, 0, 0);
            if (GetTile(tileId).IsWater)
                return new PanelPreview(false, "Panels cannot go on water yet.", 0, 0, 0, 0);
            PanelParams panel = MakePanel(tileId, tilt, azimuth, 1.0);
            AnnualYieldResult year = AnnualYield.Calculate(planet, panel);
            double energy = year.EnergyWh, capacityFactor = year.CapacityFactor;
            if (Clouds == CloudModel.EarthClimatology && energy > 0.0)
            {
                // Clouds as a multiplier on output, month by month (weighted by when the panel makes its energy).
                double[] months = MonthlyYield.Calculate(planet, panel);
                double clear = 0.0, cloudy = 0.0;
                for (int m = 0; m < months.Length; m++)
                {
                    clear += months[m];
                    cloudy += months[m] * CloudFactor(tileId, MonthMeanAnomaly(m, months.Length));
                }
                double share = clear > 0.0 ? cloudy / clear : 1.0;
                energy *= share;
                capacityFactor *= share;
            }
            // AnnualYield covers one orbit of this planet; the preview reports Earth years so lifetime, payback and
            // EROI mean the same on every planet (Proxima b's year is 11 days).
            if (YearLengthHours > 0.0)
                energy *= SolarConstants.EarthYearHours / YearLengthHours;
            double embodied = EmbodiedWhPerM2 * PanelAreaM2;
            double eroi = energy > 0 ? Economics.Eroi(energy * PanelLifetimeYears, embodied) : 0.0;
            double payback = energy > 0 ? Economics.PaybackYears(embodied, energy) : double.PositiveInfinity;
            return new PanelPreview(true, "", energy, capacityFactor, eroi, payback);
        }

        public int SiteRings { get; set; } = TileSite.DefaultRings;

        public bool PlacePanel(int tileId, int slot, double tilt, double azimuth)
        {
            if (!CanPlacePanel(tileId, slot, tilt, azimuth))
                return false;
            Build.Start(tileId, slot, BuildCostWh(BuildingType.SolarPanel));
            AddPanel(tileId, slot, tilt, azimuth);
            return true;
        }

        bool CanPlacePanel(int tileId, int slot, double tilt, double azimuth) =>
            tileId >= 0 && tileId < TileCount && slot >= 0 && slot < SlotCount(tileId) && GetBuilding(tileId, slot) == BuildingType.None
            && PreviewPanel(tileId, tilt, azimuth).CanPlace;

        void AddPanel(int tileId, int slot, double tilt, double azimuth)
        {
            panels[(tileId, slot)] = (tilt, azimuth);
            panelsPerTile[tileId] = PanelCount(tileId) + 1;
            GenerationW = ComputeGeneration() + RtgW;
        }

        public bool SetPanelAngles(int tileId, int slot, double tilt, double azimuth)
        {
            if (!panels.ContainsKey((tileId, slot)) || !PreviewPanel(tileId, tilt, azimuth).CanPlace)
                return false;
            panels[(tileId, slot)] = (tilt, azimuth);
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        // ---- Construction ----

        public double BuildStockWh => Build.StockWh;
        public double BuildPowerW { get; private set; }
        public int ConstructionQueueCount => Build.QueueCount;
        public double ConstructionRemainingWh => Build.RemainingWh;
        public bool IsUnderConstruction(int tileId, int slot) => Build.IsQueued(tileId, slot);
        public double ConstructionProgress(int tileId, int slot) => Build.Progress(tileId, slot);

        public double BuildCostWh(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.SolarPanel: return BuildCosts.Panel(EmbodiedWhPerM2, PanelAreaM2);
                case BuildingType.Battery: return BuildCosts.Battery(BatteryUnitWh);
                default: return 0.0;
            }
        }

        /// <summary>A site got all its parts: it starts working.</summary>
        void Finish(int tileId, int slot)
        {
            if (batteries.Contains((tileId, slot)))
                Grid.Battery.AddCapacity(BatteryUnitWh);
            else
                GenerationW = ComputeGeneration() + RtgW;
            BuildingCompleted?.Invoke(tileId, slot);
        }

        /// <summary>A building is going away: a site gives back its parts, a finished one is salvaged.</summary>
        void Dismantle(int tileId, int slot, BuildingType type)
        {
            if (Build.IsQueued(tileId, slot))
            {
                Build.Cancel(tileId, slot);
                return;
            }
            Build.Salvage(BuildCostWh(type));
            if (type == BuildingType.Battery)
                Grid.Battery.RemoveCapacity(BatteryUnitWh);
        }

        public bool RemovePanel(int tileId, int slot)
        {
            if (!panels.ContainsKey((tileId, slot)))
                return false;
            Dismantle(tileId, slot, BuildingType.SolarPanel);
            panels.Remove((tileId, slot));
            panelsPerTile[tileId] = PanelCount(tileId) - 1;
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        public bool HasPanel(int tileId, int slot) => panels.ContainsKey((tileId, slot));

        public double[] MonthlyEnergyWh(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater)
                return null;
            double[] months = MonthlyYield.Calculate(planet, MakePanel(tileId, tilt, azimuth, 1.0));
            if (Clouds == CloudModel.EarthClimatology)
                for (int m = 0; m < months.Length; m++)
                    months[m] *= CloudFactor(tileId, MonthMeanAnomaly(m, months.Length));
            return months;
        }

        public int SlotCount(int tileId)
        {
            if (!sites.TryGetValue(tileId, out TileSite site))
                sites[tileId] = site = TileSite.Create(Sphere, tileId, SiteRings);
            return site.SlotCount;
        }

        readonly Dictionary<int, TileSite> sites = new Dictionary<int, TileSite>();

        public int PanelCount(int tileId) => panelsPerTile.TryGetValue(tileId, out int n) ? n : 0;

        public double DemandTargetW => Colony.TargetW;
        public bool IsColonyGrowing => Colony.IsGrowing;
        public double HoursSinceBrownout => Colony.HoursSinceBrownout;
        public PowerHistory History { get; }

        /// <summary>One battery yard: about five grid-scale Li-ion units (≈ 4 MWh each) on a slot, Wh.</summary>
        public double BatteryUnitWh { get; set; } = 20_000_000.0;

        public BuildingType GetBuilding(int tileId, int slot) =>
            panels.ContainsKey((tileId, slot)) ? BuildingType.SolarPanel
            : batteries.Contains((tileId, slot)) ? BuildingType.Battery
            : BuildingType.None;

        public bool PlaceBattery(int tileId, int slot)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId)
                || GetTile(tileId).IsWater || GetBuilding(tileId, slot) != BuildingType.None)
                return false;
            bool finished = Build.Start(tileId, slot, BuildCostWh(BuildingType.Battery));
            AddBattery(tileId, slot);
            if (finished)
                Grid.Battery.AddCapacity(BatteryUnitWh);
            return true;
        }

        void AddBattery(int tileId, int slot)
        {
            batteries.Add((tileId, slot));
            batteriesPerTile[tileId] = BatteryCount(tileId) + 1;
        }

        public int BatteryCount(int tileId) => batteriesPerTile.TryGetValue(tileId, out int n) ? n : 0;

        public bool RemoveBuilding(int tileId, int slot)
        {
            if (RemovePanel(tileId, slot))
                return true;
            if (!batteries.Contains((tileId, slot)))
                return false;
            Dismantle(tileId, slot, BuildingType.Battery);
            batteries.Remove((tileId, slot));
            batteriesPerTile[tileId] = BatteryCount(tileId) - 1;
            return true;
        }

        public SimulationState CaptureState()
        {
            var state = new SimulationState
            {
                PlanetId = PlanetId,
                TileCount = TileCount,
                BaseTileId = BaseTileId,
                TimeHours = TimeHours,
                BatteryStoredWh = Grid.Battery.StoredWh,
                ColonyDemandW = Colony.DemandW,
                ColonyHoursSinceBrownout = Colony.HoursSinceBrownout,
                ColonyAverageGenerationW = Colony.AverageGenerationW,
                ColonyDayEnergyWh = Colony.DayEnergyWh,
                ColonyDayClockHours = Colony.DayClockHours,
                BuildStockWh = Build.StockWh,
                History = History.ToList(),
                HistoryGeneratedWh = History.TotalGeneratedWh,
                HistoryDemandWh = History.TotalDemandWh,
                HistoryServedWh = History.TotalServedWh,
                HistoryBuildWh = History.TotalBuildWh,
                HistoryBrownoutHours = History.BrownoutHours,
                HistoryHours = History.TotalHours,
            };
            foreach (var kv in panels)
                state.Buildings.Add(new BuildingRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Type = BuildingType.SolarPanel, Tilt = kv.Value.Tilt, Azimuth = kv.Value.Azimuth });
            foreach (var b in batteries)
                state.Buildings.Add(new BuildingRecord { Tile = b.Tile, Slot = b.Slot, Type = BuildingType.Battery });
            state.Buildings.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : a.Slot.CompareTo(b.Slot));
            int order = 0;
            var sites = new Dictionary<(int, int), (double Paid, int Order)>();
            foreach (var site in Build.Sites())
                sites[(site.Tile, site.Slot)] = (site.PaidWh, order++);
            foreach (BuildingRecord r in state.Buildings)
                if (sites.TryGetValue((r.Tile, r.Slot), out var site))
                {
                    r.UnderConstruction = true;
                    r.PaidWh = site.Paid;
                    r.QueueOrder = site.Order;
                }
            return state;
        }

        public bool RestoreState(SimulationState state)
        {
            if (state == null || state.TileCount != TileCount || state.BaseTileId != BaseTileId)
                return false;
            // Saves before version 3 have no planet id; they load on any planet with the same tiles.
            if (!string.IsNullOrEmpty(state.PlanetId) && state.PlanetId != PlanetId)
                return false;
            foreach (var key in new List<(int Tile, int Slot)>(panels.Keys))
                RemoveBuilding(key.Tile, key.Slot);
            foreach (var key in new List<(int Tile, int Slot)>(batteries))
                RemoveBuilding(key.Tile, key.Slot);
            TimeHours = state.TimeHours;
            UpdateStar();
            // A version-1 save had no parts: every building in it is finished, and the colony keeps the lander's parts.
            Build.Reset(state.Version >= 2 ? state.BuildStockWh : BuildCosts.LanderStockWh);
            var sitesInOrder = new List<BuildingRecord>();
            foreach (var r in state.Buildings)
            {
                if (r.UnderConstruction) sitesInOrder.Add(r);
                else Restore(r, false);
            }
            sitesInOrder.Sort((a, b) => a.QueueOrder.CompareTo(b.QueueOrder));
            foreach (var r in sitesInOrder)
                Restore(r, true);
            Grid.Battery.SetStored(state.BatteryStoredWh);
            History.Restore(state.History, state.HistoryGeneratedWh, state.HistoryDemandWh, state.HistoryServedWh,
                            state.HistoryBuildWh, state.HistoryBrownoutHours, state.HistoryHours);
            Colony.Restore(state.ColonyDemandW, state.ColonyHoursSinceBrownout, state.ColonyAverageGenerationW, state.ColonyDayEnergyWh, state.ColonyDayClockHours);
            Grid.DemandW = Colony.DemandW;
            DemandW = Colony.DemandW;
            IsDayAtBase = SunAt(BaseTileId).Up > 0.0;
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        void Restore(BuildingRecord r, bool site)
        {
            if (r.Type == BuildingType.SolarPanel)
            {
                if (!CanPlacePanel(r.Tile, r.Slot, r.Tilt, r.Azimuth))
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.SolarPanel), r.PaidWh);
                AddPanel(r.Tile, r.Slot, r.Tilt, r.Azimuth);
            }
            else if (r.Type == BuildingType.Battery)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.Battery), r.PaidWh);
                else Grid.Battery.AddCapacity(BatteryUnitWh);
                AddBattery(r.Tile, r.Slot);
            }
        }

        void UpdateStar()
        {
            double meanAnomaly = YearLengthHours > 0 ? 2.0 * Math.PI * TimeHours / YearLengthHours : 0.0;
            double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
            double declination = SunPosition.Declination(planet.Obliquity, nu + planet.PerihelionLongitude);
            double spin = DayLengthHours > 0 ? 2.0 * Math.PI * TimeHours / DayLengthHours : 0.0;
            StarDirection = PlanetFrame.StarDirection(declination, subStellarLongitudeAtStart - spin);
            currentIrradiance = Orbit.ExtraterrestrialIrradiance(planet.Luminosity,
                Orbit.Distance(planet.SemiMajorAxis, planet.Eccentricity, nu));
        }

        double currentIrradiance;

        Double3 SunAt(int tileId) => PlanetFrame.ToLocal(StarDirection, Sphere.Latitude(tileId), Sphere.Longitude(tileId));

        double ComputeGeneration()
        {
            double total = 0.0;
            double meanAnomaly = CurrentMeanAnomaly();
            foreach (var pair in panels)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(pair.Key.Tile, pair.Key.Slot))
                    continue;   // a site makes nothing until it is finished
                Double3 sun = SunAt(pair.Key.Tile);
                if (sun.Up <= 0.0)
                    continue;
                PanelParams p = MakePanel(pair.Key.Tile, pair.Value.Tilt, pair.Value.Azimuth,
                                          CloudFactor(pair.Key.Tile, meanAnomaly));
                double airMass = Atmosphere.AirMassKastenYoung(SunPosition.ZenithDegrees(sun), p.PressureRatio);
                double dni = Atmosphere.DirectNormal(currentIrradiance, planet.OpticalDepth, airMass);
                double dhi = Atmosphere.DiffuseHorizontal(currentIrradiance, sun.Up, planet.OpticalDepth, airMass, planet.DiffuseK);
                double ghi = Atmosphere.GlobalHorizontal(dni, dhi, sun.Up);
                double cosTheta = PanelModel.CosIncidence(PanelModel.Normal(p.Tilt, p.Azimuth), sun);
                double poa = PanelModel.PlaneOfArray(dni, dhi, ghi, cosTheta, p.Tilt, p.Albedo, p.FShade);
                double cellT = PanelModel.CellTemperature(planet.AirTemperatureC, poa, p.Noct,
                                                         planet.SurfacePressureEarth * p.PressureRatio, p.Efficiency);
                total += PanelModel.Power(poa, p.Area, p.Efficiency, cellT, p.TempCoefficient, p.FSpec, p.FDust);
            }
            return total;
        }

        /// <summary>
        /// One panel on a tile. <paramref name="cloudFactor"/> (share of clear-sky light that gets through) goes into
        /// the panel's light-loss slot f_dust as 1 − factor: the game has no dust yet, and CloudClimate is meant to be
        /// applied to the panel's light as a whole. Longitude is measured from the sub-stellar point, which only
        /// matters on a tidally locked planet.
        /// </summary>
        PanelParams MakePanel(int tileId, double tilt, double azimuth, double cloudFactor)
        {
            float elevation = Math.Max(0f, surface.Elevation[tileId]);
            double pressureRatio = Math.Exp(-elevation * 4000.0 / 8400.0); // elevation 1 ≈ 4 km, scale height 8.4 km
            double albedo = surface.Terrain[tileId] == TerrainType.Ice ? 0.8 : 0.2;
            return new PanelParams(Sphere.Latitude(tileId), tilt, azimuth, PanelAreaM2, PanelEfficiency,
                                   45.0, -0.004, albedo, pressureRatio, SpectralFactor, 1.0 - cloudFactor, 1.0,
                                   Sphere.Longitude(tileId) - subStellarLongitudeAtStart);
        }

        double CurrentMeanAnomaly() => YearLengthHours > 0 ? 2.0 * Math.PI * TimeHours / YearLengthHours : 0.0;

        static double MonthMeanAnomaly(int month, int months) => 2.0 * Math.PI * (month + 0.5) / months;

        /// <summary>Share of clear-sky light reaching panels on a tile at this point of the orbit (1 without clouds).</summary>
        public double CloudFactor(int tileId, double meanAnomaly) =>
            Clouds == CloudModel.EarthClimatology
                ? Climate.CloudClimate.Factor(Sphere.Latitude(tileId), Climate.CloudClimate.YearFractionFromMeanAnomaly(meanAnomaly))
                : 1.0;

        int PickBaseTile()
        {
            // Lowland tile closest to 20° N, longitude 0: a temperate, sunny start.
            Vector3 goal = PlanetFrame.FromLatLon(20.0 * Math.PI / 180.0, 0.0);
            int best = 0;
            float bestDot = float.NegativeInfinity;
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                if (surface.Terrain[t] != TerrainType.Lowland)
                    continue;
                float d = Vector3.Dot(Sphere.Centers[t], goal);
                if (d > bestDot) { bestDot = d; best = t; }
            }
            return best;
        }
    }
}

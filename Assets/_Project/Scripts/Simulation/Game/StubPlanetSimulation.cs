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

        public SkyNotebook Notebook { get; } = new SkyNotebook();

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
                if (!Build.IsQueued(b.Tile, b.Slot))
                    list.Add(new RobotFacility(b.Tile, b.Slot, ChargingCapability.BatteryYard));
            foreach (var c in stations)
                if (!Build.IsQueued(c.Tile, c.Slot))
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

        // ---- Colony network (stage E) ----

        public ColonyNetwork Network { get; private set; }

        public bool PlaceRelay(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.Relay, tileId, slot))
                return false;
            Build.Start(tileId, slot, BuildCostWh(BuildingType.Relay), 0.0, false);
            relays.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.Relay, tileId, slot));
            Settle((tileId, slot));
            RefreshNetwork();
            return true;
        }

        /// <summary>Finished relays relay; sites don't. Power follows the grid: a brownout takes the whole network down.</summary>
        void RefreshNetwork()
        {
            if (Network == null)
                return;
            var live = new List<int>();
            foreach (var r in relays)
                if (!Build.IsQueued(r.Tile, r.Slot))
                    live.Add(r.Tile);
            Network.SetRelays(live);
            Network.SetPowered(!Grid.IsBrownout);
        }

        // ---- Footprints: a building covers several slots of its tile (stored at its anchor slot) ----

        /// <summary>Covered slot (not the anchor) → its building's anchor slot.</summary>
        readonly Dictionary<(int Tile, int Slot), int> coveredBy = new Dictionary<(int, int), int>();
        /// <summary>Anchor → every slot the building covers (anchor first).</summary>
        readonly Dictionary<(int Tile, int Slot), List<int>> footprints = new Dictionary<(int, int), List<int>>();

        public int FootprintSize(BuildingType type) => BuildingFootprints.SlotsFor(type, PanelTier);

        TileSite SiteOf(int tileId)
        {
            SlotCount(tileId);
            return sites[tileId];
        }

        public List<int> FootprintFor(BuildingType type, int tileId, int slot)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId))
                return new List<int>();
            return SiteOf(tileId).NearestSlots(slot, FootprintSize(type));
        }

        public bool CanPlace(BuildingType type, int tileId, int slot)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId) || GetTile(tileId).IsWater)
                return false;
            foreach (int s in FootprintFor(type, tileId, slot))
                if (GetBuilding(tileId, s) != BuildingType.None) return false;
            return true;
        }

        public int AnchorOf(int tileId, int slot)
        {
            int a = coveredBy.TryGetValue((tileId, slot), out int anchor) ? anchor : slot;
            return GetBuildingAt(tileId, a) != BuildingType.None ? a : -1;
        }

        /// <summary>The anchor of the building covering a slot, or the slot itself (for calls that take any of its slots).</summary>
        int A(int tileId, int slot) => coveredBy.TryGetValue((tileId, slot), out int anchor) ? anchor : slot;

        public IReadOnlyList<int> FootprintAt(int tileId, int slot)
        {
            int a = AnchorOf(tileId, slot);
            if (a < 0) return new List<int>();
            return footprints.TryGetValue((tileId, a), out List<int> f) ? f : new List<int> { a };
        }

        void Occupy(int tileId, List<int> footprint)
        {
            int anchor = footprint[0];
            footprints[(tileId, anchor)] = new List<int>(footprint);
            for (int i = 1; i < footprint.Count; i++)
                coveredBy[(tileId, footprint[i])] = anchor;
        }

        void Vacate(int tileId, int anchor)
        {
            if (!footprints.TryGetValue((tileId, anchor), out List<int> f))
                return;
            for (int i = 1; i < f.Count; i++)
                coveredBy.Remove((tileId, f[i]));
            footprints.Remove((tileId, anchor));
        }

        public bool PlaceChargingStation(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.ChargingStation, tileId, slot))
                return false;
            Build.Start(tileId, slot, BuildCostWh(BuildingType.ChargingStation), 0.0, false);
            stations.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.ChargingStation, tileId, slot));
            Settle((tileId, slot));
            return true;
        }
        readonly List<ManufacturingJob> jobs = new List<ManufacturingJob>();

        // ---- Manufacturing (phase 3b) ----

        public IReadOnlyList<ManufacturingJob> Jobs => jobs;

        /// <summary>Finished Workshops in a fixed order (tile, then slot): job i is worked at Workshop i.</summary>
        List<(int Tile, int Slot)> FinishedWorkshops()
        {
            var list = new List<(int Tile, int Slot)>();
            foreach (var w in workshops)
                if (!Build.IsQueued(w.Tile, w.Slot))
                    list.Add(w);
            list.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : a.Slot.CompareTo(b.Slot));
            return list;
        }

        public int WorkshopCount => FinishedWorkshops().Count;

        public bool PlaceWorkshop(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.Workshop, tileId, slot))
                return false;
            Build.Start(tileId, slot, BuildCostWh(BuildingType.Workshop), 0.0, false);
            workshops.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.Workshop, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        /// <summary>Copper if there is enough, else aluminium once refining it is unlocked (and there is enough).</summary>
        bool UseAluminium(double kg) =>
            Materials[Material.Copper] < kg && MilestonesReached >= RobotTech.UnlockMilestone(Material.Aluminium)
            && Materials[Material.Aluminium] >= kg;

        bool UseSodium(double lithiumKg, double sodiumKg) =>
            Materials[Material.Lithium] < lithiumKg && MilestonesReached >= RobotTech.UnlockMilestone(Material.Sodium)
            && Materials[Material.Sodium] >= sodiumKg;

        /// <summary>Take a recipe's inputs, or say what is missing (and take nothing).</summary>
        string TakeInputs(Recipe recipe)
        {
            if (WorkshopCount == 0 && workshops.Count == 0)
                return "Build a Robot Workshop first.";
            var need = new Dictionary<Material, double>();
            foreach (var (m, kg) in recipe.Inputs)
                need[m] = (need.TryGetValue(m, out double n) ? n : 0.0) + kg;
            string missing = "";
            foreach (var kv in need)
                if (Materials[kv.Key] < kv.Value - 1e-9)
                    missing += (missing.Length > 0 ? ", " : "") + $"{kv.Value - Materials[kv.Key]:0.#} kg {Resources.Materials.Name(kv.Key).ToLowerInvariant()}";
            if (Build.StockWh < recipe.PartsWh - 1e-9)
                missing += (missing.Length > 0 ? ", " : "") + $"{(recipe.PartsWh - Build.StockWh) / 1000.0:0.#} kWh of build parts";
            if (missing.Length > 0)
                return "Missing " + missing + ".";
            foreach (var kv in need)
                Materials.TryTake(kv.Key, kv.Value);
            Build.Take(recipe.PartsWh);
            return null;
        }

        public string OrderRobot(RobotKind kind)
        {
            if (MilestonesReached < RobotTech.UnlockMilestone(kind))
                return RobotTech.UnlockMilestone(kind) == int.MaxValue
                    ? $"{kind} robots are not available (transport is a later feature)."
                    : $"{kind} robots unlock at milestone {RobotTech.UnlockMilestone(kind)}.";
            bool al = UseAluminium(40.0), na = UseSodium(2.0, 6.0);
            Recipe recipe = RobotTech.Robot(kind, na, al);
            string why = TakeInputs(recipe);
            if (why != null) return why;
            jobs.Add(new ManufacturingJob { Kind = JobKind.Robot, RobotKind = kind, SodiumBattery = na, AluminiumWiring = al, Recipe = recipe });
            Settle(null);
            return null;
        }

        public string OrderUpgrade(int robotId, RobotModule module)
        {
            Robot r = Workforce.GetRobot(robotId);
            if (r == null) return "No such robot.";
            if (MilestonesReached < RobotTech.UnlockMilestone(module))
                return $"{RobotTech.ModuleName(module)} unlocks at milestone {RobotTech.UnlockMilestone(module)}.";
            if (r.Has(module)) return $"{r.Name} already has it.";
            foreach (ManufacturingJob j in jobs)
                if (j.Kind == JobKind.Upgrade && j.RobotId == robotId && j.Module == module)
                    return "Already ordered.";
            bool al = UseAluminium(10.0), na = UseSodium(1.0, 3.0);
            Recipe recipe = RobotTech.Upgrade(module, na, al);
            string why = TakeInputs(recipe);
            if (why != null) return why;
            jobs.Add(new ManufacturingJob { Kind = JobKind.Upgrade, RobotId = robotId, Module = module, SodiumBattery = na, AluminiumWiring = al, Recipe = recipe });
            Settle(null);
            return null;
        }

        public string OrderRefine(Material output)
        {
            if (MilestonesReached < RobotTech.UnlockMilestone(output))
                return RobotTech.UnlockMilestone(output) == int.MaxValue
                    ? $"{Resources.Materials.Name(output)} can't be refined."
                    : $"Refining {Resources.Materials.Name(output).ToLowerInvariant()} unlocks at milestone {RobotTech.UnlockMilestone(output)}.";
            Recipe recipe = RobotTech.Refine(output);
            string why = TakeInputs(recipe);
            if (why != null) return why;
            jobs.Add(new ManufacturingJob { Kind = JobKind.Refine, Output = output, OutputKg = RobotTech.RefineBatchKg(output), Recipe = recipe });
            Settle(null);
            return null;
        }

        public bool CancelJob(int index)
        {
            if (index < 0 || index >= jobs.Count)
                return false;
            ManufacturingJob j = jobs[index];
            jobs.RemoveAt(index);
            foreach (var (m, kg) in j.Recipe.Inputs)
                Materials.Add(m, kg);
            Build.AddParts(j.Recipe.PartsWh, 1.0);
            Settle(null);
            return true;
        }

        /// <summary>Rebuild a job's recipe from its fields (when a game is loaded).</summary>
        static Recipe RecipeOf(ManufacturingJob j) =>
            j.Kind == JobKind.Robot ? RobotTech.Robot(j.RobotKind, j.SodiumBattery, j.AluminiumWiring)
            : j.Kind == JobKind.Upgrade ? RobotTech.Upgrade(j.Module, j.SodiumBattery, j.AluminiumWiring)
            : RobotTech.Refine(j.Output);

        void FinishJob(ManufacturingJob j)
        {
            switch (j.Kind)
            {
                case JobKind.Robot:
                    Robot made = Workforce.AddRobot(j.RobotKind, BaseTileId);
                    made.SodiumBattery = j.SodiumBattery;
                    made.Battery = new RobotBattery(made.DesignBatteryWh);
                    break;
                case JobKind.Upgrade:
                    Robot r = Workforce.GetRobot(j.RobotId);
                    if (r == null) break;
                    r.Modules |= j.Module;
                    if (j.Module == RobotModule.BigBattery)
                    {
                        double stored = r.Battery.StoredWh;
                        r.Battery = new RobotBattery(r.DesignBatteryWh, 0.0);
                        r.Battery.Restore(0.0, stored);
                    }
                    break;
                default:
                    Materials.Add(j.Output, j.OutputKg);
                    break;
            }
        }
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
            Workforce.Reset(BaseTileId);
            Workforce.CapacityOf = t => FacilityStaffing.CapacityFor(t.Kind);
            Network = new ColonyNetwork(sphere, BaseTileId);
            Workforce.Network = Network;
            Deposits = new DepositMap(sphere, surface, PlanetChemistry.EarthLike, 7);
            Routes = new RouteFinder(sphere, surface);

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
            Deposits = new DepositMap(sphere, surface, record.ToChemistry(), record.look?.terrainSeed ?? 7);
            if (record.planet.surfaceGravityMs2 > 0.0)
                Routes = new RouteFinder(sphere, surface, record.planet.surfaceGravityMs2);
        }

        // ---- Movement (spec §10) ----

        /// <summary>Driving routes on this planet (terrain pace, drive power ∝ surface gravity).</summary>
        public RouteFinder Routes { get; private set; }

        /// <summary>Robots drive between tiles (true) or work any tile from where they are (false, as before movement).</summary>
        public bool MovementEnabled { get; set; } = true;

        string chargerKey;
        Movement movement;

        /// <summary>Routes plus driving hours to the nearest charger from every tile (recomputed when chargers change).</summary>
        Movement CurrentMovement()
        {
            if (!MovementEnabled)
                return null;
            List<RobotFacility> facilities = RobotFacilities();
            var tiles = new List<int>();
            foreach (RobotFacility f in facilities)
                if (!tiles.Contains(f.Tile)) tiles.Add(f.Tile);
            tiles.Sort();
            string key = string.Join(",", tiles);
            if (movement == null || key != chargerKey)
            {
                movement = new Movement(Routes, Routes.HoursFromNearest(tiles));
                chargerKey = key;
            }
            return movement;
        }

        public double DriveHours(int fromTile, int toTile) =>
            fromTile < 0 || toTile < 0 || fromTile >= TileCount || toTile >= TileCount ? double.PositiveInfinity : Routes.HoursBetween(fromTile, toTile);

        public bool SendRobot(int robotId, int tileId)
        {
            Workforce.Movement = CurrentMovement();
            if (!Workforce.SendTo(robotId, tileId))
                return false;
            Settle(null);
            return true;
        }

        // ---- Resources (phase 3a) ----

        /// <summary>What lies in each tile's ground, and which tiles are surveyed.</summary>
        public DepositMap Deposits { get; private set; }

        /// <summary>Materials on hand, kg.</summary>
        public MaterialStock Materials { get; } = new MaterialStock();

        readonly List<int> surveyOrders = new List<int>();
        readonly List<(int Tile, Material Material)> miningOrders = new List<(int, Material)>();
        /// <summary>Hours of work done on survey tasks and the current mining batch.</summary>
        readonly Dictionary<WorkTask, double> fieldWork = new Dictionary<WorkTask, double>();

        public event Action<int> TileSurveyed;

        /// <summary>A Workshop job finished (a new robot, an upgrade or a refined batch).</summary>
        public event Action<ManufacturingJob> JobFinished;

        static WorkTask SurveyTask(int tile) => new WorkTask(TaskKind.Survey, tile, -1);

        // ---- Solar parameters (stage B of docs/design/solar-measurements.md) ----

        public ParameterBook Parameters { get; } = new ParameterBook();
        readonly List<(SolarParameter Parameter, int Tile)> measureOrders = new List<(SolarParameter, int)>();
        public event Action<SolarParameter> ParameterMeasured;
        public event Action<SolarParameter> PuzzleReady;
        readonly List<LatitudePuzzle> latitudePuzzles = new List<LatitudePuzzle>();
        public IReadOnlyList<LatitudePuzzle> LatitudePuzzles => latitudePuzzles;
        readonly List<ParameterPuzzle> parameterPuzzles = new List<ParameterPuzzle>();
        readonly List<SurveyPuzzle> surveyPuzzles = new List<SurveyPuzzle>();
        readonly HashSet<int> roughSurveys = new HashSet<int>();
        public IReadOnlyList<SurveyPuzzle> SurveyPuzzles => surveyPuzzles;
        public bool IsRoughSurvey(int tileId) => roughSurveys.Contains(tileId);

        public string AnswerSurveyPuzzle(int tileId, IReadOnlyList<double> estimates, out LatitudePuzzle.Grade grade)
        {
            grade = LatitudePuzzle.Grade.Wrong;
            SurveyPuzzle p = surveyPuzzles.Find(q => q.Tile == tileId);
            if (p == null)
                return "No core samples waiting for that tile.";
            grade = p.Check(estimates);
            if (grade == LatitudePuzzle.Grade.Wrong)
            {
                p.Attempts++;
                return "Those grades don't fit the cores. Average every core, counting a core without the material as 0.";
            }
            surveyPuzzles.Remove(p);
            Deposits.MarkSurveyed(tileId);
            if (grade == LatitudePuzzle.Grade.Rough)
                roughSurveys.Add(tileId);
            else
                roughSurveys.Remove(tileId);
            TileSurveyed?.Invoke(tileId);
            return grade == LatitudePuzzle.Grade.Precise
                ? $"Tile #{tileId} surveyed: the assays match your estimates."
                : $"Tile #{tileId} surveyed roughly: your estimates are within {SurveyPuzzle.RoughShare * 100.0:0} %, so its grades show as ±.";
        }
        public IReadOnlyList<ParameterPuzzle> ParameterPuzzles => parameterPuzzles;

        /// <summary>Parameters whose readings end in a puzzle (stages C and D); the rest are known when measured.</summary>
        static bool HasPuzzle(SolarParameter p) => p != SolarParameter.Latitude;

        /// <summary>Dust the model's panels carry: none (robots keep them clean). A dust model would set this per planet.</summary>
        const double ModelDustShare = 0.0;

        public string AnswerParameterPuzzle(SolarParameter parameter, double value, out LatitudePuzzle.Grade grade)
        {
            grade = LatitudePuzzle.Grade.Wrong;
            ParameterPuzzle p = parameterPuzzles.Find(q => q.Parameter == parameter);
            if (p == null)
                return "No readings waiting for that.";
            grade = p.Check(value);
            if (grade == LatitudePuzzle.Grade.Wrong)
            {
                p.Attempts++;
                return $"{value:0.###} doesn't fit the readings. Check the formula and the units.";
            }
            parameterPuzzles.Remove(p);
            if (grade == LatitudePuzzle.Grade.Precise)
            {
                Parameters.MarkMeasured(parameter);
                ParameterMeasured?.Invoke(parameter);
                return $"{SolarUnknowns.Name(parameter)} = {value:0.###}: known now, its spread is gone.";
            }
            Parameters.MarkRough(parameter);
            ParameterMeasured?.Invoke(parameter);
            return $"{SolarUnknowns.Name(parameter)} ≈ {value:0.###}: close, half its spread is left (measure again to pin it down).";
        }

        /// <summary>The readings a robot brings back for a light puzzle, from this planet's own values.</summary>
        ParameterPuzzle MakeLightPuzzle(SolarParameter p, int tile)
        {
            double g0 = Orbit.ExtraterrestrialIrradiance(planet.Luminosity, planet.SemiMajorAxis);
            double pressure = MakePanel(tile, 0.0, 0.0, 1.0, 1.0).PressureRatio;
            switch (p)
            {
                case SolarParameter.OpticalDepth:
                    return ParameterPuzzle.Haze(tile, g0, planet.OpticalDepth, pressure);
                case SolarParameter.DiffuseShare:
                    return ParameterPuzzle.Diffuse(tile, g0, planet.OpticalDepth, planet.DiffuseK, pressure);
                case SolarParameter.Albedo:
                    return ParameterPuzzle.Albedo(tile, g0, MakePanel(tile, 0.0, 0.0, 1.0, 1.0).Albedo);
                case SolarParameter.AirTemperature:
                    return ParameterPuzzle.AirTemperature(tile, planet.AirTemperatureC);
                case SolarParameter.Dust:
                    return ParameterPuzzle.Dust(tile, g0, ModelDustShare);
                case SolarParameter.Spectrum:
                    return ParameterPuzzle.Spectrum(tile, StarTemperatureK > 0.0 ? StarTemperatureK : 5778.0);
                default:
                {
                    // Clear-sky light on flat ground per day: a flat 1 m² panel's year without clouds, per unit efficiency.
                    PanelParams flat = MakePanel(tile, 0.0, 0.0, 1.0, 1.0);
                    double days = YearLengthHours > 0.0 && DayLengthHours > 0.0 ? YearLengthHours / DayLengthHours : 365.25;
                    double kwhPerDay = AnnualYield.Calculate(planet, flat).EnergyWh / PanelEfficiency / 1000.0 / days;
                    return ParameterPuzzle.Clouds(tile, kwhPerDay, CloudFactor(tile, CurrentMeanAnomaly()));
                }
            }
        }

        public string AnswerLatitudePuzzle(int tileId, double latitudeDeg, out LatitudePuzzle.Grade grade)
        {
            grade = LatitudePuzzle.Grade.Wrong;
            LatitudePuzzle p = latitudePuzzles.Find(q => q.Tile == tileId);
            if (p == null)
                return "No shadow readings for that tile.";
            grade = p.Check(latitudeDeg);
            if (grade == LatitudePuzzle.Grade.Wrong)
            {
                p.Attempts++;
                return $"φ = {latitudeDeg:0.0}° doesn't fit the readings. Check which way the shadow points, and the sign of δ.";
            }
            latitudePuzzles.Remove(p);
            string method = grade == LatitudePuzzle.Grade.Precise
                ? "noon shadow, worked out"
                : $"noon shadow, worked out roughly (±{LatitudePuzzle.RoughDeg:0}°)";
            return Notebook.RecordLatitude(tileId, latitudeDeg, method);
        }

        static WorkTask MeasureTask(SolarParameter p, int tile) => new WorkTask(TaskKind.Measure, tile, (int)p);

        public bool IsParameterKnown(SolarParameter parameter, int tileId) =>
            parameter == SolarParameter.Latitude ? Notebook.KnowsLatitude(tileId)
            : parameter == SolarParameter.Clouds && Clouds == CloudModel.None || Parameters.IsMeasured(parameter);

        public bool OrderMeasure(SolarParameter parameter, int tileId)
        {
            if (parameter == SolarParameter.Latitude && latitudePuzzles.Exists(q => q.Tile == tileId))
                return false;   // the readings are in: solve the puzzle
            if (parameterPuzzles.Exists(q => q.Parameter == parameter))
                return false;   // the readings are in: solve the puzzle
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater
                || IsParameterKnown(parameter, tileId) || IsMeasureOrdered(parameter))
                return false;
            measureOrders.Add((parameter, tileId));
            Settle(null);
            return true;
        }

        public bool IsMeasureOrdered(SolarParameter parameter) => measureOrders.Exists(o => o.Parameter == parameter);

        public bool CancelMeasure(SolarParameter parameter)
        {
            int i = measureOrders.FindIndex(o => o.Parameter == parameter);
            if (i < 0)
                return false;
            WorkTask t = MeasureTask(parameter, measureOrders[i].Tile);
            measureOrders.RemoveAt(i);
            Workforce.Tasks.MarkCancelled(t);
            fieldWork.Remove(t);
            Settle(null);
            return true;
        }

        public double MeasureProgress(SolarParameter parameter)
        {
            if (Parameters.IsMeasured(parameter))
                return 1.0;
            int i = measureOrders.FindIndex(o => o.Parameter == parameter);
            return i >= 0 && fieldWork.TryGetValue(MeasureTask(parameter, measureOrders[i].Tile), out double h)
                ? Math.Min(1.0, h / SolarUnknowns.MeasureHours) : 0.0;
        }

        /// <summary>A copy of the planet's solar values with the atmosphere and air changed.</summary>
        static PlanetSolarParams WithAir(in PlanetSolarParams p, double opticalDepth, double diffuseK, double airTemperatureC) =>
            new PlanetSolarParams(p.Luminosity, p.SemiMajorAxis, p.Eccentricity, p.Obliquity, p.PerihelionLongitude,
                                  p.DayLengthHours, p.YearLengthHours, opticalDepth, diffuseK, airTemperatureC,
                                  p.TidallyLocked, p.SurfacePressureEarth);

        static PanelParams WithPanel(in PanelParams p, double latitude, double albedo, double fSpec, double fDust) =>
            new PanelParams(latitude, p.Tilt, p.Azimuth, p.Area, p.Efficiency, p.Noct, p.TempCoefficient, albedo,
                            p.PressureRatio, fSpec, fDust, p.FShade, p.Longitude);

        /// <summary>A panel's yearly energy, Earth years, Wh: the preview's sum, with clouds month by month.</summary>
        double YearEnergyWh(int tileId, in PlanetSolarParams pl, in PanelParams panel)
        {
            double energy = AnnualYield.Calculate(pl, panel).EnergyWh;
            if (Clouds == CloudModel.EarthClimatology && energy > 0.0)
            {
                double[] months = MonthlyYield.Calculate(pl, panel);
                double clear = 0.0, cloudy = 0.0;
                for (int m = 0; m < months.Length; m++)
                {
                    clear += months[m];
                    cloudy += months[m] * CloudFactor(tileId, MonthMeanAnomaly(m, months.Length));
                }
                energy *= clear > 0.0 ? cloudy / clear : 1.0;
            }
            if (YearLengthHours > 0.0)
                energy *= SolarConstants.EarthYearHours / YearLengthHours;
            return energy;
        }

        public List<ParameterEffect> ParameterEffects(int tileId, double tilt, double azimuth)
        {
            var list = new List<ParameterEffect>();
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater)
                return list;
            PanelParams panel = MakePanel(tileId, tilt, azimuth, 1.0, NewPanelAreaM2(tileId));
            double baseWh = YearEnergyWh(tileId, planet, panel);
            const double Deg = Math.PI / 180.0;
            foreach (SolarParameter p in (SolarParameter[])Enum.GetValues(typeof(SolarParameter)))
            {
                bool known = IsParameterKnown(p, tileId);
                double lo = baseWh, hi = baseWh;
                if (!known)
                {
                    switch (p)
                    {
                        case SolarParameter.Latitude:
                            lo = YearEnergyWh(tileId, planet, WithPanel(panel, panel.Latitude - SolarUnknowns.LatitudeDeg * Deg, panel.Albedo, panel.FSpec, panel.FDust));
                            hi = YearEnergyWh(tileId, planet, WithPanel(panel, panel.Latitude + SolarUnknowns.LatitudeDeg * Deg, panel.Albedo, panel.FSpec, panel.FDust));
                            break;
                        case SolarParameter.OpticalDepth:
                            lo = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth * (1.0 + SolarUnknowns.OpticalDepthShare), planet.DiffuseK, planet.AirTemperatureC), panel);
                            hi = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth * (1.0 - SolarUnknowns.OpticalDepthShare), planet.DiffuseK, planet.AirTemperatureC), panel);
                            break;
                        case SolarParameter.DiffuseShare:
                            lo = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, Math.Min(1.0, planet.DiffuseK * (1.0 + SolarUnknowns.DiffuseShare)), planet.AirTemperatureC), panel);
                            hi = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, planet.DiffuseK * (1.0 - SolarUnknowns.DiffuseShare), planet.AirTemperatureC), panel);
                            break;
                        case SolarParameter.Clouds:
                            lo = baseWh * (1.0 - SolarUnknowns.CloudShare);
                            hi = baseWh * (1.0 + SolarUnknowns.CloudShare);
                            break;
                        case SolarParameter.Albedo:
                            lo = YearEnergyWh(tileId, planet, WithPanel(panel, panel.Latitude, Math.Max(0.0, panel.Albedo - SolarUnknowns.Albedo), panel.FSpec, panel.FDust));
                            hi = YearEnergyWh(tileId, planet, WithPanel(panel, panel.Latitude, Math.Min(1.0, panel.Albedo + SolarUnknowns.Albedo), panel.FSpec, panel.FDust));
                            break;
                        case SolarParameter.AirTemperature:
                            lo = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, planet.DiffuseK, planet.AirTemperatureC + SolarUnknowns.AirTemperatureC), panel);
                            hi = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, planet.DiffuseK, planet.AirTemperatureC - SolarUnknowns.AirTemperatureC), panel);
                            break;
                        case SolarParameter.Dust:
                            lo = YearEnergyWh(tileId, planet, WithPanel(panel, panel.Latitude, panel.Albedo, panel.FSpec, Math.Min(1.0, panel.FDust + SolarUnknowns.Dust)));
                            break;
                        case SolarParameter.Spectrum:
                            lo = YearEnergyWh(tileId, planet, WithPanel(panel, panel.Latitude, panel.Albedo, panel.FSpec * (1.0 - SolarUnknowns.Spectrum), panel.FDust));
                            hi = YearEnergyWh(tileId, planet, WithPanel(panel, panel.Latitude, panel.Albedo, Math.Min(1.0, panel.FSpec * (1.0 + SolarUnknowns.Spectrum)), panel.FDust));
                            break;
                    }
                }
                bool rough = !known && Parameters.IsRough(p);
                if (rough)
                {
                    lo = baseWh - (baseWh - lo) * ParameterBook.RoughRangeLeft;
                    hi = baseWh + (hi - baseWh) * ParameterBook.RoughRangeLeft;
                }
                list.Add(new ParameterEffect(p, Math.Min(lo, hi), Math.Max(lo, hi), known, rough));
            }
            return list;
        }
        static WorkTask MineTask(int tile, Material m) => new WorkTask(TaskKind.Mine, tile, (int)m);

        public bool OrderSurvey(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater || Deposits.IsSurveyed(tileId) || surveyOrders.Contains(tileId)
                || surveyPuzzles.Exists(q => q.Tile == tileId))
                return false;
            surveyOrders.Add(tileId);
            Settle(null);
            return true;
        }

        public bool IsSurveyOrdered(int tileId) => surveyOrders.Contains(tileId);

        public bool CancelSurvey(int tileId)
        {
            if (!surveyOrders.Remove(tileId))
                return false;
            Workforce.Tasks.MarkCancelled(SurveyTask(tileId));
            fieldWork.Remove(SurveyTask(tileId));
            Settle(null);
            return true;
        }

        /// <summary>How much of a tile's survey is done, 0–1 (1 once surveyed).</summary>
        public double SurveyProgress(int tileId) =>
            Deposits.IsSurveyed(tileId) ? 1.0
            : fieldWork.TryGetValue(SurveyTask(tileId), out double h) ? Math.Min(1.0, h / RobotSpecs.SurveyWorkHours) : 0.0;

        /// <summary>
        /// Keep robots mining a material on a tile (in batches, so they still take more urgent work between them).
        /// The material must be known there: rock anywhere on land, the rest after a survey.
        /// </summary>
        public bool OrderMining(int tileId, Material material)
        {
            if (tileId < 0 || tileId >= TileCount || !Deposits.IsKnown(tileId, material) || Deposits.GradeOf(tileId, material) <= 0.0
                || miningOrders.Contains((tileId, material)))
                return false;
            miningOrders.Add((tileId, material));
            Settle(null);
            return true;
        }

        public bool IsMiningOrdered(int tileId, Material material) => miningOrders.Contains((tileId, material));

        public bool CancelMining(int tileId, Material material)
        {
            if (!miningOrders.Remove((tileId, material)))
                return false;
            Workforce.Tasks.MarkCancelled(MineTask(tileId, material));
            fieldWork.Remove(MineTask(tileId, material));
            Settle(null);
            return true;
        }

        public int MiningOrderCount => miningOrders.Count;

        /// <summary>Robots on survey and mining tasks add work; finished surveys reveal tiles, finished batches add material.</summary>
        void AdvanceFieldWork(double dt)
        {
            // Interruption rule (D15): a survey (a measurement) restarts if no robot is on it; building, salvage and
            // mining keep their progress, which lives on the site or the batch.
            foreach (int tile in surveyOrders)
            {
                WorkTask st = SurveyTask(tile);
                if (!TaskPolicy.For(TaskKind.Survey).KeepsProgress && fieldWork.ContainsKey(st) && !Workforce.IsWorkedOnSite(st))
                    fieldWork.Remove(st);
            }
            foreach (var mo in measureOrders)
            {
                WorkTask mt = MeasureTask(mo.Parameter, mo.Tile);
                if (fieldWork.ContainsKey(mt) && !Workforce.IsWorkedOnSite(mt))
                    fieldWork.Remove(mt);   // a measurement restarts if the robot leaves
            }
            // Each task's crew on site works together, with diminishing returns (spec v2 §5).
            var crews = new Dictionary<WorkTask, List<double>>();
            foreach (Robot r in Workforce.Robots)
            {
                if (!r.Task.HasValue || r.Activity != RobotActivity.None || r.IsShutDown)
                    continue;
                WorkTask t = r.Task.Value;
                if (MovementEnabled && (r.IsTravelling || r.Tile != t.Tile))
                    continue;   // still driving there
                if (!crews.TryGetValue(t, out List<double> crew))
                    crews[t] = crew = new List<double>();
                crew.Add(RobotCatalog.Productivity(r, t.Kind, MilestonesReached));
            }
            foreach (var kv in crews)
            {
                WorkTask t = kv.Key;
                double speed = FacilityStaffing.Rate(kv.Value);
                if (t.Kind == TaskKind.Manufacture)
                {
                    int index = FinishedWorkshops().IndexOf((t.Tile, t.Slot));
                    if (index >= 0 && index < jobs.Count)
                    {
                        double before = jobs[index].DoneHours;
                        jobs[index].DoneHours += dt * speed;
                        // Long jobs go in rounds, like mining batches, so the robot still takes more urgent work between them.
                        if (Math.Floor(jobs[index].DoneHours / RobotSpecs.MiningBatchHours + 1e-9) > Math.Floor(before / RobotSpecs.MiningBatchHours + 1e-9))
                            Workforce.Release(t);
                    }
                    continue;
                }
                if (t.Kind != TaskKind.Survey && t.Kind != TaskKind.Mine && t.Kind != TaskKind.Measure)
                    continue;
                fieldWork[t] = (fieldWork.TryGetValue(t, out double h) ? h : 0.0) + dt * speed;
            }
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                ManufacturingJob j = jobs[i];
                if (j.DoneHours < j.Recipe.WorkHours - 1e-9)
                    continue;
                jobs.RemoveAt(i);
                FinishJob(j);
                JobFinished?.Invoke(j);
            }
            foreach (int tile in surveyOrders.ToArray())
            {
                WorkTask t = SurveyTask(tile);
                if (fieldWork.TryGetValue(t, out double h) && h >= RobotSpecs.SurveyWorkHours - 1e-9)
                {
                    fieldWork.Remove(t);
                    surveyOrders.Remove(tile);
                    SurveyPuzzle puzzle = SurveyPuzzle.Make(tile, Deposits.At(tile));
                    if (puzzle.Materials.Count == 0)
                    {
                        // Only rock here: nothing to estimate, the survey is simply done.
                        Deposits.MarkSurveyed(tile);
                        TileSurveyed?.Invoke(tile);
                        continue;
                    }
                    surveyPuzzles.RemoveAll(q => q.Tile == tile);
                    surveyPuzzles.Add(puzzle);
                    PuzzleReady?.Invoke(SolarParameter.Latitude);   // "a puzzle is waiting"; the kind is in SurveyPuzzles
                }
            }
            foreach (var mo in measureOrders.ToArray())
            {
                WorkTask t = MeasureTask(mo.Parameter, mo.Tile);
                if (!fieldWork.TryGetValue(t, out double h) || h < SolarUnknowns.MeasureHours - 1e-9)
                    continue;
                fieldWork.Remove(t);
                measureOrders.Remove(mo);
                Workforce.Release(t);
                if (HasPuzzle(mo.Parameter))
                {
                    parameterPuzzles.RemoveAll(q => q.Parameter == mo.Parameter);
                    parameterPuzzles.Add(MakeLightPuzzle(mo.Parameter, mo.Tile));
                    PuzzleReady?.Invoke(mo.Parameter);
                    continue;
                }
                if (mo.Parameter == SolarParameter.Latitude)
                {
                    // The readings are in; the player works out the latitude (stage C).
                    double lat = Sphere.Latitude(mo.Tile) * 180.0 / Math.PI;
                    latitudePuzzles.Add(LatitudePuzzle.Make(mo.Tile, lat, DescribeSky(mo.Tile, 0.0, 0.0).DeclinationDeg));
                    PuzzleReady?.Invoke(mo.Parameter);
                    continue;
                }
                Parameters.MarkMeasured(mo.Parameter);
                ParameterMeasured?.Invoke(mo.Parameter);
            }
            foreach (var order in miningOrders.ToArray())
            {
                WorkTask t = MineTask(order.Tile, order.Material);
                if (!fieldWork.TryGetValue(t, out double h) || h < RobotSpecs.MiningBatchHours - 1e-9)
                    continue;
                fieldWork[t] = h - RobotSpecs.MiningBatchHours;
                Materials.Add(order.Material, RobotSpecs.MiningKgPerHour * RobotSpecs.MiningBatchHours * Deposits.GradeOf(order.Tile, order.Material));
                Workforce.Release(t);
            }
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
        public event Action<int, int> BuildingSalvaged;
        public event Action<int, int> PanelAdjusted;

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

            // Robots at the lander charge from its RTGs first (D10); elsewhere, robots below 20 % charge right after the
            // colony and the rest top up from spare power before build parts (D6).
            Movement moving = CurrentMovement();
            Workforce.Movement = moving;
            Upkeep.Plan(Workforce, RobotFacilities(), dt, moving);
            // The network hub and relays draw as a priority load: they stay up until the colony itself browns out.
            GridStep grid = Grid.Step(ComputeGeneration(), dt, TimeHours / Rtg.HoursPerYear, Upkeep.CriticalRequestW + Network.PowerW, Upkeep.DockRequestW);
            GenerationW = grid.GenerationW;
            RtgW = grid.RtgW;
            DemandW = grid.DemandW;
            BatteryPowerW = grid.ChargeW - grid.DischargeW;
            // Spare power (battery full or at its limit) is turned into build parts, which then pay the sites.
            double topUpW = Math.Min(grid.CurtailedW, Upkeep.TopUpRequestW);
            BuildPowerW = grid.CurtailedW - topUpW;
            CurtailedW = grid.CurtailedW - topUpW - BuildPowerW;
            Upkeep.Apply(Workforce, dt, grid.DockServedW, grid.PriorityServedW, topUpW, IsFlaringStar, Build, moving,
                         Materials, planet.AirTemperatureC);
            RobotChargingW = Upkeep.ChargingW;
            Network.SetPowered(!Grid.IsBrownout);
            Workforce.UpdateConnections(dt);
            AdvanceFieldWork(dt);
            Build.AddParts(BuildPowerW, dt);
            Settle(null);
            // Used power: what reached the loads, went into the battery or made build parts; curtailed power is not used.
            KardashevPowerW = grid.ServedW + grid.ChargeW + BuildPowerW + RobotChargingW;
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
            History.Add(dt, TimeHours, grid.GenerationW, grid.DemandW, grid.ServedW, BuildPowerW, soc, grid.IsBrownout);

            bool day = SunAt(BaseTileId).Up > 0.0;
            if (day != IsDayAtBase)
            {
                IsDayAtBase = day;
                DayNightChanged?.Invoke(day);
            }
        }

        double dayEnergyWh, dayHours;

        /// <summary>The averaging window for milestones: the planet's day, or 24 h on a tidally locked planet.</summary>
        double MilestoneDayHours => DayLengthHours > 0.0 ? DayLengthHours : 24.0;

        /// <summary>Used power averaged over the last full day (see <see cref="MilestoneDayHours"/>), W.</summary>
        public double DayAveragePowerW { get; private set; }

        /// <summary>Highest milestone reached so far (0–10; 10 is this planet's Type I target).</summary>
        public int MilestonesReached { get; private set; }

        public event Action<int> MilestoneReached;

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

        public PanelPreview PreviewPanel(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount)
                return new PanelPreview(false, "No such tile.", 0, 0, 0, 0);
            if (GetTile(tileId).IsWater)
                return new PanelPreview(false, "Panels cannot go on water yet.", 0, 0, 0, 0);
            double area = NewPanelAreaM2(tileId);
            PanelParams panel = MakePanel(tileId, tilt, azimuth, 1.0, area);
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
            double embodied = EmbodiedWhPerM2 * area;
            double eroi = energy > 0 ? Economics.Eroi(energy * PanelLifetimeYears, embodied) : 0.0;
            double payback = energy > 0 ? Economics.PaybackYears(embodied, energy) : double.PositiveInfinity;
            return new PanelPreview(true, "", energy, capacityFactor, eroi, payback);
        }

        public int SiteRings { get; set; } = TileSite.DefaultRings;

        public bool PlacePanel(int tileId, int slot, double tilt, double azimuth)
        {
            if (!CanPlacePanel(tileId, slot, tilt, azimuth))
                return false;
            double area = NewPanelAreaM2(tileId);
            // It waits as a site until a robot works on it; an idle robot takes it now (design D2).
            Build.Start(tileId, slot, PanelCost(area), 0.0, false);
            AddPanel(tileId, slot, tilt, azimuth, area);
            Occupy(tileId, FootprintFor(BuildingType.SolarPanel, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        // ---- Panel tiers (bigger installations per slot as milestones unlock them) ----

        /// <summary>Index into <see cref="PanelTiers.All"/> of what new panels are built as.</summary>
        public int PanelTier => PanelTiers.For(MilestonesReached);

        /// <summary>Ground one new panel covers on a tile, m²: the tier's size, capped at its footprint's share of the tile.</summary>
        public double NewPanelAreaM2(int tileId)
        {
            double area = PanelAreaM2 * PanelTiers.All[PanelTier].Scale;
            if (tileId >= 0 && tileId < TileCount)
                area = Math.Min(area, FootprintSize(BuildingType.SolarPanel) * tileAreaM2 / SlotCount(tileId));
            return area;
        }

        /// <summary>Ground the panel in a slot covers, m² (0 if there is none).</summary>
        public double PanelAreaAt(int tileId, int slot) => panels.TryGetValue((tileId, A(tileId, slot)), out var p) ? p.Area : 0.0;

        double PanelCost(double areaM2) => BuildCosts.Panel(EmbodiedWhPerM2, areaM2);

        bool CanPlacePanel(int tileId, int slot, double tilt, double azimuth) =>
            CanPlace(BuildingType.SolarPanel, tileId, slot) && PreviewPanel(tileId, tilt, azimuth).CanPlace;

        /// <summary>A saved panel's own slot is free and allowed (its footprint is put back from the save).</summary>
        bool CanRestorePanel(int tileId, int slot, double tilt, double azimuth) =>
            tileId >= 0 && tileId < TileCount && slot >= 0 && slot < SlotCount(tileId) && GetBuilding(tileId, slot) == BuildingType.None
            && PreviewPanel(tileId, tilt, azimuth).CanPlace;

        void AddPanel(int tileId, int slot, double tilt, double azimuth, double area)
        {
            panels[(tileId, slot)] = (tilt, azimuth, area);
            panelsPerTile[tileId] = PanelCount(tileId) + 1;
            GenerationW = ComputeGeneration() + RtgW;
        }

        public bool SetPanelAngles(int tileId, int slot, double tilt, double azimuth)
        {
            slot = A(tileId, slot);
            if (!panels.ContainsKey((tileId, slot)) || !PreviewPanel(tileId, tilt, azimuth).CanPlace)
                return false;
            panels[(tileId, slot)] = (tilt, azimuth, panels[(tileId, slot)].Area);
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        public bool OrderPanelAngles(int tileId, int slot, double tilt, double azimuth)
        {
            slot = A(tileId, slot);
            if (!panels.ContainsKey((tileId, slot)) || !PreviewPanel(tileId, tilt, azimuth).CanPlace)
                return false;
            if (Build.IsQueued(tileId, slot))
            {
                CancelAdjust(tileId, slot);
                return SetPanelAngles(tileId, slot, tilt, azimuth);   // not built yet: it is just built that way
            }
            int i = adjustOrders.FindIndex(o => o.Tile == tileId && o.Slot == slot);
            if (i >= 0)
                adjustOrders[i] = (tileId, slot, tilt, azimuth);
            else
                adjustOrders.Add((tileId, slot, tilt, azimuth));
            Settle(null);
            return true;
        }

        public bool TryGetPanelAngles(int tileId, int slot, out double tilt, out double azimuth)
        {
            slot = A(tileId, slot);
            bool found = panels.TryGetValue((tileId, slot), out var p);
            tilt = found ? p.Tilt : 0.0;
            azimuth = found ? p.Azimuth : 0.0;
            return found;
        }

        public bool TryGetAdjustOrder(int tileId, int slot, out double tilt, out double azimuth)
        {
            slot = A(tileId, slot);
            int i = adjustOrders.FindIndex(o => o.Tile == tileId && o.Slot == slot);
            tilt = i >= 0 ? adjustOrders[i].Tilt : 0.0;
            azimuth = i >= 0 ? adjustOrders[i].Azimuth : 0.0;
            return i >= 0;
        }

        public bool CancelAdjust(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (adjustOrders.RemoveAll(o => o.Tile == tileId && o.Slot == slot) == 0)
                return false;
            Workforce.Tasks.MarkCancelled(new WorkTask(TaskKind.Adjust, tileId, slot));
            Settle(null);
            return true;
        }

        // ---- Construction ----

        public double BuildStockWh => Build.StockWh;
        public double BuildPowerW { get; private set; }
        public int ConstructionQueueCount => Build.QueueCount;
        public double ConstructionRemainingWh => Build.RemainingWh;
        public bool IsUnderConstruction(int tileId, int slot) => Build.IsQueued(tileId, A(tileId, slot));
        public double ConstructionProgress(int tileId, int slot) => Build.Progress(tileId, A(tileId, slot));

        public double BuildCostWh(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.SolarPanel: return PanelCost(PanelAreaM2 * PanelTiers.All[PanelTier].Scale);
                case BuildingType.Battery: return BuildCosts.Battery(BatteryUnitWh);
                case BuildingType.Workshop: return RobotTech.WorkshopPartsWh;
                case BuildingType.ChargingStation: return RobotTech.ChargingStationPartsWh;
                case BuildingType.Relay: return RobotTech.RelayPartsWh;
                default: return 0.0;
            }
        }

        /// <summary>A site got all its parts: it starts working. A building finished by the command that placed it raises no event.</summary>
        void Finish(int tileId, int slot, bool raise)
        {
            if (batteries.Contains((tileId, slot)))
                Grid.Battery.AddCapacity(BatteryUnitWh);
            else
                GenerationW = ComputeGeneration() + RtgW;
            if (relays.Contains((tileId, slot)))
                RefreshNetwork();
            if (raise)
                BuildingCompleted?.Invoke(tileId, slot);
        }

        // ---- Robots at work ----

        /// <summary>Open work: construction sites in queue order, then salvage orders in the order asked.</summary>
        List<WorkTask> OpenTasks()
        {
            var tasks = new List<WorkTask>(Build.QueueCount + salvageOrders.Count);
            int i = 0;
            foreach (var site in Build.Sites())
                tasks.Add(new WorkTask(TaskKind.Build, site.Tile, site.Slot, i++));
            for (int k = 0; k < salvageOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Salvage, salvageOrders[k].Tile, salvageOrders[k].Slot, k));
            for (int k = 0; k < adjustOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Adjust, adjustOrders[k].Tile, adjustOrders[k].Slot, k));
            for (int k = 0; k < surveyOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Survey, surveyOrders[k], -1, k));
            for (int k = 0; k < measureOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Measure, measureOrders[k].Tile, (int)measureOrders[k].Parameter, k));
            for (int k = 0; k < miningOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Mine, miningOrders[k].Tile, (int)miningOrders[k].Material, k));
            List<(int Tile, int Slot)> shops = FinishedWorkshops();
            for (int k = 0; k < Math.Min(jobs.Count, shops.Count); k++)
                tasks.Add(new WorkTask(TaskKind.Manufacture, shops[k].Tile, shops[k].Slot, k));
            return tasks;
        }

        /// <summary>
        /// Hand out work, let worked sites take parts from stock and worked salvage orders take their building down,
        /// and repeat while anything finished (a freed robot takes the next task at once).
        /// </summary>
        void Settle((int Tile, int Slot)? silent)
        {
            while (true)
            {
                Workforce.Movement = CurrentMovement();
                Workforce.Milestones = MilestonesReached;
                Workforce.NowHours = TimeHours;
                Workforce.Assign(OpenTasks(), Needs());
                bool any = false;
                foreach (var done in Build.Pay(key => Workforce.IsWorkedOnSite(new WorkTask(TaskKind.Build, key.Tile, key.Slot))))
                {
                    any = true;
                    Finish(done.Tile, done.Slot, !(silent.HasValue && silent.Value == done));
                }
                foreach (var order in salvageOrders.ToArray())
                {
                    if (!Workforce.IsWorkedOnSite(new WorkTask(TaskKind.Salvage, order.Tile, order.Slot)))
                        continue;
                    any = true;
                    RemoveBuilding(order.Tile, order.Slot);
                    BuildingSalvaged?.Invoke(order.Tile, order.Slot);
                }
                foreach (var order in adjustOrders.ToArray())
                {
                    if (!Workforce.IsWorkedOnSite(new WorkTask(TaskKind.Adjust, order.Tile, order.Slot)))
                        continue;
                    any = true;
                    adjustOrders.Remove(order);
                    SetPanelAngles(order.Tile, order.Slot, order.Tilt, order.Azimuth);
                    PanelAdjusted?.Invoke(order.Tile, order.Slot);
                }
                if (!any)
                    return;
            }
        }

        public void RefreshWork() => Settle(null);

        /// <summary>
        /// What the colony needs now, for robots on the Demand program (D16): a brownout, or the grid battery under
        /// 30 % at night (TBD), and sites waiting for a robot.
        /// </summary>
        ColonyNeeds Needs() =>
            new ColonyNeeds(Grid.IsBrownout || (!IsDayAtBase && Grid.Battery.StateOfCharge < 0.30), Build.QueueCount > 0);

        /// <summary>Every open task now (sites, salvage and survey orders, mining, Workshop jobs), for the UI.</summary>
        public IReadOnlyList<WorkTask> OpenWork() => OpenTasks();

        public bool OrderSalvage(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (GetBuilding(tileId, slot) == BuildingType.None)
                return false;
            if (Build.IsQueued(tileId, slot))
                return RemoveBuilding(tileId, slot);   // a site is simply cancelled; its parts come back
            if (salvageOrders.Contains((tileId, slot)))
                return false;
            salvageOrders.Add((tileId, slot));
            Settle(null);
            return true;
        }

        public bool IsSalvageOrdered(int tileId, int slot) => salvageOrders.Contains((tileId, A(tileId, slot)));

        public bool CancelSalvage(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (!salvageOrders.Remove((tileId, slot)))
                return false;
            Workforce.Tasks.MarkCancelled(new WorkTask(TaskKind.Salvage, tileId, slot));
            Settle(null);
            return true;
        }

        /// <summary>A building is going away: a site gives back its parts, a finished one is salvaged.</summary>
        void Dismantle(int tileId, int slot, BuildingType type)
        {
            salvageOrders.Remove((tileId, slot));
            adjustOrders.RemoveAll(o => o.Tile == tileId && o.Slot == slot);
            if (Build.IsQueued(tileId, slot))
            {
                Build.Cancel(tileId, slot);
                Workforce.Tasks.MarkCancelled(new WorkTask(TaskKind.Build, tileId, slot));
                return;
            }
            // A panel gives back a share of what its own size cost, not the current tier's.
            Build.Salvage(type == BuildingType.SolarPanel && panels.TryGetValue((tileId, slot), out var p)
                ? PanelCost(p.Area) : BuildCostWh(type));
            if (type == BuildingType.Battery)
                Grid.Battery.RemoveCapacity(BatteryUnitWh);
        }

        public bool RemovePanel(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (!panels.ContainsKey((tileId, slot)))
                return false;
            Dismantle(tileId, slot, BuildingType.SolarPanel);
            panels.Remove((tileId, slot));
            Vacate(tileId, slot);
            panelsPerTile[tileId] = PanelCount(tileId) - 1;
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        public bool HasPanel(int tileId, int slot) => panels.ContainsKey((tileId, A(tileId, slot)));

        public double[] MonthlyEnergyWh(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater)
                return null;
            double[] months = MonthlyYield.Calculate(planet, MakePanel(tileId, tilt, azimuth, 1.0, NewPanelAreaM2(tileId)));
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

        /// <summary>What covers a slot: the building stored there, or the one whose footprint covers it.</summary>
        public BuildingType GetBuilding(int tileId, int slot) => GetBuildingAt(tileId, A(tileId, slot));

        BuildingType GetBuildingAt(int tileId, int slot) =>
            panels.ContainsKey((tileId, slot)) ? BuildingType.SolarPanel
            : batteries.Contains((tileId, slot)) ? BuildingType.Battery
            : workshops.Contains((tileId, slot)) ? BuildingType.Workshop
            : stations.Contains((tileId, slot)) ? BuildingType.ChargingStation
            : relays.Contains((tileId, slot)) ? BuildingType.Relay
            : BuildingType.None;

        public bool PlaceBattery(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.Battery, tileId, slot))
                return false;
            bool finished = Build.Start(tileId, slot, BuildCostWh(BuildingType.Battery), 0.0, false);
            AddBattery(tileId, slot);
            Occupy(tileId, FootprintFor(BuildingType.Battery, tileId, slot));
            if (finished)
                Grid.Battery.AddCapacity(BatteryUnitWh);
            Settle((tileId, slot));
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
            slot = A(tileId, slot);
            if (RemovePanel(tileId, slot))
                return true;
            if (workshops.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.Workshop);
                workshops.Remove((tileId, slot));
                Vacate(tileId, slot);
                return true;
            }
            if (relays.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.Relay);
                relays.Remove((tileId, slot));
                Vacate(tileId, slot);
                RefreshNetwork();
                return true;
            }
            if (stations.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.ChargingStation);
                stations.Remove((tileId, slot));
                Vacate(tileId, slot);
                return true;
            }
            if (!batteries.Contains((tileId, slot)))
                return false;
            Dismantle(tileId, slot, BuildingType.Battery);
            batteries.Remove((tileId, slot));
            Vacate(tileId, slot);
            batteriesPerTile[tileId] = BatteryCount(tileId) - 1;
            return true;
        }

        public SimulationState CaptureState()
        {
            var state = new SimulationState
            {
                PlanetId = PlanetId,
                Notebook = Notebook.Capture(),
                MilestonesReached = MilestonesReached,
                DayAveragePowerW = DayAveragePowerW,
                DayEnergyWh = dayEnergyWh,
                DayHours = dayHours,
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
                Workforce = Workforce.Capture(),
            };
            foreach (int tile in Deposits.Surveyed)
                state.SurveyedTiles.Add(tile);
            state.SurveyedTiles.Sort();
            state.SurveyOrders.AddRange(surveyOrders);
            state.MeasuredParameters = Parameters.Capture();
            state.LatitudePuzzles.AddRange(latitudePuzzles);
            state.ParameterPuzzles.AddRange(parameterPuzzles);
            state.RoughParameters = Parameters.CaptureRough();
            state.SurveyPuzzles.AddRange(surveyPuzzles);
            state.RoughSurveys.AddRange(roughSurveys);
            state.RoughSurveys.Sort();
            foreach (var mo in measureOrders)
                state.MeasureOrders.Add(new MeasureOrderRecord { Parameter = mo.Parameter, Tile = mo.Tile });
            foreach (var mo in miningOrders)
                state.MiningOrders.Add(new MiningOrderRecord { Tile = mo.Tile, Material = mo.Material });
            foreach (Material m in Resources.Materials.All)
                if (Materials[m] > 0.0)
                    state.MaterialsKg.Add(new MaterialRecord { Material = m, Kg = Materials[m] });
            foreach (var kv in fieldWork)
                state.FieldWork.Add(new FieldWorkRecord { Kind = kv.Key.Kind, Tile = kv.Key.Tile, Slot = kv.Key.Slot, Hours = kv.Value });
            foreach (var salvage in salvageOrders)
                state.SalvageOrders.Add(new SalvageOrderRecord { Tile = salvage.Tile, Slot = salvage.Slot });
            foreach (var adjust in adjustOrders)
                state.AdjustOrders.Add(new AdjustOrderRecord { Tile = adjust.Tile, Slot = adjust.Slot, Tilt = adjust.Tilt, Azimuth = adjust.Azimuth });
            foreach (var kv in panels)
                state.Buildings.Add(new BuildingRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Type = BuildingType.SolarPanel, Tilt = kv.Value.Tilt, Azimuth = kv.Value.Azimuth, AreaM2 = kv.Value.Area });
            foreach (var b in batteries)
                state.Buildings.Add(new BuildingRecord { Tile = b.Tile, Slot = b.Slot, Type = BuildingType.Battery });
            foreach (var w in workshops)
                state.Buildings.Add(new BuildingRecord { Tile = w.Tile, Slot = w.Slot, Type = BuildingType.Workshop });
            foreach (var c in stations)
                state.Buildings.Add(new BuildingRecord { Tile = c.Tile, Slot = c.Slot, Type = BuildingType.ChargingStation });
            foreach (var c in relays)
                state.Buildings.Add(new BuildingRecord { Tile = c.Tile, Slot = c.Slot, Type = BuildingType.Relay });
            foreach (ManufacturingJob j in jobs)
                state.Jobs.Add(new JobRecord
                {
                    Kind = j.Kind, RobotKind = j.RobotKind, SodiumBattery = j.SodiumBattery, AluminiumWiring = j.AluminiumWiring,
                    RobotId = j.RobotId, Module = j.Module, Output = j.Output, OutputKg = j.OutputKg, DoneHours = j.DoneHours,
                });
            state.Buildings.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : a.Slot.CompareTo(b.Slot));
            foreach (BuildingRecord r in state.Buildings)
                if (footprints.TryGetValue((r.Tile, r.Slot), out List<int> f))
                    for (int i = 1; i < f.Count; i++) r.Covered.Add(f[i]);
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
            foreach (var key in new List<(int Tile, int Slot)>(workshops))
                RemoveBuilding(key.Tile, key.Slot);
            foreach (var key in new List<(int Tile, int Slot)>(stations))
                RemoveBuilding(key.Tile, key.Slot);
            foreach (var key in new List<(int Tile, int Slot)>(relays))
                RemoveBuilding(key.Tile, key.Slot);
            TimeHours = state.TimeHours;
            MilestonesReached = Math.Max(0, Math.Min(10, state.MilestonesReached));
            DayAveragePowerW = Math.Max(0.0, state.DayAveragePowerW);
            dayEnergyWh = Math.Max(0.0, state.DayEnergyWh);
            dayHours = Math.Max(0.0, state.DayHours);
            targetReached = MilestonesReached >= 10;
            Notebook.Restore(state.Notebook);
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
            // Version 7 added robots; an older save starts with the one General robot at the base.
            if (state.Version < 7 || !Workforce.Restore(state.Workforce))
                Workforce.Reset(BaseTileId);
            Workforce.Network = Network;
            RefreshNetwork();   // the relays restored above start relaying; power follows the restored grid
            adjustOrders.Clear();
            if (state.AdjustOrders != null)   // version 17
                foreach (AdjustOrderRecord o in state.AdjustOrders)
                    if (panels.ContainsKey((o.Tile, o.Slot)))
                        adjustOrders.Add((o.Tile, o.Slot, o.Tilt, o.Azimuth));
            salvageOrders.Clear();
            if (state.SalvageOrders != null)
                foreach (SalvageOrderRecord o in state.SalvageOrders)
                    if (GetBuilding(o.Tile, o.Slot) != BuildingType.None && !Build.IsQueued(o.Tile, o.Slot) && !salvageOrders.Contains((o.Tile, o.Slot)))
                        salvageOrders.Add((o.Tile, o.Slot));
            // Version 9 added resources; older saves start with nothing surveyed or mined.
            Deposits.SetSurveyed(state.SurveyedTiles);
            Materials.Clear();
            surveyOrders.Clear();
            miningOrders.Clear();
            fieldWork.Clear();
            Parameters.Restore(state.MeasuredParameters);   // version 18
            Parameters.RestoreRough(state.RoughParameters);   // version 20
            surveyPuzzles.Clear();
            roughSurveys.Clear();
            if (state.SurveyPuzzles != null)   // version 21
                foreach (SurveyPuzzle p in state.SurveyPuzzles)
                    if (p != null && p.Tile >= 0 && p.Tile < TileCount && !Deposits.IsSurveyed(p.Tile))
                        surveyPuzzles.Add(p);
            if (state.RoughSurveys != null)
                foreach (int tile in state.RoughSurveys)
                    if (Deposits.IsSurveyed(tile))
                        roughSurveys.Add(tile);
            parameterPuzzles.Clear();
            if (state.ParameterPuzzles != null)
                foreach (ParameterPuzzle p in state.ParameterPuzzles)
                    if (p != null && p.Tile >= 0 && p.Tile < TileCount)
                        parameterPuzzles.Add(p);
            latitudePuzzles.Clear();
            if (state.LatitudePuzzles != null)   // version 19
                foreach (LatitudePuzzle p in state.LatitudePuzzles)
                    if (p != null && p.Tile >= 0 && p.Tile < TileCount)
                        latitudePuzzles.Add(p);
            measureOrders.Clear();
            if (state.MeasureOrders != null)
                foreach (MeasureOrderRecord mo in state.MeasureOrders)
                    if (mo.Tile >= 0 && mo.Tile < TileCount && !Parameters.IsMeasured(mo.Parameter) && !IsMeasureOrdered(mo.Parameter))
                        measureOrders.Add((mo.Parameter, mo.Tile));
            if (state.MaterialsKg != null)
                foreach (MaterialRecord m in state.MaterialsKg)
                    Materials.Set(m.Material, m.Kg);
            if (state.SurveyOrders != null)
                foreach (int tile in state.SurveyOrders)
                    if (tile >= 0 && tile < TileCount && !Deposits.IsSurveyed(tile) && !surveyOrders.Contains(tile))
                        surveyOrders.Add(tile);
            if (state.MiningOrders != null)
                foreach (MiningOrderRecord o in state.MiningOrders)
                    if (o.Tile >= 0 && o.Tile < TileCount && Deposits.IsKnown(o.Tile, o.Material) && Deposits.GradeOf(o.Tile, o.Material) > 0.0
                        && !miningOrders.Contains((o.Tile, o.Material)))
                        miningOrders.Add((o.Tile, o.Material));
            if (state.FieldWork != null)
                foreach (FieldWorkRecord f in state.FieldWork)
                    fieldWork[new WorkTask(f.Kind, f.Tile, f.Slot)] = Math.Max(0.0, f.Hours);
            // Version 10 added the Workshop's jobs; older saves have none.
            jobs.Clear();
            if (state.Jobs != null)
                foreach (JobRecord jr in state.Jobs)
                {
                    var j = new ManufacturingJob
                    {
                        Kind = jr.Kind, RobotKind = jr.RobotKind, SodiumBattery = jr.SodiumBattery, AluminiumWiring = jr.AluminiumWiring,
                        RobotId = jr.RobotId, Module = jr.Module, Output = jr.Output, OutputKg = jr.OutputKg, DoneHours = Math.Max(0.0, jr.DoneHours),
                    };
                    j.Recipe = RecipeOf(j);
                    jobs.Add(j);
                }
            Workforce.GroupCapacity = Math.Max(Workforce.GroupCapacity, RobotTech.GroupCapacity(MilestonesReached));
            Workforce.Movement = CurrentMovement();
            Workforce.Milestones = MilestonesReached;
            Workforce.NowHours = TimeHours;
            Workforce.Assign(OpenTasks(), Needs());
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        void Restore(BuildingRecord r, bool site)
        {
            if (r.Type == BuildingType.SolarPanel)
            {
                if (!CanRestorePanel(r.Tile, r.Slot, r.Tilt, r.Azimuth))
                    return;
                // Saves before version 5 have no area: their panels are base panel fields.
                double area = r.AreaM2 > 0.0 ? r.AreaM2 : PanelAreaM2;
                if (site) Build.RestoreSite(r.Tile, r.Slot, PanelCost(area), r.PaidWh);
                AddPanel(r.Tile, r.Slot, r.Tilt, r.Azimuth, area);
            }
            else if (r.Type == BuildingType.ChargingStation)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.ChargingStation), r.PaidWh);
                stations.Add((r.Tile, r.Slot));
            }
            else if (r.Type == BuildingType.Relay)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.Relay), r.PaidWh);
                relays.Add((r.Tile, r.Slot));
            }
            else if (r.Type == BuildingType.Workshop)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.Workshop), r.PaidWh);
                workshops.Add((r.Tile, r.Slot));
            }
            else if (r.Type == BuildingType.Battery)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.Battery), r.PaidWh);
                else Grid.Battery.AddCapacity(BatteryUnitWh);
                AddBattery(r.Tile, r.Slot);
            }
            // Its footprint, as saved (version 16); older saves: one slot each.
            if (GetBuildingAt(r.Tile, r.Slot) != BuildingType.None && r.Covered != null && r.Covered.Count > 0)
            {
                var f = new List<int> { r.Slot };
                foreach (int s in r.Covered)
                    if (s >= 0 && s < SlotCount(r.Tile) && GetBuilding(r.Tile, s) == BuildingType.None) f.Add(s);
                Occupy(r.Tile, f);
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
                                          CloudFactor(pair.Key.Tile, meanAnomaly), pair.Value.Area);
                total += PanelPowerNow(p, sun, out _, out _, out _, out _);
            }
            return total;
        }

        /// <summary>One panel's power under the star right now (sun up), with the light and temperature along the way.</summary>
        double PanelPowerNow(in PanelParams p, Double3 sun, out double dni, out double dhi, out double poa, out double cellT)
        {
            double airMass = Atmosphere.AirMassKastenYoung(SunPosition.ZenithDegrees(sun), p.PressureRatio);
            dni = Atmosphere.DirectNormal(currentIrradiance, planet.OpticalDepth, airMass);
            dhi = Atmosphere.DiffuseHorizontal(currentIrradiance, sun.Up, planet.OpticalDepth, airMass, planet.DiffuseK);
            double ghi = Atmosphere.GlobalHorizontal(dni, dhi, sun.Up);
            double cosTheta = PanelModel.CosIncidence(PanelModel.Normal(p.Tilt, p.Azimuth), sun);
            poa = PanelModel.PlaneOfArray(dni, dhi, ghi, cosTheta, p.Tilt, p.Albedo, p.FShade);
            cellT = PanelModel.CellTemperature(planet.AirTemperatureC, poa, p.Noct,
                                               planet.SurfacePressureEarth * p.PressureRatio, p.Efficiency);
            return PanelModel.Power(poa, p.Area, p.Efficiency, cellT, p.TempCoefficient, p.FSpec, p.FDust);
        }

        /// <summary>The sky over a tile right now and what a new panel at these angles would make (radians in).</summary>
        public SkyReport DescribeSky(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount)
                return default;
            const double deg = 180.0 / Math.PI;
            double meanAnomaly = CurrentMeanAnomaly();
            double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
            double lambda = (nu + planet.PerihelionLongitude) * deg % 360.0;
            if (lambda < 0.0) lambda += 360.0;
            double declination = Math.Asin(Math.Max(-1f, Math.Min(1f, StarDirection.Y))) * deg;
            double hourAngle = (Sphere.Longitude(tileId) - PlanetFrame.Longitude(StarDirection)) * deg;
            hourAngle = (hourAngle + 540.0) % 360.0 - 180.0;
            Double3 sun = SunAt(tileId);
            double cloud = CloudFactor(tileId, meanAnomaly);
            PanelParams p = MakePanel(tileId, tilt, azimuth, cloud, NewPanelAreaM2(tileId));
            double dni = 0.0, dhi = 0.0, poa = 0.0, cellT = planet.AirTemperatureC, power = 0.0;
            if (sun.Up > 0.0)
                power = PanelPowerNow(p, sun, out dni, out dhi, out poa, out cellT);
            return new SkyReport(planet.TidallyLocked, Sphere.Latitude(tileId) * deg, planet.Obliquity * deg, lambda,
                                 declination, hourAngle, planet.TidallyLocked ? double.NaN : SimClock.LocalTime24(hourAngle / deg),
                                 Math.Asin(Math.Max(-1.0, Math.Min(1.0, sun.Up))) * deg, SunPosition.AzimuthDegrees(sun),
                                 tilt * deg, azimuth * deg, PanelModel.Normal(tilt, azimuth).Dot(sun),
                                 currentIrradiance, dni, dhi, poa, cellT, power, SpectralFactor, cloud);
        }

        /// <summary>
        /// One panel on a tile. <paramref name="cloudFactor"/> (share of clear-sky light that gets through) goes into
        /// the panel's light-loss slot f_dust as 1 − factor: the game has no dust yet, and CloudClimate is meant to be
        /// applied to the panel's light as a whole. Longitude is measured from the sub-stellar point, which only
        /// matters on a tidally locked planet.
        /// </summary>
        PanelParams MakePanel(int tileId, double tilt, double azimuth, double cloudFactor, double area)
        {
            float elevation = Math.Max(0f, surface.Elevation[tileId]);
            double pressureRatio = Math.Exp(-elevation * 4000.0 / 8400.0); // elevation 1 ≈ 4 km, scale height 8.4 km
            double albedo = Terrains.Albedo(surface.Terrain[tileId]);
            return new PanelParams(Sphere.Latitude(tileId), tilt, azimuth, area, PanelEfficiency,
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

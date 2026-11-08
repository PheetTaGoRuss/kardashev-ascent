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
            return list;
        }

        /// <summary>Finished buildings the player asked a robot to take down, in the order asked.</summary>
        readonly List<(int Tile, int Slot)> salvageOrders = new List<(int, int)>();

        readonly HashSet<(int Tile, int Slot)> batteries = new HashSet<(int, int)>();
        readonly HashSet<(int Tile, int Slot)> workshops = new HashSet<(int, int)>();
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
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId)
                || GetTile(tileId).IsWater || GetBuilding(tileId, slot) != BuildingType.None)
                return false;
            Build.Start(tileId, slot, BuildCostWh(BuildingType.Workshop), 0.0, false);
            workshops.Add((tileId, slot));
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
        static WorkTask MineTask(int tile, Material m) => new WorkTask(TaskKind.Mine, tile, (int)m);

        public bool OrderSurvey(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater || Deposits.IsSurveyed(tileId) || surveyOrders.Contains(tileId))
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
                if (fieldWork.ContainsKey(st) && !Workforce.IsWorkedOnSite(st))
                    fieldWork.Remove(st);
            }
            foreach (Robot r in Workforce.Robots)
            {
                if (!r.Task.HasValue || r.Activity != RobotActivity.None || r.IsShutDown)
                    continue;
                WorkTask t = r.Task.Value;
                if (MovementEnabled && (r.IsTravelling || r.Tile != t.Tile))
                    continue;   // still driving there
                double speed = RobotAbilities.Speed(r.Kind, t.Kind) * (r.Has(RobotModule.FastDrive) ? RobotTech.FastDriveFactor : 1.0);
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
                if (t.Kind != TaskKind.Survey && t.Kind != TaskKind.Mine)
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
                    Deposits.MarkSurveyed(tile);
                    TileSurveyed?.Invoke(tile);
                }
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
            GridStep grid = Grid.Step(ComputeGeneration(), dt, TimeHours / Rtg.HoursPerYear, Upkeep.CriticalRequestW, Upkeep.DockRequestW);
            GenerationW = grid.GenerationW;
            RtgW = grid.RtgW;
            DemandW = grid.DemandW;
            BatteryPowerW = grid.ChargeW - grid.DischargeW;
            // Spare power (battery full or at its limit) is turned into build parts, which then pay the sites.
            double topUpW = Math.Min(grid.CurtailedW, Upkeep.TopUpRequestW);
            BuildPowerW = grid.CurtailedW - topUpW;
            CurtailedW = grid.CurtailedW - topUpW - BuildPowerW;
            Upkeep.Apply(Workforce, dt, grid.DockServedW, grid.PriorityServedW, topUpW, IsFlaringStar, Build, moving);
            RobotChargingW = Upkeep.ChargingW;
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
            Settle((tileId, slot));
            return true;
        }

        // ---- Panel tiers (bigger installations per slot as milestones unlock them) ----

        /// <summary>Index into <see cref="PanelTiers.All"/> of what new panels are built as.</summary>
        public int PanelTier => PanelTiers.For(MilestonesReached);

        /// <summary>Ground one new panel covers on a tile, m²: the tier's size, capped at the slot's share of the tile.</summary>
        public double NewPanelAreaM2(int tileId)
        {
            double area = PanelAreaM2 * PanelTiers.All[PanelTier].Scale;
            if (tileId >= 0 && tileId < TileCount)
                area = Math.Min(area, tileAreaM2 / SlotCount(tileId));
            return area;
        }

        /// <summary>Ground the panel in a slot covers, m² (0 if there is none).</summary>
        public double PanelAreaAt(int tileId, int slot) => panels.TryGetValue((tileId, slot), out var p) ? p.Area : 0.0;

        double PanelCost(double areaM2) => BuildCosts.Panel(EmbodiedWhPerM2, areaM2);

        bool CanPlacePanel(int tileId, int slot, double tilt, double azimuth) =>
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
            if (!panels.ContainsKey((tileId, slot)) || !PreviewPanel(tileId, tilt, azimuth).CanPlace)
                return false;
            panels[(tileId, slot)] = (tilt, azimuth, panels[(tileId, slot)].Area);
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
                case BuildingType.SolarPanel: return PanelCost(PanelAreaM2 * PanelTiers.All[PanelTier].Scale);
                case BuildingType.Battery: return BuildCosts.Battery(BatteryUnitWh);
                case BuildingType.Workshop: return RobotTech.WorkshopPartsWh;
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
            for (int k = 0; k < surveyOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Survey, surveyOrders[k], -1, k));
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

        public bool IsSalvageOrdered(int tileId, int slot) => salvageOrders.Contains((tileId, slot));

        public bool CancelSalvage(int tileId, int slot)
        {
            if (!salvageOrders.Remove((tileId, slot)))
                return false;
            Settle(null);
            return true;
        }

        /// <summary>A building is going away: a site gives back its parts, a finished one is salvaged.</summary>
        void Dismantle(int tileId, int slot, BuildingType type)
        {
            salvageOrders.Remove((tileId, slot));
            if (Build.IsQueued(tileId, slot))
            {
                Build.Cancel(tileId, slot);
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

        public BuildingType GetBuilding(int tileId, int slot) =>
            panels.ContainsKey((tileId, slot)) ? BuildingType.SolarPanel
            : batteries.Contains((tileId, slot)) ? BuildingType.Battery
            : workshops.Contains((tileId, slot)) ? BuildingType.Workshop
            : BuildingType.None;

        public bool PlaceBattery(int tileId, int slot)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId)
                || GetTile(tileId).IsWater || GetBuilding(tileId, slot) != BuildingType.None)
                return false;
            bool finished = Build.Start(tileId, slot, BuildCostWh(BuildingType.Battery), 0.0, false);
            AddBattery(tileId, slot);
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
            if (RemovePanel(tileId, slot))
                return true;
            if (workshops.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.Workshop);
                workshops.Remove((tileId, slot));
                return true;
            }
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
            foreach (var mo in miningOrders)
                state.MiningOrders.Add(new MiningOrderRecord { Tile = mo.Tile, Material = mo.Material });
            foreach (Material m in Resources.Materials.All)
                if (Materials[m] > 0.0)
                    state.MaterialsKg.Add(new MaterialRecord { Material = m, Kg = Materials[m] });
            foreach (var kv in fieldWork)
                state.FieldWork.Add(new FieldWorkRecord { Kind = kv.Key.Kind, Tile = kv.Key.Tile, Slot = kv.Key.Slot, Hours = kv.Value });
            foreach (var salvage in salvageOrders)
                state.SalvageOrders.Add(new SalvageOrderRecord { Tile = salvage.Tile, Slot = salvage.Slot });
            foreach (var kv in panels)
                state.Buildings.Add(new BuildingRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Type = BuildingType.SolarPanel, Tilt = kv.Value.Tilt, Azimuth = kv.Value.Azimuth, AreaM2 = kv.Value.Area });
            foreach (var b in batteries)
                state.Buildings.Add(new BuildingRecord { Tile = b.Tile, Slot = b.Slot, Type = BuildingType.Battery });
            foreach (var w in workshops)
                state.Buildings.Add(new BuildingRecord { Tile = w.Tile, Slot = w.Slot, Type = BuildingType.Workshop });
            foreach (ManufacturingJob j in jobs)
                state.Jobs.Add(new JobRecord
                {
                    Kind = j.Kind, RobotKind = j.RobotKind, SodiumBattery = j.SodiumBattery, AluminiumWiring = j.AluminiumWiring,
                    RobotId = j.RobotId, Module = j.Module, Output = j.Output, OutputKg = j.OutputKg, DoneHours = j.DoneHours,
                });
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
            foreach (var key in new List<(int Tile, int Slot)>(workshops))
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
            Workforce.Assign(OpenTasks(), Needs());
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        void Restore(BuildingRecord r, bool site)
        {
            if (r.Type == BuildingType.SolarPanel)
            {
                if (!CanPlacePanel(r.Tile, r.Slot, r.Tilt, r.Azimuth))
                    return;
                // Saves before version 5 have no area: their panels are base panel fields.
                double area = r.AreaM2 > 0.0 ? r.AreaM2 : PanelAreaM2;
                if (site) Build.RestoreSite(r.Tile, r.Slot, PanelCost(area), r.PaidWh);
                AddPanel(r.Tile, r.Slot, r.Tilt, r.Azimuth, area);
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

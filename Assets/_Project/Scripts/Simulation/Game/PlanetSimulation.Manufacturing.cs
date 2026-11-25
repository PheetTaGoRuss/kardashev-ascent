using KardashevAscent.Simulation.Logistics;
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
    public sealed partial class PlanetSimulation
    {
        public IReadOnlyList<ManufacturingJob> Jobs => jobs;
        public IReadOnlyList<IndustrialReport> IndustrialReports => industrialReports.Reports;

        void ReportIndustry(IndustrialBlocker blocker, string message) =>
            industrialReports.Add(TimeHours, "Robot Workshop", blocker, message);

        /// <summary>Finished Workshops in a fixed order (tile, then slot): job i is worked at Workshop i.</summary>
        List<(int Tile, int Slot)> FinishedWorkshops()
        {
            var list = new List<(int Tile, int Slot)>();
            foreach (var w in workshops)
                if (!Build.IsQueued(w.Tile, w.Slot) && WiredOrOff(w))   // an unwired Workshop has no power
                    list.Add(w);
            list.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : a.Slot.CompareTo(b.Slot));
            return list;
        }

        public int WorkshopCount => FinishedWorkshops().Count;

        public bool PlaceWorkshop(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.Workshop, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.Workshop), BuildTimes.Hours(BuildingType.Workshop));
            workshops.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.Workshop, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        /// <summary>Copper if there is enough, else aluminium once refining it is unlocked (and there is enough).</summary>
        bool UseAluminium(double kg) =>
            Materials[Material.Copper] < kg && UnlockLevel >= RobotTech.UnlockMilestone(Material.Aluminium)
            && Materials[Material.Aluminium] >= kg;

        bool UseSodium(double lithiumKg, double sodiumKg) =>
            Materials[Material.Lithium] < lithiumKg && UnlockLevel >= RobotTech.UnlockMilestone(Material.Sodium)
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
            if (missing.Length > 0)
                return "Missing " + missing + ".";
            foreach (var kv in need)
                Materials.TryTake(kv.Key, kv.Value);
            return null;
        }

        public string OrderRobot(RobotKind kind)
        {
            if (UnlockLevel < RobotTech.UnlockMilestone(kind))
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
            if (UnlockLevel < RobotTech.UnlockMilestone(module))
                return $"{RobotTech.ModuleName(module)} unlocks at milestone {RobotTech.UnlockMilestone(module)}.";
            if (r.Has(module)) return $"{r.Name} already has it.";
            if (module == RobotModule.AdvancedProcessor && !r.Has(RobotModule.Processor))
                return $"{r.Name} needs a processor first.";
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

        /// <summary>Queue a batch of the usual process for this material (refinery modules do it now: director 2026-11-15).</summary>
        public string OrderRefine(Material output)
        {
            RefineProcess? p = Refining.For(output);
            if (!p.HasValue)
            {
                string no = $"{Resources.Materials.Name(output)} can't be refined.";
                ReportIndustry(IndustrialBlocker.EnvironmentUnavailable, no);
                return no;
            }
            return OrderRefine(p.Value);
        }

        public bool CancelJob(int index)
        {
            if (index < 0 || index >= jobs.Count)
                return false;
            ManufacturingJob j = jobs[index];
            jobs.RemoveAt(index);
            foreach (var (m, kg) in j.Recipe.Inputs)
                Materials.Add(m, kg);
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
                    // Mass is conserved: the rock that went in comes out as the metal plus slag.
                    double rockInKg = 0.0;
                    foreach (var input in j.Recipe.Inputs)
                        if (input.Material == Material.Rock) rockInKg += input.Kg;
                    double slagKg = Math.Max(0.0, rockInKg - j.OutputKg);
                    Materials.Add(j.Output, j.OutputKg);
                    if (slagKg > 0.0) Materials.Add(Material.Slag, slagKg);
                    ReportIndustry(IndustrialBlocker.None, $"Refining completed: {j.OutputKg:0} kg {Resources.Materials.Name(j.Output).ToLowerInvariant()} is available" +
                                   (slagKg > 0.0 ? $", with {slagKg:0} kg of slag." : "."));
                    break;
            }
        }
        readonly Dictionary<int, int> batteriesPerTile = new Dictionary<int, int>();

        public PlanetSimulation(HexSphere sphere, PlanetSurface surface, PlanetSolarParams planet,
                                    double planetRadiusM = Kardashev.EarthRadiusM, int baseTileId = -1)
        {
            Sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            this.planet = planet;
            this.planetRadiusM = planetRadiusM;
            tileAreaM2 = 4.0 * Math.PI * planetRadiusM * planetRadiusM / sphere.TileCount;
            BaseTileId = baseTileId >= 0 ? baseTileId : PickBaseTile();
            Workforce.Reset(BaseTileId);
            inventories.Adopt(PlaceId.Depot(BaseTileId), Materials);   // the lander depot is the colony's old single stock
            inventories.ClassCapacity = ClassCapacityKg;
            Upkeep.CargoKgOf = r => inventories.MassAt(PlaceId.Cargo(r.Id)) + TowedKgBy(r.Id);   // cargo loads the drive
            // The lander's cargo: materials for the first buildings (game rule, TBD).
            foreach (var (m, kg) in BuildCosts.LanderCargo.Items())
                Materials.Add(m, kg);
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
        public PlanetSimulation(HexSphere sphere, PlanetSurface surface, PlanetRecord record, int baseTileId = -1)
            : this(sphere, surface, (record ?? throw new ArgumentNullException(nameof(record))).ToSolarParams(),
                   record.planet.radiusM > 0.0 ? record.planet.radiusM : Kardashev.EarthRadiusM, baseTileId)
        {
            PlanetId = record.id ?? "";
            TerrainSeed = record.look?.terrainSeed ?? DefaultTerrainSeed;
            StarTemperatureK = record.star.teffK;
            Clouds = record.Clouds;
            Deposits = new DepositMap(sphere, surface, record.ToChemistry(), record.look?.terrainSeed ?? 7);
            if (record.planet.surfaceGravityMs2 > 0.0)
            {
                Routes = new RouteFinder(sphere, surface, record.planet.surfaceGravityMs2);
                SurfaceGravityMs2 = record.planet.surfaceGravityMs2;
            }
            else if (record.planet.massKg > 0.0 && record.planet.radiusM > 0.0)
                SurfaceGravityMs2 = GravitationalConstant * record.planet.massKg / (record.planet.radiusM * record.planet.radiusM);
        }
    }
}

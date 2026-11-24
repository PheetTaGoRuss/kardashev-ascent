using System;
using System.Diagnostics;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// The mission operations long runs (P5): 60 days on Mars (dry, so no copper ore: the colony needs aluminium for copper) and
    /// on LHS 1140 b (1.87 g, so haulage shows). Mass and energy close, the runs are deterministic, and 300 robots with 20 plants
    /// stay inside the stepping budget (KI-34). Planet values are from planets.json (NASA NSSDC Mars Fact Sheet; NASA Exoplanet Archive).
    /// </summary>
    public class MissionOperationsScenarioTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetRecord Mars()
        {
            var r = new PlanetRecord { id = "mars", displayName = "Mars" };
            r.solar.luminosityW = 3.828e26; r.solar.semiMajorAxisM = 227956038589.6944; r.solar.eccentricity = 0.0935;
            r.solar.yearLengthHours = 16487.52; r.solar.dayLengthHours = 24.6597; r.solar.obliquityDeg = 25.19;
            r.solar.perihelionLongitudeDeg = 250.87; r.solar.opticalDepth = 0.5; r.solar.surfacePressureEarth = 0.00628;
            r.solar.diffuseK = 0.3; r.solar.airTemperatureC = -59.0;
            r.star.teffK = 5772.0; r.star.count = 1;
            r.planet.radiusM = 3393149.2; r.planet.massKg = 6.390254e23; r.planet.surfaceGravityMs2 = 3.7044004790761145;
            r.planet.bondAlbedo = 0.25;
            r.flags.clouds = "none"; r.flags.surface = "rocky";
            r.look.terrainSeed = 7; r.look.water = "none"; r.look.oceanFraction = 0.33;
            r.chemistry.metallicityDex = 0.0; r.chemistry.ironMassFraction = 0.32;
            return r;
        }

        static PlanetRecord Lhs1140b()
        {
            var r = new PlanetRecord { id = "lhs-1140-b", displayName = "LHS 1140 b" };
            r.solar.luminosityW = 1.4546279534250807e24; r.solar.semiMajorAxisM = 14151958568.22; r.solar.eccentricity = 0.043;
            r.solar.yearLengthHours = 593.69352; r.solar.dayLengthHours = 0.0; r.solar.obliquityDeg = 0.0;
            r.solar.opticalDepth = 0.32; r.solar.surfacePressureEarth = 1.0; r.solar.diffuseK = 0.3; r.solar.airTemperatureC = -47.15;
            r.star.teffK = 3096.0; r.star.radiusM = 150201630.0; r.star.massKg = 3.66673868e29; r.star.count = 1; r.star.distancePc = 14.9861;
            r.planet.radiusM = 11034113.0; r.planet.massKg = 3.344432e25; r.planet.surfaceGravityMs2 = 18.333831659114153;
            r.planet.bondAlbedo = 0.3;
            r.flags.tidallyLocked = true; r.flags.clouds = "none"; r.flags.surface = "uncertain";
            r.look.terrainSeed = 7; r.look.water = "liquid"; r.look.oceanFraction = 0.62;
            r.chemistry.metallicityDex = -0.15; r.chemistry.ironMassFraction = 0.32;
            return r;
        }

        /// <summary>A colony with a Workshop, an Alumina plant and an Electrolysis cell, plenty of panels and batteries, a charger and the starter rulebook.</summary>
        static PlanetSimulation Colony(PlanetRecord record, int panels = 40)
        {
            var sim = new PlanetSimulation(Sphere, Surface, record) { ConstructionTimeEnabled = false, LogisticsEnabled = true };
            sim.Workforce.Reset(sim.BaseTileId, 3);
            sim.Debug.FreeBuilding = true;
            int t = sim.BaseTileId;
            // plants first, so the modules find slots touching the Workshop
            for (int s = 0; s < sim.SlotCount(t); s++)
                if (sim.GetBuilding(t, s) == BuildingType.None && sim.PlaceWorkshop(t, s)) break;
            foreach (BuildingType type in new[] { BuildingType.AluminaPlant, BuildingType.Electrolyser })
                for (int s = 0; s < sim.SlotCount(t); s++)
                    if (sim.GetBuilding(t, s) == BuildingType.None && sim.RefineryBlocker(type, t, s) == null) { sim.PlaceRefinery(type, t, s); break; }
            int slot = 0;
            int Free() { while (slot < sim.SlotCount(t) && sim.GetBuilding(t, slot) != BuildingType.None) slot++; return slot; }
            for (int i = 0; i < panels && Free() < sim.SlotCount(t); i++) sim.PlacePanel(t, Free(), 0.35, Math.PI);
            for (int i = 0; i < 2 && Free() < sim.SlotCount(t); i++) sim.PlaceBattery(t, Free());
            if (Free() < sim.SlotCount(t)) sim.PlaceChargingStation(t, Free());
            sim.Debug.FreeBuilding = false;
            sim.InstallStarterRulebook();
            return sim;
        }

        static string Fingerprint(PlanetSimulation sim)
        {
            SimulationState s = sim.CaptureState();
            return $"{sim.TimeHours:R}|{sim.BatteryStoredWh:R}|" + string.Join(",", s.MaterialsKg.Select(m => $"{m.Material}={m.Kg:R}"))
                   + "|" + string.Join(";", s.Inventories.Select(r => $"{r.Kind}{r.Id}:" + string.Join(",", r.Stock.Select(m => $"{m.Material}={m.Kg:R}"))))
                   + "|" + string.Join(",", s.VentedKg.Select(m => $"{m.Material}={m.Kg:R}")) + "|" + s.HaulWh.ToString("R") + "|" + s.OperationsLog.Count;
        }

        /// <summary>Run for days of game time, checking the energy balance on every step; returns the worst mismatch, W.</summary>
        static string WorstAt = "";
        static double RunChecked(PlanetSimulation sim, double days)
        {
            double worst = 0.0;
            for (double h = 0.0; h < days * 24.0; h += 1.0)
            {
                sim.Tick(1.0);
                double discharge = Math.Max(0.0, -sim.BatteryPowerW);
                double err = Math.Abs(sim.GenerationW + discharge - (sim.KardashevPowerW + sim.CurtailedW));
                if (err > worst) { worst = err; WorstAt = $"t={sim.TimeHours:0}h gen {sim.GenerationW:0} discharge {discharge:0} kard {sim.KardashevPowerW:0} curt {sim.CurtailedW:0} brown {sim.IsBrownout} charging {sim.RobotChargingW:0} workshop {sim.WorkshopPowerW:0} demand {sim.DemandW:0} batt {sim.BatteryPowerW:0}"; }
            }
            return worst;
        }

        [Test]
        public void Mars_60Days_ACopperStarvedSiteIsBuiltWithRefinedAluminium_MassAndEnergyClose()
        {
            PlanetRecord mars = Mars();
            var sim = Colony(mars);
            sim.WeatherEnabled = false;   // this scenario tests the logistics chain; the dust of Mars is tested in ThinAirClimateTests and the balance run
            Assert.IsFalse(mars.HasLiquidWater, "Mars is dry: no seas, so no copper or lithium ore");
            int baseTile = sim.BaseTileId;
            int outpost = Sphere.Neighbors[baseTile].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            // the lander's copper is gone; refine aluminium from rock to take its place
            sim.Materials.Set(Material.Copper, 0.0);
            double rockBefore = sim.Materials[Material.Rock];
            for (int i = 0; i < 3; i++)
            {
                sim.OrderRefine(RefineProcess.AluminaFromRock);
                sim.OrderRefine(RefineProcess.Aluminium);
            }
            Assert.IsTrue(sim.PlaceChargingStation(outpost, 0), "a charging station at the outpost wants copper wiring");
            double worst = RunChecked(sim, 60.0);
            Assert.Less(worst, 1.0, "energy closes on every step of the 60 days, on Mars: " + WorstAt);
            Assert.IsFalse(sim.IsUnderConstruction(outpost, 0), "built within 60 days: aluminium " + string.Join(",", sim.Holdings().Where(h => h.Material == Material.Aluminium || h.Material == Material.Alumina).Select(h => h.Kind + h.Id.ToString() + ":" + h.Material + "=" + h.Kg.ToString("0.0"))) + " need " + sim.ConstructionRemainingAt(outpost, 0).Describe() + " standins " + sim.StandInsAt(outpost, 0) + " lines " + string.Join(";", sim.RefineryLines().Select(l => l.Kind + ":" + l.Process + ":" + l.Progress.ToString("0.00"))) + " queue " + sim.RefineQueue.Count + " reports " + string.Join("|", sim.IndustrialReports.Select(r => r.Message).TakeLast(3)));
            Assert.AreNotEqual(StandIn.None, sim.StandInsAt(outpost, 0) & StandIn.AluminiumForCopper, "with aluminium refined from rock in place of copper");
            Assert.Greater(sim.HaulTotals.KgMoved, 0.0, "aluminium was hauled to the outpost");
            Assert.Greater(sim.VentedKg(Material.Oxygen), 0.0, "the electrolysis gave off oxygen, accounted for");
            foreach (Material m in Materials.All) Assert.GreaterOrEqual(sim.TotalKg(m), -1e-9, m + " never goes negative");
            FlowsheetSnapshot snap = sim.FlowsheetSnapshot(7);
            foreach (ProcessFlow p in snap.Processes) Assert.AreEqual(100.0, p.ClosurePct, 0.01, p.Name + " closes in mass");
            Assert.Less(sim.TotalKg(Material.Rock), rockBefore, "the alumina took rock");
        }

        /// <summary>
        /// The same copper-starved Mars outpost with the weather on (Weather W8, 2026-11-24). The control above runs with it off. With weather on the first
        /// version lost all three robots in the open at -46 to -59 C: the robot's reserve for the trip home counted the drive but not the heater that runs on the way,
        /// so they set off back with too little charge. The reserve now includes the heater (RobotUpkeep.Plan); the physics and the figures are unchanged.
        /// </summary>
        [Test]
        public void Mars_WithWeatherOn_TheSameSiteIsBuilt_AndNoRobotIsLost()
        {
            PlanetRecord mars = Mars();
            var sim = Colony(mars);
            Assert.IsTrue(sim.HasWeather);
            int outpost = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            sim.Materials.Set(Material.Copper, 0.0);
            for (int i = 0; i < 3; i++) { sim.OrderRefine(RefineProcess.AluminaFromRock); sim.OrderRefine(RefineProcess.Aluminium); }
            Assert.IsTrue(sim.PlaceChargingStation(outpost, 0));
            double worst = RunChecked(sim, 60.0);
            Assert.Less(worst, 1.0, "energy closes on every step with the weather on");
            Assert.AreEqual(0, sim.RobotsDestroyed, "no robot is lost on the way home: " + string.Join(" | ", sim.OperationsLog.Where(r => r.Quantity == "Robot destroyed").Select(r => r.Text)));
            Assert.IsFalse(sim.IsUnderConstruction(outpost, 0), "built within 60 days with the weather on: " + sim.ConstructionRemainingAt(outpost, 0).Describe());
            foreach (Material m in Materials.All) Assert.GreaterOrEqual(sim.TotalKg(m), -1e-9, m + " never goes negative");
        }

        [Test]
        public void Lhs1140b_HaulingCostsGravityTimesTheEnergyItCostsOnMars()
        {
            double Wh(PlanetRecord rec, out double kgKm)
            {
                var sim = Colony(rec, panels: 60);
                int tile = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
                sim.SetStockTarget(tile, Material.Iron, 400.0);
                RunChecked(sim, 60.0);
                Assert.AreEqual(400.0, sim.KgAt(tile, Material.Iron), 1e-6, rec.id + ": the target was met");
                kgKm = sim.HaulTotals.KgKm;
                return sim.HaulTotals.Wh;
            }
            double marsWh = Wh(Mars(), out double marsKgKm);
            double lhsWh = Wh(Lhs1140b(), out double lhsKgKm);
            // Rolling-resistance energy per hop grows with gravity; a hop is as long as the planet is wide, so per kg-km it goes as g / radius.
            double g = 18.333831659114153 / 3.7044004790761145, radius = 11034113.0 / 3393149.2;
            double perMars = marsWh / marsKgKm, perLhs = lhsWh / lhsKgKm;
            Assert.AreEqual(g / radius, perLhs / perMars, g / radius * 0.25, "per kg-km, haulage goes as g over the planet's radius");
            Assert.Greater(lhsWh, marsWh * 3.0, "and the same haul costs far more energy on the heavy planet");
        }

        [Test]
        public void SixtyDays_AreDeterministic()
        {
            string Run()
            {
                var sim = Colony(Mars());
                int tile = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater);
                sim.Materials.Set(Material.Copper, 0.0);
                sim.OrderRefine(RefineProcess.AluminaFromRock);
                sim.OrderRefine(RefineProcess.Aluminium);
                sim.SetStockTarget(tile, Material.Rock, 300.0);
                RunChecked(sim, 60.0);
                return Fingerprint(sim);
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void ASaveInTheMiddle_LoadsAndTheRunCarriesOn()
        {
            var sim = Colony(Mars());
            int tile = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater);
            sim.SetStockTarget(tile, Material.Iron, 300.0);
            sim.OrderRefine(RefineProcess.AluminaFromRock);
            RunChecked(sim, 20.0);
            SimulationState state = sim.CaptureState();
            var other = new PlanetSimulation(Sphere, Surface, Mars()) { ConstructionTimeEnabled = false };
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(Fingerprint(sim), Fingerprint(other), "the loaded colony is the saved one");
            double worst = RunChecked(other, 40.0);
            Assert.Less(worst, 1.0);
            Assert.AreEqual(300.0, other.KgAt(tile, Material.Iron), 1e-6);
        }

        [Test]
        public void ThreeHundredRobotsAndTwentyPlants_StayInsideTheSteppingBudget()
        {
            var sim = Colony(Mars(), panels: 6);
            int t = sim.BaseTileId;
            sim.Debug.FreeBuilding = true;
            int plants = 0;
            for (int w = 0; w < 6; w++)
                for (int s = 0; s < sim.SlotCount(t); s++)
                    if (sim.GetBuilding(t, s) == BuildingType.None && sim.PlaceWorkshop(t, s)) break;
            foreach (BuildingType type in new[] { BuildingType.AluminaPlant, BuildingType.Electrolyser })
                for (int round = 0; round < 12; round++)
                    for (int s = 0; s < sim.SlotCount(t); s++)
                        if (sim.GetBuilding(t, s) == BuildingType.None && sim.RefineryBlocker(type, t, s) == null)
                        {
                            if (sim.PlaceRefinery(type, t, s)) plants++;
                            break;
                        }
            sim.Debug.FreeBuilding = false;
            Assert.GreaterOrEqual(sim.RefineryLines().Count, 10, "plants placed: " + plants);
            while (sim.Workforce.Robots.Count < 300) sim.Workforce.AddRobot(RobotKind.General, t);
            sim.Materials.Add(Material.Rock, 20000.0);
            sim.Materials.Add(Material.Anorthosite, 5000.0);
            for (int i = 0; i < 20; i++) { sim.OrderRefine(RefineProcess.AluminaFromRock); sim.OrderRefine(RefineProcess.AluminaFromAnorthosite); }
            sim.SetStockTarget(Sphere.Neighbors[t].First(n => !sim.GetTile(n).IsWater), Material.Iron, 500.0);
            for (int i = 0; i < 40; i++) sim.Tick(0.25);   // warm up
            var watch = Stopwatch.StartNew();
            const int steps = 400;
            for (int i = 0; i < steps; i++) sim.Tick(0.25);
            watch.Stop();
            double msPerStep = watch.Elapsed.TotalMilliseconds / steps;
            TestContext.WriteLine($"mission operations step cost with {sim.Workforce.Robots.Count} robots and {sim.RefineryLines().Count} plants: {msPerStep:0.00} ms per 0.25 h step");
            Assert.Less(msPerStep, 25.0, "KI-34 measured about 5 ms per step for 300 robots; the budget at 1 d/s (96 steps a second) is about 10 ms; 25 ms guards against a big regression on a slow machine");
        }
    }
}

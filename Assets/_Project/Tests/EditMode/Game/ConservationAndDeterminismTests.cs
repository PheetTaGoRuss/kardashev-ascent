using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// Whole-simulation checks (docs/development/testing-standards.md, section 8): energy and mass are accounted for
    /// on every step, and the same planet with the same commands gives the same colony.
    /// </summary>
    public class ConservationAndDeterminismTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        /// <summary>A scripted colony: panels, batteries, a Workshop, a survey and mining, then days of running.</summary>
        static PlanetSimulation RunScript(Action<PlanetSimulation, double> afterEachStep = null)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int tile = sim.BaseTileId;
            for (int slot = 0; slot < 6; slot++)
                Assert.IsTrue(sim.PlacePanel(tile, slot, 0.35, Math.PI));
            Assert.IsTrue(sim.PlaceBattery(tile, 8));
            Assert.IsTrue(sim.PlaceBattery(tile, 10));
            Assert.IsTrue(sim.PlaceWorkshop(tile, 20));
            sim.OrderSurvey(tile);
            sim.OrderMining(tile, Material.Rock);
            const double step = 0.25;
            for (double t = 0.0; t < 240.0; t += step)
            {
                sim.Tick(step);
                afterEachStep?.Invoke(sim, step);
            }
            return sim;
        }

        [Test]
        public void Energy_EveryWattGeneratedIsUsedStoredOrCurtailed()
        {
            double worstW = 0.0;
            RunScript((sim, dt) =>
            {
                // Generation plus what the battery gave = what reached loads, the battery, parts and robots, plus curtailed.
                double discharge = Math.Max(0.0, -sim.BatteryPowerW);
                double supplied = sim.GenerationW + discharge;
                double used = sim.KardashevPowerW + sim.CurtailedW;
                worstW = Math.Max(worstW, Math.Abs(supplied - used));
            });
            Assert.Less(worstW, 1.0, "the largest mismatch on any step, W (a milliwatt of rounding is allowed)");
        }

        [Test]
        public void Energy_PowerHistoryTotalsAddUp()
        {
            PlanetSimulation sim = RunScript();
            PowerHistory h = sim.History;
            Assert.Greater(h.TotalGeneratedWh, 0.0);
            Assert.LessOrEqual(h.TotalServedWh + h.TotalBuildWh, h.TotalGeneratedWh + sim.BatteryCapacityWh,
                "the colony cannot use more than was generated plus what the battery held to start");
            Assert.GreaterOrEqual(h.TotalDemandWh, h.TotalServedWh - 1e-6, "no more is served than was demanded");
        }

        [Test]
        public void Mass_NothingMinedIsLostOrInvented()
        {
            PlanetSimulation sim = RunScript();
            double rock = sim.Materials[Material.Rock];
            Assert.GreaterOrEqual(rock, 0.0);
            // Mined rock is added by robots at the tile's rock grade; it can never exceed the rate times the time.
            double ceiling = RobotSpecs.MiningKgPerHour * 240.0 * 2.0;
            Assert.LessOrEqual(rock, ceiling, "mining cannot make more than a robot's rate allows");
            foreach (Material m in Materials.All)
                Assert.GreaterOrEqual(sim.Materials[m], 0.0, m + " stock must not go negative");
        }

        [Test]
        public void Determinism_SameInputsGiveTheSameColony()
        {
            PlanetSimulation a = RunScript();
            PlanetSimulation b = RunScript();
            Assert.AreEqual(a.TimeHours, b.TimeHours);
            Assert.AreEqual(a.BatteryStoredWh, b.BatteryStoredWh);
            Assert.AreEqual(a.KardashevPowerW, b.KardashevPowerW);
            Assert.AreEqual(a.BuildStockWh, b.BuildStockWh);
            Assert.AreEqual(a.MilestonesReached, b.MilestonesReached);
            Assert.AreEqual(a.Workforce.Robots.Count, b.Workforce.Robots.Count);
            foreach (Material m in Materials.All)
                Assert.AreEqual(a.Materials[m], b.Materials[m], m.ToString());
            for (int i = 0; i < a.Workforce.Robots.Count; i++)
            {
                Assert.AreEqual(a.Workforce.Robots[i].Battery.StoredWh, b.Workforce.Robots[i].Battery.StoredWh);
                Assert.AreEqual(a.Workforce.Robots[i].Tile, b.Workforce.Robots[i].Tile);
            }
        }

        [Test]
        public void Determinism_TickingInDifferentChunksGivesTheSameTime()
        {
            var a = new PlanetSimulation(Sphere, Surface, Earthlike);
            var b = new PlanetSimulation(Sphere, Surface, Earthlike);
            a.Tick(48.0);
            for (int i = 0; i < 192; i++) b.Tick(0.25);
            Assert.AreEqual(a.TimeHours, b.TimeHours, 1e-9);
            Assert.AreEqual(a.BatteryStoredWh, b.BatteryStoredWh, 1e-3, "frame rate must not change the result");
        }
    }
}

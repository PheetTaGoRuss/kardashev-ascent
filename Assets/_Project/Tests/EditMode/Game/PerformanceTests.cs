using System;
using System.Diagnostics;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// A first performance budget (docs/development/testing-standards.md, section 8). The limits are deliberately loose
    /// so a slow machine does not fail them; they exist to catch a step that suddenly takes ten times longer.
    /// </summary>
    public class PerformanceTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        /// <summary>Wall-clock milliseconds one 0.25 h simulation step takes, averaged over a day of game time.</summary>
        static double MillisecondsPerStep(PlanetSimulation sim)
        {
            sim.Tick(1.0);   // warm up
            var watch = Stopwatch.StartNew();
            const int steps = 96;   // 24 game hours
            for (int i = 0; i < steps; i++) sim.Tick(0.25);
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / steps;
        }

        [Test]
        public void Step_WithAFewRobots_IsFast()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            double ms = MillisecondsPerStep(sim);
            TestContext.WriteLine($"1 robot: {ms:0.000} ms per step");
            Assert.Less(ms, 50.0, "one simulation step must stay far below a frame");
        }

        [Test]
        public void Step_WithHundredsOfRobots_StaysInsideTheBudget()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int tile = sim.BaseTileId;
            for (int slot = 0; slot < 6; slot++) sim.PlacePanel(tile, slot, 0.35, Math.PI);
            for (int i = 0; i < 300; i++) sim.Workforce.AddRobot(RobotKind.General, tile);
            sim.OrderSurvey(tile);
            sim.OrderMining(tile, KardashevAscent.Simulation.Resources.Material.Rock);
            double ms = MillisecondsPerStep(sim);
            TestContext.WriteLine($"{sim.Workforce.Robots.Count} robots: {ms:0.000} ms per step");
            Assert.Less(ms, 50.0, "about ten times the 5 ms measured on 2026-11-13 with 300 robots");
        }
    }
}

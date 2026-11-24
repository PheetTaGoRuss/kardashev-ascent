using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Weather (PL-07, W5): on a storm day robots stop outdoor work and shelter; the colony is told once.</summary>
    public class StormTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Earthlike => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);

        /// <summary>Step the colony until a storm is over the base (the weather is deterministic), at most a few game years.</summary>
        static bool TickToBaseStorm(PlanetSimulation sim)
        {
            for (int i = 0; i < 24 * 365 * 4; i++) { if (sim.StormAt(sim.BaseTileId)) return true; sim.Tick(1.0); }
            return false;
        }

        [Test]
        public void ARobotUnderAStormIsHeldAndDoesNoOutdoorWork_AndIsFreeAgainWhenItPasses()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Robot r = sim.Workforce.Robots[0];
            Assert.IsTrue(TickToBaseStorm(sim), "a storm comes within four years");
            sim.Tick(0.01);
            Assert.IsTrue(r.StormHold);
            Assert.IsFalse(r.Available, "no new work in a storm");
            for (int i = 0; i < 24 * 30 && sim.StormAt(sim.BaseTileId); i++) sim.Tick(1.0);
            sim.Tick(0.01);
            Assert.IsFalse(r.StormHold);
        }

        [Test]
        public void TheColonyIsToldOnceWhenAStormReachesTheBase_AsAnActNowEntry()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(TickToBaseStorm(sim));
            sim.Tick(0.01);
            var entries = sim.OperationsLog.Where(x => x.Quantity == "Storm").ToList();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(LogPriority.ActNow, entries[0].Priority);
            sim.Tick(0.5);
            Assert.AreEqual(1, sim.OperationsLog.Count(x => x.Quantity == "Storm"), "not again while it lasts");
        }

        [Test]
        public void AnOutdoorRobotUnderAStormGoesToShelter()
        {
            var probe = new PlanetSimulation(Sphere, Surface, Earthlike);
            int far = Enumerable.Range(0, probe.TileCount).First(t => !probe.GetTile(t).IsWater && probe.Routes.HoursBetween(probe.BaseTileId, t) > 8.0 && probe.Routes.HoursBetween(probe.BaseTileId, t) < 20.0);
            double when = -1;
            for (int i = 0; i < 24 * 365 * 6; i++) { if (probe.StormAt(far)) { when = probe.TimeHours; break; } probe.Tick(1.0); }
            Assert.GreaterOrEqual(when, 0.0, "a storm over the far tile within six years");
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Tick(when - 1.0);   // the weather is a function of time only, so the same storm comes
            Robot r = sim.Workforce.AddRobot(RobotKind.General, far);
            sim.Tick(1.0);
            Assert.IsTrue(sim.StormAt(far));
            Assert.IsTrue(r.StormHold);
            Assert.IsTrue(r.ManualTarget.HasValue || r.Tile != far, "sent to shelter");
        }

        [Test]
        public void NoStormsWithoutWeather()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WeatherEnabled = false };
            for (int i = 0; i < 2000; i++) { sim.Tick(1.0); Assert.IsFalse(sim.StormAt(sim.BaseTileId)); }
        }

        [Test]
        public void ADustStormDimsPanelsButDoesNotStopRobots()
        {
            var mars = new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);
            var sim = new PlanetSimulation(Sphere, Surface, mars) { Clouds = CloudModel.None };
            bool dust = false;
            for (int d = 0; d < 668 * 8 && !dust; d++) { sim.Tick(24.6597); var w = sim.WeatherAt(sim.BaseTileId); if (w.DustStorm) { dust = true; Assert.Greater(w.DustTau, 2.0); Assert.IsFalse(sim.Workforce.Robots[0].StormHold && !w.Storm); } }
            Assert.IsTrue(dust, "a dust storm within eight Mars years");
        }
    }
}

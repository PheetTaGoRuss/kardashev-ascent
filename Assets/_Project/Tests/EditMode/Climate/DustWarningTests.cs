using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Weather W8: a WATCH entry when the dusty season starts and when a global storm begins, once each.</summary>
    public class DustWarningTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Mars => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);

        [Test]
        public void TheDustySeasonIsAnnouncedOncePerYear_AsAWatchEntryWithATauOutlook()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Mars);
            for (int d = 0; d < 668 * 2; d++) sim.Tick(24.6597);
            var entries = sim.OperationsLog.Where(r => r.Quantity == "Dusty season").ToList();
            Assert.GreaterOrEqual(entries.Count, 1); Assert.LessOrEqual(entries.Count, 3, "once a Mars year");
            Assert.AreEqual(LogPriority.Watch, entries[0].Priority);
            StringAssert.Contains("◐", entries[0].Text);
            StringAssert.Contains("sunlight", entries[0].Text);
        }

        [Test]
        public void AGlobalStormIsAnnouncedOnceWhenItBegins()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Mars);
            for (int d = 0; d < 668 * 12; d++) sim.Tick(24.6597);
            var entries = sim.OperationsLog.Where(r => r.Quantity == "Global dust storm").ToList();
            Assert.GreaterOrEqual(entries.Count, 1, "a storm within 12 Mars years");
            Assert.AreEqual(LogPriority.Watch, entries[0].Priority);
            StringAssert.Contains("RTG", entries[0].Text);
            Assert.Less(entries.Count, 12, "not every sol of a storm");
        }

        [Test]
        public void SunlightShareFallsWithTheExtraOpticalDepth()
        {
            Assert.AreEqual(1.0, PlanetSimulation.SolarShareForExtraTau(0.0), 1e-12);
            Assert.Less(PlanetSimulation.SolarShareForExtraTau(10.0), 1e-3);
        }

        [Test]
        public void EarthHasNoDustySeasonWarning()
        {
            var earth = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 8766.0, 0.3, 0.3, 15.0, false, 1.0);
            var sim = new PlanetSimulation(Sphere, Surface, earth);
            for (int d = 0; d < 800; d++) sim.Tick(24.0);
            Assert.AreEqual(0, sim.OperationsLog.Count(r => r.Quantity == "Dusty season" || r.Quantity == "Global dust storm"));
        }
    }
}

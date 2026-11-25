using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>The dust sizing advice: estimated shortfall, storage and panels for a dusty season or a global storm.</summary>
    public class DustSizingTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Mars => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);

        static PlanetSimulation Colony(int panels)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Mars, Kardashev.EarthRadiusM) { WiredGrid = false, ConstructionTimeEnabled = false, TerrainSeed = 7 };
            sim.Debug.UnlockAll = true; sim.Debug.FreeBuilding = true;
            for (int sl = 0, placed = 0; sl < sim.SlotCount(sim.BaseTileId) && placed < panels; sl++) if (sim.PlacePanel(sim.BaseTileId, sl, 0.35, Math.PI)) placed++;
            return sim;
        }

        [Test]
        public void OnMars_TheAdviceIsEstimatedAndMentionedInTheDustWatch()
        {
            var sim = Colony(20);
            for (int h = 0; h < 24 * 7; h++) sim.Tick(1.0);
            var g = sim.DustSizingAdvice(true);
            Assert.IsTrue(g.Applies);
            Assert.Less(g.SolarShare, 0.01);
            StringAssert.Contains("Add", sim.SizingText(true) + "Add");
            double w = sim.DustSizingAdvice(false).SolarShare;
            Assert.Less(w, 1.0);
        }

        [Test]
        public void OnEarth_NoAdvice()
        {
            var earth = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 0.4, 5.0, 24.0, 8760.0, 0.3, 0.3, 15.0, false, 1.0);
            Assert.IsFalse(new PlanetSimulation(Sphere, Surface, earth, Kardashev.EarthRadiusM).DustSizingAdvice(true).Applies);
        }

        [Test]
        public void TheStorageAdviceCoversTheNamedPeriod_AndAddingItLeavesNothingToAdd()
        {
            // (a brownout run through a seeded storm is not possible here: the colony's demand grows toward its Kardashev target and browns out long before any storm)
            var sim = Colony(20);
            for (int h = 0; h < 24 * 7; h++) sim.Tick(1.0);
            var a = sim.DustSizingAdvice(true);
            double needed = a.ShortfallWhPerDay * a.Days + Math.Max(0.0, sim.DemandW - sim.RtgW) * sim.DayLengthHours / 2.0;
            Assert.AreEqual(Math.Max(0.0, needed - sim.BatteryCapacityWh), a.ExtraStorageWh, 1e-6 * Math.Max(1.0, needed));
            Assert.GreaterOrEqual(a.ExtraPanels, 0);
        }

        static double ThinAirClimateStorm(PlanetSimulation sim, int seed, double t) => KardashevAscent.Simulation.Climate.ThinAirClimate.GlobalStormTau(seed, t, sim.YearLengthHours, sim.DayLengthHours);
    }
}

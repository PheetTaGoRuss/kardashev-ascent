using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class PanelAngleSearchTests
    {
        const double Deg = Math.PI / 180.0;

        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Deg,
            282.94 * Deg, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0);

        static PlanetSimulation sim;

        [OneTimeSetUp]
        public void CreateSimulation()
        {
            var sphere = HexSphere.Create(12);
            var surface = PlanetSurface.Generate(sphere, new SurfaceSettings(7));
            sim = new PlanetSimulation(sphere, surface, Earthlike);
        }

        static int LandTileNear(double latitudeDeg)
        {
            int best = -1;
            double bestGap = double.MaxValue;
            for (int t = 0; t < sim.TileCount; t++)
            {
                TileInfo tile = sim.GetTile(t);
                double gap = Math.Abs(tile.Latitude / Deg - latitudeDeg);
                if (!tile.IsWater && gap < bestGap) { bestGap = gap; best = t; }
            }
            Assert.Less(bestGap, 5.0, "test planet has land near the latitude");
            return best;
        }

        static double AngleGapDeg(double a, double b)
        {
            double d = Math.Abs(a - b) % (2.0 * Math.PI);
            return Math.Min(d, 2.0 * Math.PI - d) / Deg;
        }

        [TestCase(40.0)]
        [TestCase(-35.0)]
        public void MidLatitude_FacesTheEquatorAtAboutTheLatitude(double latitudeDeg)
        {
            int tile = LandTileNear(latitudeDeg);
            double lat = sim.GetTile(tile).Latitude;
            PanelAngles best = PanelAngleSearch.Best(sim, tile);

            Assert.IsTrue(best.Found);
            double equatorward = lat >= 0 ? Math.PI : 0.0;
            Assert.Less(AngleGapDeg(best.Azimuth, equatorward), 15.0, "faces the equator");
            // Clear-sky annual optimum sits a little under the latitude (diffuse light favours flatter panels).
            Assert.AreEqual(Math.Abs(lat) / Deg, best.Tilt / Deg, 15.0, "tilt close to |latitude|");
        }

        [Test]
        public void Best_BeatsTheRuleOfThumbAndEveryCoarseAngle()
        {
            int tile = LandTileNear(40.0);
            double lat = sim.GetTile(tile).Latitude;
            PanelAngles best = PanelAngleSearch.Best(sim, tile);

            double ruleOfThumb = sim.PreviewPanel(tile, Math.Abs(lat), Math.PI).EnergyPerYearWh;
            Assert.GreaterOrEqual(best.EnergyPerYearWh, ruleOfThumb);
            for (int t = 0; t <= 90; t += 30)
                for (int a = 0; a < 360; a += 90)
                    Assert.GreaterOrEqual(best.EnergyPerYearWh, sim.PreviewPanel(tile, t * Deg, a * Deg).EnergyPerYearWh);
            Assert.AreEqual(1.0, PanelAngleSearch.ShareOfBest(best.EnergyPerYearWh, best.EnergyPerYearWh), 1e-12);
        }

        [Test]
        public void WaterTile_HasNoBestAngle()
        {
            int water = -1;
            for (int t = 0; t < sim.TileCount && water < 0; t++)
                if (sim.GetTile(t).IsWater)
                    water = t;
            Assert.GreaterOrEqual(water, 0);
            Assert.IsFalse(PanelAngleSearch.Best(sim, water).Found);
        }

        [Test]
        public void FindsAKnownPeakBetweenGridPoints()
        {
            // Smooth bowl peaking at β = 37°, γ = 200°: off both coarse grids.
            PanelPreview Bowl(double tilt, double azimuth)
            {
                double dt = tilt / Deg - 37.0, da = AngleGapDeg(azimuth, 200.0 * Deg);
                return new PanelPreview(true, "", 1000.0 - dt * dt - da * da, 0, 0, 0);
            }

            PanelAngles best = PanelAngleSearch.Best(Bowl);
            Assert.AreEqual(37.0, best.Tilt / Deg, 2.0);
            Assert.AreEqual(200.0, best.Azimuth / Deg, 4.0);
            Assert.Less(best.Evaluations, 150, "cheap enough to run per selected tile");
        }

        [Test]
        public void PeakAcrossNorth_WrapsAzimuth()
        {
            PanelPreview Bowl(double tilt, double azimuth)
            {
                double dt = tilt / Deg - 50.0, da = AngleGapDeg(azimuth, 355.0 * Deg);
                return new PanelPreview(true, "", 1000.0 - dt * dt - da * da, 0, 0, 0);
            }

            PanelAngles best = PanelAngleSearch.Best(Bowl);
            Assert.Less(AngleGapDeg(best.Azimuth, 355.0 * Deg), 4.0);
            Assert.That(best.Azimuth, Is.InRange(0.0, 2.0 * Math.PI));
        }

        [TestCase(1.0, AngleRating.Optimal)]
        [TestCase(0.98, AngleRating.Optimal)]
        [TestCase(0.95, AngleRating.Good)]
        [TestCase(0.90, AngleRating.Good)]
        [TestCase(0.5, AngleRating.Off)]
        public void Rating(double share, AngleRating expected)
        {
            Assert.AreEqual(expected, PanelAngleSearch.Rate(share));
        }

        [Test]
        public void ShareOfBest_IsZeroWithoutABest()
        {
            Assert.AreEqual(0.0, PanelAngleSearch.ShareOfBest(500.0, 0.0));
            Assert.AreEqual(0.5, PanelAngleSearch.ShareOfBest(500.0, 1000.0), 1e-12);
        }
    }
}

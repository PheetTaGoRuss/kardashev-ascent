using System;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Earth ground reflectance and aerosol haze from NASA POWER (tools/nasa_power/fetch_ground_and_haze.py).</summary>
    public class GroundAndHazeTests
    {
        const double Deg = Math.PI / 180.0;

        [Test]
        public void BroadbandAerosol_Is700nmByAngstrom()
        {
            // Molineaux et al. 1998: broadband = AOD at ~700 nm. Ångström α = 1.3: (700/550)^-1.3 = 0.7307.
            Assert.AreEqual(Math.Pow(700.0 / 550.0, -1.3), EarthGroundAndHaze.BroadbandPerAod550, 1e-12);
            Assert.AreEqual(0.731, EarthGroundAndHaze.BroadbandPerAod550, 1e-3);
        }

        [Test]
        public void Values_AreInEarthsRange()
        {
            for (int lat = -90; lat <= 90; lat += 5)
                for (int m = 0; m < 48; m++)
                {
                    Assert.That(EarthGroundAndHaze.Albedo(lat * Deg, m / 48.0), Is.InRange(0.02, 0.95), $"albedo {lat} {m}");
                    Assert.That(EarthGroundAndHaze.AerosolDepth(lat * Deg, m / 48.0), Is.InRange(0.0, 1.0), $"aod {lat} {m}");
                }
            Assert.That(EarthGroundAndHaze.GlobalMeanAerosolDepth, Is.InRange(0.03, 0.3));
        }

        [Test]
        public void SeasonalChanges_AverageToZeroOverTheYear()
        {
            foreach (int lat in new[] { -60, 0, 50, 70 })
            {
                double sum = 0.0;
                for (int m = 0; m < 12; m++) sum += EarthGroundAndHaze.AlbedoChange(lat * Deg, (m + 0.5) / 12.0);
                Assert.AreEqual(0.0, sum / 12.0, 1e-9, $"lat {lat}");
            }
        }

        [Test]
        public void SnowyWinter_MakesTheGroundBrighter_AtSixtyNorth()
        {
            Assert.Greater(EarthGroundAndHaze.AlbedoChange(60 * Deg, 0.5 / 12.0), 0.05, "January at 60°N");
            Assert.Less(EarthGroundAndHaze.AlbedoChange(60 * Deg, 6.5 / 12.0), 0.0, "July at 60°N");
        }

        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static int TileNear(double latDeg)
        {
            int best = 0;
            for (int t = 0; t < Sphere.TileCount; t++)
                if (Math.Abs(Sphere.Latitude(t) / Deg - latDeg) < Math.Abs(Sphere.Latitude(best) / Deg - latDeg)) best = t;
            return best;
        }

        [Test]
        public void EarthLikePlanet_HazeMovesAroundThePlanetsTau_OthersKeepIt()
        {
            var plain = new PlanetSimulation(Sphere, Surface, Earthlike);
            var earth = new PlanetSimulation(Sphere, Surface, Earthlike) { Clouds = CloudModel.EarthClimatology };
            int t = TileNear(10.0);
            Assert.AreEqual(0.32, plain.AnnualOpticalDepth(t), 1e-12);
            Assert.AreEqual(0.0, plain.GroundReflectanceChange(t, 1.0), 1e-12);
            Assert.AreEqual(0.32 + EarthGroundAndHaze.HazeChangeYear(Sphere.Latitude(t)), earth.AnnualOpticalDepth(t), 1e-9);
            Assert.AreNotEqual(earth.AnnualOpticalDepth(TileNear(10.0)), earth.AnnualOpticalDepth(TileNear(-60.0)), "it varies by place");
        }

        [Test]
        public void HazyPlaces_HaveThickerAirThanThePlanetsMean()
        {
            var earth = new PlanetSimulation(Sphere, Surface, Earthlike) { Clouds = CloudModel.EarthClimatology };
            var plain = new PlanetSimulation(Sphere, Surface, Earthlike);
            // A tile whose NASA aerosols are above Earth's mean: thicker air than the planet's τ there.
            int hazy = -1;
            for (int t = 0; t < Sphere.TileCount && hazy < 0; t++)
                if (!earth.GetTile(t).IsWater && earth.AnnualOpticalDepth(t) > 0.32 + 0.03) hazy = t;
            Assume.That(hazy >= 0);
            Assert.Greater(earth.AnnualOpticalDepth(hazy), plain.AnnualOpticalDepth(hazy));
        }
    }
}

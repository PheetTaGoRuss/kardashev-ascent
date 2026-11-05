using System;
using System.Linq;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Solar
{
    public class LightPotentialTests
    {
        const double Deg = Math.PI / 180.0;

        static PlanetSolarParams EarthLike(double eccentricity = 0.0167, bool locked = false) => new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, eccentricity, locked ? 0.0 : 23.44 * Deg,
            282.94 * Deg, locked ? 0.0 : 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, locked);

        [Test]
        public void StandardPanel_FacesTheEquatorAtLatitudeTilt()
        {
            PanelParams north = LightPotential.StandardPanel(40 * Deg);
            PanelParams south = LightPotential.StandardPanel(-25 * Deg);
            Assert.AreEqual(40 * Deg, north.Tilt, 1e-12);
            Assert.AreEqual(Math.PI, north.Azimuth, 1e-12, "northern panels face south");
            Assert.AreEqual(25 * Deg, south.Tilt, 1e-12);
            Assert.AreEqual(0.0, south.Azimuth, 1e-12, "southern panels face north");
            Assert.AreEqual(LightPotential.StandardEfficiency, north.Efficiency);
        }

        [Test]
        public void TileValues_MatchDirectCalculationWithinHalfAPercent()
        {
            var sphere = HexSphere.Create(12);
            var planet = EarthLike();
            double[] map = LightPotential.ForTiles(sphere, planet);
            for (int t = 0; t < sphere.TileCount; t += 97)
            {
                double direct = LightPotential.AnnualKwhPerM2(planet, sphere.Latitude(t));
                Assert.AreEqual(direct, map[t], Math.Max(0.5, direct * 0.005), $"tile {t} at {sphere.Latitude(t) / Deg:F1}°");
            }
        }

        [Test]
        public void TileValues_AreBetweenZeroAndAPlausibleCeiling()
        {
            var sphere = HexSphere.Create(10);
            double[] map = LightPotential.ForTiles(sphere, EarthLike());
            Assert.That(map.All(v => v >= 0.0 && !double.IsNaN(v)));
            // 20 % of the clear-sky ceiling (~3,300 kWh/m² POA at τ ≈ 0.1 in the report) bounds it well.
            Assert.Less(map.Max(), 700.0);
            Assert.Greater(map.Max(), 350.0);
        }

        [Test]
        public void CircularOrbit_NorthAndSouthMatch()
        {
            var planet = EarthLike(eccentricity: 0.0);
            double north = LightPotential.AnnualKwhPerM2(planet, 35 * Deg);
            double south = LightPotential.AnnualKwhPerM2(planet, -35 * Deg);
            Assert.AreEqual(north, south, north * 1e-6);
        }

        [Test]
        public void Tropics_BeatHighLatitudes()
        {
            var planet = EarthLike();
            Assert.Greater(LightPotential.AnnualKwhPerM2(planet, 15 * Deg), LightPotential.AnnualKwhPerM2(planet, 60 * Deg));
        }

        [Test]
        public void TidallyLocked_DaySideBrightest_NightSideZero()
        {
            var sphere = HexSphere.Create(8);
            double[] map = LightPotential.ForTiles(sphere, EarthLike(eccentricity: 0.0, locked: true));
            int brightest = Array.IndexOf(map, map.Max());
            // The star hangs over latitude 0, longitude 0 (+Z).
            Assert.Greater(sphere.Centers[brightest].Z, 0.95f);
            for (int t = 0; t < sphere.TileCount; t++)
                if (sphere.Centers[t].Z < -0.05f)
                    Assert.AreEqual(0.0, map[t], $"night-side tile {t}");
        }
    }
}

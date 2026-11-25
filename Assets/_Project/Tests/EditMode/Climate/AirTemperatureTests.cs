using System;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Earth air temperature from NASA POWER T2M (tools/nasa_power/fetch_air_temperature.py).</summary>
    public class AirTemperatureTests
    {
        const double Deg = Math.PI / 180.0;

        [Test]
        public void AtMonthCentre_MatchesTheTableRow()
        {
            // 0° row, March centre: 27.3 °C in the generated AirTemperatureTable.
            Assert.AreEqual(27.3, EarthAirTemperature.Celsius(0.0, 2.5 / 12.0), 1e-9);
        }

        [Test]
        public void TropicsAreWarm_PolesAreCold_AllInEarthRange()
        {
            Assert.That(EarthAirTemperature.AnnualMean(0.0), Is.InRange(24.0, 30.0));
            Assert.Less(EarthAirTemperature.AnnualMean(-80 * Deg), -25.0);
            for (int lat = -90; lat <= 90; lat += 5)
                for (int m = 0; m < 48; m++)
                    Assert.That(EarthAirTemperature.Celsius(lat * Deg, m / 48.0), Is.InRange(-60.0, 40.0), $"lat {lat}, step {m}");
        }

        [Test]
        public void Seasons_AreOppositeInTheTwoHemispheres()
        {
            double janN = EarthAirTemperature.Celsius(50 * Deg, 0.5 / 12.0), julN = EarthAirTemperature.Celsius(50 * Deg, 6.5 / 12.0);
            double janS = EarthAirTemperature.Celsius(-40 * Deg, 0.5 / 12.0), julS = EarthAirTemperature.Celsius(-40 * Deg, 6.5 / 12.0);
            Assert.Greater(julN - janN, 15.0, "50°N: a real winter");
            Assert.Greater(janS, julS, "40°S: January is summer");
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
        public void EarthLikePlanet_UsesNasaTemperatureByTile_OthersKeepTheirOneValue()
        {
            var plain = new PlanetSimulation(Sphere, Surface, Earthlike);
            int pole = TileNear(70.0), equator = TileNear(0.0);
            Assert.AreEqual(25.0, plain.AirTemperatureAt(pole, 0.0), 1e-12, "no Earth climatology: the planet's value");

            var earth = new PlanetSimulation(Sphere, Surface, Earthlike) { Clouds = CloudModel.EarthClimatology };
            // NASA's pattern by latitude, moved to this planet's own mean (a uniform offset, ColdEarthTests): the pole-to-equator difference is NASA's
            double nasaDiff = EarthAirTemperature.AnnualMean(Sphere.Latitude(equator)) - EarthAirTemperature.AnnualMean(Sphere.Latitude(pole));
            Assert.AreEqual(nasaDiff, earth.AnnualAirTemperature(equator) - earth.AnnualAirTemperature(pole), 1e-9);
            Assert.Greater(earth.AnnualAirTemperature(equator), earth.AnnualAirTemperature(pole) + 10.0);
        }

        [Test]
        public void ColderAir_KeepsPanelCellsCooler_SoTheSameLightMakesMore()
        {
            // The same tile, the planet's 25 °C against NASA's colder 70°N year: cooler cells lose less to heat.
            var plain = new PlanetSimulation(Sphere, Surface, Earthlike);
            var earth = new PlanetSimulation(Sphere, Surface, Earthlike) { Clouds = CloudModel.EarthClimatology };
            int t = TileNear(70.0);
            Assume.That(earth.AnnualAirTemperature(t) < 0.0);
            // Clouds apply on the Earth-like planet only: take them back out month by month to compare the same light.
            double plainWh = plain.PreviewPanel(t, 0.8, Math.PI).EnergyPerYearWh;
            double earthClearWh = earth.MonthlyEnergyWh(t, 0.8, Math.PI) is double[] months ? Sum(months, earth, t) : 0.0;
            Assert.Greater(earthClearWh, plainWh, "cold air: more output from the same clear-sky light");
        }

        static double Sum(double[] cloudyMonths, PlanetSimulation sim, int tile)
        {
            double sum = 0.0;
            for (int m = 0; m < cloudyMonths.Length; m++)
            {
                double f = sim.CloudFactor(tile, 2.0 * Math.PI * (m + 0.5) / cloudyMonths.Length);
                sum += f > 0.0 ? cloudyMonths[m] / f : 0.0;
            }
            return sum;
        }
    }
}

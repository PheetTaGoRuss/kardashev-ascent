using System;
using System.Linq;
using KardashevAscent.Simulation.Climate;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Weather (PL-07, W2): deterministic noise around the climatology; its spread and persistence come from NASA POWER's daily data.</summary>
    public class WeatherModelTests
    {
        const double Deg = Math.PI / 180.0, Earth = 6.371e6;
        static WeatherModel Make(double pressure = 1.0, int seed = 7) => new WeatherModel(seed, Earth, pressure, true);

        static double Interp(double[][] t, double lat, int month)
        {
            double y = Math.Max(0, Math.Min(t.Length - 1, (lat / Deg - WeatherVariabilityTable.FirstLatitudeDeg) / WeatherVariabilityTable.StepDeg));
            int i0 = Math.Min((int)y, t.Length - 2); double f = y - i0;
            return t[i0][month] + f * (t[i0 + 1][month] - t[i0][month]);
        }

        [Test]
        public void TheSamePlaceAndTimeGiveTheSameWeather_EveryTime()
        {
            var a = Make().At(40 * Deg, 10 * Deg, 1234.5, 0.3);
            var b = Make().At(40 * Deg, 10 * Deg, 1234.5, 0.3);
            Assert.AreEqual(a.TemperatureK, b.TemperatureK);
            Assert.AreEqual(a.WindMs, b.WindMs);
            Assert.AreNotEqual(a.TemperatureK, Make(1.0, 8).At(40 * Deg, 10 * Deg, 1234.5, 0.3).TemperatureK, "another seed, another weather");
        }

        [Test]
        public void ANearbyPlaceHasNearbyWeather_AFarOneDoesNot()
        {
            var m = Make();
            double near = 0, far = 0;
            int n = 400;
            for (int i = 0; i < n; i++)
            {
                double t = i * 24.0;
                double a = m.Noise(WeatherModel.Channel.Temperature, 40 * Deg, 10 * Deg, t, 0.75);
                near += a * m.Noise(WeatherModel.Channel.Temperature, 40.5 * Deg, 10.5 * Deg, t, 0.75);
                far += a * m.Noise(WeatherModel.Channel.Temperature, -30 * Deg, 100 * Deg, t, 0.75);
            }
            Assert.Greater(near / n, 0.85, "neighbouring tiles share their weather systems");
            Assert.Less(Math.Abs(far / n), 0.25, "far apart, independent");
        }

        [Test]
        public void TheNoiseHasUnitSpreadAndTheRequestedDayToDayPersistence()
        {
            var m = Make();
            foreach (double rho in new[] { 0.2, 0.5, 0.75, 0.85 })
            {
                var x = Enumerable.Range(0, 6000).Select(i => m.Noise(WeatherModel.Channel.Temperature, 20 * Deg, 30 * Deg, i * 24.0, rho)).ToArray();
                double mean = x.Average(), var = x.Select(v => (v - mean) * (v - mean)).Average();
                double cov = 0; for (int i = 0; i + 1 < x.Length; i++) cov += (x[i] - mean) * (x[i + 1] - mean);
                Assert.AreEqual(0.0, mean, 0.08, "rho " + rho);
                Assert.AreEqual(1.0, var, 0.15, "rho " + rho);
                Assert.AreEqual(rho, cov / (x.Length - 1) / var, 0.05, "lag-1 autocorrelation, rho " + rho);
            }
        }

        [Test]
        public void TheSpreadAndPersistenceMatchTheNasaTable_AtSeveralLatitudes()
        {
            var m = Make();
            foreach (double lat in new[] { 0.0, 40.0, -40.0 })
            {
                int month = 6; double yf = (month + 0.5) / 12.0;
                double sdTable = Interp(WeatherVariabilityTable.TemperatureSd, lat * Deg, month);
                var x = Enumerable.Range(0, 6000).Select(i => m.At(lat * Deg, 20 * Deg, i * 24.0, yf).TemperatureK).ToArray();
                double mean = x.Average(), sd = Math.Sqrt(x.Select(v => (v - mean) * (v - mean)).Average());
                Assert.AreEqual(sdTable, sd, sdTable * 0.15, "temperature sd at " + lat);
                double cov = 0; for (int i = 0; i + 1 < x.Length; i++) cov += (x[i] - mean) * (x[i + 1] - mean);
                double rhoTable = Interp(WeatherVariabilityTable.TemperatureRho, lat * Deg, month);
                Assert.AreEqual(rhoTable, cov / (x.Length - 1) / (sd * sd), 0.05, "lag-1 at " + lat);
            }
        }

        [Test]
        public void TenYearsOfWeatherAverageToTheClimatology_Within1Percent()
        {
            var m = Make();
            double yf = 0.5;
            foreach (double lat in new[] { 0.0, 45.0, -45.0 })
            {
                double clim = CloudClimate.Factor(lat * Deg, yf);
                var x = Enumerable.Range(0, 3650).Select(i => Math.Max(0.02, Math.Min(1.2, clim + m.At(lat * Deg, 50 * Deg, i * 24.0, yf).CloudFactor))).ToArray();
                Assert.AreEqual(clim, x.Average(), clim * 0.01, "cloud factor mean at " + lat);
                var t = Enumerable.Range(0, 3650).Select(i => m.At(lat * Deg, 50 * Deg, i * 24.0, yf).TemperatureK).ToArray();
                Assert.AreEqual(0.0, t.Average(), 0.3, "temperature anomaly mean at " + lat);
            }
        }

        [Test]
        public void AboutTwoPercentOfDaysAreStormDays()
        {
            var m = Make();
            foreach (double lat in new[] { 0.0, 50.0, -60.0 })
            {
                int storms = Enumerable.Range(0, 20000).Count(i => m.At(lat * Deg, 70 * Deg, i * 24.0, 0.25).Storm);
                Assert.AreEqual(0.02, storms / 20000.0, 0.012, "storm days at " + lat);
            }
        }

        [Test]
        public void AnAirlessWorldHasNoWeather()
        {
            var m = new WeatherModel(7, 3.4e6, 0.0, false);
            Assert.IsFalse(m.HasWeather);
            var w = m.At(10 * Deg, 10 * Deg, 100.0, 0.2);
            Assert.AreEqual(0.0, w.TemperatureK); Assert.AreEqual(0.0, w.WindMs); Assert.IsFalse(w.Storm);
        }

        [Test]
        public void WindIsNeverNegative_AndTheTableMeanIsReproduced()
        {
            var m = Make();
            var w = Enumerable.Range(0, 5000).Select(i => m.At(40 * Deg, 0, i * 24.0, 0.05).WindMs).ToArray();
            Assert.GreaterOrEqual(w.Min(), 0.0);
            Assert.AreEqual(Interp(WeatherVariabilityTable.WindMean, 40 * Deg, 0), w.Average(), 0.5);
        }
    }
}

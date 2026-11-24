using System;
using System.Linq;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Weather (PL-07, W4): Mars-like thin CO2 air has a seasonal pressure cycle and dust storms, deterministic from the seed and the year.</summary>
    public class ThinAirClimateTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Mars(double pressure = 0.00628) => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0,
            250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, pressure);

        [Test]
        public void ThePressureCycleRunsFrom6_9To9_0hPa_LowestAtLs150_HighestAtLs250_AndAveragesToOne()
        {
            double mean = 7.95;   // the planet's tabulated mean pressure stands for the cycle's mean
            double lo = double.MaxValue, hi = double.MinValue, lsLo = 0, lsHi = 0, sum = 0;
            for (int ls = 0; ls < 360; ls++)
            {
                double f = ThinAirClimate.PressureFactor(ls + 0.5);
                sum += f;
                if (f < lo) { lo = f; lsLo = ls; }
                if (f > hi) { hi = f; lsHi = ls; }
            }
            Assert.AreEqual(1.0, sum / 360.0, 1e-3, "the mean over the year is unchanged");
            Assert.AreEqual(150.0, lsLo, 2.0); Assert.AreEqual(250.0, lsHi, 2.0);
            Assert.AreEqual(9.0 / 6.9, hi / lo, 0.01, "the ratio of the extremes is Viking's");
            Assert.Greater(hi * mean, 8.9); Assert.Less(lo * mean, 7.0);
        }

        [Test]
        public void TheDustySeasonIsDustierThanTheClearOne_AndTheYearlyMeanIsTheTabulatedDepth()
        {
            Assert.Greater(ThinAirClimate.SeasonFactor(270.0), ThinAirClimate.SeasonFactor(90.0));
            double mean = Enumerable.Range(0, 360).Average(ls => ThinAirClimate.SeasonFactor(ls + 0.5));
            Assert.AreEqual(1.0, mean, 0.01);
        }

        [Test]
        public void GlobalStormsComeInAboutOneYearInThree_PeakNearTau10_AndDecay()
        {
            int years = 600, stormYears = 0; double best = 0;
            const double year = 16487.52, day = 24.6597;
            for (int y = 0; y < years; y++)
            {
                double peak = 0;
                for (int d = 0; d < 668 + 60; d += 2) peak = Math.Max(peak, ThinAirClimate.GlobalStormTau(11, (y + d / 668.0) * year, year, day));
                if (peak > 3.0) stormYears++;
                best = Math.Max(best, peak);
            }
            Assert.AreEqual(1.0 / 3.0, stormYears / (double)years, 0.07);
            Assert.Greater(best, 9.0); Assert.LessOrEqual(best, 11.0 + 1e-6);
        }

        [Test]
        public void TheStormsAreTheSameEveryRun_AndDifferentWithAnotherSeed()
        {
            const double year = 16487.52, day = 24.6597;
            double a = 0, b = 0;
            for (int d = 0; d < 3000; d += 7) { a += ThinAirClimate.GlobalStormTau(5, d * day, year, day); b += ThinAirClimate.GlobalStormTau(6, d * day, year, day); }
            Assert.AreEqual(a, Enumerable.Range(0, 3000 / 7 + 1).Sum(i => ThinAirClimate.GlobalStormTau(5, i * 7 * day, year, day)), 1e-9);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void MarsLikeAirHasTheCycleAndTheDust_EarthDoesNot()
        {
            var mars = new PlanetSimulation(Sphere, Surface, Mars());
            Assert.IsTrue(mars.ThinCo2Air);
            var earth = new PlanetSimulation(Sphere, Surface, Mars(1.0));
            Assert.IsFalse(earth.ThinCo2Air);
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < 80; i++)
            {
                mars.Tick(24.6597 * 8.0);
                double p = mars.WeatherAt(mars.BaseTileId).PressurePa;
                lo = Math.Min(lo, p); hi = Math.Max(hi, p);
            }
            Assert.Greater(hi / lo, 1.15, "the CO2 cycle moves the pressure by tens of per cent over a year");
        }
    }
}

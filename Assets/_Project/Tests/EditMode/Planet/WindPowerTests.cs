using System;
using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    /// <summary>Wind turbine physics (T2): the NPS 100C-21 datasheet curve, the density scaling, the power-law shear and the Rayleigh day, checked against the datasheet's own annual energy table.</summary>
    public class WindPowerTests
    {
        [Test]
        public void TheCurveIsTheDatasheet_CutInRatedAndCutOut()
        {
            Assert.AreEqual(0.0, WindPower.CurveW(2.9), "below cut-in");
            Assert.AreEqual(500.0, WindPower.CurveW(3.0), 1e-6);
            Assert.AreEqual(100_000.0, WindPower.CurveW(15.0), 1e-6, "rated 100 kW at 15 m/s");
            Assert.AreEqual(29_400.0, WindPower.CurveW(7.0), 1e-6);
            Assert.AreEqual(0.0, WindPower.CurveW(25.5), "above cut-out");
        }

        [Test]
        public void ThePowerCoefficientIsBelowTheBetzLimit_Everywhere()
        {
            for (double v = 3.0; v <= 25.0; v += 0.5)
                Assert.Less(WindPower.PowerCoefficient(v), 16.0 / 27.0, "Betz limit 0.593 at " + v);
            Assert.AreEqual(0.39, WindPower.PowerCoefficient(8.0), 0.04, "a good rotor takes about 0.39 at 8 m/s");
        }

        [Test]
        public void PowerScalesWithAirDensity_AndIsCappedAtTheRating()
        {
            Assert.AreEqual(WindPower.CurveW(8.0) * 0.5, WindPower.PowerW(8.0, 0.6125), 1e-6, "half the air, half the power");
            Assert.AreEqual(WindPower.RatedW, WindPower.PowerW(15.0, 2.0), "the generator caps it");
            Assert.AreEqual(0.0, WindPower.PowerW(8.0, 0.0));
        }

        [Test]
        public void TheHubWindFollowsThePowerLaw_AndTheAirDensityTheGasLaw()
        {
            Assert.AreEqual(Math.Pow(3.7, 1.0 / 7.0), WindPower.HubWindMs(1.0), 1e-9);
            Assert.AreEqual(1.20, WindPower.HubWindMs(1.0), 0.01, "about 1.20 at 37 m");
            Assert.AreEqual(1.225, WindPower.AirDensity(101_325.0, 15.0, 0.0289647), 0.002, "standard sea-level air");
            Assert.AreEqual(0.0155, WindPower.AirDensity(610.0, -63.0, 0.04401), 0.002, "the CO2 air of Mars");
        }

        [Test]
        public void TheRayleighDayReproducesTheDatasheetAnnualEnergy_Within5Percent()
        {
            // the datasheet's annual energy at standard conditions with a Rayleigh wind distribution (MWh/year)
            foreach (var (mean, mwh) in new[] { (5.0, 147.0), (6.0, 223.0), (7.0, 299.0), (8.0, 369.0), (8.5, 401.0) })
            {
                double aep = WindPower.DailyMeanPowerW(mean, 1.225) * 8760.0 / 1e6;
                Assert.AreEqual(mwh, aep, mwh * 0.05, "annual energy at a mean of " + mean + " m/s");
            }
        }

        [Test]
        public void TheDailyMeanIsNotThePowerAtTheMeanWind_AndStopsAboveCutOut()
        {
            Assert.Greater(WindPower.DailyMeanPowerW(5.0, 1.225), WindPower.PowerW(5.0, 1.225) * 1.3, "gusts count more than the mean (the cube)");
            Assert.AreEqual(0.0, WindPower.DailyMeanPowerW(26.0, 1.225), "a day averaging above cut-out is shut down");
        }
    }
}

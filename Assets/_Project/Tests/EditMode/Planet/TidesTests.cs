using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    public class TidesTests
    {
        const double EarthRadiusM = 6.371e6, EarthMassKg = 5.972e24;

        [Test]
        public void EarthMoon_EquilibriumRange_IsTheKnownHalfMetre()
        {
            double range = Tides.EquilibriumRangeM(Moon.EarthMoon, EarthRadiusM);
            Assert.AreEqual(0.54, range, 0.03, "known lunar equilibrium tide range, m");
        }

        [Test]
        public void EarthMoon_StrengthIsOne_AndTheCeilingIs3p7TW()
        {
            Assert.AreEqual(1.0, Tides.StrengthRelativeToEarth(Moon.EarthMoon), 1e-9);
            Assert.AreEqual(3.7e12, Tides.PlanetCeilingW(Moon.EarthMoon), 1.0);
        }

        [Test]
        public void MoonPeriod_OfTheEarthMoonPair_IsAboutAMonth()
        {
            double days = Tides.MoonPeriodHours(Moon.EarthMoon, EarthMassKg, EarthRadiusM) / 24.0;
            Assert.AreEqual(27.3, days, 0.7, "sidereal month, days");
        }

        [Test]
        public void TidePeriod_OnEarth_IsHalfALunarDay()
        {
            double moonHours = Tides.MoonPeriodHours(Moon.EarthMoon, EarthMassKg, EarthRadiusM);
            double period = Tides.TidePeriodHours(23.934, moonHours);   // sidereal day
            Assert.AreEqual(12.42, period, 0.15, "time between high tides, hours (M2 tide)");
        }

        [Test]
        public void TidePeriod_WithNoRelativeMotion_NeverCycles()
        {
            Assert.IsTrue(double.IsInfinity(Tides.TidePeriodHours(100.0, 100.0)));
        }

        [Test]
        public void CloserAndHeavierMoons_RaiseHigherTides()
        {
            var near = new Moon(0.0123, 30.0);
            var far = new Moon(0.0123, 90.0);
            var light = new Moon(0.003, 60.0);
            Assert.Greater(Tides.EquilibriumRangeM(near, EarthRadiusM), Tides.EquilibriumRangeM(far, EarthRadiusM));
            Assert.Greater(Tides.EquilibriumRangeM(Moon.EarthMoon, EarthRadiusM), Tides.EquilibriumRangeM(light, EarthRadiusM));
            Assert.AreEqual(27.0, Tides.EquilibriumRangeM(near, EarthRadiusM) / Tides.EquilibriumRangeM(far, EarthRadiusM), 1e-6, "range goes as 1/d cubed");
        }

        [Test]
        public void NoMoon_NoTides()
        {
            var none = new Moon(0.0, 60.0);
            Assert.AreEqual(0.0, Tides.EquilibriumRangeM(none, EarthRadiusM));
            Assert.AreEqual(0.0, Tides.PlanetCeilingW(none));
        }

        [Test]
        public void Generate_IsDeterministic_AndStaysInRange()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                Moon a = Tides.Generate(seed), b = Tides.Generate(seed);
                Assert.AreEqual(a.MassRatio, b.MassRatio);
                Assert.AreEqual(a.DistanceRadii, b.DistanceRadii);
                Assert.That(a.MassRatio, Is.InRange(0.002, 0.0200001));
                Assert.That(a.DistanceRadii, Is.InRange(25.0, 90.0));
            }
            Assert.AreNotEqual(Tides.Generate(1).DistanceRadii, Tides.Generate(2).DistanceRadii);
        }

        [Test]
        public void Turbine_PowerIsHalfRhoAV3Cp_AndBelowBetz()
        {
            Assert.AreEqual(0.5 * 1025.0 * 380.0 * 8.0 * 0.35, Tides.TurbinePowerW(2.0), 1e-6);
            Assert.Less(Tides.TurbineCp, Tides.BetzLimit);
            Assert.AreEqual(0.0, Tides.TurbinePowerW(0.5), "below the cut-in speed");
            Assert.AreEqual(2.0 * Tides.TurbinePowerW(2.0), Tides.TurbinePowerW(2.0, 2), 1e-6);
            Assert.AreEqual(8.0, Tides.TurbinePowerW(2.0) / Tides.TurbinePowerW(1.0), 1e-6, "power goes as speed cubed");
        }

        [Test]
        public void Turbine_HoldsItsRating_AboveTheRatedSpeed()
        {
            Assert.AreEqual(Tides.TurbinePowerW(Tides.RatedMs), Tides.TurbinePowerW(5.0), 1e-6);
            Assert.Less(Tides.TurbinePowerW(Tides.RatedMs), 3.0e6, "one rotor is a few MW at most");
        }

        [Test]
        public void Stream_SlacksTwicePerCycle_AndPeaksBetween()
        {
            double period = 12.42, peak = 2.5;
            Assert.AreEqual(0.0, Tides.StreamMs(peak, 0.0, period), 1e-9);
            Assert.AreEqual(peak, Tides.StreamMs(peak, period / 2.0, period), 1e-9);
            Assert.AreEqual(0.0, Tides.StreamMs(peak, period, period), 1e-9);
            Assert.AreEqual(Tides.MaxPeakMs, Tides.PeakStreamMs(100.0), "capped");
        }

        [Test]
        public void MeanPowerOverACycle_IsBelowPeak_ButAboveZero()
        {
            double period = 12.42, peak = 2.5, sum = 0.0;
            const int n = 2000;
            for (int i = 0; i < n; i++)
                sum += Tides.TurbinePowerW(Tides.StreamMs(peak, period * i / n, period));
            double mean = sum / n, peakPower = Tides.TurbinePowerW(peak);
            Assert.Greater(mean, 0.0);
            Assert.Less(mean, 0.6 * peakPower, "a sine stream gives a cube-law mean well below the peak");
        }
    }
}

using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class SimClockTests
    {
        const double Deg = Math.PI / 180.0;
        const double EarthYearHours = 365.256 * 24.0;

        static readonly PlanetSolarParams Earth = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Deg,
            282.94 * Deg, 24.0, EarthYearHours, 0.32, 0.3, 15.0);

        [TestCase(ClockSpeed.Paused, 0.0)]
        [TestCase(ClockSpeed.RealTime, 1.0 / 3600.0)]
        [TestCase(ClockSpeed.HourPerSecond, 1.0)]
        [TestCase(ClockSpeed.DayPerSecond, 24.0)]
        [TestCase(ClockSpeed.YearPerMinute, EarthYearHours / 60.0)]
        public void Speeds(ClockSpeed speed, double hoursPerSecond)
        {
            var clock = new SimClock(24.0, EarthYearHours) { Speed = speed };
            Assert.AreEqual(hoursPerSecond, clock.HoursPerSecond, 1e-12);
            Assert.AreEqual(hoursPerSecond * 0.5, clock.Advance(0.5), 1e-12);
        }

        [Test]
        public void Advance_IsCappedAndIgnoresNegativeTime()
        {
            var clock = new SimClock(24.0, EarthYearHours) { Speed = ClockSpeed.YearPerMinute };
            Assert.AreEqual(clock.MaxHoursPerAdvance, clock.Advance(3600.0));
            Assert.AreEqual(0.0, clock.Advance(-1.0));
        }

        [Test]
        public void TidallyLockedPlanet_UsesA24HourCalendarDay()
        {
            var clock = new SimClock(0.0, 11.186 * 24.0) { Speed = ClockSpeed.DayPerSecond };
            Assert.AreEqual(24.0, clock.HoursPerSecond);
        }

        [TestCase(0.0, 0, 1)]
        [TestCase(23.9, 0, 1)]
        [TestCase(24.0, 0, 2)]
        [TestCase(EarthYearHours + 30.0, 1, 2)]
        public void Calendar(double hours, int year, int day)
        {
            var clock = new SimClock(24.0, EarthYearHours);
            Assert.AreEqual(year, clock.YearIndex(hours));
            Assert.AreEqual(day, clock.DayOfYear(hours));
        }

        [TestCase(0.0, 12.0)]
        [TestCase(-90.0, 6.0)]
        [TestCase(90.0, 18.0)]
        [TestCase(180.0, 0.0)]
        [TestCase(-180.0, 0.0)]
        public void LocalTime_FromHourAngle(double hourAngleDeg, double expected)
        {
            Assert.AreEqual(expected, SimClock.LocalTime24(hourAngleDeg * Deg), 1e-9);
        }

        // Time 0 is perihelion (about 3 January on Earth).
        [TestCase(0.0, Season.Winter, TestName = "Earth at perihelion (3 Jan): northern winter")]
        [TestCase(80.0, Season.Spring, TestName = "Earth 80 days later (late March): spring")]
        [TestCase(175.0, Season.Summer, TestName = "Earth 175 days later (late June): summer")]
        [TestCase(270.0, Season.Autumn, TestName = "Earth 270 days later (late September): autumn")]
        public void Season_FollowsTheOrbit(double days, Season expected)
        {
            double lambda = SimClock.EclipticLongitude(Earth, days * 24.0);
            Assert.AreEqual(expected, SimClock.SeasonFromEclipticLongitude(lambda));
        }

        [Test]
        public void NorthernSpringEquinox_IsAbout76DaysAfterPerihelion()
        {
            // λ reaches 360° (vernal equinox) ~20 March, 76–77 days after perihelion.
            double before = SimClock.EclipticLongitude(Earth, 75.5 * 24.0) / Deg;
            double after = SimClock.EclipticLongitude(Earth, 77.5 * 24.0) / Deg;
            Assert.Less(before, 360.0);
            Assert.Greater(after, 360.0);
        }

        [Test]
        public void Sun_RadiusAndAngularSize()
        {
            double r = StarGeometry.Radius(SolarConstants.SolarLuminosity, 5772.0);
            Assert.AreEqual(6.957e8, r, 0.002e8, "nominal solar radius");
            double angular = StarGeometry.AngularDiameter(r, SolarConstants.AstronomicalUnit) / Deg;
            Assert.AreEqual(0.533, angular, 0.002, "Sun seen from Earth");
        }

        [Test]
        public void RedDwarf_LooksBiggerFromAClosePlanet()
        {
            // Proxima Centauri (L ≈ 0.0017 L☉, 3042 K) from Proxima b at 0.0485 AU: ≈ 0.15 R☉, ≈ 1.6°.
            double r = StarGeometry.Radius(0.0017 * SolarConstants.SolarLuminosity, 3042.0);
            Assert.AreEqual(0.148, r / 6.957e8, 0.01);
            double angular = StarGeometry.AngularDiameter(r, 0.0485 * SolarConstants.AstronomicalUnit) / Deg;
            Assert.AreEqual(1.6, angular, 0.15);
        }
    }
}

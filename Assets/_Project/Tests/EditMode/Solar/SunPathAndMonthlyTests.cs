using System;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Solar
{
    public class SunPathTests
    {
        const double Deg = Math.PI / 180.0;
        const double Obliquity = 23.44 * Deg;

        [Test]
        public void Equator_Equinox_RisesAt6_OverheadAtNoon_SetsAt18()
        {
            SunPathPoint[] day = SunPath.Day(0.0, 0.0, 24.0, steps: 96);
            Assert.AreEqual(0.0, day[24].ElevationDeg, 1e-6, "06:00 on the horizon");
            Assert.AreEqual(90.0, day[48].ElevationDeg, 1e-6, "noon overhead");
            Assert.AreEqual(0.0, day[72].ElevationDeg, 1e-6, "18:00 on the horizon");
            Assert.AreEqual(90.0, day[30].AzimuthDeg, 1e-6, "rises due east");
            Assert.AreEqual(0.5, SunPath.DaylightFraction(0.0, 0.0), 1e-12);
        }

        [Test]
        public void Lat40_JuneSolstice_NoonElevationAndDayLength()
        {
            double lat = 40.0 * Deg;
            SunPathPoint noon = SunPath.Day(lat, Obliquity, 24.0, steps: 96)[48];
            Assert.AreEqual(73.44, noon.ElevationDeg, 1e-6, "90° − 40° + 23.44°");
            Assert.AreEqual(180.0, noon.AzimuthDeg, 1e-6, "due south at noon");
            Assert.AreEqual(73.44, SunPath.NoonElevationDeg(lat, Obliquity), 1e-9);
            // 2·acos(−tan 40° · tan 23.44°) / 15°/h ≈ 14.85 h.
            Assert.AreEqual(14.85, SunPath.DaylightFraction(lat, Obliquity) * 24.0, 0.01);
            Assert.AreEqual(9.15, SunPath.DaylightFraction(lat, -Obliquity) * 24.0, 0.01, "December: the rest of the day");
        }

        [Test]
        public void Arctic_MidnightSunAndPolarNight()
        {
            double lat = 70.0 * Deg;
            Assert.AreEqual(1.0, SunPath.DaylightFraction(lat, Obliquity));
            Assert.AreEqual(0.0, SunPath.DaylightFraction(lat, -Obliquity));
            SunPathPoint midnight = SunPath.Day(lat, Obliquity, 24.0)[0];
            Assert.Greater(midnight.ElevationDeg, 0.0, "still up at midnight");
            Assert.AreEqual(0.0, midnight.AzimuthDeg % 360.0, 1e-6, "low in the north");
        }
    }

    public class MonthlyYieldTests
    {
        const double Deg = Math.PI / 180.0;

        static PlanetSolarParams Earth(double eccentricity = 0.0167, bool locked = false) => new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, eccentricity, locked ? 0.0 : 23.44 * Deg,
            282.94 * Deg, locked ? 0.0 : 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, locked);

        static PanelParams Panel(double latDeg, double tiltDeg, double azimuthDeg, double lonDeg = 0.0) =>
            new PanelParams(latDeg * Deg, tiltDeg * Deg, azimuthDeg * Deg, 10.0, 0.2, 45.0, -0.004, 0.2, 1.0, 1.0, 0.0, 1.0, lonDeg * Deg);

        [TestCase(40.0, 40.0, 180.0, 0.0167, false)]
        [TestCase(-30.0, 10.0, 0.0, 0.0167, false)]
        [TestCase(10.0, 0.0, 0.0, 0.0, false)]
        [TestCase(20.0, 30.0, 90.0, 0.25, false)]
        [TestCase(0.0, 20.0, 270.0, 0.0, true)]
        public void TwelveMonths_AddUpToTheYear(double lat, double tilt, double azimuth, double e, bool locked)
        {
            PlanetSolarParams planet = Earth(e, locked);
            PanelParams panel = Panel(lat, tilt, azimuth, locked ? 30.0 : 0.0);
            double[] months = MonthlyYield.Calculate(planet, panel);
            double sum = 0.0;
            foreach (double m in months)
                sum += m;
            Assert.AreEqual(12, months.Length);
            Assert.AreEqual(AnnualYield.Calculate(planet, panel).EnergyWh, sum, 1e-6 * sum + 1e-9);
        }

        [Test]
        public void FlatPanel_NorthernSummerBeatsWinter_AndSouthIsTheOtherWay()
        {
            // Month 1 starts at periapsis (≈ 3 January), so month 7 ≈ July.
            double[] north = MonthlyYield.Calculate(Earth(), Panel(45.0, 0.0, 0.0));
            double[] south = MonthlyYield.Calculate(Earth(), Panel(-45.0, 0.0, 0.0));
            Assert.Greater(north[6], 2.0 * north[0], "45° N: July far above January");
            Assert.Greater(south[0], 2.0 * south[6], "45° S: January far above July");
        }
    }
}

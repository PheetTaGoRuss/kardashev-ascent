using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Steps 2–3 of the solar model: where the star is on the local sky.</summary>
    public static class SunPosition
    {
        /// <summary>sin δ = sin ε · sin λ. λ is the star's ecliptic longitude, measured from the equinox.</summary>
        public static double Declination(double obliquity, double eclipticLongitude) =>
            Math.Asin(Math.Sin(obliquity) * Math.Sin(eclipticLongitude));

        /// <summary>h = 2π·t/D − π: zero at local noon, negative in the morning. t and D in the same unit.</summary>
        public static double HourAngle(double localSolarTime, double dayLength) =>
            2.0 * Math.PI * localSolarTime / dayLength - Math.PI;

        /// <summary>Unit vector toward the star in (east, north, up). Up ≤ 0 means night.</summary>
        public static Double3 Direction(double latitude, double declination, double hourAngle)
        {
            double cd = Math.Cos(declination);
            double sd = Math.Sin(declination);
            double cl = Math.Cos(latitude);
            double sl = Math.Sin(latitude);
            double ch = Math.Cos(hourAngle);
            return new Double3(
                -cd * Math.Sin(hourAngle),
                cl * sd - sl * cd * ch,
                sl * sd + cl * cd * ch);
        }

        public static double ZenithDegrees(Double3 sun) =>
            Math.Acos(Math.Max(-1.0, Math.Min(1.0, sun.Up))) * SolarConstants.RadToDeg;

        /// <summary>Azimuth clockwise from north, 0–360°.</summary>
        public static double AzimuthDegrees(Double3 sun)
        {
            double az = Math.Atan2(sun.East, sun.North) * SolarConstants.RadToDeg;
            return az < 0.0 ? az + 360.0 : az;
        }
    }
}

using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>One point of the star's track across the local sky.</summary>
    public readonly struct SunPathPoint
    {
        /// <summary>Local solar time, hours (noon = half the day length).</summary>
        public readonly double Hour;
        /// <summary>Clockwise from north, 0–360°.</summary>
        public readonly double AzimuthDeg;
        /// <summary>Above the horizon, degrees (negative = below).</summary>
        public readonly double ElevationDeg;

        public SunPathPoint(double hour, double azimuthDeg, double elevationDeg)
        {
            Hour = hour;
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
        }
    }

    /// <summary>
    /// The star's path across one place's sky over a day (#13): elevation against azimuth, for teaching why panels
    /// face the equator and tilt with latitude. Uses the same sun vector as the solar model
    /// (<see cref="SunPosition.Direction"/>). Key days: the two solstices (δ = ±ε) and the equinox (δ = 0).
    /// </summary>
    public static class SunPath
    {
        /// <summary>The star's path on a day with declination δ, sampled evenly over the day (including night).</summary>
        public static SunPathPoint[] Day(double latitude, double declination, double dayLengthHours, int steps = 96)
        {
            if (steps < 2)
                throw new ArgumentOutOfRangeException(nameof(steps));
            var points = new SunPathPoint[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double hour = dayLengthHours * i / steps;
                Double3 sun = SunPosition.Direction(latitude, declination, SunPosition.HourAngle(hour, dayLengthHours));
                points[i] = new SunPathPoint(hour, SunPosition.AzimuthDegrees(sun), 90.0 - SunPosition.ZenithDegrees(sun));
            }
            return points;
        }

        /// <summary>
        /// Share of the day the star's centre is above the horizon: cos h₀ = −tan φ · tan δ
        /// (0 in polar night, 1 in midnight sun).
        /// </summary>
        public static double DaylightFraction(double latitude, double declination)
        {
            double x = -Math.Tan(latitude) * Math.Tan(declination);
            if (x >= 1.0) return 0.0;
            if (x <= -1.0) return 1.0;
            return Math.Acos(x) / Math.PI;
        }

        /// <summary>Highest elevation of the day (at local noon), degrees: 90° − |φ − δ|.</summary>
        public static double NoonElevationDeg(double latitude, double declination) =>
            90.0 - Math.Abs(latitude - declination) * SolarConstants.RadToDeg;
    }
}

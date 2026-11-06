using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>
    /// The textbook chain the sky view teaches, in degrees: date → declination → sun altitude and azimuth →
    /// angle on the panel, and the reverse step a player uses to find their latitude from the noon sun.
    /// The game's own model (<see cref="SunPosition"/>, <see cref="PanelModel"/>) gives the same numbers; tests check it.
    /// </summary>
    public static class SkyMath
    {
        const double Deg = Math.PI / 180.0;

        /// <summary>
        /// Cooper's formula for Earth: δ = 23.44° · sin(360° · (284 + n) / 365), n = day of the year (1 January = 1).
        /// n = 80 (21 March) gives −0.4035° (−0° 24′ 12.5″). An approximation (within about 1°); the game uses sin δ = sin ε · sin λ.
        /// </summary>
        public static double CooperDeclinationDeg(int dayOfYear) =>
            23.44 * Math.Sin(360.0 * (284.0 + dayOfYear) / 365.0 * Deg);

        /// <summary>Exact declination from the axial tilt ε and the star's ecliptic longitude λ (both degrees).</summary>
        public static double DeclinationDeg(double obliquityDeg, double eclipticLongitudeDeg) =>
            Math.Asin(Math.Sin(obliquityDeg * Deg) * Math.Sin(eclipticLongitudeDeg * Deg)) / Deg;

        /// <summary>
        /// Earth calendar day (1–365) for an ecliptic longitude: the March equinox (λ = 0) is day 80 (21 March) and
        /// the year runs 365 days. Lets the sky view use Cooper's formula on an Earth-like planet.
        /// </summary>
        public static int EarthDayOfYear(double eclipticLongitudeDeg)
        {
            double l = eclipticLongitudeDeg % 360.0;
            if (l < 0.0) l += 360.0;
            int n = (int)Math.Floor(80.0 + l / 360.0 * 365.0 + 0.5);
            return (n - 1) % 365 + 1;
        }

        /// <summary>sin h = sin φ · sin δ + cos φ · cos δ · cos H (altitude above the horizon, degrees).</summary>
        public static double SunAltitudeDeg(double latitudeDeg, double declinationDeg, double hourAngleDeg)
        {
            double s = Math.Sin(latitudeDeg * Deg) * Math.Sin(declinationDeg * Deg)
                       + Math.Cos(latitudeDeg * Deg) * Math.Cos(declinationDeg * Deg) * Math.Cos(hourAngleDeg * Deg);
            return Math.Asin(Math.Max(-1.0, Math.Min(1.0, s))) / Deg;
        }

        /// <summary>Sun azimuth, degrees clockwise from north (0–360).</summary>
        public static double SunAzimuthDeg(double latitudeDeg, double declinationDeg, double hourAngleDeg) =>
            SunPosition.AzimuthDegrees(SunPosition.Direction(latitudeDeg * Deg, declinationDeg * Deg, hourAngleDeg * Deg));

        /// <summary>
        /// cos θ = cos h · sin β · cos(A_s − A_p) + sin h · cos β: the angle between the sun and the panel's normal
        /// (h sun altitude, A_s sun azimuth, β panel tilt, A_p panel azimuth; degrees). Negative: the sun is behind the panel.
        /// </summary>
        public static double CosIncidence(double sunAltitudeDeg, double sunAzimuthDeg, double tiltDeg, double panelAzimuthDeg) =>
            Math.Cos(sunAltitudeDeg * Deg) * Math.Sin(tiltDeg * Deg) * Math.Cos((sunAzimuthDeg - panelAzimuthDeg) * Deg)
            + Math.Sin(sunAltitudeDeg * Deg) * Math.Cos(tiltDeg * Deg);

        /// <summary>Highest the sun gets today: h_noon = 90° − |φ − δ|.</summary>
        public static double NoonAltitudeDeg(double latitudeDeg, double declinationDeg) =>
            90.0 - Math.Abs(latitudeDeg - declinationDeg);

        /// <summary>
        /// Latitude from the sun's altitude at noon and today's declination. With the noon sun in the south
        /// (azimuth near 180°): φ = 90° − h_noon + δ; in the north: φ = h_noon − 90° + δ.
        /// </summary>
        public static double LatitudeFromNoonDeg(double noonAltitudeDeg, double declinationDeg, bool sunInTheSouth) =>
            sunInTheSouth ? 90.0 - noonAltitudeDeg + declinationDeg : noonAltitudeDeg - 90.0 + declinationDeg;

        /// <summary>
        /// Hour angle of sunrise and sunset, cos H₀ = −tan φ · tan δ (degrees; the day lasts 2H₀ of the 360°).
        /// 180 when the sun never sets, 0 when it never rises.
        /// </summary>
        public static double SunriseHourAngleDeg(double latitudeDeg, double declinationDeg)
        {
            double c = -Math.Tan(latitudeDeg * Deg) * Math.Tan(declinationDeg * Deg);
            if (c <= -1.0) return 180.0;
            if (c >= 1.0) return 0.0;
            return Math.Acos(c) / Deg;
        }

        /// <summary>Rule of thumb for a fixed panel: tilt it by your latitude, facing the equator.</summary>
        public static double RuleOfThumbTiltDeg(double latitudeDeg) => Math.Abs(latitudeDeg);
    }
}

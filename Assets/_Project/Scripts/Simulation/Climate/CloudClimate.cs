using System;

namespace KardashevAscent.Simulation.Climate
{
    /// <summary>
    /// Earth's real weather as a multiplier on clear-sky sunlight (docs/physics/physics-verification.md, P1 item 13),
    /// from NASA POWER: cloud factor = all-sky ÷ clear-sky surface sunlight, by latitude and month.
    /// Meant for Earth-like planets (Viridia). Simplification: the factor is applied to the panel's light as a whole,
    /// although clouds turn direct light into diffuse light, which a tilted panel catches less of.
    /// </summary>
    public static class CloudClimate
    {
        /// <summary>Year fraction of Earth's perihelion (about 3 January), where mean anomaly is 0.</summary>
        public const double PerihelionYearFraction = 2.5 / 365.25;

        /// <summary>
        /// Share of clear-sky sunlight that gets through clouds at this latitude (radians) and time of year
        /// (0 = 1 January, 0.5 ≈ 2 July). Interpolated between 10° latitude rows and month centres; clamped at ±80°.
        /// </summary>
        public static double Factor(double latitude, double yearFraction)
        {
            double[][] t = CloudClimateTable.Factor;
            double y = (latitude * 180.0 / Math.PI - CloudClimateTable.FirstLatitudeDeg) / CloudClimateTable.StepDeg;
            y = Math.Max(0.0, Math.Min(t.Length - 1, y));
            int i0 = Math.Min((int)y, t.Length - 2);
            double fy = y - i0;

            double m = (yearFraction - Math.Floor(yearFraction)) * 12.0 - 0.5;   // month centres at 0..11
            int m0 = (int)Math.Floor(m);
            double fm = m - m0;
            int a = (m0 + 12) % 12, b = (m0 + 13) % 12;

            double low = t[i0][a] + fm * (t[i0][b] - t[i0][a]);
            double high = t[i0 + 1][a] + fm * (t[i0 + 1][b] - t[i0 + 1][a]);
            return low + fy * (high - low);
        }

        /// <summary>Mean over the year at one latitude (radians).</summary>
        public static double AnnualMean(double latitude)
        {
            double sum = 0.0;
            for (int m = 0; m < 12; m++)
                sum += Factor(latitude, (m + 0.5) / 12.0);
            return sum / 12.0;
        }

        /// <summary>Time of year for an Earth-like orbit at this mean anomaly (radians, 0 at perihelion).</summary>
        public static double YearFractionFromMeanAnomaly(double meanAnomaly)
        {
            double f = meanAnomaly / (2.0 * Math.PI) + PerihelionYearFraction;
            return f - Math.Floor(f);
        }
    }
}

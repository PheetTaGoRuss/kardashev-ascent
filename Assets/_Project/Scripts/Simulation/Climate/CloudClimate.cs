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
    /// <summary>
    /// Earth's real air temperature 2 m above the ground (NASA POWER T2M climatology, 2001–2025), by latitude and month,
    /// for Earth-like planets (the same ones that use <see cref="CloudClimate"/>). Each row mixes land and ocean.
    /// </summary>
    public static class EarthAirTemperature
    {
        /// <summary>Air temperature, °C, at this latitude (radians) and time of year (0 = 1 January). Interpolated like the clouds; clamped at ±80°.</summary>
        public static double Celsius(double latitude, double yearFraction)
        {
            double[][] t = AirTemperatureTable.Celsius;
            double y = (latitude * 180.0 / Math.PI - AirTemperatureTable.FirstLatitudeDeg) / AirTemperatureTable.StepDeg;
            y = Math.Max(0.0, Math.Min(t.Length - 1, y));
            int i0 = Math.Min((int)y, t.Length - 2);
            double fy = y - i0;
            double m = (yearFraction - Math.Floor(yearFraction)) * 12.0 - 0.5;
            int m0 = (int)Math.Floor(m);
            double fm = m - m0;
            int a = (m0 + 12) % 12, b = (m0 + 13) % 12;
            double low = t[i0][a] + fm * (t[i0][b] - t[i0][a]);
            double high = t[i0 + 1][a] + fm * (t[i0 + 1][b] - t[i0 + 1][a]);
            return low + fy * (high - low);
        }

        /// <summary>Mean over the year at one latitude (radians), °C.</summary>
        public static double AnnualMean(double latitude)
        {
            double sum = 0.0;
            for (int m = 0; m < 12; m++)
                sum += Celsius(latitude, (m + 0.5) / 12.0);
            return sum / 12.0;
        }
    }
    /// <summary>
    /// Earth's real ground reflectance and aerosol haze (NASA POWER climatology 2001–2025: ALLSKY_SRF_ALB and AOD_55),
    /// by latitude and month, for Earth-like planets. Both are used as changes around the game's own values, so nothing
    /// is counted twice: a terrain keeps its reflectance and NASA adds the seasons (snow); the planet keeps its tested
    /// mean optical depth τ and NASA's aerosols move it up or down by place and season.
    /// </summary>
    public static class EarthGroundAndHaze
    {
        /// <summary>
        /// The one wavelength whose aerosol optical depth equals the broadband (whole-spectrum) one, nm:
        /// Molineaux, Ineichen &amp; O'Neill 1998, Applied Optics 37(30), 7008–7018 (±0.01 at about 0.7 µm).
        /// </summary>
        public const double KeyWavelengthNm = 700.0;

        /// <summary>Ångström exponent α in τ ∝ λ^−α: about 1.3 for natural atmospheres (Ångström 1929).</summary>
        public const double AngstromExponent = 1.3;

        /// <summary>Broadband aerosol depth per unit of NASA's 550 nm value: (700/550)^−1.3 ≈ 0.731.</summary>
        public static double BroadbandPerAod550 => Math.Pow(KeyWavelengthNm / 550.0, -AngstromExponent);

        /// <summary>NASA's ground reflectance at this latitude (radians) and time of year (0 = 1 January).</summary>
        public static double Albedo(double latitude, double yearFraction) =>
            Interpolate(GroundAndHazeTable.Albedo, latitude, yearFraction);

        /// <summary>Reflectance now minus the latitude's yearly mean: the season's change (more in snowy winters).</summary>
        public static double AlbedoChange(double latitude, double yearFraction) =>
            Albedo(latitude, yearFraction) - YearMean(GroundAndHazeTable.Albedo, latitude);

        /// <summary>Broadband aerosol optical depth at this latitude and time of year (NASA AOD at 550 nm, moved to 700 nm).</summary>
        public static double AerosolDepth(double latitude, double yearFraction) =>
            Interpolate(GroundAndHazeTable.Aod550, latitude, yearFraction) * BroadbandPerAod550;

        /// <summary>Broadband aerosol depth over the year at one latitude.</summary>
        public static double AerosolDepthYear(double latitude) => YearMean(GroundAndHazeTable.Aod550, latitude) * BroadbandPerAod550;

        static double globalMean = double.NaN;

        /// <summary>Earth's mean broadband aerosol depth, weighted by area (cos latitude) and averaged over the year.</summary>
        public static double GlobalMeanAerosolDepth
        {
            get
            {
                if (!double.IsNaN(globalMean)) return globalMean;
                double sum = 0.0, weight = 0.0;
                for (int r = 0; r < GroundAndHazeTable.Aod550.Length; r++)
                {
                    double lat = (GroundAndHazeTable.FirstLatitudeDeg + r * GroundAndHazeTable.StepDeg) * Math.PI / 180.0;
                    double w = Math.Cos(lat);
                    double row = 0.0;
                    foreach (double v in GroundAndHazeTable.Aod550[r]) row += v;
                    sum += w * row / 12.0;
                    weight += w;
                }
                return globalMean = sum / weight * BroadbandPerAod550;
            }
        }

        /// <summary>How far this place and season's haze is from Earth's mean (add it to the planet's τ).</summary>
        public static double HazeChange(double latitude, double yearFraction) => AerosolDepth(latitude, yearFraction) - GlobalMeanAerosolDepth;

        /// <summary>The same over the year at one latitude.</summary>
        public static double HazeChangeYear(double latitude) => AerosolDepthYear(latitude) - GlobalMeanAerosolDepth;

        static double YearMean(double[][] t, double latitude)
        {
            double sum = 0.0;
            for (int m = 0; m < 12; m++)
                sum += Interpolate(t, latitude, (m + 0.5) / 12.0);
            return sum / 12.0;
        }

        static double Interpolate(double[][] t, double latitude, double yearFraction)
        {
            double y = (latitude * 180.0 / Math.PI - GroundAndHazeTable.FirstLatitudeDeg) / GroundAndHazeTable.StepDeg;
            y = Math.Max(0.0, Math.Min(t.Length - 1, y));
            int i0 = Math.Min((int)y, t.Length - 2);
            double fy = y - i0;
            double m = (yearFraction - Math.Floor(yearFraction)) * 12.0 - 0.5;
            int m0 = (int)Math.Floor(m);
            double fm = m - m0;
            int a = (m0 + 12) % 12, b = (m0 + 13) % 12;
            double low = t[i0][a] + fm * (t[i0][b] - t[i0][a]);
            double high = t[i0 + 1][a] + fm * (t[i0 + 1][b] - t[i0 + 1][a]);
            return low + fy * (high - low);
        }
    }
}

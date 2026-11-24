using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// Sea waves and wave-energy converters (director 2026-11-19: tidal and wave energy). Physics first: the fully developed
    /// sea of Pierson and Moskowitz (1964) from the wind, and the deep-water wave energy flux; then game rules marked TBD
    /// for where the wind blows and what a converter line takes. Pure functions.
    /// </summary>
    public static class Waves
    {
        public const double RhoSeaWater = 1025.0;   // kg/m3

        /// <summary>
        /// Significant wave height of a fully developed sea, m: H = 2 sqrt(alpha / beta) U^2 / g = 0.209 U^2 / g, with the
        /// Pierson-Moskowitz constants alpha = 8.1e-3 and beta = 0.74 and U the wind at 19.5 m. For 19.5 m/s on Earth: 8.1 m.
        /// (Stewart, Introduction to Physical Oceanography, 16.4.)
        /// </summary>
        public static double SignificantHeightM(double windMs, double gravityMs2)
        {
            if (windMs <= 0.0 || gravityMs2 <= 0.0) return 0.0;
            const double alpha = 8.1e-3, beta = 0.74;
            return 2.0 * Math.Sqrt(alpha / beta) * windMs * windMs / gravityMs2;
        }

        /// <summary>Peak wave period of a fully developed sea, s: T = 2 pi / (0.877 g / U). For 19.5 m/s on Earth: 14.2 s.</summary>
        public static double PeakPeriodS(double windMs, double gravityMs2)
        {
            if (windMs <= 0.0 || gravityMs2 <= 0.0) return 0.0;
            return 2.0 * Math.PI * windMs / (0.877 * gravityMs2);
        }

        /// <summary>The energy period is about 0.9 of the peak period for this spectrum.</summary>
        public const double EnergyPeriodShare = 0.9;

        /// <summary>
        /// Deep-water wave energy flux per metre of wave front, W/m: P = rho g^2 H^2 T / (64 pi), H the significant height and
        /// T the energy period. A 3 m, 8 s sea gives about 36 kW/m.
        /// </summary>
        public static double EnergyFluxWPerM(double heightM, double energyPeriodS, double gravityMs2)
        {
            if (heightM <= 0.0 || energyPeriodS <= 0.0 || gravityMs2 <= 0.0) return 0.0;
            return RhoSeaWater * gravityMs2 * gravityMs2 * heightM * heightM * energyPeriodS / (64.0 * Math.PI);
        }

        /// <summary>
        /// The wind at the 19.5 m height of the Pierson-Moskowitz sea from the 10 m wind: the 1/7 power law of the neutral boundary layer,
        /// U(z) = U10 (z / 10)^(1/7), so U19.5 = 1.10 U10 (Hsu, Meindl and Gilhousen 1994, J. Appl. Meteorol. 33).
        /// </summary>
        public static double WindAt195FromAt10(double wind10Ms) => wind10Ms * Math.Pow(19.5 / 10.0, 1.0 / 7.0);

        // ---- game rules (TBD) ----

        /// <summary>Wind at 19.5 m, m/s, by latitude: calm at the equator and poles, strongest in the mid-latitudes (about 45 degrees). Game rule, TBD.</summary>
        public static double WindMs(double latitudeRad)
        {
            double s = Math.Sin(2.0 * latitudeRad);
            return 3.0 + 7.0 * s * s;
        }

        /// <summary>A real sea is not fully developed (limited fetch and time): this share of the height (game rule, TBD).</summary>
        public const double DevelopmentShare = 0.5;

        /// <summary>Width of one wave-converter line, m (game rule, TBD).</summary>
        public const double LineWidthM = 150.0;

        /// <summary>Share of the wave power a line turns into electricity (real converters take about a third; game rule, TBD).</summary>
        public const double CaptureShare = 0.35;

        /// <summary>One line's electric rating, W (a Pelamis was 0.75 MW; game rule, TBD).</summary>
        public const double RatedW = 1.5e6;

        /// <summary>Below this height the line does not move enough to run, m (game rule, TBD).</summary>
        public const double CutInHeightM = 0.8;

        /// <summary>Above this height the line shuts down to survive (game rule, TBD).</summary>
        public const double SurvivalHeightM = 8.0;

        /// <summary>Electric power of one converter line in a sea of this height and period, W.</summary>
        public static double LinePowerW(double heightM, double energyPeriodS, double gravityMs2)
        {
            if (heightM < CutInHeightM || heightM > SurvivalHeightM) return 0.0;
            double flux = EnergyFluxWPerM(heightM, energyPeriodS, gravityMs2);
            return Math.Min(RatedW, flux * LineWidthM * CaptureShare);
        }
    }
}

using System;

namespace KardashevAscent.Simulation.Climate
{
    /// <summary>
    /// The seasonal pressure cycle and the dust of a thin CO2 atmosphere like Mars's (Weather, PL-07, W4, decided 2026-11-24, director delegated).
    /// The record has no atmosphere-composition field, so the class is a game rule: surface pressure between <see cref="MinPressureEarth"/> and
    /// <see cref="MaxPressureEarth"/> atmospheres. All of it is deterministic from the seed and the year (nothing is saved).
    /// </summary>
    public static class ThinAirClimate
    {
        public const double MinPressureEarth = 0.001, MaxPressureEarth = 0.05;

        public static bool Applies(double surfacePressureEarth) => surfacePressureEarth >= MinPressureEarth && surfacePressureEarth <= MaxPressureEarth;

        // ---- the pressure cycle ----

        /// <summary>
        /// Viking Lander 1 saw the surface pressure run between about 6.9 and 9.0 hPa in a Mars year, lowest near areocentric longitude Ls 150 deg
        /// (the CO2 of the winter pole freezing out) and highest near Ls 250 deg (it sublimes back) [Hess et al. 1980, JGR 85; Tillman et al. 1993, JGR 98].
        /// Game rule, TBD: between those two points the pressure follows a raised cosine (the smaller second maximum is not modelled), and the cycle
        /// is scaled so its mean over the year is the planet's tabulated pressure.
        /// </summary>
        public const double MinHPa = 6.9, MaxHPa = 9.0, MinLsDeg = 150.0, MaxLsDeg = 250.0;

        static double Shape(double lsDeg)
        {
            double ls = ((lsDeg % 360.0) + 360.0) % 360.0;
            double up = ((ls - MinLsDeg) % 360.0 + 360.0) % 360.0;          // degrees since the minimum
            const double riseDeg = MaxLsDeg - MinLsDeg, fallDeg = 360.0 - riseDeg;
            double x = up <= riseDeg ? 0.5 - 0.5 * Math.Cos(Math.PI * up / riseDeg)            // 0 at the minimum, 1 at the maximum
                                     : 0.5 + 0.5 * Math.Cos(Math.PI * (up - riseDeg) / fallDeg);
            return MinHPa + (MaxHPa - MinHPa) * x;
        }

        static readonly double ShapeMean = MeanShape();
        static double MeanShape() { double s = 0.0; for (int i = 0; i < 360; i++) s += Shape(i + 0.5); return s / 360.0; }

        /// <summary>The pressure now over the planet's yearly mean (1 on average over the year).</summary>
        public static double PressureFactor(double lsDeg) => Shape(lsDeg) / ShapeMean;

        // ---- dust ----

        /// <summary>
        /// Background dust: the optical depth is higher in the dusty season (Ls 180 to 360 deg, southern spring and summer, when Mars is nearest the Sun) and lower
        /// in the clear half [Curiosity and Mars Exploration Rover optical-depth records, Lemmon et al. 2015, Icarus 251; Smith 2004]. Game rule: the
        /// seasonal factor is 1 + 0.8 (half-wave cosine peaking at Ls 270 minus its yearly mean), so the yearly mean of the optical depth is the planet's tabulated one.
        /// </summary>
        public const double SeasonAmplitude = 0.8;
        static readonly double BumpMean = 1.0 / Math.PI;

        public static double SeasonFactor(double lsDeg)
        {
            double bump = Math.Max(0.0, Math.Cos((lsDeg - 270.0) * Math.PI / 180.0));
            return 1.0 + SeasonAmplitude * (bump - BumpMean);
        }

        /// <summary>
        /// Global dust storms: about one Mars year in three has one [Zurek and Martin 1993, JGR 98; Montabone et al. 2015, Icarus 251]. A storm starts between
        /// Ls 200 and 290 deg, the optical depth rises over 15 days to a peak between 6 and 11 (the 2018 storm: Opportunity measured about 10.8, NASA/JPL),
        /// then decays with an e-folding time of 40 days. Which years and how strong is a function of the seed and the year number only.
        /// </summary>
        public const double GlobalChancePerYear = 1.0 / 3.0, GlobalPeakMin = 6.0, GlobalPeakMax = 11.0, RiseDays = 15.0, DecayDays = 40.0;

        static uint Mix(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; } }
        static double Unit(int seed, int a, int b, int c)
        {
            unchecked
            {
                uint h = Mix((uint)seed ^ 0x51ed270bu);
                h = Mix(h + (uint)a * 0x9e3779b1u);
                h = Mix(h + (uint)b * 0x85ebca6bu);
                h = Mix(h + (uint)c * 0xc2b2ae35u);
                return (h >> 8) / 16777216.0;
            }
        }

        /// <summary>The extra optical depth from global storms at this time (hours), for a year of <paramref name="yearHours"/> and a sol of <paramref name="dayHours"/>.</summary>
        public static double GlobalStormTau(int seed, double timeHours, double yearHours, double dayHours)
        {
            if (yearHours <= 0.0) return 0.0;
            int year = (int)Math.Floor(timeHours / yearHours);
            double extra = 0.0;
            for (int y = Math.Max(0, year - 1); y <= year; y++)   // last year's storm may still be decaying
            {
                if (Unit(seed, y, 1, 0) >= GlobalChancePerYear) continue;
                double startLs = 200.0 + 90.0 * Unit(seed, y, 2, 0);
                double peak = GlobalPeakMin + (GlobalPeakMax - GlobalPeakMin) * Unit(seed, y, 3, 0);
                double dayH = Math.Max(1.0, dayHours);
                double t = timeHours - (y + startLs / 360.0) * yearHours;   // hours since onset
                double days = t / dayH;
                if (days < 0.0 || days > RiseDays + 6.0 * DecayDays) continue;
                extra = Math.Max(extra, days < RiseDays ? peak * days / RiseDays : peak * Math.Exp(-(days - RiseDays) / DecayDays));
            }
            return extra;
        }

        /// <summary>Regional storms: in windows of 10 sols a region of about 30 deg has a storm with a chance of 1 in 20 in the dusty season and 1 in 100 in the clear half. A storm adds up to 0.8 to 2.0 of optical depth, rising in 2 sols and decaying with an e-folding time of 5 sols. Game rule, TBD, shaped by the seasonal frequency of regional storms (Cantor et al. 2001, JGR 106).</summary>
        public static double RegionalStormTau(int seed, double latRad, double lonRad, double timeHours, double yearHours, double dayHours, Func<double, double> lsAtTime)
        {
            double dayH = Math.Max(1.0, dayHours);
            double sol = timeHours / dayH;
            const double WindowSols = 10.0, CellRad = 30.0 * Math.PI / 180.0;
            int la = (int)Math.Floor((latRad + Math.PI / 2.0) / CellRad), lo = (int)Math.Floor((lonRad + Math.PI) / CellRad);
            double extra = 0.0;
            long w1 = (long)Math.Floor(sol / WindowSols);
            for (long w = Math.Max(0, w1 - 3); w <= w1; w++)
            {
                double ls = lsAtTime((w + 0.5) * WindowSols * dayH);
                double chance = ls >= 180.0 && ls < 360.0 ? 0.05 : 0.01;
                double u = Unit(seed, la * 1000 + lo, (int)(w & 0x7fffffff), 7);
                if (u >= chance) continue;
                double amp = 0.8 + 1.2 * Unit(seed, la * 1000 + lo, (int)(w & 0x7fffffff), 8);
                double s = sol - w * WindowSols;   // sols since the window began
                extra = Math.Max(extra, s < 2.0 ? amp * s / 2.0 : amp * Math.Exp(-(s - 2.0) / 5.0));
            }
            return extra;
        }
    }
}

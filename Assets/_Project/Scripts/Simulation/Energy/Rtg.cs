using System;

namespace KardashevAscent.Simulation.Energy
{
    /// <summary>
    /// Radioisotope thermoelectric generator (Pu-238, MMRTG-like): a small, steady source that slowly fades.
    /// P(t) = P₀ · exp(−λ_eff · t), λ_eff = ln 2 / 87.7 y (fuel decay) + k_TE (thermocouple ageing).
    /// Default k_TE matches NASA's fact sheet: 110 W at the start, 72 W after 17 years (≈ 2.46 %/year in all).
    /// Source: docs/physics/notes/energy_sources.md §3.
    /// </summary>
    public static class Rtg
    {
        public const double HalfLifeYears = 87.7;
        /// <summary>Electric power of one fresh MMRTG, W.</summary>
        public const double MmrtgPowerW = 110.0;
        public const double HoursPerYear = 8766.0;

        /// <summary>Fuel-decay constant, per year.</summary>
        public static readonly double FuelDecayPerYear = Math.Log(2.0) / HalfLifeYears;

        /// <summary>Thermocouple ageing that, with fuel decay, takes 110 W to 72 W in 17 years, per year.</summary>
        public static readonly double DefaultAgeingPerYear = -Math.Log(72.0 / 110.0) / 17.0 - FuelDecayPerYear;

        /// <summary>Electric power after <paramref name="years"/>, W.</summary>
        public static double Power(double startW, double years, double ageingPerYear) =>
            startW * Math.Exp(-(FuelDecayPerYear + ageingPerYear) * Math.Max(0.0, years));

        public static double Power(double startW, double years) => Power(startW, years, DefaultAgeingPerYear);
    }
}

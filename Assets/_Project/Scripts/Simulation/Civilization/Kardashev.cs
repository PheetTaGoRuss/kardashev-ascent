using System;

namespace KardashevAscent.Simulation.Civilization
{
    /// <summary>
    /// Kardashev scale (docs/physics/physics-verification.md, P1 item 10).
    /// K uses Sagan's interpolation K = (log₁₀ P − 6) / 10, so every +0.1 K is ten times the power.
    /// The per-planet Type I target is a game rule, not a scientific definition: the UI shows absolute K
    /// next to "planet completion" (P / P_I) and must not call Type I "all of the planet's sunlight".
    /// </summary>
    public static class Kardashev
    {
        /// <summary>Sagan's Type I, W (K = 1).</summary>
        public const double TypeIW = 1e16;

        /// <summary>Power treated as 1 W when P ≤ 1 W, so K never goes below −0.6.</summary>
        public const double MinimumW = 1.0;

        /// <summary>Earth's solar constant, W/m².</summary>
        public const double EarthIrradiance = 1361.0;

        /// <summary>Earth's mean radius, m.</summary>
        public const double EarthRadiusM = 6.371e6;

        /// <summary>Humanity's primary power in 2024 (592 EJ/yr, Energy Institute), W; K ≈ 0.727.</summary>
        public const double Humanity2024W = 1.876e13;

        /// <summary>K = (log₁₀ P − 6) / 10.</summary>
        public static double K(double powerW) => (Math.Log10(Math.Max(powerW, MinimumW)) - 6.0) / 10.0;

        /// <summary>Power for a given K: P = 10^(10K + 6) W.</summary>
        public static double PowerForK(double k) => Math.Pow(10.0, 10.0 * k + 6.0);

        /// <summary>
        /// Game rule: this planet's Type I target scales with the starlight it intercepts,
        /// P_I = 10¹⁶ W × (S·R²) / (S⊕·R⊕²), so the end-game waste-heat problem is the same on every planet.
        /// </summary>
        public static double TypeITargetW(double insolationWm2, double planetRadiusM) =>
            TypeIW * (insolationWm2 / EarthIrradiance) * Math.Pow(planetRadiusM / EarthRadiusM, 2.0);

        /// <summary>Progress toward this planet's target, P / P_I (1 = target reached; may exceed 1).</summary>
        public static double PlanetCompletion(double powerW, double targetW) =>
            targetW > 0.0 ? Math.Max(0.0, powerW) / targetW : 0.0;

        /// <summary>Starlight the planet's disc intercepts, S·πR², W. Earth ≈ 1.74 × 10¹⁷ W.</summary>
        public static double InterceptedStarlightW(double insolationWm2, double planetRadiusM) =>
            insolationWm2 * Math.PI * planetRadiusM * planetRadiusM;
    }
}

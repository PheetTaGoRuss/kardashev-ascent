using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Step 5 of the solar model: how the atmosphere dims and scatters starlight.</summary>
    public static class Atmosphere
    {
        /// <summary>Zenith where air mass is capped (Kasten–Young gives 37.92 here).</summary>
        public const double MaxZenithForAirMass = 90.0;

        /// <summary>
        /// Relative air mass (Kasten &amp; Young 1989), times p/p₀ for altitude.
        /// Zenith is clamped to 90° so the result is never NaN; callers set DNI = 0 below the horizon.
        /// </summary>
        public static double AirMassKastenYoung(double zenithDegrees, double pressureRatio = 1.0)
        {
            double z = Math.Min(zenithDegrees, MaxZenithForAirMass);
            double cz = Math.Cos(z * SolarConstants.DegToRad);
            return pressureRatio / (cz + 0.50572 * Math.Pow(96.07995 - z, -1.6364));
        }

        /// <summary>DNI = G₀·e^(−τ·AM) (Beer–Lambert).</summary>
        public static double DirectNormal(double g0, double opticalDepth, double airMass) =>
            g0 * Math.Exp(-opticalDepth * airMass);

        /// <summary>
        /// Game model: DHI = k·G₀·cos z·(1 − e^(−τ·AM)). Earth k ≈ 0.3.
        /// k is clamped to 0–1 so GHI never exceeds G₀·cos z (energy is conserved).
        /// </summary>
        public static double DiffuseHorizontal(double g0, double cosZenith, double opticalDepth, double airMass, double k) =>
            Math.Max(0.0, Math.Min(1.0, k)) * g0 * cosZenith * (1.0 - Math.Exp(-opticalDepth * airMass));

        /// <summary>GHI = DNI·cos z + DHI.</summary>
        public static double GlobalHorizontal(double dni, double dhi, double cosZenith) =>
            dni * cosZenith + dhi;
    }
}

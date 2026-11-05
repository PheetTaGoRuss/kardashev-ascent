using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Step 5 of the solar model: how the atmosphere dims and scatters starlight.</summary>
    public static class Atmosphere
    {
        /// <summary>
        /// Relative air mass (Kasten &amp; Young 1989), times p/p₀ for altitude.
        /// Valid while the star is above the horizon (zenith &lt; 90°).
        /// </summary>
        public static double AirMassKastenYoung(double zenithDegrees, double pressureRatio = 1.0)
        {
            double cz = Math.Cos(zenithDegrees * SolarConstants.DegToRad);
            return pressureRatio / (cz + 0.50572 * Math.Pow(96.07995 - zenithDegrees, -1.6364));
        }

        /// <summary>DNI = G₀·e^(−τ·AM) (Beer–Lambert).</summary>
        public static double DirectNormal(double g0, double opticalDepth, double airMass) =>
            g0 * Math.Exp(-opticalDepth * airMass);

        /// <summary>Game model: DHI = k·G₀·cos z·(1 − e^(−τ·AM)), k ≈ 0.3–0.5.</summary>
        public static double DiffuseHorizontal(double g0, double cosZenith, double opticalDepth, double airMass, double k) =>
            k * g0 * cosZenith * (1.0 - Math.Exp(-opticalDepth * airMass));

        /// <summary>GHI = DNI·cos z + DHI.</summary>
        public static double GlobalHorizontal(double dni, double dhi, double cosZenith) =>
            dni * cosZenith + dhi;
    }
}

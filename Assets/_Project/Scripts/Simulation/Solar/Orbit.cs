using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Step 1 of the solar model: orbit position and light above the atmosphere.</summary>
    public static class Orbit
    {
        /// <summary>Eccentric anomaly E from Kepler's equation M = E − e·sin E (Newton iteration).</summary>
        public static double SolveKepler(double meanAnomaly, double eccentricity, int iterations = 10)
        {
            double e = eccentricity;
            double E = e < 0.8 ? meanAnomaly : Math.PI;
            for (int i = 0; i < iterations; i++)
                E -= (E - e * Math.Sin(E) - meanAnomaly) / (1.0 - e * Math.Cos(E));
            return E;
        }

        /// <summary>True anomaly ν from the eccentric anomaly, radians.</summary>
        public static double TrueAnomaly(double eccentricAnomaly, double eccentricity)
        {
            double half = eccentricAnomaly / 2.0;
            return 2.0 * Math.Atan2(Math.Sqrt(1.0 + eccentricity) * Math.Sin(half),
                                    Math.Sqrt(1.0 - eccentricity) * Math.Cos(half));
        }

        /// <summary>r = a(1 − e²) / (1 + e·cos ν), in the unit of <paramref name="semiMajorAxis"/>.</summary>
        public static double Distance(double semiMajorAxis, double eccentricity, double trueAnomaly) =>
            semiMajorAxis * (1.0 - eccentricity * eccentricity) / (1.0 + eccentricity * Math.Cos(trueAnomaly));

        /// <summary>G₀ = L / (4πr²), W/m². <paramref name="distance"/> in metres.</summary>
        public static double ExtraterrestrialIrradiance(double luminosity, double distance) =>
            luminosity / (4.0 * Math.PI * distance * distance);
    }
}

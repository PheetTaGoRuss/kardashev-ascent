using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// What a planet is made of, for resources (geology_resources.md claim 10). <see cref="MetalFactor"/> = 10^[Fe/H]
    /// scales metal deposits with the host star's metals. Iron follows the planet's measured iron fraction when there
    /// is one (TRAPPIST-1: 21 wt %, Agol et al. 2021) relative to Earth's 32 %, else the star's metals.
    /// </summary>
    public readonly struct PlanetChemistry
    {
        public const double EarthIronFraction = 0.32;

        public readonly double MetallicityDex;
        public readonly double IronMassFraction;
        public readonly bool IronMeasured;
        /// <summary>Seas now (or frozen seas): what copper and lithium deposits need (claims 1, 2).</summary>
        public readonly bool WaterHistory;

        public PlanetChemistry(double metallicityDex, double ironMassFraction, bool ironMeasured, bool waterHistory)
        {
            MetallicityDex = metallicityDex;
            IronMassFraction = ironMassFraction > 0.0 ? ironMassFraction : EarthIronFraction;
            IronMeasured = ironMeasured && ironMassFraction > 0.0;
            WaterHistory = waterHistory;
        }

        public static PlanetChemistry EarthLike => new PlanetChemistry(0.0, EarthIronFraction, false, true);

        public double MetalFactor => Math.Pow(10.0, MetallicityDex);

        public double IronFactor => IronMeasured ? IronMassFraction / EarthIronFraction : MetalFactor;
    }
}

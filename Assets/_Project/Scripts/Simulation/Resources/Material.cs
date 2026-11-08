using System;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// Materials robots dig, in kg (design D3, tiers A and B: docs/design/robotic-workforce.md). Build parts (Wh) stay
    /// the energy every building costs; materials come on top. Aluminium (from rock, in place of copper) and sodium
    /// (in place of lithium) are substitutes that the Workshop makes (phase 3b).
    /// </summary>
    public enum Material { Rock, Iron, Water, Copper, Lithium, Aluminium, Sodium }

    public static class Materials
    {
        /// <summary>Materials found in the ground (the rest are made from them).</summary>
        public static readonly Material[] Mined = { Material.Rock, Material.Iron, Material.Water, Material.Copper, Material.Lithium };

        public static readonly Material[] All = (Material[])Enum.GetValues(typeof(Material));

        public static string Name(Material m)
        {
            switch (m)
            {
                case Material.Rock: return "Rock";
                case Material.Iron: return "Iron";
                case Material.Water: return "Water ice";
                case Material.Copper: return "Copper";
                case Material.Lithium: return "Lithium";
                case Material.Aluminium: return "Aluminium";
                case Material.Sodium: return "Sodium";
                default: return m.ToString();
            }
        }

        /// <summary>Where it forms, for the tile panel (docs/physics/notes/geology_resources.md).</summary>
        public static string Origin(Material m)
        {
            switch (m)
            {
                case Material.Rock: return "every land tile (silicate rock: Si, Al, O, Mg)";
                case Material.Iron: return "everywhere; how much follows the planet's iron fraction and its star's metals";
                case Material.Water: return "glaciers, frozen seas, tundra ground ice, and shores of liquid seas";
                case Material.Copper: return "coastal mountains on planets with seas (stand-in for volcanic arcs: porphyry copper needs water and subduction)";
                case Material.Lithium: return "inland basins beside highlands on planets with seas (stand-in for arid closed basins with a volcanic source)";
                case Material.Aluminium: return "made from rock in the Workshop; replaces copper in wiring (1.6× the resistance, 30 % of the weight)";
                case Material.Sodium: return "made in the Workshop; Na-ion batteries replace lithium (~160 vs ~250 Wh/kg)";
                default: return "";
            }
        }
    }

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

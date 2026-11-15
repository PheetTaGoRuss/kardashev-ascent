using System;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// Materials robots dig, in kg (design D3, tiers A and B: docs/design/robotic-workforce.md). Build parts (Wh) stay
    /// the energy every building costs; materials come on top. Aluminium (from rock, in place of copper) and sodium
    /// (in place of lithium) are substitutes that the Workshop makes (phase 3b).
    /// </summary>
    public enum Material
    {
        Rock, Iron, Water, Copper, Lithium, Aluminium, Sodium,
        /// <summary>What is left of the rock after refining (saved as an integer: add new materials after it, never before).</summary>
        Slag,
        /// <summary>
        /// Rare-earth elements (neodymium and its kin) for the strong NdFeB magnets in motors (director 2026-11-15).
        /// Deep ore: only a seismic survey finds it, in mountains, canyons and caves.
        /// </summary>
        RareEarth,
    }

    public static class Materials
    {
        /// <summary>Materials found in the ground (the rest are made from them).</summary>
        public static readonly Material[] Mined = { Material.Rock, Material.Iron, Material.Water, Material.Copper, Material.Lithium, Material.RareEarth };

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
                case Material.Slag: return "Slag";
                case Material.RareEarth: return "Rare earths";
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
                case Material.Copper: return "highlands and mountains: richest within a few tiles of a sea on planets with seas (volcanic arcs), poorer magmatic copper-nickel ores in high ground anywhere";
                case Material.Lithium: return "inland basins beside highlands on planets with seas (stand-in for arid closed basins with a volcanic source)";
                case Material.Aluminium: return "made from rock in the Workshop; replaces copper in wiring (1.6× the resistance, 30 % of the weight)";
                case Material.Sodium: return "made in the Workshop; Na-ion batteries replace lithium (~160 vs ~250 Wh/kg)";
                case Material.Slag: return "the rock left over when a Workshop refines aluminium or sodium (mass is conserved: rock in = metal + slag)";
                case Material.RareEarth: return "deep in mountains, canyons (rifts) and caves, in carbonatite and alkaline igneous rock; a seismic survey finds it";
                default: return "";
            }
        }
    }
}

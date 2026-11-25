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
        /// <summary>Anorthosite: pale highland rock rich in alumina (about 30 % Al₂O₃), the aluminium ore of the colony (2026-11-15).</summary>
        Anorthosite,
        /// <summary>Salt (halite, NaCl) from evaporite flats and sea water: the source of sodium (2026-11-15).</summary>
        Salt,
        /// <summary>Alumina (Al₂O₃) powder: step 1 of aluminium.</summary>
        Alumina,
        /// <summary>High-purity aluminium (99.99 %), for advanced electronics.</summary>
        PureAluminium,
        /// <summary>Oxygen gas (O2): what an inert-anode Hall-Heroult cell gives off (mission operations MO-06). Appended; saved as an integer.</summary>
        Oxygen,
        /// <summary>Chlorine gas (Cl2): what a Downs cell gives off with the sodium (MO-06). Toxic if vented.</summary>
        Chlorine,
        /// <summary>Electronic waste (2026-11-25, OQ-9): the circuit boards and cell of a destroyed robot. Hazardous. Appended; saved as an integer.</summary>
        ElectronicWaste,
        /// <summary>Rust, iron oxide-hydroxide FeOOH (goethite): what iron becomes in damp air (2026-11-25). It can go back into the smelter as ore.</summary>
        Rust,
        /// <summary>Sodium hydroxide NaOH: what sodium becomes in moist air (2NaNa + 2H2O gives 2NaOH + H2). Hazardous.</summary>
        SodiumHydroxide,
        /// <summary>Lithium hydroxide LiOH: what lithium becomes in moist air. Hazardous.</summary>
        LithiumHydroxide,
    }

    public static class Materials
    {
        /// <summary>Materials found in the ground (the rest are made from them).</summary>
        public static readonly Material[] Mined = { Material.Rock, Material.Iron, Material.Water, Material.Copper, Material.Lithium, Material.RareEarth,
                                                    Material.Anorthosite, Material.Salt };

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
                case Material.Anorthosite: return "Anorthosite";
                case Material.Salt: return "Salt";
                case Material.Alumina: return "Alumina";
                case Material.PureAluminium: return "Aluminium 99.99 %";
                case Material.Oxygen: return "Oxygen";
                case Material.Chlorine: return "Chlorine";
                case Material.ElectronicWaste: return "Electronic waste";
                case Material.Rust: return "Rust";
                case Material.SodiumHydroxide: return "Sodium hydroxide";
                case Material.LithiumHydroxide: return "Lithium hydroxide";
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
                case Material.Anorthosite: return "highlands and mountains: the oldest crust, made of feldspar rich in alumina (like the Moon's pale highlands)";
                case Material.Salt: return "dry basins and deserts on planets that once had seas (evaporites), and every shore of a salty sea";
                case Material.Alumina: return "made from anorthosite or rock in an Alumina plant (step 1 of aluminium)";
                case Material.PureAluminium: return "aluminium refined again in an Electrolysis cell (three-layer process)";
                case Material.Oxygen: return "given off by aluminium electrolysis (inert-anode cell, an emerging technology); needs a gas tank or is vented";
                case Material.ElectronicWaste: return "what is left of a destroyed robot's circuit boards and cell: hazardous, kept in a sealed store";
                case Material.Rust: return "iron that has rusted in damp air (FeOOH); it gains the mass of the air it took, and goes back into the smelter as ore";
                case Material.SodiumHydroxide: return "sodium that met moisture (caustic): hazardous, needs a sealed store";
                case Material.LithiumHydroxide: return "lithium that met moisture: hazardous, needs a sealed store";
                case Material.Chlorine: return "given off with sodium by a Downs cell; toxic: needs a gas tank, or venting it is logged and warned";
                default: return "";
            }
        }
    }
}

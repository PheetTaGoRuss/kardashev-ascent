using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>One material in the ground of a tile. Grade 1 = an ordinary deposit of its kind on Earth.</summary>
    public readonly struct Deposit
    {
        public readonly Material Material;
        public readonly double Grade;

        public Deposit(Material material, double grade)
        {
            Material = material;
            Grade = grade;
        }
    }

    /// <summary>
    /// What lies in each tile's ground, made once per planet from its terrain and chemistry, and which tiles have
    /// been surveyed (the player only sees surveyed tiles; rock is always known). Rules follow
    /// docs/physics/notes/geology_resources.md; the terrain stand-ins (coast, inland basin) are game rules because
    /// the map has no plate tectonics or climate model. Share and spread constants are TBD placeholders (D4).
    /// </summary>
    public sealed class DepositMap
    {
        /// <summary>Share of eligible tiles that hold copper or lithium. TBD (D4).</summary>
        public const double CopperShare = 0.35, LithiumShare = 0.35;
        /// <summary>Tile-to-tile spread of grades: grade × (1 − Spread … 1 + Spread). TBD (D4).</summary>
        public const double Spread = 0.5;
        /// <summary>Ground ice under tundra compared with a glacier. TBD (D4).</summary>
        public const double TundraIceGrade = 0.3;

        readonly List<Deposit>[] deposits;
        readonly HashSet<int> surveyed = new HashSet<int>();

        public DepositMap(HexSphere sphere, PlanetSurface surface, PlanetChemistry chemistry, int seed)
        {
            if (sphere == null) throw new ArgumentNullException(nameof(sphere));
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            Chemistry = chemistry;
            deposits = new List<Deposit>[sphere.TileCount];
            for (int t = 0; t < sphere.TileCount; t++)
                deposits[t] = Generate(sphere, surface, chemistry, seed, t);
        }

        public PlanetChemistry Chemistry { get; }
        public int TileCount => deposits.Length;

        /// <summary>Everything in a tile's ground (whether or not it was surveyed).</summary>
        public IReadOnlyList<Deposit> At(int tile) => deposits[tile];

        public double GradeOf(int tile, Material m)
        {
            foreach (Deposit d in deposits[tile])
                if (d.Material == m) return d.Grade;
            return 0.0;
        }

        public bool IsSurveyed(int tile) => surveyed.Contains(tile);
        public void MarkSurveyed(int tile) => surveyed.Add(tile);
        public IEnumerable<int> Surveyed => surveyed;
        public void SetSurveyed(IEnumerable<int> tiles)
        {
            surveyed.Clear();
            if (tiles != null)
                foreach (int t in tiles)
                    if (t >= 0 && t < deposits.Length) surveyed.Add(t);
        }

        /// <summary>True if the player can see this material on this tile: rock always, the rest once surveyed.</summary>
        public bool IsKnown(int tile, Material m) => m == Material.Rock || IsSurveyed(tile);

        static List<Deposit> Generate(HexSphere sphere, PlanetSurface surface, PlanetChemistry chem, int seed, int t)
        {
            var list = new List<Deposit>();
            TerrainType here = surface.Terrain[t];
            if (Terrains.IsWater(here))
                return list;   // the sea floor (VMS copper) needs equipment robots don't have yet

            bool waterNeighbour = false, highNeighbour = false;
            foreach (int n in sphere.Neighbors[t])
            {
                TerrainType nt = surface.Terrain[n];
                waterNeighbour |= Terrains.IsWater(nt);
                highNeighbour |= nt == TerrainType.Highland || nt == TerrainType.Mountain;
            }

            list.Add(new Deposit(Material.Rock, 1.0));
            list.Add(new Deposit(Material.Iron, chem.IronFactor * Vary(seed, t, 1)));

            double ice = here == TerrainType.Ice || here == TerrainType.FrozenSea ? 1.0
                       : here == TerrainType.Tundra ? TundraIceGrade
                       : waterNeighbour ? 1.0   // a shore: pump the sea
                       : 0.0;
            if (ice > 0.0)
                list.Add(new Deposit(Material.Water, ice * Vary(seed, t, 2)));

            // Porphyry copper: magmatic arcs above subduction, with water (claim 1). Stand-in: mountains on a coast.
            if (chem.WaterHistory && (here == TerrainType.Mountain || here == TerrainType.Highland) && waterNeighbour
                && Hash01(seed, t, 3) < CopperShare)
                list.Add(new Deposit(Material.Copper, chem.MetalFactor * Vary(seed, t, 4)));

            // Lithium brine: arid closed basin with a volcanic source and a water history (claim 2, USGS).
            // Stand-in: inland low ground (no sea next to it) beside highlands.
            bool low = here == TerrainType.Lowland || here == TerrainType.Desert || here == TerrainType.DryBasin;
            if (chem.WaterHistory && low && !waterNeighbour && highNeighbour && Hash01(seed, t, 5) < LithiumShare)
                list.Add(new Deposit(Material.Lithium, chem.MetalFactor * Vary(seed, t, 6)));
            return list;
        }

        static double Vary(int seed, int tile, int salt) => 1.0 + Spread * (2.0 * Hash01(seed, tile, salt) - 1.0);

        /// <summary>A stable number in [0, 1) for a tile (same planet, same answer every time).</summary>
        static double Hash01(int seed, int tile, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)tile * 0x85EBCA77u ^ (uint)salt * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }

    /// <summary>The colony's materials on hand, kg.</summary>
    public sealed class MaterialStock
    {
        readonly double[] kg = new double[Materials.All.Length];

        public double this[Material m] => kg[(int)m];

        public void Add(Material m, double amountKg)
        {
            if (amountKg < 0.0) throw new ArgumentOutOfRangeException(nameof(amountKg));
            kg[(int)m] += amountKg;
        }

        /// <summary>Take an exact amount; false (and nothing taken) if there isn't enough.</summary>
        public bool TryTake(Material m, double amountKg)
        {
            if (amountKg < 0.0 || kg[(int)m] < amountKg - 1e-9) return false;
            kg[(int)m] = Math.Max(0.0, kg[(int)m] - amountKg);
            return true;
        }

        public void Set(Material m, double amountKg) => kg[(int)m] = Math.Max(0.0, amountKg);

        public void Clear()
        {
            for (int i = 0; i < kg.Length; i++) kg[i] = 0.0;
        }
    }
}

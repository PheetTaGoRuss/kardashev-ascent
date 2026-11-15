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
        /// <summary>
        /// Arc copper sits in high ground up to this many tiles from a sea (the map has no plate boundaries; on
        /// Earth porphyry belts lie 100–300 km inland of the trench, geology_resources.md claim 1). TBD.
        /// </summary>
        public const int ArcReachTiles = 4;
        /// <summary>
        /// Magmatic and impact nickel-copper sulfides (claims 1 and 5): any high ground on any planet, rarer and
        /// poorer than arc copper. Share and grade factor TBD.
        /// </summary>
        public const double SulfideCopperShare = 0.12, SulfideCopperGrade = 0.5;
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
            int[] toSea = SeaDistance(sphere, surface);
            for (int t = 0; t < sphere.TileCount; t++)
                deposits[t] = Generate(sphere, surface, chemistry, seed, t, toSea[t]);
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

        /// <summary>Tiles from each tile to the nearest sea (open or frozen); int.MaxValue on a planet without seas.</summary>
        static int[] SeaDistance(HexSphere sphere, PlanetSurface surface)
        {
            int n = sphere.TileCount;
            var d = new int[n];
            var queue = new Queue<int>();
            for (int t = 0; t < n; t++)
            {
                TerrainType tt = surface.Terrain[t];
                bool sea = Terrains.IsWater(tt) || tt == TerrainType.FrozenSea;
                d[t] = sea ? 0 : int.MaxValue;
                if (sea) queue.Enqueue(t);
            }
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                foreach (int nb in sphere.Neighbors[t])
                    if (d[nb] == int.MaxValue)
                    {
                        d[nb] = d[t] + 1;
                        queue.Enqueue(nb);
                    }
            }
            return d;
        }

        // Where each material can be, as named rules: the generator below and the guide book read the same ones.

        /// <summary>Ground that holds ice, or (with a sea next to it) water to pump.</summary>
        public static bool IsIceGround(TerrainType t) => t == TerrainType.Ice || t == TerrainType.FrozenSea || t == TerrainType.Tundra;

        /// <summary>High ground where copper ores form (arc porphyry near a sea; magmatic sulfides anywhere).</summary>
        public static bool IsCopperGround(TerrainType t) => t == TerrainType.Mountain || t == TerrainType.Highland || t == TerrainType.VolcanicRock;

        /// <summary>Low ground where lithium brines collect (inland, beside high ground, on a planet that had water).</summary>
        public static bool IsLithiumGround(TerrainType t) => t == TerrainType.Lowland || t == TerrainType.Desert || t == TerrainType.DryBasin;

        /// <summary>Ground counted as "high" next to a lithium basin.</summary>
        public static bool IsHighNeighbour(TerrainType t) => IsCopperGround(t);

        static List<Deposit> Generate(HexSphere sphere, PlanetSurface surface, PlanetChemistry chem, int seed, int t, int tilesToSea)
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
                highNeighbour |= IsHighNeighbour(nt);
            }

            list.Add(new Deposit(Material.Rock, 1.0));
            list.Add(new Deposit(Material.Iron, chem.IronFactor * Vary(seed, t, 1)));

            double ice = here == TerrainType.Ice || here == TerrainType.FrozenSea ? 1.0
                       : here == TerrainType.Tundra ? TundraIceGrade
                       : waterNeighbour ? 1.0   // a shore: pump the sea
                       : 0.0;
            if (ice > 0.0)
                list.Add(new Deposit(Material.Water, ice * Vary(seed, t, 2)));

            // Copper (claims 1 and 5). Arc copper (porphyry, epithermal) forms in high ground near a sea on a planet with
            // water; stand-in: highland or mountain within ArcReachTiles of a sea. Magmatic and impact Ni-Cu sulfides
            // form in high ground on any planet, rarer and poorer.
            bool high = IsCopperGround(here);
            if (high && chem.WaterHistory && tilesToSea <= ArcReachTiles && Hash01(seed, t, 3) < CopperShare)
                list.Add(new Deposit(Material.Copper, chem.MetalFactor * Vary(seed, t, 4)));
            else if (high && Hash01(seed, t, 7) < SulfideCopperShare)
                list.Add(new Deposit(Material.Copper, SulfideCopperGrade * chem.MetalFactor * Vary(seed, t, 8)));

            // Lithium brine: arid closed basin with a volcanic source and a water history (claim 2, USGS).
            // Stand-in: inland low ground (no sea next to it) beside highlands.
            bool low = IsLithiumGround(here);
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

        /// <summary>Take up to <paramref name="amountKg"/>; returns what was taken, kg.</summary>
        public double Take(Material m, double amountKg)
        {
            double t = Math.Max(0.0, Math.Min(kg[(int)m], amountKg));
            kg[(int)m] -= t;
            return t;
        }

        public void Clear()
        {
            for (int i = 0; i < kg.Length; i++) kg[i] = 0.0;
        }
    }

    /// <summary>
    /// An amount of each material, kg: what a building costs, what a site still needs, what a lander brings.
    /// Changing a bill never changes another one (every operation returns a new bill).
    /// </summary>
    public sealed class MaterialBill
    {
        readonly double[] kg = new double[Materials.All.Length];

        public double this[Material m] => kg[(int)m];

        /// <summary>A bill with <paramref name="amountKg"/> more of <paramref name="m"/> (negative amounts are ignored).</summary>
        public MaterialBill Add(Material m, double amountKg)
        {
            MaterialBill b = Copy();
            if (amountKg > 0.0) b.kg[(int)m] += amountKg;
            return b;
        }

        /// <summary>This bill with none of <paramref name="m"/>.</summary>
        public MaterialBill Without(Material m)
        {
            MaterialBill b = Copy();
            b.kg[(int)m] = 0.0;
            return b;
        }

        public MaterialBill Times(double factor)
        {
            var b = new MaterialBill();
            for (int i = 0; i < kg.Length; i++) b.kg[i] = kg[i] * Math.Max(0.0, factor);
            return b;
        }

        public MaterialBill Plus(MaterialBill other)
        {
            MaterialBill b = Copy();
            if (other != null)
                for (int i = 0; i < kg.Length; i++) b.kg[i] += other.kg[i];
            return b;
        }

        /// <summary>What is left of this bill once <paramref name="stock"/> is used (never below 0).</summary>
        public MaterialBill Minus(MaterialStock stock)
        {
            var b = new MaterialBill();
            for (int i = 0; i < kg.Length; i++) b.kg[i] = Math.Max(0.0, kg[i] - (stock != null ? stock[(Material)i] : 0.0));
            return b;
        }

        public double TotalKg
        {
            get
            {
                double sum = 0.0;
                foreach (double v in kg) sum += v;
                return sum;
            }
        }

        public bool IsEmpty => TotalKg <= 1e-9;

        /// <summary>The materials in the bill, largest first.</summary>
        public List<(Material Material, double Kg)> Items()
        {
            var list = new List<(Material, double)>();
            for (int i = 0; i < kg.Length; i++)
                if (kg[i] > 1e-9) list.Add(((Material)i, kg[i]));
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return list;
        }

        /// <summary>"1,035 kg rock, 104 kg iron, 12 kg copper" (or "nothing").</summary>
        public string Describe()
        {
            List<(Material Material, double Kg)> items = Items();
            if (items.Count == 0) return "nothing";
            var parts = new List<string>();
            foreach (var (m, v) in items)
                parts.Add($"{(v >= 10.0 ? v.ToString("#,0") : v.ToString("0.#"))} kg {Materials.Name(m).ToLowerInvariant()}");
            return string.Join(", ", parts);
        }

        /// <summary>True when the stock holds all of it.</summary>
        public bool CoveredBy(MaterialStock stock)
        {
            for (int i = 0; i < kg.Length; i++)
                if (kg[i] > 1e-9 && (stock == null || stock[(Material)i] < kg[i] - 1e-9)) return false;
            return true;
        }

        MaterialBill Copy()
        {
            var b = new MaterialBill();
            Array.Copy(kg, b.kg, kg.Length);
            return b;
        }
    }
}

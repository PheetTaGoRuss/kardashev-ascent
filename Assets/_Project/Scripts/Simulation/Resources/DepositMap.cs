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
        /// <summary>Tiles whose water is pumped from the sea next to them (no ice): it does not run out.</summary>
        readonly bool[] seaWater;
        readonly Dictionary<(int Tile, Material Material), double> mined = new Dictionary<(int, Material), double>();
        readonly bool[] caves;
        readonly bool[] evaporite;

        /// <summary>
        /// Anorthosite (real support on Mars: anorthositic highland terrains, Carter and Poulet 2013, Nature Geoscience 6, 1008, "Ancient plutonic processes on Mars inferred from the detection of possible anorthositic terrains"): the ancient light crust of highlands (the Moon's highlands are almost all anorthosite; on Earth
        /// massif anorthosites stand in old mountain belts). Share of highland and mountain tiles, TBD.
        /// </summary>
        public const double AnorthositeShare = 0.5;
        /// <summary>
        /// Salt: evaporites form where salty water dried up (Death Valley, the Atacama; Mars's chloride deposits, Osterloo
        /// et al. 2008). Share of dry basins and deserts on planets that had water, TBD. Every shore of a liquid sea gives
        /// sea salt (about 2.7 % NaCl in sea water) by evaporation, without end.
        /// </summary>
        public const double SaltFlatShare = 0.5;

        /// <summary>Low dry ground where salt flats form.</summary>
        public static bool IsSaltFlat(TerrainType t) => t == TerrainType.DryBasin || t == TerrainType.Desert;

        /// <summary>Share of mountain, canyon and volcanic tiles with a cave (karst or a lava tube). Game rule, TBD.</summary>
        public const double CaveShare = 0.2;

        /// <summary>
        /// Ore in a mountain or a canyon is reached deep in the rock (tunnel walls, canyon walls cut through the layers),
        /// so its deposits hold this many times as much (director 2026-11-15: "good amount of ore"). Game rule, TBD.
        /// </summary>
        public const double DeepReserveFactor = 3.0;

        /// <summary>
        /// Rare earths: carbonatites and alkaline intrusions form in continental rifts (the East African Rift has many) and
        /// sit deep in old igneous mountains (Mountain Pass, California; Bayan Obo, China). Stand-in: canyons (rifts),
        /// mountains, volcanic rock; a cave doubles the chance (it reaches deeper rock). Shares are game rules, TBD.
        /// </summary>
        public const double RareEarthCanyonShare = 0.4, RareEarthMountainShare = 0.15, RareEarthVolcanicShare = 0.1;

        /// <summary>True once the colony has a seismic survey: deep ore (rare earths) shows on surveyed tiles.</summary>
        public bool DeepScanned { get; set; }

        /// <summary>A cave on this tile (whether or not the colony knows it: it shows after a survey).</summary>
        public bool HasCave(int tile) => caves[tile];

        /// <summary>Deep ore: hidden until a seismic survey.</summary>
        public static bool IsDeep(Material m) => m == Material.RareEarth;

        /// <summary>The deposits the colony can see on a tile: rock always, the rest once surveyed, deep ore after a seismic survey too.</summary>
        public List<Deposit> Known(int tile)
        {
            var list = new List<Deposit>();
            foreach (Deposit d in deposits[tile])
                if (IsKnown(tile, d.Material)) list.Add(d);
            return list;
        }

        /// <summary>
        /// What a grade-1 deposit holds within reach of the colony's robots on one tile, kg (director 2026-11-15:
        /// deposits run out; KI-12). Game rule, TBD (D4): only the order matters so far. Common materials hold more
        /// than rare ones, as in the crust (rock and iron are bulk; copper and lithium are scarce). A deposit holds
        /// its grade times this. Sea water pumped from a shore never runs out.
        /// </summary>
        public static double ReserveKgAtGrade1(Material m)
        {
            // B1, decided 2026-11-25 (director's delegate): typical deposit sizes scaled to what one tile offers a colony. A tile's minable share of a deposit is 1e-4 (an assumption, TBD).
            // Median contained metal: porphyry copper about 3 Mt of copper (Singer, Berger and Moring 2008, USGS Open-File Report 2008-1155; recalled, to verify), a lithium brine or pegmatite
            // about 0.5 Mt of lithium, a carbonatite rare-earth deposit about 0.2 Mt of oxide (USGS Mineral Commodity Summaries; recalled). Iron, anorthosite and salt are bulk (game rule).
            // Ice on Mars follows SHARAD and MRO ground-ice mapping (Bramson et al. 2015: widespread excess ice within a metre or two; rule: a tile holds 5 000 t).
            switch (m)
            {
                case Material.Rock: return 100_000_000.0;
                case Material.Iron: return 5_000_000.0;
                case Material.Water: return 5_000_000.0;
                case Material.Copper: return 300_000.0;            // 1e-4 of 3 Mt
                case Material.Lithium: return 50_000.0;            // 1e-4 of 0.5 Mt
                case Material.RareEarth: return 20_000.0;          // 1e-4 of 0.2 Mt
                case Material.Anorthosite: return 20_000_000.0;
                case Material.Salt: return 5_000_000.0;
                default: return 0.0;
            }
        }

        /// <summary>
        /// The ore grade of an ordinary deposit, kg of product per kg of rock moved (B2): porphyry copper about 0.5 % Cu (Singer et al. 2008 median), iron ore about 60 % Fe (hematite ore), lithium
        /// brine about 0.1 % Li, rare-earth carbonatite about 6 % oxide, anorthosite and salt and ice are mined as the product itself. A deposit of relative grade g has g times this.
        /// </summary>
        public static double TypicalOreGrade(Material m)
        {
            switch (m)
            {
                case Material.Copper: return 0.005;
                case Material.Iron: return 0.60;
                case Material.Lithium: return 0.001;
                case Material.RareEarth: return 0.06;
                default: return 1.0;
            }
        }

        /// <summary>The ore grade of a deposit, kg of product per kg of rock moved: the typical grade times its relative grade.</summary>
        public double OreGrade(int tile, Material m) => TypicalOreGrade(m) * GradeOf(tile, m);

        /// <summary>
        /// The grade the miners get now: rich ore first, falling as the deposit is mined (game rule, TBD): the relative grade times 0.5 plus 0.5 of the share still in the ground, so a deposit
        /// yields half as much per hour at the end as at the start. Sea water and the like never fall.
        /// </summary>
        public double EffectiveGrade(int tile, Material m)
        {
            double g = GradeOf(tile, m);
            if (g <= 0.0 || IsEndless(tile, m) || TypicalOreGrade(m) >= 1.0) return g;   // only ores fall; rock, ice, salt and anorthosite are the product itself
            return g * (0.5 + 0.5 * RemainingShare(tile, m));
        }

        public DepositMap(HexSphere sphere, PlanetSurface surface, PlanetChemistry chemistry, int seed)
        {
            if (sphere == null) throw new ArgumentNullException(nameof(sphere));
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            Chemistry = chemistry;
            deposits = new List<Deposit>[sphere.TileCount];
            seaWater = new bool[sphere.TileCount];
            caves = new bool[sphere.TileCount];
            evaporite = new bool[sphere.TileCount];
            for (int t = 0; t < sphere.TileCount; t++)
                caves[t] = IsDeepGround(surface.Terrain[t]) && Hash01(seed, t, 11) < CaveShare;
            terrainOf = surface.Terrain;
            int[] toSea = SeaDistance(sphere, surface);
            for (int t = 0; t < sphere.TileCount; t++)
            {
                deposits[t] = Generate(sphere, surface, chemistry, seed, t, toSea[t], caves[t]);
                TerrainType here = surface.Terrain[t];
                seaWater[t] = !Terrains.IsWater(here) && !IsIceGround(here) && GradeOf(t, Material.Water) > 0.0;
                evaporite[t] = IsSaltFlat(here) && GradeOf(t, Material.Salt) > 0.0 && !seaWater[t];
            }
        }

        /// <summary>True when this material never runs out here (sea water pumped from a shore).</summary>
        public bool IsEndless(int tile, Material m) => (m == Material.Water || m == Material.Salt) && seaWater[tile] && !evaporite[tile];

        /// <summary>What the deposit held before any mining, kg (0 if there is none; infinity for sea water).</summary>
        public double ReserveKg(int tile, Material m)
        {
            double grade = GradeOf(tile, m);
            if (grade <= 0.0) return 0.0;
            if (IsEndless(tile, m)) return double.PositiveInfinity;
            return grade * ReserveKgAtGrade1(m) * (IsDeepGround(terrainOf[tile]) && m != Material.Rock ? DeepReserveFactor : 1.0);
        }

        /// <summary>Mined from this tile so far, kg.</summary>
        public double MinedKg(int tile, Material m) => mined.TryGetValue((tile, m), out double kg) ? kg : 0.0;

        /// <summary>What is still in the ground, kg.</summary>
        public double RemainingKg(int tile, Material m) => Math.Max(0.0, ReserveKg(tile, m) - MinedKg(tile, m));

        /// <summary>Share of the deposit still in the ground, 0–1 (1 for sea water).</summary>
        public double RemainingShare(int tile, Material m)
        {
            double reserve = ReserveKg(tile, m);
            return reserve <= 0.0 ? 0.0 : double.IsPositiveInfinity(reserve) ? 1.0 : RemainingKg(tile, m) / reserve;
        }

        public bool IsExhausted(int tile, Material m) => GradeOf(tile, m) > 0.0 && RemainingKg(tile, m) <= 1e-9;

        /// <summary>Take up to <paramref name="kg"/> out of the ground; returns what came out.</summary>
        public double Extract(int tile, Material m, double kg)
        {
            if (kg <= 0.0) return 0.0;
            double take = Math.Min(kg, RemainingKg(tile, m));
            if (take <= 0.0) return 0.0;
            if (!IsEndless(tile, m))
                mined[(tile, m)] = MinedKg(tile, m) + take;
            return take;
        }

        /// <summary>Everything mined so far (for saving).</summary>
        public IEnumerable<(int Tile, Material Material, double Kg)> Mined
        {
            get
            {
                foreach (var kv in mined)
                    yield return (kv.Key.Tile, kv.Key.Material, kv.Value);
            }
        }

        /// <summary>Put back what was mined (when a game is loaded).</summary>
        public void SetMined(IEnumerable<(int Tile, Material Material, double Kg)> list)
        {
            mined.Clear();
            if (list == null) return;
            foreach (var (t, m, kg) in list)
                if (t >= 0 && t < deposits.Length && kg > 0.0 && !IsEndless(t, m))
                    mined[(t, m)] = MinedKg(t, m) + kg;
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
        public bool IsKnown(int tile, Material m) => m == Material.Rock || (IsSurveyed(tile) && (!IsDeep(m) || DeepScanned));

        readonly TerrainType[] terrainOf;

        /// <summary>Ground whose ore lies deep: mountains, canyons, volcanic rock (rare earths, caves, bigger deposits).</summary>
        public static bool IsDeepGround(TerrainType t) => t == TerrainType.Mountain || t == TerrainType.Canyon || t == TerrainType.VolcanicRock;

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

        static List<Deposit> Generate(HexSphere sphere, PlanetSurface surface, PlanetChemistry chem, int seed, int t, int tilesToSea, bool cave)
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

            // Anorthosite (aluminium ore): old highland crust.
            if ((here == TerrainType.Highland || here == TerrainType.Mountain) && Hash01(seed, t, 12) < AnorthositeShare)
                list.Add(new Deposit(Material.Anorthosite, Vary(seed, t, 13)));
            // Salt: dried-up salt lakes on planets that had water; sea salt on any shore of a liquid sea.
            if (IsSaltFlat(here) && chem.WaterHistory && Hash01(seed, t, 14) < SaltFlatShare)
                list.Add(new Deposit(Material.Salt, Vary(seed, t, 15)));
            else if (waterNeighbour && !IsIceGround(here))
                list.Add(new Deposit(Material.Salt, 1.0));

            // Rare earths: deep in rifts, old igneous mountains and alkaline volcanic rock; a cave reaches deeper.
            double ree = here == TerrainType.Canyon ? RareEarthCanyonShare
                       : here == TerrainType.Mountain ? RareEarthMountainShare
                       : here == TerrainType.VolcanicRock ? RareEarthVolcanicShare : 0.0;
            if (ree > 0.0 && Hash01(seed, t, 9) < ree * (cave ? 2.0 : 1.0))
                list.Add(new Deposit(Material.RareEarth, chem.MetalFactor * Vary(seed, t, 10)));
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

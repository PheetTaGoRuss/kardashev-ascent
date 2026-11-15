using System;
using System.Collections.Generic;
using System.Numerics;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>What a tile is. Values are saved nowhere, but keep new ones at the end anyway.</summary>
    public enum TerrainType : byte
    {
        DeepOcean,
        ShallowOcean,
        Lowland,
        Highland,
        Mountain,
        /// <summary>Glacier: land under permanent ice.</summary>
        Ice,
        /// <summary>A sea frozen over (liquid-water planets, past the cold line). Solid enough to build on.</summary>
        FrozenSea,
        /// <summary>Cold, bare ground past the cold line.</summary>
        Tundra,
        /// <summary>Dry lowland: on planets without surface water, or hot ones.</summary>
        Desert,
        /// <summary>A low basin with no water in it (where a sea would be on a wet planet).</summary>
        DryBasin,
        /// <summary>Dark, humid ground on watery planets (appended 2026-11-13).</summary>
        Forest,
        /// <summary>Low, waterlogged ground beside the sea: too soft for land buildings, fine for floating solar.</summary>
        Wetland,
        /// <summary>Sandy land right on the shore.</summary>
        Beach,
        /// <summary>Dark volcanic rock on high ground.</summary>
        VolcanicRock,
        /// <summary>
        /// A canyon or rift valley: a deep gorge with walls far too steep to drive down (appended 2026-11-15). Crossing
        /// or using it needs a bridge.
        /// </summary>
        Canyon,
    }

    /// <summary>Names and properties of terrain types, in one place for the simulation and the UI.</summary>
    public static class Terrains
    {
        /// <summary>Open water: no building.</summary>
        public static bool IsWater(TerrainType t) => t == TerrainType.DeepOcean || t == TerrainType.ShallowOcean;

        /// <summary>Ground a normal building can stand on: land that is not open water and not a wetland.</summary>
        public static bool IsBuildableLand(TerrainType t) => !IsWater(t) && t != TerrainType.Wetland;

        /// <summary>
        /// Ground robots can't drive across until the colony opens it (director 2026-11-15): mountains need a tunnel or
        /// blasting, canyons a bridge. Robots can still go to the edge of one to work on it.
        /// </summary>
        public static bool IsBarrier(TerrainType t) => t == TerrainType.Mountain || t == TerrainType.Canyon;

        /// <summary>Where floating solar can be built: shallow sea and wetland.</summary>
        public static bool AllowsFloating(TerrainType t) => t == TerrainType.ShallowOcean || t == TerrainType.Wetland;

        /// <summary>
        /// Why the lander can't come down on this ground, or null if it can (director 2026-11-14: no landing on sea,
        /// wetland or ground the colony can't start on). It needs flat, open ground: <see cref="BaseSiteRank"/> above 0.
        /// </summary>
        public static string LandingProblem(TerrainType t)
        {
            if (IsWater(t)) return "The lander can't come down on water.";
            if (t == TerrainType.Wetland) return "Wetland is too soft for the lander and the first buildings.";
            if (BaseSiteRank(t) == 0) return $"The lander needs flat, open ground (lowland, desert, basin, beach, forest or tundra), not {Name(t).ToLowerInvariant()}.";
            return null;
        }

        /// <summary>Flat, open ground a colony would land on (best first: <see cref="BaseSiteRank"/>).</summary>
        public static int BaseSiteRank(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.Lowland:
                case TerrainType.Desert:
                case TerrainType.DryBasin: return 2;
                case TerrainType.Beach:
                case TerrainType.Forest: return 2;   // converted lowland: the landing site stays where it was
                case TerrainType.Tundra: return 1;
                default: return 0;
            }
        }

        /// <summary>Ground reflectance ρ_g for the panels' reflected light.</summary>
        public static double Albedo(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.Ice: return 0.8;
                case TerrainType.FrozenSea: return 0.7;
                case TerrainType.Desert: return 0.35;
                case TerrainType.Tundra: return 0.3;
                case TerrainType.DryBasin: return 0.25;
                case TerrainType.Beach: return 0.30;
                case TerrainType.Forest: return 0.13;
                case TerrainType.Wetland: return 0.12;
                case TerrainType.VolcanicRock: return 0.10;
                case TerrainType.Canyon: return 0.15;   // a shaded gorge
                default: return 0.2;
            }
        }

        public static string Name(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.DeepOcean: return "Deep ocean";
                case TerrainType.ShallowOcean: return "Shallow ocean";
                case TerrainType.Lowland: return "Lowland";
                case TerrainType.Highland: return "Highland";
                case TerrainType.Mountain: return "Mountain";
                case TerrainType.Ice: return "Glacier";
                case TerrainType.FrozenSea: return "Frozen sea";
                case TerrainType.Tundra: return "Tundra";
                case TerrainType.Desert: return "Desert";
                case TerrainType.DryBasin: return "Dry basin";
                case TerrainType.Forest: return "Forest";
                case TerrainType.Wetland: return "Wetland";
                case TerrainType.Beach: return "Beach";
                case TerrainType.VolcanicRock: return "Volcanic rock";
                case TerrainType.Canyon: return "Canyon";
                default: return t.ToString();
            }
        }
    }

    /// <summary>Settings for generating a planet's surface.</summary>
    public readonly struct SurfaceSettings
    {
        public readonly int Seed;
        /// <summary>Share of tiles under the sea, 0–1.</summary>
        public readonly double OceanFraction;
        /// <summary>Latitude (degrees) where polar ice starts, before noise.</summary>
        public readonly double IceLatitudeDeg;
        /// <summary>Size of continents: higher = more, smaller land masses.</summary>
        public readonly double NoiseScale;
        /// <summary>
        /// Tidally locked "eyeball": ice everywhere except a circle around the sub-stellar point (latitude 0,
        /// longitude 0); <see cref="IceLatitudeDeg"/> is then that circle's radius in degrees.
        /// </summary>
        public readonly bool IceAroundSubStellar;
        /// <summary>Seas in the basins (frozen past the cold line); false: the basins are dry.</summary>
        public readonly bool LiquidWater;
        /// <summary>Air temperature, °C: above <see cref="HotDesertC"/> the lowlands are desert even with seas.</summary>
        public readonly double AirTemperatureC;
        /// <summary>Also make forest, wetland, beach and volcanic rock (<see cref="PlanetSurface"/> second pass). Off for plain test worlds.</summary>
        public readonly bool RichTerrain;

        public SurfaceSettings(int seed, double oceanFraction = 0.62, double iceLatitudeDeg = 70.0, double noiseScale = 1.6,
                               bool iceAroundSubStellar = false, bool liquidWater = true, double airTemperatureC = 15.0, bool richTerrain = false)
        {
            Seed = seed;
            OceanFraction = oceanFraction;
            IceLatitudeDeg = iceLatitudeDeg;
            NoiseScale = noiseScale;
            IceAroundSubStellar = iceAroundSubStellar;
            LiquidWater = liquidWater;
            AirTemperatureC = airTemperatureC;
            RichTerrain = richTerrain;
        }

        /// <summary>Lowlands turn to desert above this air temperature, °C (game rule).</summary>
        public const double HotDesertC = 40.0;

        /// <summary>
        /// Past the cold line the ground is tundra and seas freeze; this much further (degrees) it is glacier.
        /// An eyeball's night side is far wider, so its band is wider too.
        /// </summary>
        public double GlacierBandDeg => IceAroundSubStellar ? 45.0 : 8.0;

        /// <summary>Earth-like ice line and the temperature band where it stays there, °C.</summary>
        public const double TemperateIceLatitudeDeg = 70.0, TemperateMinC = 0.0, TemperateMaxC = 35.0;

        /// <summary>
        /// A planet's look from its air temperature (game rules, not climate science):
        /// 0–35 °C keeps Earth's cold line at 70°; colder planets move it 1.5° of latitude nearer the equator per °C
        /// (never past 10°); hotter ones push it 2° per °C past the poles and dry out (1 % less sea per °C, down to 10 %).
        /// Past the cold line seas freeze and land is tundra, then glacier (<see cref="GlacierBandDeg"/>).
        /// A tidally locked planet is an eyeball: open ground within 80° + T (35°–180°) of the point under its star.
        /// <paramref name="liquidWater"/> false: the basins (<paramref name="oceanFraction"/> of the surface) are dry.
        /// </summary>
        public static SurfaceSettings ForClimate(int seed, double airTemperatureC, bool tidallyLocked,
                                                 bool liquidWater = true, double oceanFraction = 0.62)
        {
            double ocean = airTemperatureC > TemperateMaxC
                ? Math.Max(0.10, oceanFraction - 0.01 * (airTemperatureC - TemperateMaxC))
                : oceanFraction;
            if (tidallyLocked)
                return new SurfaceSettings(seed, ocean, Math.Max(35.0, Math.Min(180.0, 80.0 + airTemperatureC)), 1.6, true,
                                           liquidWater, airTemperatureC, true);
            double ice = airTemperatureC < TemperateMinC
                ? Math.Max(10.0, TemperateIceLatitudeDeg + 1.5 * (airTemperatureC - TemperateMinC))
                : airTemperatureC > TemperateMaxC
                    ? TemperateIceLatitudeDeg + 2.0 * (airTemperatureC - TemperateMaxC)
                    : TemperateIceLatitudeDeg;
            return new SurfaceSettings(seed, ocean, ice, 1.6, false, liquidWater, airTemperatureC, true);
        }
    }

    /// <summary>Elevation and terrain for every tile of a <see cref="HexSphere"/>.</summary>
    public sealed class PlanetSurface
    {
        /// <summary>Elevation relative to sea level, about −1..1 (negative = under water).</summary>
        public float[] Elevation { get; }
        public TerrainType[] Terrain { get; }

        PlanetSurface(float[] elevation, TerrainType[] terrain)
        {
            Elevation = elevation;
            Terrain = terrain;
        }

        public static PlanetSurface Generate(HexSphere sphere, in SurfaceSettings settings)
        {
            int count = sphere.TileCount;
            var raw = new double[count];
            for (int t = 0; t < count; t++)
            {
                var c = sphere.Centers[t];
                double s = settings.NoiseScale;
                // Warp the lookup a little so coastlines are less blobby.
                double warp = 0.35 * Noise3D.Fractal(c.X * s + 11.3, c.Y * s - 4.1, c.Z * s + 7.7, settings.Seed + 77, 3);
                raw[t] = Noise3D.Fractal(c.X * s + warp, c.Y * s + warp, c.Z * s + warp, settings.Seed);
            }

            double seaLevel = Quantile(raw, settings.OceanFraction);
            double maxAbove = 1e-9, maxBelow = 1e-9;
            foreach (double r in raw)
            {
                if (r > seaLevel) maxAbove = Math.Max(maxAbove, r - seaLevel);
                else maxBelow = Math.Max(maxBelow, seaLevel - r);
            }

            var elevation = new float[count];
            var terrain = new TerrainType[count];
            for (int t = 0; t < count; t++)
            {
                double e = raw[t] > seaLevel ? (raw[t] - seaLevel) / maxAbove : (raw[t] - seaLevel) / maxBelow;
                elevation[t] = (float)e;

                var c = sphere.Centers[t];
                // Rotating planet: distance from the equator. Eyeball: angle from the point under the star (+Z).
                double latDeg = settings.IceAroundSubStellar
                    ? Math.Acos(Math.Max(-1.0, Math.Min(1.0, c.Z / c.Length()))) * 180.0 / Math.PI
                    : Math.Abs(sphere.Latitude(t)) * 180.0 / Math.PI;
                double iceEdge = settings.IceLatitudeDeg
                                 + 6.0 * Noise3D.Value(c.X * 6.0, c.Y * 6.0, c.Z * 6.0, settings.Seed + 991)
                                 - (e > 0 ? 8.0 * e : 0.0);
                terrain[t] = Classify(e, latDeg >= iceEdge, latDeg >= iceEdge + settings.GlacierBandDeg, settings);
            }

            if (settings.RichTerrain)
            {
                Refine(sphere, settings, elevation, terrain);
                CarveCanyons(sphere, settings, elevation, terrain);
            }
            return new PlanetSurface(elevation, terrain);
        }

        /// <summary>Canyon tiles per thousand tiles of the planet, roughly (game rule, TBD).</summary>
        public const double CanyonTilesPerThousand = 12.0;
        /// <summary>Length of one rift, tiles (game rule, TBD).</summary>
        public const int CanyonMinLength = 6, CanyonMaxLength = 14;
        /// <summary>How far a canyon floor sits below the ground around it (in elevation units; game rule, for the look).</summary>
        public const float CanyonDepth = 0.35f;

        /// <summary>
        /// Rifts: long, nearly straight gorges across dry land, like Valles Marineris on Mars (a tectonic rift) or a river
        /// canyon. Game rule, TBD: the map has no tectonics, so a rift is a walk across the sphere in a near-steady
        /// direction. Never on water, ice or beach; the floor is lowered for the look, but stays land.
        /// </summary>
        static void CarveCanyons(HexSphere sphere, in SurfaceSettings s, float[] elevation, TerrainType[] terrain)
        {
            int n = sphere.TileCount;
            int target = (int)Math.Round(n * CanyonTilesPerThousand / 1000.0);
            var rng = new Random(s.Seed * 7919 + 4243);
            int carved = 0;
            for (int attempt = 0; attempt < 200 && carved < target; attempt++)
            {
                int t = rng.Next(n);
                if (!CanCarve(terrain[t])) continue;
                // A random heading along the surface at the start.
                Vector3 c = sphere.Centers[t];
                var r = new Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5));
                Vector3 heading = r - Vector3.Dot(r, c) * c;
                if (heading.LengthSquared() < 1e-6f) continue;
                heading = Vector3.Normalize(heading);
                int length = rng.Next(CanyonMinLength, CanyonMaxLength + 1);
                var visited = new HashSet<int>();
                for (int k = 0; k < length && carved < target; k++)
                {
                    if (!CanCarve(terrain[t])) break;
                    if (terrain[t] != TerrainType.Canyon)
                    {
                        terrain[t] = TerrainType.Canyon;
                        elevation[t] = Math.Max(0.02f, elevation[t] - CanyonDepth);
                        carved++;
                    }
                    visited.Add(t);
                    int next = -1;
                    float best = float.NegativeInfinity;
                    foreach (int nb in sphere.Neighbors[t])
                    {
                        if (visited.Contains(nb)) continue;
                        float d = Vector3.Dot(Vector3.Normalize(sphere.Centers[nb] - sphere.Centers[t]), heading);
                        if (d > best) { best = d; next = nb; }
                    }
                    if (next < 0) break;
                    Vector3 step = sphere.Centers[next] - sphere.Centers[t];
                    // Keep the heading on the surface and let it wander a little.
                    Vector3 cn = sphere.Centers[next];
                    Vector3 h = heading + 0.15f * new Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5));
                    h -= Vector3.Dot(h, cn) * cn;
                    heading = h.LengthSquared() > 1e-6f ? Vector3.Normalize(h) : Vector3.Normalize(step);
                    t = next;
                }
            }
        }

        static bool CanCarve(TerrainType t) =>
            t == TerrainType.Lowland || t == TerrainType.Desert || t == TerrainType.DryBasin || t == TerrainType.Highland
            || t == TerrainType.Tundra || t == TerrainType.Forest || t == TerrainType.Canyon;

        /// <summary>
        /// Second pass (game rules, not geology): shores become beach, low ground behind them wetland, humid land forest,
        /// and some high ground volcanic rock. Only warm planets with liquid water get beach, wetland and forest; lowland is
        /// never made dry, so the landing rules and the dry-world checks are unchanged. Reads the first pass, writes a copy.
        /// </summary>
        static void Refine(HexSphere sphere, in SurfaceSettings s, float[] elevation, TerrainType[] terrain)
        {
            var first = (TerrainType[])terrain.Clone();
            bool wet = s.LiquidWater && s.AirTemperatureC > 0.0 && s.AirTemperatureC <= SurfaceSettings.HotDesertC;
            for (int t = 0; t < first.Length; t++)
            {
                var c = sphere.Centers[t];
                double noiseA = Noise3D.Value(c.X * 5.0 + 3.1, c.Y * 5.0 - 8.2, c.Z * 5.0 + 1.7, s.Seed + 1313);
                double noiseB = Noise3D.Value(c.X * 4.0 - 2.4, c.Y * 4.0 + 5.9, c.Z * 4.0 - 6.3, s.Seed + 2027);
                float e = elevation[t];
                TerrainType here = first[t];
                if (here == TerrainType.Lowland && wet)
                {
                    bool nearSea = false, nearSea2 = false;
                    foreach (int n in sphere.Neighbors[t])
                    {
                        if (Terrains.IsWater(first[n])) nearSea = true;
                        foreach (int m in sphere.Neighbors[n])
                            if (Terrains.IsWater(first[m])) nearSea2 = true;
                    }
                    if (nearSea && e < 0.12) terrain[t] = TerrainType.Beach;
                    else if ((nearSea || nearSea2) && e < 0.2 && noiseA > -0.1) terrain[t] = TerrainType.Wetland;
                    else if (noiseB > 0.05) terrain[t] = TerrainType.Forest;
                }
                else if (here == TerrainType.Highland && wet && e < 0.5 && noiseB > 0.25)
                    terrain[t] = TerrainType.Forest;
                else if ((here == TerrainType.Highland || here == TerrainType.Mountain) && noiseA > 0.4)
                    terrain[t] = TerrainType.VolcanicRock;
            }
        }

        /// <summary>
        /// One tile: basins hold seas (frozen past the cold line) or lie dry; land past the cold line is tundra, and
        /// glacier a band further; warmer land is lowland (desert when dry or hot), highland or mountain.
        /// </summary>
        static TerrainType Classify(double e, bool cold, bool glacier, in SurfaceSettings s)
        {
            if (e < 0.0)
            {
                if (!s.LiquidWater) return TerrainType.DryBasin;
                if (cold) return TerrainType.FrozenSea;
                return e < -0.35 ? TerrainType.DeepOcean : TerrainType.ShallowOcean;
            }
            if (glacier) return TerrainType.Ice;
            if (cold) return TerrainType.Tundra;
            if (e < 0.35)
                return !s.LiquidWater || s.AirTemperatureC > SurfaceSettings.HotDesertC ? TerrainType.Desert : TerrainType.Lowland;
            return e < 0.7 ? TerrainType.Highland : TerrainType.Mountain;
        }

        static double Quantile(double[] values, double fraction)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int index = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, fraction)) * (sorted.Length - 1));
            return sorted[index];
        }
    }
}

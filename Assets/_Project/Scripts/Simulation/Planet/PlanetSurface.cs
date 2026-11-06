using System;

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
    }

    /// <summary>Names and properties of terrain types, in one place for the simulation and the UI.</summary>
    public static class Terrains
    {
        /// <summary>Open water: no building.</summary>
        public static bool IsWater(TerrainType t) => t == TerrainType.DeepOcean || t == TerrainType.ShallowOcean;

        /// <summary>Flat, open ground a colony would land on (best first: <see cref="BaseSiteRank"/>).</summary>
        public static int BaseSiteRank(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.Lowland:
                case TerrainType.Desert:
                case TerrainType.DryBasin: return 2;
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

        public SurfaceSettings(int seed, double oceanFraction = 0.62, double iceLatitudeDeg = 70.0, double noiseScale = 1.6,
                               bool iceAroundSubStellar = false, bool liquidWater = true, double airTemperatureC = 15.0)
        {
            Seed = seed;
            OceanFraction = oceanFraction;
            IceLatitudeDeg = iceLatitudeDeg;
            NoiseScale = noiseScale;
            IceAroundSubStellar = iceAroundSubStellar;
            LiquidWater = liquidWater;
            AirTemperatureC = airTemperatureC;
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
                                           liquidWater, airTemperatureC);
            double ice = airTemperatureC < TemperateMinC
                ? Math.Max(10.0, TemperateIceLatitudeDeg + 1.5 * (airTemperatureC - TemperateMinC))
                : airTemperatureC > TemperateMaxC
                    ? TemperateIceLatitudeDeg + 2.0 * (airTemperatureC - TemperateMaxC)
                    : TemperateIceLatitudeDeg;
            return new SurfaceSettings(seed, ocean, ice, 1.6, false, liquidWater, airTemperatureC);
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

            return new PlanetSurface(elevation, terrain);
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

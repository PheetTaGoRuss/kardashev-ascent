using System;

namespace KardashevAscent.Simulation.Planet
{
    public enum TerrainType : byte
    {
        DeepOcean,
        ShallowOcean,
        Lowland,
        Highland,
        Mountain,
        Ice,
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

        public SurfaceSettings(int seed, double oceanFraction = 0.62, double iceLatitudeDeg = 70.0, double noiseScale = 1.6,
                               bool iceAroundSubStellar = false)
        {
            Seed = seed;
            OceanFraction = oceanFraction;
            IceLatitudeDeg = iceLatitudeDeg;
            NoiseScale = noiseScale;
            IceAroundSubStellar = iceAroundSubStellar;
        }

        /// <summary>Earth-like ice line and the temperature band where it stays there, °C.</summary>
        public const double TemperateIceLatitudeDeg = 70.0, TemperateMinC = 0.0, TemperateMaxC = 35.0;

        /// <summary>
        /// A planet's look from its air temperature (game rules, not climate science):
        /// 0–35 °C keeps Earth's ice line at 70°; colder planets freeze 1.5° of latitude nearer the equator per °C
        /// (never past 10°); hotter ones lose their ice caps and dry out (1 % less ocean per °C, down to 10 %).
        /// A tidally locked planet is an eyeball: open ground within 80° + T (35°–180°) of the point under its star.
        /// </summary>
        public static SurfaceSettings ForClimate(int seed, double airTemperatureC, bool tidallyLocked)
        {
            double ocean = airTemperatureC > TemperateMaxC
                ? Math.Max(0.10, 0.62 - 0.01 * (airTemperatureC - TemperateMaxC))
                : 0.62;
            if (tidallyLocked)
                return new SurfaceSettings(seed, ocean, Math.Max(35.0, Math.Min(180.0, 80.0 + airTemperatureC)), 1.6, true);
            double ice = airTemperatureC < TemperateMinC
                ? Math.Max(10.0, TemperateIceLatitudeDeg + 1.5 * (airTemperatureC - TemperateMinC))
                : airTemperatureC > TemperateMaxC
                    ? TemperateIceLatitudeDeg + (airTemperatureC - TemperateMaxC)
                    : TemperateIceLatitudeDeg;
            return new SurfaceSettings(seed, ocean, ice, 1.6);
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
                terrain[t] = latDeg >= iceEdge ? TerrainType.Ice
                    : e < -0.35 ? TerrainType.DeepOcean
                    : e < 0.0 ? TerrainType.ShallowOcean
                    : e < 0.35 ? TerrainType.Lowland
                    : e < 0.7 ? TerrainType.Highland
                    : TerrainType.Mountain;
            }

            return new PlanetSurface(elevation, terrain);
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

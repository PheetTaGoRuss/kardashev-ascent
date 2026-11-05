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

        public SurfaceSettings(int seed, double oceanFraction = 0.62, double iceLatitudeDeg = 70.0, double noiseScale = 1.6)
        {
            Seed = seed;
            OceanFraction = oceanFraction;
            IceLatitudeDeg = iceLatitudeDeg;
            NoiseScale = noiseScale;
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

                double latDeg = Math.Abs(sphere.Latitude(t)) * 180.0 / Math.PI;
                var c = sphere.Centers[t];
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

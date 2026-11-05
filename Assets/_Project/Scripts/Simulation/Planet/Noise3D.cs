using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>Deterministic 3D value noise and fractal (fBm) noise for planet generation.</summary>
    public static class Noise3D
    {
        /// <summary>Smooth value noise in about −1..1.</summary>
        public static double Value(double x, double y, double z, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y), z0 = (int)Math.Floor(z);
            double fx = Fade(x - x0), fy = Fade(y - y0), fz = Fade(z - z0);

            double Lerp(double a, double b, double t) => a + (b - a) * t;
            double C(int dx, int dy, int dz) => Hash(x0 + dx, y0 + dy, z0 + dz, seed);

            double x00 = Lerp(C(0, 0, 0), C(1, 0, 0), fx);
            double x10 = Lerp(C(0, 1, 0), C(1, 1, 0), fx);
            double x01 = Lerp(C(0, 0, 1), C(1, 0, 1), fx);
            double x11 = Lerp(C(0, 1, 1), C(1, 1, 1), fx);
            return Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
        }

        /// <summary>Fractal noise: <paramref name="octaves"/> layers, each at double frequency and half amplitude. About −1..1.</summary>
        public static double Fractal(double x, double y, double z, int seed, int octaves = 5,
                                     double lacunarity = 2.0, double gain = 0.5)
        {
            double sum = 0.0, amplitude = 1.0, norm = 0.0;
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * Value(x, y, z, seed + o * 1013);
                norm += amplitude;
                amplitude *= gain;
                x *= lacunarity; y *= lacunarity; z *= lacunarity;
            }
            return sum / norm;
        }

        static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        /// <summary>Lattice value in −1..1.</summary>
        static double Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE3Du;
                h = (h << 13) | (h >> 19);
                h ^= (uint)z * 0x27D4EB2Fu;
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return h / (double)uint.MaxValue * 2.0 - 1.0;
            }
        }
    }
}

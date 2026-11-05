using System;
using System.Collections.Generic;
using System.Numerics;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// Hex tiles covering a unit sphere (a Goldberg polyhedron): 10·n² + 2 tiles, of which 12 are pentagons.
    /// Built as the dual of an icosahedron subdivided with frequency n. +Y is the planet's north pole.
    /// </summary>
    public sealed class HexSphere
    {
        /// <summary>Subdivision frequency n.</summary>
        public int Frequency { get; }

        /// <summary>Unit vector to each tile's centre.</summary>
        public Vector3[] Centers { get; }

        /// <summary>Unit vectors to each tile's corners, in order around the tile.</summary>
        public Vector3[][] Corners { get; }

        /// <summary>Indices of each tile's neighbours, in order around the tile.</summary>
        public int[][] Neighbors { get; }

        public int TileCount => Centers.Length;

        public static int TileCountFor(int frequency) => 10 * frequency * frequency + 2;

        public bool IsPentagon(int tile) => Neighbors[tile].Length == 5;

        /// <summary>Latitude of a tile's centre, radians (+ north).</summary>
        public double Latitude(int tile) => Math.Asin(Math.Max(-1f, Math.Min(1f, Centers[tile].Y)));

        /// <summary>Longitude of a tile's centre, radians (−π..π, 0 toward +Z, + toward +X).</summary>
        public double Longitude(int tile) => Math.Atan2(Centers[tile].X, Centers[tile].Z);

        HexSphere(int frequency, Vector3[] centers, Vector3[][] corners, int[][] neighbors)
        {
            Frequency = frequency;
            Centers = centers;
            Corners = corners;
            Neighbors = neighbors;
        }

        public static HexSphere Create(int frequency)
        {
            if (frequency < 1)
                throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be at least 1.");

            var points = new List<Vector3D>();
            var lookup = new Dictionary<(long, long, long), int>();
            var triangles = new List<(int A, int B, int C)>(20 * frequency * frequency);
            int n = frequency;
            var grid = new int[n + 1][];

            foreach (var (fa, fb, fc) in IcosahedronFaces)
            {
                Vector3D a = IcosahedronVertices[fa], b = IcosahedronVertices[fb], c = IcosahedronVertices[fc];
                for (int i = 0; i <= n; i++)
                {
                    grid[i] = new int[n + 1 - i];
                    for (int j = 0; j <= n - i; j++)
                    {
                        Vector3D p = a + (b - a) * ((double)i / n) + (c - a) * ((double)j / n);
                        grid[i][j] = GetOrAdd(p.Normalized(), points, lookup);
                    }
                }
                for (int i = 0; i < n; i++)
                for (int j = 0; j < n - i; j++)
                {
                    triangles.Add((grid[i][j], grid[i + 1][j], grid[i][j + 1]));
                    if (i + j < n - 1)
                        triangles.Add((grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]));
                }
            }

            int count = points.Count;
            var incident = new List<int>[count];
            var adjacent = new HashSet<int>[count];
            for (int v = 0; v < count; v++)
            {
                incident[v] = new List<int>(6);
                adjacent[v] = new HashSet<int>();
            }
            for (int t = 0; t < triangles.Count; t++)
            {
                var (a, b, c) = triangles[t];
                incident[a].Add(t); incident[b].Add(t); incident[c].Add(t);
                adjacent[a].Add(b); adjacent[a].Add(c);
                adjacent[b].Add(a); adjacent[b].Add(c);
                adjacent[c].Add(a); adjacent[c].Add(b);
            }

            var centers = new Vector3[count];
            var corners = new Vector3[count][];
            var neighbors = new int[count][];
            for (int v = 0; v < count; v++)
            {
                Vector3D center = points[v];
                centers[v] = center.ToVector3();

                var cornerDirs = new List<Vector3D>(incident[v].Count);
                foreach (int t in incident[v])
                {
                    var (a, b, c) = triangles[t];
                    cornerDirs.Add((points[a] + points[b] + points[c]).Normalized());
                }
                cornerDirs.Sort((p, q) => AngleAround(center, p).CompareTo(AngleAround(center, q)));
                corners[v] = cornerDirs.ConvertAll(p => p.ToVector3()).ToArray();

                var around = new List<int>(adjacent[v]);
                around.Sort((p, q) => AngleAround(center, points[p]).CompareTo(AngleAround(center, points[q])));
                neighbors[v] = around.ToArray();
            }

            return new HexSphere(frequency, centers, corners, neighbors);
        }

        static int GetOrAdd(Vector3D p, List<Vector3D> points, Dictionary<(long, long, long), int> lookup)
        {
            const double scale = 1e7;
            var key = ((long)Math.Round(p.X * scale), (long)Math.Round(p.Y * scale), (long)Math.Round(p.Z * scale));
            if (lookup.TryGetValue(key, out int index))
                return index;
            index = points.Count;
            points.Add(p);
            lookup.Add(key, index);
            return index;
        }

        /// <summary>Angle of <paramref name="p"/> around the axis <paramref name="center"/>.</summary>
        static double AngleAround(Vector3D center, Vector3D p)
        {
            Vector3D reference = Math.Abs(center.Y) < 0.99 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
            Vector3D u = Vector3D.Cross(reference, center).Normalized();
            Vector3D w = Vector3D.Cross(center, u);
            return Math.Atan2(Vector3D.Dot(p, w), Vector3D.Dot(p, u));
        }

        static readonly Vector3D[] IcosahedronVertices = BuildIcosahedron();

        static Vector3D[] BuildIcosahedron()
        {
            double t = (1.0 + Math.Sqrt(5.0)) / 2.0;
            var v = new[]
            {
                new Vector3D(-1, t, 0), new Vector3D(1, t, 0), new Vector3D(-1, -t, 0), new Vector3D(1, -t, 0),
                new Vector3D(0, -1, t), new Vector3D(0, 1, t), new Vector3D(0, -1, -t), new Vector3D(0, 1, -t),
                new Vector3D(t, 0, -1), new Vector3D(t, 0, 1), new Vector3D(-t, 0, -1), new Vector3D(-t, 0, 1),
            };
            for (int i = 0; i < v.Length; i++)
                v[i] = v[i].Normalized();
            return v;
        }

        static readonly (int, int, int)[] IcosahedronFaces =
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };

        readonly struct Vector3D
        {
            public readonly double X, Y, Z;

            public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }

            public static Vector3D operator +(Vector3D a, Vector3D b) => new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector3D operator -(Vector3D a, Vector3D b) => new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector3D operator *(Vector3D a, double s) => new Vector3D(a.X * s, a.Y * s, a.Z * s);

            public static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            public static Vector3D Cross(Vector3D a, Vector3D b) =>
                new Vector3D(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

            public Vector3D Normalized()
            {
                double len = Math.Sqrt(X * X + Y * Y + Z * Z);
                return new Vector3D(X / len, Y / len, Z / len);
            }

            public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
        }
    }
}

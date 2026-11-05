using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.Rendering;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>Turns a <see cref="HexSphere"/> into one mesh: each tile a flat polygon with a thin darker border.</summary>
    public static class PlanetMeshBuilder
    {
        /// <param name="tileColors">One colour per tile; alpha is used as surface smoothness by the PlanetTiles shader.</param>
        /// <param name="borderWidth">Border as a fraction of the tile's centre-to-corner distance (0 = no border).</param>
        /// <param name="borderDarken">How much darker the border is than the tile (0–1).</param>
        public static Mesh Build(HexSphere sphere, float radius, IReadOnlyList<Color32> tileColors,
                                 float borderWidth = 0.08f, float borderDarken = 0.35f)
        {
            int tiles = sphere.TileCount;
            int maxCorners = 6;
            var vertices = new List<Vector3>(tiles * (1 + 2 * maxCorners));
            var normals = new List<Vector3>(vertices.Capacity);
            var colors = new List<Color32>(vertices.Capacity);
            var indices = new List<int>(tiles * maxCorners * 9);
            bool border = borderWidth > 0f;

            for (int t = 0; t < tiles; t++)
            {
                Vector3 n = ToUnity(sphere.Centers[t]);
                Vector3 center = n * radius;
                var cornerDirs = sphere.Corners[t];
                int k = cornerDirs.Length;
                Color32 fill = tileColors[t];
                Color32 edge = Darken(fill, borderDarken);

                int centerIndex = vertices.Count;
                Add(vertices, normals, colors, center, n, fill);

                int innerStart = vertices.Count;
                for (int i = 0; i < k; i++)
                {
                    Vector3 corner = ToUnity(cornerDirs[i]) * radius;
                    Add(vertices, normals, colors, Vector3.Lerp(corner, center, border ? borderWidth : 0f), n, fill);
                }

                // Corner order goes counter-clockwise seen from outside, which faces outward in Unity.
                for (int i = 0; i < k; i++)
                {
                    indices.Add(centerIndex);
                    indices.Add(innerStart + i);
                    indices.Add(innerStart + (i + 1) % k);
                }

                if (!border)
                    continue;

                int outerStart = vertices.Count;
                for (int i = 0; i < k; i++)
                    Add(vertices, normals, colors, ToUnity(cornerDirs[i]) * radius, n, edge);

                // The inner ring again, in the border colour, so the border is a crisp band.
                int innerEdgeStart = vertices.Count;
                for (int i = 0; i < k; i++)
                    Add(vertices, normals, colors, vertices[innerStart + i], n, edge);

                for (int i = 0; i < k; i++)
                {
                    int j = (i + 1) % k;
                    int inI = innerEdgeStart + i, inJ = innerEdgeStart + j;
                    int outI = outerStart + i, outJ = outerStart + j;
                    indices.Add(inI); indices.Add(outI); indices.Add(outJ);
                    indices.Add(inI); indices.Add(outJ); indices.Add(inJ);
                }
            }

            var mesh = new Mesh { name = $"HexPlanet n={sphere.Frequency}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Add(List<Vector3> v, List<Vector3> n, List<Color32> c, Vector3 position, Vector3 normal, Color32 color)
        {
            v.Add(position);
            n.Add(normal);
            c.Add(color);
        }

        static Color32 Darken(Color32 c, float amount)
        {
            float f = 1f - amount;
            return new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), c.a);
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

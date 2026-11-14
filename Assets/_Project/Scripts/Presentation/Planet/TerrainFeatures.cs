using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.Rendering;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// What the planet's surface shows from orbit, on top of the tiles: a peak on each mountain tile, a few tree crowns
    /// on each forest tile, and the rivers as ribbons from tile to tile. One mesh for the whole planet. The tile the
    /// player is zoomed into is left out, so its close-up detail (<see cref="SiteDecor"/>) takes over there.
    /// </summary>
    public static class TerrainFeatures
    {
        static readonly Color32 Rock = new Color32(118, 110, 102, 40);
        static readonly Color32 Snow = new Color32(232, 238, 244, 90);
        static readonly Color32 Canopy = new Color32(34, 78, 38, 20);
        static readonly Color32 River = new Color32(70, 150, 215, 220);

        /// <param name="tileRadius">The tile tops' distance from the centre (relief).</param>
        /// <param name="seaRadius">Sea level.</param>
        /// <param name="excludeTile">The tile shown close up (−1: none).</param>
        public static Mesh Build(HexSphere sphere, PlanetSurface surface, RiverMap rivers, IReadOnlyList<float> tileRadius,
                                 float seaRadius, int excludeTile)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color32>();
            var tri = new List<int>();
            for (int t = 0; t < sphere.TileCount; t++)
            {
                if (t == excludeTile)
                    continue;
                Vector3 up = ToUnity(sphere.Centers[t]);
                float r = tileRadius[t];
                var corners = sphere.Corners[t];
                float span = (ToUnity(corners[0]) - up).magnitude * seaRadius;   // centre to corner, world units
                TerrainType type = surface.Terrain[t];
                if (type == TerrainType.Mountain || type == TerrainType.VolcanicRock)
                {
                    // A peak: a pyramid over most of the tile, taller on higher ground, snow-capped on the highest.
                    float height = span * (0.5f + 0.9f * Mathf.Clamp01(surface.Elevation[t]));
                    var baseRing = new Vector3[corners.Length];
                    for (int i = 0; i < corners.Length; i++)
                        baseRing[i] = Vector3.Lerp(ToUnity(corners[i]) * r, up * r, 0.25f);
                    Color32 tip = surface.Elevation[t] > 0.7f ? Snow : Rock;
                    Pyramid(v, n, c, tri, baseRing, up * (r + height), Rock, tip);
                }
                else if (type == TerrainType.Forest)
                {
                    // Small tree crowns, scattered by the tile id: a canopy texture from orbit, not giant trees when a
                    // neighbouring site is open.
                    for (int k = 0; k < 7; k++)
                    {
                        float a = (t * 2.399f + k * 1.7f) % (2f * Mathf.PI);
                        float d = span * (0.1f + 0.6f * Hash(t, k));
                        Vector3 east = Vector3.Cross(Vector3.up, up).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, up).normalized : Vector3.right;
                        Vector3 north = Vector3.Cross(up, east);
                        Vector3 dir = (up * r + (east * Mathf.Cos(a) + north * Mathf.Sin(a)) * d).normalized;
                        Cone(v, n, c, tri, dir * r, dir, span * 0.09f, span * 0.2f, Canopy);
                    }
                }
                if (rivers != null && rivers.IsRiver(t))
                {
                    int next = rivers.Downstream(t);
                    if (next >= 0 && next != excludeTile)
                    {
                        // From this tile's centre to the next one's, just above both tops (or sea level).
                        float width = span * (0.12f + 0.05f * Mathf.Min(6, rivers.Flow(t)));
                        Vector3 a = up * (r + span * 0.02f);
                        Vector3 nextUp = ToUnity(sphere.Centers[next]);
                        float rNext = Terrains.IsWater(surface.Terrain[next]) ? seaRadius : tileRadius[next];
                        Vector3 b = nextUp * (rNext + span * 0.02f);
                        Ribbon(v, n, c, tri, a, b, up, nextUp, width, River);
                    }
                }
            }
            var mesh = new Mesh { name = "Terrain features", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Pyramid(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri, Vector3[] ring, Vector3 apex,
                            Color32 side, Color32 tip)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % ring.Length];
                Vector3 normal = Vector3.Cross(b - a, apex - a).normalized;
                if (Vector3.Dot(normal, apex) < 0f) normal = -normal;
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(apex);
                n.Add(normal); n.Add(normal); n.Add(normal);
                c.Add(side); c.Add(side); c.Add(tip);
                // Both windings: features are few, and this way no face goes missing whichever way is front.
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
                tri.Add(s); tri.Add(s + 2); tri.Add(s + 1);
            }
        }

        /// <summary>A cone standing on <paramref name="baseCentre"/> along <paramref name="up"/> (a tree crown).</summary>
        public static void Cone(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri, Vector3 baseCentre, Vector3 up,
                                float radius, float height, Color32 colour, int sides = 6)
        {
            up = up.normalized;
            Vector3 e = Vector3.Cross(up, Vector3.up).sqrMagnitude > 1e-6f ? Vector3.Cross(up, Vector3.up).normalized : Vector3.right;
            Vector3 f = Vector3.Cross(up, e);
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * 2f * Mathf.PI / sides;
                ring[i] = baseCentre + (e * Mathf.Cos(a) + f * Mathf.Sin(a)) * radius;
            }
            Pyramid(v, n, c, tri, ring, baseCentre + up * height, colour, colour);
        }

        /// <summary>A flat strip from a to b, facing up (a river).</summary>
        public static void Ribbon(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri, Vector3 a, Vector3 b,
                                  Vector3 upA, Vector3 upB, float width, Color32 colour)
        {
            Vector3 along = (b - a).normalized;
            Vector3 sideA = Vector3.Cross(upA, along).normalized * (width * 0.5f);
            Vector3 sideB = Vector3.Cross(upB, along).normalized * (width * 0.5f);
            int s = v.Count;
            v.Add(a - sideA); v.Add(a + sideA); v.Add(b + sideB); v.Add(b - sideB);
            n.Add(upA); n.Add(upA); n.Add(upB); n.Add(upB);
            c.Add(colour); c.Add(colour); c.Add(colour); c.Add(colour);
            // Both windings (see Pyramid).
            tri.Add(s); tri.Add(s + 1); tri.Add(s + 2); tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
            tri.Add(s); tri.Add(s + 2); tri.Add(s + 1); tri.Add(s); tri.Add(s + 3); tri.Add(s + 2);
        }

        /// <summary>A stable number in [0, 1) for a tile and an index.</summary>
        public static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ 0x27D4EB2Fu;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

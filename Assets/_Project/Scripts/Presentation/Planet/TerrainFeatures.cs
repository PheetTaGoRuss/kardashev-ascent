using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.Rendering;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// What the planet's surface shows from orbit, on top of the tiles: a peak on each mountain tile, a few low lava
    /// mounds on volcanic rock and a few tree crowns on each forest tile. One mesh for the whole planet. The tile the
    /// player is zoomed into is left out, so its close-up detail (<see cref="SiteDecor"/>) takes over there.
    /// </summary>
    public static class TerrainFeatures
    {
        static readonly Color32 Rock = new Color32(118, 110, 102, 40);
        static readonly Color32 Snow = new Color32(232, 238, 244, 90);
        static readonly Color32 Canopy = new Color32(34, 78, 38, 20);
        static readonly Color32 Lava = new Color32(62, 50, 50, 30);
        static readonly Color32 Portal = new Color32(20, 18, 18, 0);
        static readonly Color32 Rubble = new Color32(160, 150, 138, 40);
        static readonly Color32 Steel = new Color32(170, 178, 186, 60);

        /// <param name="tileRadius">The tile tops' distance from the centre (relief).</param>
        /// <param name="seaRadius">Sea level.</param>
        /// <param name="excludeTile">The tile shown close up (−1: none).</param>
        /// <param name="builtShare">Share of each tile's slots built on (missing: 0). Forest crowns thin out to match.</param>
        public static Mesh Build(HexSphere sphere, PlanetSurface surface, IReadOnlyList<float> tileRadius,
                                 float seaRadius, int excludeTile, IReadOnlyDictionary<int, float> builtShare = null,
                                 IReadOnlyDictionary<int, KardashevAscent.Simulation.Resources.PassageKind> passages = null)
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
                if (type == TerrainType.VolcanicRock)
                {
                    // A lava field, not a mountain (director 2026-11-15: tall peaks walled in sites next to it): a few
                    // low dark mounds, scattered by the tile id.
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (t * 2.399f + k * 1.9f) % (2f * Mathf.PI);
                        float d = span * (0.15f + 0.5f * Hash(t, k + 11));
                        Vector3 east = Vector3.Cross(Vector3.up, up).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, up).normalized : Vector3.right;
                        Vector3 north = Vector3.Cross(up, east);
                        Vector3 dir = (up * r + (east * Mathf.Cos(a) + north * Mathf.Sin(a)) * d).normalized;
                        Cone(v, n, c, tri, dir * r, dir, span * 0.16f, span * 0.12f, Lava);
                    }
                }
                else if (type == TerrainType.Mountain)
                {
                    // A peak: a pyramid over most of the tile, taller on higher ground, snow-capped on the highest.
                    float height = span * (0.5f + 0.9f * Mathf.Clamp01(surface.Elevation[t]));
                    var baseRing = new Vector3[corners.Length];
                    for (int i = 0; i < corners.Length; i++)
                        baseRing[i] = Vector3.Lerp(ToUnity(corners[i]) * r, up * r, 0.25f);
                    Color32 tip = surface.Elevation[t] > 0.7f ? Snow : Rock;
                    var how = passages != null && passages.TryGetValue(t, out var p) ? p : KardashevAscent.Simulation.Resources.PassageKind.None;
                    Vector3 east = Vector3.Cross(Vector3.up, up).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, up).normalized : Vector3.right;
                    Vector3 north = Vector3.Cross(up, east);
                    if (how == KardashevAscent.Simulation.Resources.PassageKind.Blast)
                    {
                        // A blasted pass: two lower peaks either side of a notch, and the cut floor in pale rubble.
                        foreach (float side in new[] { -1f, 1f })
                            Cone(v, n, c, tri, up * r + east * side * span * 0.42f, up, span * 0.38f, height * 0.6f, Rock, 5);
                        Slab(v, n, c, tri, up * r, up, north, span * 0.85f, span * 0.22f, span * 0.03f, Rubble);
                    }
                    else
                    {
                        Pyramid(v, n, c, tri, baseRing, up * (r + height), Rock, tip);
                        if (how == KardashevAscent.Simulation.Resources.PassageKind.Tunnel)
                            // A tunnel mouth at the foot of the peak, both sides.
                            foreach (float side in new[] { -1f, 1f })
                                Slab(v, n, c, tri, up * r + north * side * span * 0.62f, up, east, span * 0.2f, span * 0.06f, span * 0.16f, Portal);
                    }
                }
                else if (type == TerrainType.Canyon && passages != null && passages.TryGetValue(t, out var bridge)
                         && bridge == KardashevAscent.Simulation.Resources.PassageKind.Bridge)
                {
                    // A steel bridge across the gorge, at the height of the ground beside it.
                    Vector3 east = Vector3.Cross(Vector3.up, up).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, up).normalized : Vector3.right;
                    Vector3 north = Vector3.Cross(up, east);
                    float rim = r + span * 0.35f;
                    Slab(v, n, c, tri, up * rim, up, east, span * 1.7f, span * 0.16f, span * 0.04f, Steel);
                    foreach (float side in new[] { -0.5f, 0.5f })
                        Slab(v, n, c, tri, up * (r + span * 0.17f) + east * side * span, up, east, span * 0.06f, span * 0.06f, span * 0.35f, Steel);
                }
                else if (type == TerrainType.Forest)
                {
                    // Small tree crowns, scattered by the tile id: a canopy texture from orbit, not giant trees when a
                    // neighbouring site is open.
                    // Buildings clear the forest: a tile half built on keeps about half its crowns.
                    float cleared = builtShare != null && builtShare.TryGetValue(t, out float share) ? share : 0f;
                    int crowns = cleared > 0f ? Mathf.Max(0, 7 - Mathf.CeilToInt(7f * cleared)) : 7;
                    for (int k = 0; k < crowns; k++)
                    {
                        float a = (t * 2.399f + k * 1.7f) % (2f * Mathf.PI);
                        float d = span * (0.1f + 0.6f * Hash(t, k));
                        Vector3 east = Vector3.Cross(Vector3.up, up).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, up).normalized : Vector3.right;
                        Vector3 north = Vector3.Cross(up, east);
                        Vector3 dir = (up * r + (east * Mathf.Cos(a) + north * Mathf.Sin(a)) * d).normalized;
                        Cone(v, n, c, tri, dir * r, dir, span * 0.09f, span * 0.2f, Canopy);
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

        /// <summary>
        /// A box standing on <paramref name="baseCentre"/>: <paramref name="length"/> along <paramref name="along"/>,
        /// <paramref name="width"/> across, <paramref name="height"/> up.
        /// </summary>
        static void Slab(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri, Vector3 baseCentre, Vector3 up, Vector3 along,
                         float length, float width, float height, Color32 colour)
        {
            up = up.normalized;
            along = (along - Vector3.Dot(along, up) * up).normalized;
            Vector3 across = Vector3.Cross(up, along);
            Vector3 hl = along * length * 0.5f, hw = across * width * 0.5f, h = up * height;
            Vector3[] p =
            {
                baseCentre - hl - hw, baseCentre + hl - hw, baseCentre + hl + hw, baseCentre - hl + hw,
                baseCentre - hl - hw + h, baseCentre + hl - hw + h, baseCentre + hl + hw + h, baseCentre - hl + hw + h,
            };
            int[][] faces = { new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            foreach (int[] f in faces)
            {
                Vector3 normal = Vector3.Cross(p[f[1]] - p[f[0]], p[f[2]] - p[f[0]]).normalized;
                int s = v.Count;
                for (int i = 0; i < 4; i++) { v.Add(p[f[i]]); n.Add(normal); c.Add(colour); }
                // Both windings, as for the peaks.
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2); tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
                tri.Add(s); tri.Add(s + 2); tri.Add(s + 1); tri.Add(s); tri.Add(s + 3); tri.Add(s + 2);
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

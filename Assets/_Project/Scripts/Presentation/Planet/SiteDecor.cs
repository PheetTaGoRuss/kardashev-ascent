using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.Rendering;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Close-up nature on the open building site: trees on forest (and a few on lowland), rocks on high and dry ground,
    /// and the river's channel when one runs through the tile. A building clears its slots: trees, rocks and the river
    /// there disappear when the site starts (visual only, decided 2026-11-14; nothing grows back). From orbit the
    /// same tile shows its large-scale features instead (<see cref="TerrainFeatures"/>). Sets itself up after the scene
    /// loads, next to the <see cref="SiteView"/>.
    /// </summary>
    public sealed class SiteDecor : MonoBehaviour
    {
        static readonly Color32 Trunk = new Color32(92, 64, 40, 10);
        static readonly Color32 Crown = new Color32(38, 92, 44, 20);
        static readonly Color32 DarkCrown = new Color32(26, 70, 34, 20);
        static readonly Color32 Reed = new Color32(96, 128, 60, 15);
        static readonly Color32 Stone = new Color32(128, 120, 110, 40);
        static readonly Color32 DarkStone = new Color32(58, 52, 50, 50);
        static readonly Color32 Water = new Color32(70, 150, 215, 230);

        SiteView site;
        PlanetView planet;
        GameObject holder;
        Mesh mesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SiteView s = FindAnyObjectByType<SiteView>();
            if (s != null && s.GetComponent<SiteDecor>() == null)
                s.gameObject.AddComponent<SiteDecor>();
        }

        void Awake()
        {
            site = GetComponent<SiteView>();
            planet = GetComponent<PlanetView>() ?? FindAnyObjectByType<PlanetView>();
        }

        void OnEnable()
        {
            if (site == null)
                return;
            site.SiteChanged += OnSiteChanged;
            site.PadsRebuilt += Rebuild;
        }

        void OnDisable()
        {
            if (site == null)
                return;
            site.SiteChanged -= OnSiteChanged;
            site.PadsRebuilt -= Rebuild;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (holder != null) Destroy(holder);
        }

        void OnSiteChanged(int tile) => Rebuild();

        void Rebuild()
        {
            TileSite s = site != null ? site.Site : null;
            if (s == null || planet == null || planet.Surface == null)
            {
                if (holder != null) holder.SetActive(false);
                return;
            }
            if (holder == null)
            {
                holder = new GameObject("Site Nature");
                holder.transform.SetParent(planet.transform, false);
                holder.AddComponent<MeshFilter>();
                holder.AddComponent<MeshRenderer>();
            }

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color32>();
            var tri = new List<int>();
            int tile = s.Tile;
            TerrainType terrain = planet.Surface.Terrain[tile];
            float slot = (float)s.Spacing * planet.Radius;   // one slot's width, world units

            // The river's channel first: the slots it crosses get no trees or rocks.
            var riverSlots = new HashSet<int>();
            RiverMap rivers = planet.Rivers;
            if (rivers != null && rivers.IsRiver(tile))
                AddRiver(s, rivers, slot, riverSlots, v, n, c, tri);

            for (int k = 0; k < s.SlotCount; k++)
            {
                if (Built(k) || riverSlots.Contains(k))
                    continue;
                Vector3 centre = ToUnity(s.SlotDirections[k]);
                switch (terrain)
                {
                    case TerrainType.Forest:
                        for (int i = 0; i < 3; i++) Tree(centre, slot, tile, k, i, DarkCrown, 1.0f, v, n, c, tri);
                        break;
                    case TerrainType.Lowland:
                    case TerrainType.Beach:
                        if (TerrainFeatures.Hash(tile * 97 + k, 1) < 0.3f) Tree(centre, slot, tile, k, 0, Crown, 0.8f, v, n, c, tri);
                        break;
                    case TerrainType.Wetland:
                        for (int i = 0; i < 3; i++) Spike(centre, slot, tile, k, i, Reed, 0.25f, v, n, c, tri);
                        break;
                    case TerrainType.Highland:
                        if (TerrainFeatures.Hash(tile * 97 + k, 2) < 0.5f) Rock(centre, slot, tile, k, 0, Stone, 0.18f, v, n, c, tri);
                        break;
                    case TerrainType.Mountain:
                        for (int i = 0; i < 2; i++) Rock(centre, slot, tile, k, i, Stone, 0.28f, v, n, c, tri);
                        break;
                    case TerrainType.VolcanicRock:
                        for (int i = 0; i < 2; i++) Rock(centre, slot, tile, k, i, DarkStone, 0.24f, v, n, c, tri);
                        break;
                    case TerrainType.Desert:
                    case TerrainType.DryBasin:
                    case TerrainType.Tundra:
                        if (TerrainFeatures.Hash(tile * 97 + k, 3) < 0.3f) Rock(centre, slot, tile, k, 0, Stone, 0.12f, v, n, c, tri);
                        break;
                }
            }

            if (mesh == null)
                mesh = new Mesh { name = "Site nature", indexFormat = IndexFormat.UInt32 };
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            holder.GetComponent<MeshFilter>().sharedMesh = mesh;
            holder.GetComponent<MeshRenderer>().sharedMaterial = planet.TerrainMaterial;
            holder.SetActive(true);
        }

        /// <summary>A building (or its site) covers this slot: its nature is cleared.</summary>
        bool Built(int slot) => site.PadTint != null && site.PadTint(slot).HasValue;

        /// <summary>
        /// The channel: in from the upstream neighbour's side (or a spring at the centre), out toward the downstream
        /// tile, as short ribbon pieces on the ground. Pieces over a built slot are left out (filled in).
        /// </summary>
        void AddRiver(TileSite s, RiverMap rivers, float slot, HashSet<int> riverSlots,
                      List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri)
        {
            int tile = s.Tile;
            Vector3 centre = ToUnity(s.Centre);
            Vector3 entry = centre;
            foreach (int nb in planet.Sphere.Neighbors[tile])
                if (rivers.Downstream(nb) == tile)
                {
                    entry = Vector3.Lerp(centre, ToUnity(planet.Sphere.Centers[nb]), 0.5f).normalized;   // the shared edge
                    break;
                }
            int down = rivers.Downstream(tile);
            Vector3 exit = down >= 0 ? Vector3.Lerp(centre, ToUnity(planet.Sphere.Centers[down]), 0.5f).normalized : centre;
            float width = slot * (0.9f + 0.2f * Mathf.Min(6, rivers.Flow(tile)));
            const int pieces = 16;
            var points = new List<Vector3>();
            for (int i = 0; i <= pieces; i++)
            {
                float f = i / (float)pieces;
                // Through the centre with a gentle bend: a quadratic curve entry → centre → exit.
                Vector3 p = ((1 - f) * (1 - f) * entry + 2f * (1 - f) * f * centre + f * f * exit).normalized;
                points.Add(p);
            }
            for (int i = 0; i < pieces; i++)
            {
                Vector3 mid = (points[i] + points[i + 1]).normalized;
                int under = s.FindSlot(new System.Numerics.Vector3(mid.x, mid.y, mid.z));
                if (under >= 0)
                {
                    riverSlots.Add(under);
                    if (Built(under))
                        continue;   // the building filled this stretch in
                }
                float lift = slot * 0.015f;
                Vector3 a = points[i] * (site.GroundRadius(points[i]) + lift);
                Vector3 b = points[i + 1] * (site.GroundRadius(points[i + 1]) + lift);
                TerrainFeatures.Ribbon(v, n, c, tri, a, b, points[i], points[i + 1], width, Water);
            }
        }

        void Tree(Vector3 slotCentre, float slot, int tile, int k, int i, Color32 crown, float scale,
                  List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri)
        {
            Vector3 dir = Scatter(slotCentre, slot, tile, k, i);
            if (LandsOnBuilt(dir)) return;   // scattered into a neighbouring building's slot
            float ground = site.GroundRadius(dir);
            float h = slot * 0.55f * scale * (0.8f + 0.4f * TerrainFeatures.Hash(tile * 31 + k, i + 10));
            TerrainFeatures.Cone(v, n, c, tri, dir * ground, dir, slot * 0.035f * scale, h * 0.45f, Trunk, 4);
            TerrainFeatures.Cone(v, n, c, tri, dir * (ground + h * 0.25f), dir, slot * 0.17f * scale, h * 0.8f, crown, 6);
        }

        void Spike(Vector3 slotCentre, float slot, int tile, int k, int i, Color32 colour, float size,
                   List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri)
        {
            Vector3 dir = Scatter(slotCentre, slot, tile, k, i);
            if (LandsOnBuilt(dir)) return;   // scattered into a neighbouring building's slot
            TerrainFeatures.Cone(v, n, c, tri, dir * site.GroundRadius(dir), dir, slot * 0.03f, slot * size, colour, 3);
        }

        void Rock(Vector3 slotCentre, float slot, int tile, int k, int i, Color32 colour, float size,
                  List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> tri)
        {
            Vector3 dir = Scatter(slotCentre, slot, tile, k, i);
            if (LandsOnBuilt(dir)) return;   // scattered into a neighbouring building's slot
            float r = slot * size * (0.6f + 0.6f * TerrainFeatures.Hash(tile * 53 + k, i + 20));
            // A low, wide pyramid: a boulder.
            TerrainFeatures.Cone(v, n, c, tri, dir * site.GroundRadius(dir), dir, r, r * 0.8f, colour, 5);
        }

        bool LandsOnBuilt(Vector3 dir)
        {
            int under = site.Site.FindSlot(new System.Numerics.Vector3(dir.x, dir.y, dir.z));
            return under >= 0 && Built(under);
        }

        /// <summary>A point inside a slot, the same every time for this tile, slot and index.</summary>
        Vector3 Scatter(Vector3 slotCentre, float slot, int tile, int k, int i)
        {
            Vector3 e = Vector3.Cross(slotCentre, Vector3.up).sqrMagnitude > 1e-6f ? Vector3.Cross(slotCentre, Vector3.up).normalized : Vector3.right;
            Vector3 f = Vector3.Cross(slotCentre, e);
            float a = TerrainFeatures.Hash(tile * 131 + k, i) * 2f * Mathf.PI;
            float d = TerrainFeatures.Hash(tile * 17 + k, i + 5) * 0.3f * slot / planet.Radius;
            return (slotCentre + (e * Mathf.Cos(a) + f * Mathf.Sin(a)) * d).normalized;
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// The wires you can see (director 2026-11-20: "I don't see wired system", like Oxygen Not Included). On the planet a wire
    /// is drawn between neighbouring tiles that have power lines (and the lander tile), coloured by how loaded it is:
    /// amber when it has room, orange when nearly full, red when overloaded, grey when it is not joined to the lander tile,
    /// dim while it is still a construction site. In an open site every building gets a wire to the tile's power line (or to
    /// the lander for the lander tile), and a red cross when the tile is off the grid. Sets itself up after the scene loads.
    /// </summary>
    public sealed class GridView : MonoBehaviour
    {
        static readonly Color Free = new Color(1.00f, 0.82f, 0.28f);
        static readonly Color Busy = new Color(1.00f, 0.55f, 0.20f);
        static readonly Color Full = new Color(1.00f, 0.25f, 0.22f);
        static readonly Color Dead = new Color(0.52f, 0.54f, 0.58f);
        const float Lift = 1.012f;

        SimulationRunner runner;
        PlanetView planet;
        SiteView siteView;
        Material material;
        readonly List<LineRenderer> planetWires = new List<LineRenderer>();
        readonly List<LineRenderer> siteWires = new List<LineRenderer>();
        readonly List<LineRenderer> crosses = new List<LineRenderer>();
        float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<GridView>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Grid View").AddComponent<GridView>();
        }

        void Start()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            planet = FindFirstObjectByType<PlanetView>();
            siteView = FindFirstObjectByType<SiteView>();
            material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
        }

        void LateUpdate()
        {
            if (runner == null || runner.Simulation == null || planet == null || planet.Sphere == null)
            {
                Hide(planetWires, 0); Hide(siteWires, 0); Hide(crosses, 0);
                return;
            }
            if (Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + 0.3f;
            IPlanetSimulation sim = runner.Simulation;
            if (!sim.WiredGrid || !GameFlow.IsPlaying)
            {
                Hide(planetWires, 0); Hide(siteWires, 0); Hide(crosses, 0);
                return;
            }
            DrawPlanetWires(sim);
            DrawSiteWires(sim);
        }

        Vector3 TilePoint(int tile, float lift)
        {
            var c = planet.Sphere.Centers[tile];
            return planet.transform.TransformPoint(new Vector3(c.X, c.Y, c.Z) * planet.Radius * lift);
        }

        void DrawPlanetWires(IPlanetSimulation sim)
        {
            var nodes = new HashSet<int> { sim.BaseTileId };
            foreach (int t in sim.LineTiles()) nodes.Add(t);
            int used = 0;
            foreach (int t in nodes)
                foreach (int nb in planet.Sphere.Neighbors[t])
                {
                    if (nb <= t || !nodes.Contains(nb)) continue;
                    Vector3 pa = TilePoint(t, Lift), pb = TilePoint(nb, Lift);
                    LineRenderer wire = Get(planetWires, used++, WidthAt((pa + pb) * 0.5f, 0.006f, 0.003f, 0.03f));
                    bool powered = sim.IsPowered(t) && sim.IsPowered(nb);
                    bool built = (t == sim.BaseTileId || sim.HasLineOn(t, true)) && (nb == sim.BaseTileId || sim.HasLineOn(nb, true));
                    Color c = Dead;
                    if (powered)
                    {
                        // the far end of the wire from the lander carries the flow; the busier of the two ends colours it
                        double flow = System.Math.Max(sim.LineFlowW(t), sim.LineFlowW(nb));
                        double cap = System.Math.Min(sim.LineCapacityW(t), sim.LineCapacityW(nb));
                        bool over = sim.IsLineOverloaded(t) || sim.IsLineOverloaded(nb);
                        double share = double.IsInfinity(cap) || cap <= 0.0 ? 0.0 : flow / cap;
                        c = over ? Full : share > 0.6 ? Busy : Free;
                    }
                    if (!built) c = new Color(c.r, c.g, c.b, 0.35f);
                    wire.startColor = wire.endColor = c;
                    wire.SetPosition(0, pa);
                    wire.SetPosition(1, pb);
                    wire.gameObject.SetActive(true);
                }
            // a pole at every wired tile, so a single line is still seen from far away
            foreach (int t in nodes)
            {
                LineRenderer pole = Get(planetWires, used++, WidthAt(TilePoint(t, Lift), 0.010f, 0.006f, 0.05f));
                bool powered = sim.IsPowered(t);
                Color c = !powered ? Dead : sim.IsLineOverloaded(t) ? Full : Free;
                if (t != sim.BaseTileId && !sim.HasLineOn(t, true)) c = new Color(c.r, c.g, c.b, 0.35f);
                pole.startColor = pole.endColor = c;
                pole.SetPosition(0, TilePoint(t, Lift));
                pole.SetPosition(1, TilePoint(t, Lift + 0.05f));
                pole.gameObject.SetActive(true);
            }
            Hide(planetWires, used);
        }

        void DrawSiteWires(IPlanetSimulation sim)
        {
            int used = 0, crossed = 0;
            TileSiteReader site = TileSiteReader.Of(siteView);
            if (site.Open)
            {
                int tile = site.Tile;
                bool onGrid = sim.IsPowered(tile);
                int pole = -1;
                for (int s = 0; s < sim.SlotCount(tile) && pole < 0; s++)
                    if (sim.GetBuilding(tile, s) == BuildingType.PowerLine || sim.GetBuilding(tile, s) == BuildingType.HighVoltageLine) pole = s;
                float spacing = (float)site.Spacing * planet.Radius;   // one slot across, in world units
                Vector3 hub = pole >= 0 ? site.Point(planet, pole) : planet.transform.TransformPoint(site.CentreDirection * planet.Radius * Lift);
                var seen = new HashSet<int>();
                for (int s = 0; s < sim.SlotCount(tile); s++)
                {
                    BuildingType b = sim.GetBuilding(tile, s);
                    if (b == BuildingType.None || b == BuildingType.PowerLine || b == BuildingType.HighVoltageLine) continue;
                    int anchor = sim.AnchorOf(tile, s);
                    if (anchor < 0 || !seen.Add(anchor)) continue;
                    Vector3 from = site.Point(planet, anchor);
                    if (onGrid)
                    {
                        LineRenderer wire = Get(siteWires, used++, spacing * 0.07f);
                        wire.startColor = wire.endColor = sim.IsLineOverloaded(tile) ? Full : Free;
                        wire.SetPosition(0, from);
                        wire.SetPosition(1, hub);
                        wire.gameObject.SetActive(true);
                    }
                    else if (IsGenerator(b) || NeedsGrid(b))
                    {
                        // off the grid: a red cross over the building
                        Vector3 up = (from - planet.transform.position).normalized;
                        float size = spacing * 0.32f;
                        Vector3 a = Vector3.Cross(up, Vector3.right).normalized * size;
                        LineRenderer c1 = Get(crosses, crossed++, spacing * 0.09f);
                        c1.startColor = c1.endColor = Full;
                        c1.SetPosition(0, from + up * spacing * 0.6f - a); c1.SetPosition(1, from + up * spacing * 0.6f + a);
                        c1.gameObject.SetActive(true);
                        LineRenderer c2 = Get(crosses, crossed++, spacing * 0.09f);
                        c2.startColor = c2.endColor = Full;
                        Vector3 b2 = Vector3.Cross(up, a).normalized * size;
                        c2.SetPosition(0, from + up * spacing * 0.6f - b2); c2.SetPosition(1, from + up * spacing * 0.6f + b2);
                        c2.gameObject.SetActive(true);
                    }
                }
            }
            Hide(siteWires, used);
            Hide(crosses, crossed);
        }

        static bool IsGenerator(BuildingType b) =>
            b == BuildingType.SolarPanel || b == BuildingType.FloatingSolar || b == BuildingType.TidalTurbine || b == BuildingType.WaveConverter || b == BuildingType.TidalBarrage;

        static bool NeedsGrid(BuildingType b) =>
            b == BuildingType.Battery || b == BuildingType.Workshop || b == BuildingType.ChargingStation || b == BuildingType.Relay
            || b == BuildingType.ResearchStation || b == BuildingType.AluminaPlant || b == BuildingType.Electrolyser;

        /// <summary>A wire that stays thin on screen: wider when the camera is far, never wider than the cap.</summary>
        static float WidthAt(Vector3 point, float perUnit, float min, float max)
        {
            Camera cam = Camera.main;
            float distance = cam != null ? Vector3.Distance(cam.transform.position, point) : 10f;
            return Mathf.Clamp(distance * perUnit, min, max);
        }

        LineRenderer Get(List<LineRenderer> pool, int index, float width)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("Wire") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.sharedMaterial = material;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.numCapVertices = 2;
                pool.Add(line);
            }
            LineRenderer l = pool[index];
            l.widthMultiplier = width;
            return l;
        }

        static void Hide(List<LineRenderer> pool, int from)
        {
            for (int i = from; i < pool.Count; i++)
                if (pool[i] != null) pool[i].gameObject.SetActive(false);
        }

        /// <summary>The open site's tile and slot positions, read from the site view.</summary>
        readonly struct TileSiteReader
        {
            readonly SiteView view;
            public readonly bool Open;
            public readonly int Tile;
            public readonly Vector3 CentreDirection;
            public readonly double Spacing;

            TileSiteReader(SiteView view, bool open, int tile, Vector3 centre, double spacing)
            {
                this.view = view; Open = open; Tile = tile; CentreDirection = centre; Spacing = spacing;
            }

            public static TileSiteReader Of(SiteView view)
            {
                var site = view != null ? view.Site : null;
                if (site == null) return new TileSiteReader(view, false, -1, Vector3.up, 0.0);
                var c = site.Centre;
                return new TileSiteReader(view, true, site.Tile, new Vector3(c.X, c.Y, c.Z), site.Spacing);
            }

            public Vector3 Point(PlanetView planet, int slot)
            {
                var d = view.Site.SlotDirections[slot];
                var dir = new Vector3(d.X, d.Y, d.Z);
                float r = view.GroundRadius(dir) + 0.02f;
                return planet.transform.TransformPoint(dir * r);
            }
        }
    }
}

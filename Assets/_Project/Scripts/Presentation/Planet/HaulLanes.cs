using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using UnityEngine;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Haul lanes (mission operations 6, MO-15): each haul under way is a line on the planet from the tile it loads at to the
    /// tile it unloads at, coloured by material and as wide as the load. A dashed lane is planned: no robot has loaded it yet.
    /// Drawn like the wires, tile to tile; hidden while a building site is open. Sets itself up after the scene loads.
    /// </summary>
    public sealed class HaulLanes : MonoBehaviour
    {
        const float Interval = 0.4f;

        SimulationRunner runner;
        PlanetView planet;
        PanelPlacer placer;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        UnityEngine.Material solid, dashed;
        float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<HaulLanes>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Haul Lanes").AddComponent<HaulLanes>();
        }

        void Start()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            planet = FindFirstObjectByType<PlanetView>();
            placer = FindFirstObjectByType<PanelPlacer>();
            solid = new UnityEngine.Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            dashed = new UnityEngine.Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            var tex = new Texture2D(4, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            tex.SetPixels(new[] { Color.white, Color.white, new Color(1, 1, 1, 0), new Color(1, 1, 1, 0) });
            tex.Apply();
            dashed.mainTexture = tex;
        }

        static Color ColourOf(Material m)
        {
            switch (m)
            {
                case Material.Rock: return new Color(0.78f, 0.72f, 0.62f);
                case Material.Iron: return new Color(0.90f, 0.50f, 0.35f);
                case Material.Copper: return new Color(0.95f, 0.60f, 0.30f);
                case Material.Lithium: return new Color(0.75f, 0.60f, 0.95f);
                case Material.Aluminium: case Material.PureAluminium: case Material.Alumina: return new Color(0.70f, 0.82f, 0.98f);
                case Material.Anorthosite: return new Color(0.88f, 0.90f, 0.95f);
                case Material.Salt: return new Color(1f, 1f, 1f);
                case Material.Sodium: return new Color(1f, 0.80f, 0.35f);
                case Material.Water: return new Color(0.45f, 0.75f, 1f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        Vector3 TilePoint(int tile)
        {
            var c = planet.Sphere.Centers[tile];
            return planet.transform.TransformPoint(new Vector3(c.X, c.Y, c.Z) * planet.Radius * 1.03f);
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            bool show = sim != null && sim.LogisticsEnabled && planet != null && !(placer != null && placer.Tile >= 0);
            var orders = show ? sim.HaulOrders() : null;
            int n = show ? orders.Count : 0;
            while (lines.Count < n)
            {
                var go = new GameObject("Haul Lane") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.textureMode = LineTextureMode.Tile;
                lines.Add(lr);
            }
            Camera cam = Camera.main;
            for (int i = 0; i < lines.Count; i++)
            {
                LineRenderer lr = lines[i];
                if (i >= n) { lr.enabled = false; continue; }
                var o = orders[i];
                if (o.From == o.To) { lr.enabled = false; continue; }
                Vector3 a = TilePoint(o.From), b = TilePoint(o.To);
                lr.enabled = true;
                lr.sharedMaterial = o.Loaded ? solid : dashed;
                Color c = ColourOf(o.Material);
                lr.startColor = lr.endColor = c;
                lr.SetPosition(0, a);
                lr.SetPosition(1, b);
                float distance = cam != null ? Vector3.Distance(cam.transform.position, a) : 10f;
                // width follows the load (50 kg thin, 500 kg and over thick) and the view distance, so a lane reads at any zoom
                float load = Mathf.Clamp01(Mathf.InverseLerp(0f, 500f, (float)o.Kg));
                lr.widthMultiplier = Mathf.Clamp(distance * (0.004f + 0.012f * load), 0.004f, 0.06f);
                lr.textureScale = new Vector2(1f / Mathf.Max(0.001f, lr.widthMultiplier * 4f), 1f);
            }
        }
    }
}

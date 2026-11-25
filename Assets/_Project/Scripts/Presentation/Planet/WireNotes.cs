using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Wiring notes (director 2026-11-20: "don't auto wire it", "pop up when a building needs a wire", "when entering wire mode
    /// show the buildings that need input or output"). Nothing is wired for the player: a pop-up names each new building that
    /// still needs a wire (and lines that overload), and while the wire tool or a power-line card is on, every building still
    /// waiting is marked in place: blue OUT for what gives power, orange IN for what takes it. Sets itself up after the scene loads.
    /// </summary>
    public sealed class WireNotes : MonoBehaviour
    {
        SimulationRunner runner;
        PlanetView planet;
        PanelPlacer placer;
        readonly List<(int Tile, int Slot, BuildingType Type)> unwired = new List<(int, int, BuildingType)>();
        readonly HashSet<(int, int)> known = new HashSet<(int, int)>();
        bool knownReady;
        float nextScan;
        string popup = "";
        float popupUntil;
        double lastOverload;
        int lastOffGrid;
        GUIStyle box, outTag, inTag;

        static GUIStyle Solid(Color fill)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, fill);
            t.Apply();
            var st = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            st.normal.background = t;
            st.normal.textColor = Color.white;
            return st;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<WireNotes>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Wire Notes").AddComponent<WireNotes>();
        }

        void Start()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            planet = FindFirstObjectByType<PlanetView>();
            placer = FindFirstObjectByType<PanelPlacer>();
        }

        bool MarkersOn => WireTool.Active || (placer != null && placer.Tile >= 0 && PanelPlacer.IsWireMode(placer.Mode));

        static string NameOf(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.SolarPanel: return "Solar panel";
                case BuildingType.FloatingSolar: return "Floating solar";
                case BuildingType.TidalTurbine: return "Tidal turbine";
                case BuildingType.WaveConverter: return "Wave converter";
                case BuildingType.WindTurbine: return "Wind turbine";
                case BuildingType.TidalBarrage: return "Tidal barrage";
                case BuildingType.Battery: return "Battery yard";
                case BuildingType.Workshop: return "Workshop";
                case BuildingType.ChargingStation: return "Charger";
                case BuildingType.Relay: return "Relay";
                case BuildingType.ResearchStation: return "Research Station";
                case BuildingType.AluminaPlant: return "Alumina plant";
                case BuildingType.Electrolyser: return "Electrolysis cell";
                default: return t.ToString();
            }
        }

        void Say(string text)
        {
            popup = text;
            popupUntil = Time.unscaledTime + 8f;
        }

        void Update()
        {
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim == null || !sim.WiredGrid || !GameFlow.IsPlaying)
            {
                unwired.Clear();
                knownReady = false;
                known.Clear();
                return;
            }
            if (Time.unscaledTime < nextScan)
                return;
            nextScan = Time.unscaledTime + 0.25f;
            unwired.Clear();
            unwired.AddRange(sim.UnwiredBuildings());
            if (!knownReady)
            {
                // what is already waiting when the game starts or loads is not news
                known.Clear();
                foreach (var b in unwired) known.Add((b.Tile, b.Slot));
                knownReady = true;
                lastOverload = KnownOverload(sim) ? 1e9 : 0.0;
                lastOffGrid = sim.GeneratorsOffGrid;
                return;
            }
            var now = new HashSet<(int, int)>();
            foreach (var b in unwired)
            {
                now.Add((b.Tile, b.Slot));
                if (known.Add((b.Tile, b.Slot)))
                {
                    bool output = WireRoles.IsOutput(b.Type);
                    Say($"{NameOf(b.Type)} needs a wire to {(output ? "give power: it makes nothing yet" : "get power: robots can't use it yet")}. " +
                        "Drag a Power line (Grid) onto it, and press X to wire tiles together.");
                }
            }
            known.IntersectWith(now);
            int off = sim.GeneratorsOffGrid;
            if (off > lastOffGrid)
                Say($"{off} generator{(off == 1 ? "" : "s")} stand on tiles the grid does not reach: press X and run wire tile by tile from the lander.");
            lastOffGrid = off;
            // only a line the colony can measure tells it that it is overloaded (electrical stage 7)
            double overload = KnownOverload(sim) ? 1e9 : 0.0;
            if (overload > 1e6 && lastOverload <= 1e6)
                Say("A metered wire is overloaded and wastes power: add a second wire, or a high-voltage line (opens at milestone 1).");
            lastOverload = overload;
        }

        static bool KnownOverload(IPlanetSimulation sim)
        {
            foreach (int t in sim.LineTiles())
            {
                LineReadout r = sim.LineReadout(t);
                if (r.Measured && r.Overloaded) return true;
            }
            return false;
        }

        void OnGUI()
        {
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim == null || !sim.WiredGrid || !GameFlow.IsPlaying)
                return;
            box ??= new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            outTag ??= Solid(new Color(0.16f, 0.38f, 0.85f, 0.95f));
            inTag ??= Solid(new Color(0.88f, 0.45f, 0.10f, 0.95f));
            if (Time.unscaledTime < popupUntil)
            {
                float w = Mathf.Min(620f, Screen.width - 40f);
                GUI.Box(new Rect(Screen.width / 2f - w / 2f, UiLayout.ClearBottomY - 132f, w, 56f), "⚠ " + popup, box);
            }
            if (!MarkersOn)
                return;
            Camera cam = Camera.main;
            if (cam == null || planet == null)
                return;
            bool inSite = placer != null && placer.Tile >= 0;
            var perTile = new Dictionary<int, (int In, int Out)>();
            foreach (var b in unwired)
            {
                bool output = WireRoles.IsOutput(b.Type);
                if (inSite && b.Tile == placer.Tile)
                {
                    bool okWorld = placer.TryGetBuildingWorld(b.Tile, b.Slot, out Vector3 world);
                    if (okWorld)
                        Mark(cam, world + (world - planet.transform.position).normalized * (Vector3.Distance(cam.transform.position, world) * 0.08f), (output ? "OUT " : "IN ") + NameOf(b.Type), output);
                }
                else
                {
                    perTile.TryGetValue(b.Tile, out var c);
                    perTile[b.Tile] = output ? (c.In, c.Out + 1) : (c.In + 1, c.Out);
                }
            }
            if (inSite)
                return;
            foreach (var kv in perTile)
            {
                var c = planet.Sphere.Centers[kv.Key];
                Vector3 world = planet.transform.TransformPoint(new Vector3(c.X, c.Y, c.Z) * planet.Radius * 1.03f);
                if (kv.Value.Out > 0) Mark(cam, world, $"OUT {kv.Value.Out}", true);
                if (kv.Value.In > 0) Mark(cam, world + cam.transform.up * 0.05f * Mathf.Max(1f, Vector3.Distance(cam.transform.position, world) * 0.1f), $"IN {kv.Value.In}", false);
            }
        }

        void Mark(Camera cam, Vector3 world, string text, bool output)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            if (s.z <= 0f) return;
            // hidden behind the planet: skip tiles on the far side
            if (planet != null && Vector3.Dot((world - planet.transform.position).normalized, (cam.transform.position - world).normalized) < -0.05f && !(placer != null && placer.Tile >= 0))
                return;
            float w = 24f + text.Length * 7.5f;
            GUI.Box(new Rect(s.x - w / 2f, Screen.height - s.y - 26f, w, 22f), (output ? "▶ " : "▼ ") + text, output ? outTag : inTag);
        }
    }
}

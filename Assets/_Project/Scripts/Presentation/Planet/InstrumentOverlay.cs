using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using Material = KardashevAscent.Simulation.Resources.Material;
using KardashevAscent.Simulation.Robots;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// The instrument overlay (UI information design, stage 6; F4): one toggle turns the planet into a readable schematic. Each tile that has something to
    /// report gets a small tag with its power state, what the robots on it are doing, and the knowledge state of its main ore (the same marks as the
    /// values in the windows). Every tag is read from the simulation; a tile with nothing to report gets none. Sets itself up after the scene loads.
    /// </summary>
    public sealed class InstrumentOverlay : MonoBehaviour
    {
        public const KeyCode ToggleKey = KeyCode.F4;
        public const int MaxTags = 160;

        SimulationRunner runner;
        PlanetView planet;
        GUIStyle tag;
        public static bool On { get; private set; }
        public static void Set(bool on) => On = on;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<InstrumentOverlay>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Instrument Overlay").AddComponent<InstrumentOverlay>();
        }

        void Start()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            planet = FindFirstObjectByType<PlanetView>();
        }

        void Update()
        {
            if (GameFlow.IsPlaying && ScreenInput.KeyDown(ToggleKey)) On = !On;
        }

        /// <summary>The tags of one tile, from what the simulation reports, or an empty list.</summary>
        public static List<string> TagsOf(IPlanetSimulation sim, int tile, HashSet<int> failedTiles)
        {
            var tags = new List<string>();
            if (sim.WiredGrid && sim.HasLineOn(tile, true))
            {
                LineReadout r = sim.LineReadout(tile);
                if (failedTiles.Contains(tile)) tags.Add("⚡ line FAILED");
                else if (r.Measured && r.Overloaded) tags.Add("⚡ OVERLOAD");
                else if (r.Measured) tags.Add("⚡ ok");
                else tags.Add("⚡ unmetered");
            }
            if (sim.WindTurbineCount(tile) > 0)
            {
                double kw = sim.WindTurbinePowerAtW(tile) / 1000.0;
                tags.Add(kw >= 0.5 ? $"wind {kw:0} kW" : "wind idle");
            }
            var robots = sim.Workforce.Robots.Where(x => x.Tile == tile).ToList();
            if (robots.Count > 0)
                tags.Add("R" + string.Join(",", robots.Select(x => x.Id)) + " " + string.Join("/", robots.Select(x => x.State.ToString().ToLowerInvariant()).Distinct()));
            foreach (var rb in robots)
            {
                var lad = sim.LadderOf(rb.Id);
                if (lad.State == KardashevAscent.Simulation.Game.LadderState.WaitingForHelp) tags.Add("⚠ R" + rb.Id + " rescue coming");
                else if (lad.State == KardashevAscent.Simulation.Game.LadderState.NeedsYou) tags.Add("⚠ R" + rb.Id + " NEEDS YOU");
            }
            if (sim.Deposits.IsSurveyed(tile))
            {
                Deposit best = default; bool any = false;
                foreach (Deposit d in sim.Deposits.Known(tile))
                    if (d.Material != Material.Rock && (!any || d.Grade > best.Grade)) { best = d; any = true; }
                if (any)
                {
                    var e = sim.GradeEstimate(tile, best.Material);
                    tags.Add(Marks.Glyph(Marks.Of(e)) + " " + Materials.Name(best.Material).ToLowerInvariant());
                }
            }
            return tags;
        }

        /// <summary>How bad a tag is, for picking the worst of a cluster: a failed or overloaded line, then an unmetered line or a value not yet measured, then the rest.</summary>
        public static int Severity(string tag) => tag.Contains("FAILED") || tag.Contains("OVERLOAD") ? 3 : tag.Contains("unmetered") || tag.Contains("◐") || tag.Contains("○") ? 1 : 0;

        /// <summary>
        /// Far away tiles are small and their tags pile up: group the tags by screen cell and show how many there are and the worst of them. Near, every tag
        /// shows in full (UI information design, finish round).
        /// </summary>
        public static List<(Vector2 Pos, string Text)> Cluster(IList<(Vector2 Pos, string Text)> tags, float cellPx)
        {
            var cells = new Dictionary<(int, int), List<(Vector2 Pos, string Text)>>();
            foreach (var t in tags)
            {
                var key = ((int)Mathf.Floor(t.Pos.x / cellPx), (int)Mathf.Floor(t.Pos.y / cellPx));
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<(Vector2, string)>();
                list.Add(t);
            }
            var result = new List<(Vector2, string)>();
            foreach (var list in cells.Values)
            {
                if (list.Count == 1) { result.Add(list[0]); continue; }
                var worst = list.OrderByDescending(x => Severity(x.Text)).First();
                Vector2 centre = Vector2.zero;
                foreach (var x in list) centre += x.Pos;
                result.Add((centre / list.Count, $"{list.Count} tags · worst: {worst.Text}"));
            }
            return result;
        }

        void OnGUI()
        {
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (!On || sim == null || !GameFlow.IsPlaying || planet == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            tag ??= new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            var failed = new HashSet<int>(sim.FailedLines().Select(f => f.Tile));
            // candidate tiles: lines, robots, surveyed ground
            var tiles = new HashSet<int>(sim.LineTiles());
            foreach (Robot r in sim.Workforce.Robots) tiles.Add(r.Tile);
            foreach (int t in sim.Deposits.Surveyed) tiles.Add(t);
            bool far = Vector3.Distance(cam.transform.position, planet.transform.position) > planet.Radius * 2.2f;
            var drawn = new List<(float Dist, Rect Box, string Text)>();
            var raw = new List<(Vector2 Pos, string Text)>();
            foreach (int t in tiles)
            {
                var c = planet.Sphere.Centers[t];
                Vector3 world = planet.transform.TransformPoint(new Vector3(c.X, c.Y, c.Z) * planet.Radius * 1.03f);
                Vector3 s = cam.WorldToScreenPoint(world);
                if (s.z <= 0f) continue;
                if (Vector3.Dot((world - planet.transform.position).normalized, (cam.transform.position - world).normalized) < -0.05f) continue;   // far side
                var tags = TagsOf(sim, t, failed);
                if (tags.Count == 0) continue;
                string text = string.Join(" · ", tags);
                if (far) { raw.Add((new Vector2(s.x, Screen.height - s.y), text)); continue; }
                float w = 16f + text.Length * 8f;
                drawn.Add((s.z, new Rect(s.x - w / 2f, Screen.height - s.y - 14f, w, 24f), text));
            }
            if (far)
                foreach (var c in Cluster(raw, 90f))
                {
                    float w = 16f + c.Text.Length * 8f;
                    drawn.Add((0f, new Rect(c.Pos.x - w / 2f, c.Pos.y - 14f, w, 24f), c.Text));
                }
            foreach (var d in drawn.OrderBy(x => x.Dist).Take(MaxTags))
                GUI.Box(d.Box, d.Text, tag);
            GUI.Box(new Rect(10f, UiLayout.ClearBottomY - 36f, 460f, 26f), "Instrument overlay (F4): ⚡ power · R robots · ●◐ ore knowledge", tag);
        }
    }
}

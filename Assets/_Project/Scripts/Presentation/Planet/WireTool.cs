using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// The wire tool (director 2026-11-20: "connect from tile to tile"). Press X (or the button in the Power window), click a
    /// tile, then the next tile along: a wire is run between the two, and the second tile becomes the start of the next one, so
    /// a path is drawn tile after tile, like drawing wire in Oxygen Not Included. A robot builds each wire where the grid is.
    /// Tab switches between the power line and the high-voltage line; X or Esc or the right mouse button stops. Sets itself up
    /// after the scene loads.
    /// </summary>
    public sealed class WireTool : MonoBehaviour
    {
        /// <summary>True while the tool is on (other panels can stay out of its way).</summary>
        public static bool Active { get; private set; }

        static WireTool instance;

        SimulationRunner runner;
        PlanetView planet;
        TilePicker picker;
        LineRenderer preview;
        int first = -1;
        bool highVoltage;
        /// <summary>0 power line, 1 high-voltage line, 2 AC 33 kV, 3 AC 500 kV.</summary>
        int lineKind;
        string message = "";
        float messageUntil;
        GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<WireTool>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Wire Tool").AddComponent<WireTool>();
        }

        /// <summary>Turn the tool on or off (the Power window button).</summary>
        public static void Toggle()
        {
            if (instance != null) instance.SetActive(!Active);
        }

        void Awake() => instance = this;

        void Start()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            planet = FindFirstObjectByType<PlanetView>();
            picker = FindFirstObjectByType<TilePicker>();
            if (picker != null)
                picker.SelectedTileChanged += OnSelected;
            var go = new GameObject("Wire Preview") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            preview = go.AddComponent<LineRenderer>();
            preview.positionCount = 2;
            preview.useWorldSpace = true;
            preview.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            preview.startColor = preview.endColor = new Color(1f, 1f, 1f, 0.85f);
            preview.widthMultiplier = 0.03f;
            preview.enabled = false;
        }

        void OnDestroy()
        {
            if (picker != null)
                picker.SelectedTileChanged -= OnSelected;
            if (instance == this) instance = null;
            Active = false;
        }

        void SetActive(bool on)
        {
            if (on && (runner == null || runner.Simulation == null || !runner.Simulation.WiredGrid || !GameFlow.IsPlaying))
                return;
            Active = on;
            first = -1;
            if (preview != null) preview.enabled = false;
            if (on && picker != null) picker.Select(-1);
            Say(on ? "Wire tool: click a tile, then the next tile along." : "");
        }

        void Say(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + 4f;
        }

        void Update()
        {
            if (ScreenInput.KeyDown(KeyCode.X) && GameFlow.IsPlaying && !GameFlow.InSetup)
                SetActive(!Active);
            if (!Active)
                return;
            if (ScreenInput.KeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                SetActive(false);
                return;
            }
            if (ScreenInput.KeyDown(KeyCode.Tab))
            {
                lineKind = (lineKind + 1) % 4;
                highVoltage = lineKind == 1;
                Say(lineKind == 0 ? "Power line (20 MW a tile)."
                    : lineKind == 1 ? "High-voltage line (1.5 GW a tile; opens at milestone 1)."
                    : lineKind == 2 ? "AC line, 33 kV, 50 Hz (34 MW a tile; opens at milestone 2). Needs a rectifier and an inverter to join the DC grid."
                    : "AC line, 500 kV, 50 Hz (2.6 GW a tile; opens at milestone 2). Needs a rectifier and an inverter to join the DC grid.");
            }
            UpdatePreview();
        }

        Vector3 TilePoint(int tile)
        {
            var c = planet.Sphere.Centers[tile];
            return planet.transform.TransformPoint(new Vector3(c.X, c.Y, c.Z) * planet.Radius * 1.02f);
        }

        void UpdatePreview()
        {
            int hover = picker != null ? picker.HoveredTile : -1;
            bool show = first >= 0 && hover >= 0 && hover != first;
            preview.enabled = show;
            if (!show) return;
            preview.SetPosition(0, TilePoint(first));
            preview.SetPosition(1, TilePoint(hover));
            Camera cam = Camera.main;
            float distance = cam != null ? Vector3.Distance(cam.transform.position, TilePoint(first)) : 10f;
            preview.widthMultiplier = Mathf.Clamp(distance * 0.006f, 0.003f, 0.03f);
        }

        void OnSelected(int tile)
        {
            if (!Active || tile < 0 || runner == null || runner.Simulation == null)
                return;
            IPlanetSimulation sim = runner.Simulation;
            if (first < 0)
            {
                first = tile;
                Say($"Tile {tile}: now click the next tile along.");
            }
            else if (tile != first)
            {
                if (lineKind >= 2 ? sim.PlaceAcWire(first, tile, lineKind == 2 ? 33000 : 500000) : sim.PlaceWire(first, tile, highVoltage))
                {
                    Say($"Wire run from tile {first} to tile {tile}: a robot builds it. Click the next tile to go on.");
                    first = tile;
                }
                else
                {
                    Say(sim.WireProblem ?? "No wire there.");
                    first = tile;   // start again from the tile just clicked
                }
            }
            // the tool owns the click: do not open the site of the tile
            if (picker != null) picker.Select(-1);
        }

        void OnGUI()
        {
            if (!Active && Time.unscaledTime > messageUntil)
                return;
            style ??= new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            string text = Active
                ? $"WIRE TOOL · {(lineKind == 0 ? "power" : lineKind == 1 ? "high-voltage" : lineKind == 2 ? "AC 33 kV" : "AC 500 kV")} line · click a tile, then the next tile along · Tab: switch line · X / Esc: stop · blue OUT = gives power, orange IN = takes power\\n{message}"
                : message;
            float height = Active ? 52f : 28f;
            GUI.Box(new Rect(Screen.width / 2f - 400f, UiLayout.ClearBottomY - height - 8f, 800f, height), text.Replace("\\n", "\n"), style);
        }
    }
}

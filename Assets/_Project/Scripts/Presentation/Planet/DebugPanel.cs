using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Robots;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Debug mode (F10): a small panel of testing switches that bypass the resource rules (free and instant building,
    /// unlimited materials, everything unlocked, full battery), a few shortcuts, and the god-mode ground map. Nothing
    /// here is saved. A "DEBUG" badge stays on screen once any switch has been used, so a test run is never mistaken
    /// for a played one. Sets itself up after the scene loads.
    /// </summary>
    public sealed class DebugPanel : MonoBehaviour
    {
        SimulationRunner runner;
        GodMode god;
        bool open;

        /// <summary>Whether the panel is showing (the F10 key toggles it).</summary>
        public bool Open { get => open; set => open = value; }
        Rect window = new Rect(20f, 80f, 270f, 10f);
        GUIStyle badge;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<DebugPanel>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Debug Panel").AddComponent<DebugPanel>();
        }

        void Start()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            god = FindFirstObjectByType<GodMode>();
        }

        void Update()
        {
            if (ScreenInput.KeyDown(KeyCode.F10))
                open = !open;
        }

        IDebugTools Tools => runner != null ? runner.Simulation as IDebugTools : null;

        void OnGUI()
        {
            IDebugTools tools = Tools;
            if (tools == null)
                return;
            if (tools.Debug.EverUsed || GodMode.Active)
            {
                badge ??= new GUIStyle(GUI.skin.box) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(Screen.width - 150f, 8f, 140f, 22f), tools.Debug.AnyOn || GodMode.Active ? "DEBUG MODE" : "DEBUG USED", badge);
            }
            if (open)
            {
                window.height = 10f;
                window = GUILayout.Window(7710, window, Draw, "Debug mode  (F10)");
            }
        }

        void Draw(int id)
        {
            IDebugTools tools = Tools;
            DebugOptions d = tools.Debug;
            GUILayout.Label("Bypass the resource rules. Not saved.");
            d.FreeBuilding = GUILayout.Toggle(d.FreeBuilding, " Free, instant building");
            d.InfiniteMaterials = GUILayout.Toggle(d.InfiniteMaterials, " Unlimited materials");
            d.UnlockAll = GUILayout.Toggle(d.UnlockAll, " Unlock everything");
            d.FullBattery = GUILayout.Toggle(d.FullBattery, " Battery always full");
            GUILayout.Space(6f);
            GUILayout.Label("Shortcuts");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1000 kg all")) tools.DebugAddMaterials(1000.0);
            if (GUILayout.Button("+ robot")) tools.DebugAddRobot(RobotKind.General);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Survey every tile")) tools.DebugSurveyAll();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Skip 1 day")) runner.Simulation.Tick(24.0);
            if (GUILayout.Button("Skip 10 days")) runner.Simulation.Tick(240.0);
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.Label("God view: ground map (every tile)");
            if (god == null) god = FindFirstObjectByType<GodMode>();
            if (god != null)
            {
                GUILayout.BeginHorizontal();
                for (int i = -1; i < GodMode.MapCount; i++)
                {
                    bool on = god.MapIndex == i;
                    if (GUILayout.Toggle(on, GodMode.MapName(i), GUI.skin.button) && !on)
                        god.SetMap(i);
                }
                GUILayout.EndHorizontal();
            }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }
}

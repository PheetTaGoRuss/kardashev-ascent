using System;
using System.Collections.Generic;
using System.IO;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace KardashevAscent.EditorTools
{
    /// <summary>
    /// Drives the editor from a text file, for developers and AI sessions that cannot click in the editor.
    /// Write commands, one per line, to <c>Temp/dev-driver.txt</c> in the project folder; each line is removed as it
    /// runs. Screenshots go to <c>Temp/shots/</c>. Nothing here ships in a build.
    /// <list type="bullet">
    /// <item><c>play</c> / <c>stop</c>: enter or leave Play mode.</item>
    /// <item><c>wait 2.5</c>: wait that many seconds.</item>
    /// <item><c>shot name</c>: save the Game view to Temp/shots/name.png (Play mode only).</item>
    /// <item><c>tab id</c>: open or close a system window by its id (power, manufacturing, robots ...).</item>
    /// <item><c>menu Kardashev Ascent/Set Up Scene</c>: run an editor menu item.</item>
    /// <item><c>speed 3</c>: set the game clock speed (0–4, as keys 1–5).</item>
    /// <item><c>tick 240</c>: advance the simulation by that many game hours at once.</item>
    /// <item><c>site</c>: fly the camera to the colony's base tile; <c>planet</c>: back out to the planet.</item>
    /// <item><c>click text</c>: press the first visible button whose text contains this.</item>
    /// <item><c>select N</c>: select slot N of the open site.</item>
    /// <item><c>refresh</c>: import changed files and recompile (works while the editor is in the background).</item>
    /// <item><c>tests</c>: run every EditMode test (stop Play mode first); see the TESTS line in the log.</item>
    /// <item><c>save</c>: save the open scene.</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    static class DevDriver
    {
        const string RequestPath = "Temp/dev-driver.txt";
        const string ShotFolder = "Temp/shots";
        const string WaitKey = "KardashevAscent.DevDriver.waitUntil";
        const string LogPath = "Temp/dev-driver.log";

        static DevDriver()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isCompiling)
                return;
            if (EditorApplication.timeSinceStartup < SessionState.GetFloat(WaitKey, 0f))
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                return;   // entering or leaving Play mode

            var lines = new List<string>(File.ReadAllLines(RequestPath));
            while (lines.Count > 0 && lines[0].Trim().Length == 0)
                lines.RemoveAt(0);
            if (lines.Count == 0)
            {
                File.Delete(RequestPath);
                return;
            }

            string line = lines[0].Trim();
            bool done;
            try
            {
                done = Run(line);
            }
            catch (Exception e)
            {
                Log($"FAILED {line}: {e.Message}");
                done = true;
            }
            if (!done)
                return;
            Log("ran " + line);
            lines.RemoveAt(0);
            if (lines.Count == 0) File.Delete(RequestPath);
            else File.WriteAllLines(RequestPath, lines);
        }

        static void Log(string text) => File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {text}\n");

        static void Wait(double seconds) =>
            SessionState.SetFloat(WaitKey, (float)(EditorApplication.timeSinceStartup + seconds));

        /// <summary>Run one command; false means "not finished, try again next update".</summary>
        static bool Run(string line)
        {
            int space = line.IndexOf(' ');
            string command = space < 0 ? line : line.Substring(0, space);
            string argument = space < 0 ? "" : line.Substring(space + 1).Trim();
            switch (command)
            {
                case "play":
                    if (EditorApplication.isPlaying) { Application.runInBackground = true; return true; }
                    EditorApplication.isPlaying = true;
                    return false;
                case "stop":
                    if (!EditorApplication.isPlaying) return true;
                    EditorApplication.isPlaying = false;
                    return false;
                case "wait":
                    Wait(double.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
                    return true;
                case "shot":
                    Directory.CreateDirectory(ShotFolder);
                    ScreenCapture.CaptureScreenshot($"{ShotFolder}/{argument}.png");
                    Wait(0.5);
                    return true;
                case "tab":
                    SystemTabs.Toggle(argument);
                    return true;
                case "menu":
                    if (!EditorApplication.ExecuteMenuItem(argument)) Log("no such menu: " + argument);
                    return true;
                case "speed":
                    { var r = Runner(); if (r != null && r.Clock != null) r.Clock.Speed = (Simulation.Game.ClockSpeed)int.Parse(argument); }
                    return true;
                case "tick":
                    Runner()?.Simulation?.Tick(double.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
                    return true;
                case "site":
                {
                    SimulationRunner runner = Runner();
                    var picker = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>();
                    if (runner?.Simulation != null && picker != null)
                        picker.Select(runner.Simulation.BaseTileId);
                    return true;
                }
                case "planet":
                    UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>()?.Select(-1);
                    return true;
                case "click":
                {
                    // Press the first visible UI Toolkit button whose text contains the argument.
                    foreach (var doc in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsSortMode.None))
                    {
                        if (doc.rootVisualElement == null) continue;
                        UnityEngine.UIElements.Button hit = null;
                        UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Button>(doc.rootVisualElement).ForEach(b =>
                        {
                            if (hit == null && b.resolvedStyle.display != UnityEngine.UIElements.DisplayStyle.None && b.worldBound.width > 0f
                                && b.text != null && b.text.IndexOf(argument, StringComparison.OrdinalIgnoreCase) >= 0)
                                hit = b;
                        });
                        if (hit == null) continue;
                        using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled())
                        {
                            e.target = hit;
                            hit.SendEvent(e);
                        }
                        return true;
                    }
                    Log("no button: " + argument);
                    return true;
                }
                case "build":
                {
                    // build panel 5  /  build battery 2  /  build workshop 1: fill free slots of the base tile.
                    string[] w = argument.Split(' ');
                    int count = w.Length > 1 ? int.Parse(w[1]) : 1;
                    var sim = Runner()?.Simulation;
                    if (sim == null) return true;
                    int tile = sim.BaseTileId, done = 0;
                    for (int slot = 0; slot < sim.SlotCount(tile) && done < count; slot++)
                    {
                        if (sim.GetBuilding(tile, slot) != Simulation.Game.BuildingType.None) continue;
                        bool ok = w[0] == "battery" ? sim.PlaceBattery(tile, slot)
                                : w[0] == "workshop" ? sim.PlaceWorkshop(tile, slot)
                                : w[0] == "charger" ? sim.PlaceChargingStation(tile, slot)
                                : sim.PlacePanel(tile, slot, 0.35, Math.PI);
                        if (ok) done++;
                    }
                    Log($"built {done} {w[0]}");
                    return true;
                }
                case "status":
                {
                    var sim = Runner()?.Simulation;
                    if (sim != null)
                        Log($"STATUS t={sim.TimeHours:0}h gen={sim.GenerationW:0}W demand={sim.DemandW:0}W battery={sim.BatteryStoredWh / 1e6:0.0}MWh brownout={sim.IsBrownout} K={sim.PlanetK:0.000} milestones={sim.MilestonesReached} iron={sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron]:0}kg queue={sim.ConstructionQueueCount} robots={sim.Workforce.Robots.Count} tidal={sim.TidalPowerW():0}W period={sim.TidePeriodHours:0.0}h moonM={sim.Moon.MassRatio:0.0000} moonD={sim.Moon.DistanceRadii:0}");
                    return true;
                }
                case "select":
                    UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.SiteView>()?.SetSelected(int.Parse(argument));
                    return true;
                case "nudge":
                {
                    // nudge 30 90: turn the open angle sliders by that many degrees of tilt and facing (what dragging the rings does).
                    string[] w = argument.Split(' ');
                    var pv = UnityEngine.Object.FindFirstObjectByType<PanelsView>();
                    Log("nudge " + (pv != null && pv.TryNudgeAngles(float.Parse(w[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture))));
                    return true;
                }
                case "place":
                    // Build in the selected slot the way a click and P would (a panel at the planned angles).
                    Log("place " + UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.PanelPlacer>()?.Place());
                    return true;
                case "selects":
                {
                    // selects 1 2 3: select several slots of the open site.
                    var slots = new List<int>();
                    foreach (string w in argument.Split(' ')) slots.Add(int.Parse(w));
                    UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.SiteView>()?.SetSelection(slots);
                    return true;
                }
                case "terrain":
                {
                    // terrain ShallowOcean: open the site of the first tile of that terrain.
                    var sim = Runner()?.Simulation;
                    var picker = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>();
                    var kind = (Simulation.Planet.TerrainType)Enum.Parse(typeof(Simulation.Planet.TerrainType), argument);
                    for (int t = 0; sim != null && picker != null && t < sim.TileCount; t++)
                        if (sim.GetTile(t).Terrain == kind) { picker.Select(t); Log("terrain tile " + t); break; }
                    return true;
                }
                case "experiment":
                {
                    // experiment TideGauge: order it at the station, and log what the order said and what is known.
                    var sim = Runner()?.Simulation;
                    var kind = (Simulation.Planet.ExperimentKind)Enum.Parse(typeof(Simulation.Planet.ExperimentKind), argument);
                    if (sim != null)
                        Log($"experiment {kind}: blocker={sim.ExperimentBlocker(kind) ?? "none"} ordered={sim.OrderExperiment(kind)} known={sim.IsExperimentKnown(kind)} result={sim.ExperimentResult(kind) ?? "-"}");
                    return true;
                }
                case "survey":
                {
                    var sim = Runner()?.Simulation;
                    if (sim != null) Log($"survey base tile {sim.BaseTileId}: ordered={sim.OrderSurvey(sim.BaseTileId)} surveyed={sim.Deposits.IsSurveyed(sim.BaseTileId)}");
                    return true;
                }
                case "known":
                {
                    var sim = Runner()?.Simulation;
                    var kind = (Simulation.Planet.ExperimentKind)Enum.Parse(typeof(Simulation.Planet.ExperimentKind), argument);
                    if (sim != null)
                        Log($"known {kind}={sim.IsExperimentKnown(kind)} station={sim.HasResearchStation} turbineBlocker={sim.KnowledgeBlocker(Simulation.Game.BuildingType.TidalTurbine) ?? "none"} result={sim.ExperimentResult(kind) ?? "-"}");
                    return true;
                }
                case "tide":
                {
                    // tide: open the site of the shallow-sea tile with the strongest tide.
                    var sim = Runner()?.Simulation;
                    var picker = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>();
                    int best = -1;
                    double most = -1.0;
                    for (int t = 0; sim != null && t < sim.TileCount; t++)
                        if (sim.GetTile(t).Terrain == Simulation.Planet.TerrainType.ShallowOcean && sim.TidalRangeAtM(t) > most) { most = sim.TidalRangeAtM(t); best = t; }
                    if (best >= 0 && picker != null) picker.Select(best);
                    Log("tide tile " + best + " range " + most);
                    return true;
                }
                case "mode":
                    UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.PanelPlacer>().Mode =
                        (Presentation.Planet.BuildMode)Enum.Parse(typeof(Presentation.Planet.BuildMode), argument);
                    return true;
                case "debug":
                    // "debug" opens the debug panel; "debug free|material|unlock|battery" flips one switch.
                    {
                        var panel = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.DebugPanel>();
                        var tools = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>().Simulation as Simulation.Game.IDebugTools;
                        if (argument == "free") tools.Debug.FreeBuilding = true;
                        else if (argument == "material") tools.Debug.InfiniteMaterials = true;
                        else if (argument == "unlock") tools.Debug.UnlockAll = true;
                        else if (argument == "battery") tools.Debug.FullBattery = true;
                        else if (argument == "tides") tools.Debug.StrongTides = true;
                        else if (argument == "surveyall") tools.DebugSurveyAll();
                        panel.Open = true;
                        return true;
                    }
                case "orbit":
                    UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.OrbitView>().Open = argument != "off";
                    return true;
                case "refresh":
                    // Import changed files and recompile without the editor being in front.
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    return true;
                case "tests":
                {
                    // Run every EditMode test in this editor; the result goes to the log (TESTS passed=... failed=...).
                    var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                    api.RegisterCallbacks(new TestLog());
                    api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
                    return true;
                }
                case "save":
                    UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                    return true;
                default:
                    Log("unknown command: " + line);
                    return true;
            }
        }

        sealed class TestLog : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { Log("tests started"); }
            public void RunFinished(ITestResultAdaptor result) { Log($"TESTS passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} inconclusive={result.InconclusiveCount}"); }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                    Log("FAILED " + result.FullName + " :: " + result.Message);
            }
        }

        static SimulationRunner Runner() => UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
    }
}

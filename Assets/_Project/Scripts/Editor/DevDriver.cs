using System;
using System.Collections.Generic;
using System.IO;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using UnityEditor;
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
                case "save":
                    UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                    return true;
                default:
                    Log("unknown command: " + line);
                    return true;
            }
        }

        static SimulationRunner Runner() => UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
    }
}

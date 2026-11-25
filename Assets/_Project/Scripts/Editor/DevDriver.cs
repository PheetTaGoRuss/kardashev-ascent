using System;
using System.Collections.Generic;
using System.IO;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.UIElements;

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
            // a PlayMode run reloads the scripts, so the result logger is registered on every load while a run is pending
            if (SessionState.GetBool("DevDriver.PlayTestsPending", false))
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(new TestLog(() => SessionState.SetBool("DevDriver.PlayTestsPending", false)));
            }
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
                case "land":
                {
                    // land: put the lander on the buildable land tile nearest the equator (or on tile N when given).
                    SimulationRunner runner = Runner();
                    var sim = runner?.Simulation;
                    if (sim == null) return true;
                    if (int.TryParse(argument, out int given)) { Log($"land tile={given} -> {runner.LandAt(given, true) ?? "ok"}"); return true; }
                    // otherwise try buildable land tiles from the equator outward until the lander accepts one
                    var candidates = new List<int>();
                    for (int t = 0; t < sim.TileCount; t++) if (sim.GetTile(t).IsBuildableLand) candidates.Add(t);
                    candidates.Sort((x, y) => Math.Abs(sim.GetTile(x).Latitude).CompareTo(Math.Abs(sim.GetTile(y).Latitude)));
                    foreach (int t in candidates)
                    {
                        string why = runner.LandAt(t, true);
                        if (string.IsNullOrEmpty(why)) { Log($"land tile={t} ok"); return true; }
                    }
                    Log("land: no tile accepted");
                    return true;
                }
                case "acchain":
                {
                    // acchain: lander - DC - rectifier - AC 33 kV - transformer - AC 500 kV - transformer - AC 33 kV - inverter, along a land path from the base.
                    var runner = Runner();
                    var sim = runner?.Simulation;
                    if (sim == null) return true;
                    if (sim is Simulation.Game.IDebugTools dbg) { dbg.Debug.FreeBuilding = true; dbg.Debug.UnlockAll = true; }
                    var path = new List<int> { sim.BaseTileId };
                    while (path.Count < 6)
                    {
                        int last = path[path.Count - 1], next = -1;
                        foreach (int n in sim.Sphere.Neighbors[last])
                        {
                            if (path.Contains(n) || sim.GetTile(n).IsWater || !sim.IsOpenGround(n)) continue;
                            bool touches = false;
                            foreach (int p in path) if (p != last && System.Array.IndexOf(sim.Sphere.Neighbors[p], n) >= 0) touches = true;
                            if (!touches) { next = n; break; }
                        }
                        if (next < 0) { Log("acchain: no land path"); return true; }
                        path.Add(next);
                    }
                    int FreeSlot(int tile, Simulation.Game.BuildingType type)
                    {
                        for (int s = 0; s < sim.SlotCount(tile); s++) if (sim.CanPlace(type, tile, s)) return s;
                        return -1;
                    }
                    bool Conv(Simulation.Game.BuildingType type, int tile) { int s = FreeSlot(tile, type); return s >= 0 && sim.PlaceStorage(type, tile, s); }
                    var why = new List<string>();
                    bool Step(bool placed) { why.Add(placed ? "ok" : (sim.WireProblem ?? "no")); return placed; }
                    var ok = new List<bool>
                    {
                        Step(sim.PlaceWire(path[0], path[1], false)),
                        Conv(Simulation.Game.BuildingType.Rectifier, path[1]),
                        Step(sim.PlaceAcWire(path[1], path[2], 33000)),
                        Conv(Simulation.Game.BuildingType.Transformer, path[2]),
                        sim.PlaceAcWire(path[2], path[3], 500000),
                        Conv(Simulation.Game.BuildingType.Transformer, path[3]),
                        sim.PlaceAcWire(path[3], path[4], 33000),
                        sim.PlaceAcWire(path[4], path[5], 33000),
                        Conv(Simulation.Game.BuildingType.Inverter, path[5]),
                    };
                    Log($"acchain path={string.Join(",", path)} placed={string.Join("", ok.ConvertAll(b => b ? '1' : '0'))} why={string.Join(" | ", why)} unlocked={sim.KnowledgeBlocker(Simulation.Game.BuildingType.Rectifier) ?? "yes"} wired={sim.WiredGrid}");
                    var picker = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>();
                    if (picker != null) picker.Select(path[3]);
                    return true;
                }
                case "fitpuzzle":
                {
                    // fitpuzzle <ExperimentKind>: pretend the experiment's robot hours finished, so its fit puzzle appears.
                    var sim = Runner()?.Simulation;
                    if (sim == null || !Enum.TryParse(argument, true, out KardashevAscent.Simulation.Planet.ExperimentKind kind)) { Log("fitpuzzle: no sim or kind"); return true; }
                    const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                    sim.GetType().GetMethod("LearnExperimentForTests", F).Invoke(sim, new object[] { kind });
                    sim.GetType().GetMethod("OnExperimentFinished", F).Invoke(sim, new object[] { kind });
                    Log("fit puzzle pending: " + kind);
                    return true;
                }
                case "logistics":
                {
                    // logistics: survey a few tiles, order mining, then log depot advice, a far haul's staging, contour against straight routes, and mine rate estimates.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) { Log("logistics: no sim"); return true; }
                    int lander = sim.BaseTileId;
                    var land = new System.Collections.Generic.List<int>();
                    for (int t = 0; t < sim.TileCount; t++)
                        if (!sim.GetTile(t).IsWater && t != lander && sim.Routes.HoursBetween(lander, t) > 8.0 && sim.Routes.HoursBetween(lander, t) < 90.0 && sim.Deposits.GradeOf(t, KardashevAscent.Simulation.Resources.Material.Iron) > 0.0) land.Add(t);
                    land.Sort((x, y) => sim.TileHeightM(y).CompareTo(sim.TileHeightM(x)));
                    for (int i = 0; i < Math.Min(4, land.Count); i++) { sim.Deposits.MarkSurveyed(land[i]); sim.OrderMining(land[i], KardashevAscent.Simulation.Resources.Material.Iron); }
                    Log($"logistics: {land.Count} reachable iron tiles; lander {lander}; budget {sim.LegBudgetWh:0} Wh");
                    foreach (var d in sim.DepotSites(3)) Log($"depot advice: tile {d.Tile} saves {d.SavedWhPerDay:0} Wh/day");
                    if (land.Count > 0)
                    {
                        int far = land[0];
                        Log($"mine tile {far}: rate {sim.MiningRateEstimate(far, KardashevAscent.Simulation.Resources.Material.Iron).Say("0.#", "kg/h")}; grade {sim.GradeEstimate(far, KardashevAscent.Simulation.Resources.Material.Iron).Say("0.00", "")}");
                        Log("haul " + lander + "->" + far + ": " + sim.HaulCostText(lander, far, 400.0));
                        double bestGain = 0.0; int bestTile = -1;
                        foreach (int t in land)
                        {
                            sim.ContourRouting = true; double ec = sim.HaulLegCost(lander, t, 0.0).ClimbWh, hc = sim.Routes.HoursBetween(lander, t);
                            sim.ContourRouting = false; double es = sim.HaulLegCost(lander, t, 0.0).ClimbWh, hs = sim.Routes.HoursBetween(lander, t);
                            if (es - ec > bestGain) { bestGain = es - ec; bestTile = t; Log($"contour vs straight to tile {t}: climb {ec:0} Wh vs {es:0} Wh, hours {hc:0.#} vs {hs:0.#}"); }
                        }
                        sim.ContourRouting = true;
                        // a refused haul, then staged through a depot with a charging bay on its route (free building, instant)
                        int staged = -1;
                        foreach (int t in System.Linq.Enumerable.OrderBy(land, x => sim.Routes.HoursBetween(lander, x)))
                            if (!sim.PlanHaul(lander, t, 400.0).Feasible && sim.Routes.HoursBetween(lander, t) < 70.0) { staged = t; break; }
                        if (staged >= 0)
                        {
                            Log("refused: " + lander + "->" + staged + ": " + sim.HaulCostText(lander, staged, 400.0));
                            sim.ConstructionTimeEnabled = false; sim.Debug.FreeBuilding = true; sim.Debug.UnlockAll = true;
                            var route = sim.Routes.Route(lander, staged);
                            int placed = 0;
                            // a charger needs power: lay a line along the route first (the wired grid; free building)
                            { int prevTile = lander; foreach (int rt in route) { sim.PlaceWire(prevTile, rt, false); prevTile = rt; } sim.Tick(0.01); }
                            for (int k = route.Count / 3; k < route.Count && placed < 2; k += Math.Max(1, route.Count / 3))
                                for (int j = k; j < route.Count && j < k + 4; j++)
                                {
                                    if (!sim.IsOpenGround(route[j])) continue;
                                    bool yard = false;
                                    for (int slot = 0; slot < sim.SlotCount(route[j]) && !yard; slot++) yard = sim.PlaceStorage(KardashevAscent.Simulation.Game.BuildingType.StorageYard, route[j], slot);
                                    if (!yard) continue;
                                    sim.Tick(0.01);
                                    bool bay = false;
                                    for (int slot = 0; slot < sim.SlotCount(route[j]) && !bay; slot++) bay = sim.PlaceChargingBay(route[j], slot);
                                    Log($"tile {route[j]}: yard placed, bay {(bay ? "placed" : "refused: " + sim.ChargingBayBlocker(route[j]))}");
                                    if (bay) { placed++; break; }
                                }
                            sim.Debug.FreeBuilding = false;
                            Log($"bays placed: {placed}");
                            sim.Tick(0.01);
                            Log("staged: " + lander + "->" + staged + ": " + sim.HaulCostText(lander, staged, 400.0));
                        }
                        int shown = 0;
                        foreach (int t in System.Linq.Enumerable.OrderBy(land, x => sim.Routes.HoursBetween(lander, x)))
                        {
                            if (sim.Routes.HoursBetween(lander, t) < 6.0 || shown >= 2) continue;
                            var (d2, s2) = sim.BestHaulStart(lander, t, 400.0);
                            if (d2 > 0.0) { Log($"short haul to tile {t} ({sim.Routes.HoursBetween(lander, t):0} h one way): {sim.HaulCostText(lander, t, 400.0)}"); shown++; }
                        }
                        var (dl, sv) = sim.BestHaulStart(lander, land[0], 400.0);
                        Log($"night vs day: best start in {dl:0} h saves {sv:0} Wh of heater on the haul to tile {land[0]}; air now {sim.DiurnalAirTemperatureAt(lander, sim.TimeHours):0.0} C, in 12 h {sim.DiurnalAirTemperatureAt(lander, sim.TimeHours + sim.DayLengthHours / 2.0):0.0} C");
                    }
                    return true;
                }
                case "guide":
                    // guide <page title>: open the Guide at a page.
                    Presentation.UI.GuideWindow.Open(argument);
                    return true;
                case "flyback":
                {
                    var cam = UnityEngine.Object.FindFirstObjectByType<Presentation.CameraControl.OrbitCamera>();
                    if (cam != null) { cam.LeaveSite(); cam.ResetView(true); }
                    return true;
                }
                case "savegame":
                    UnityEngine.Object.FindFirstObjectByType<Presentation.Game.SaveGameController>()?.Save();
                    return true;
                case "layoutinfo":
                    foreach (var doc in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsSortMode.None))
                    {
                        var root = doc.rootVisualElement; if (root == null) continue;
                        var l = root.Q("left"); var r = root.Q(className: "tile-info");
                        Log($"doc {doc.name}: root {root.worldBound} left {(l == null ? "none" : l.worldBound + " disp=" + l.resolvedStyle.display + " op=" + l.resolvedStyle.opacity)} right {(r == null ? "none" : r.worldBound + " disp=" + r.resolvedStyle.display + " op=" + r.resolvedStyle.opacity)}");
                        root.Query(className: "workforce").ForEach(w => Log("  window " + w.name + " " + w.worldBound + " pinned=" + w.ClassListContains("drag-pinned") + " maxW=" + w.resolvedStyle.maxWidth));
                    }
                    return true;
                case "emptybattery":
                {
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    sim?.ForceBrownoutForTests();
                    return true;
                }
                case "heavyhaul":
                {
                    // heavyhaul: put a haul to a tile too far for one charge into the book (reflection: no public way), so the planner marks it waiting.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    int lander = sim.BaseTileId, far = -1;
                    for (int t = 0; t < sim.TileCount && far < 0; t++)
                        if (!sim.GetTile(t).IsWater && t != lander && !double.IsInfinity(sim.Routes.HoursBetween(lander, t)) && sim.Routes.HoursBetween(lander, t) > 30.0 && !sim.PlanHaul(lander, t, 400.0).Feasible) far = t;
                    const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    object book = sim.GetType().GetField("haulBook", F)?.GetValue(sim);
                    book?.GetType().GetMethod("Add")?.Invoke(book, new object[] { lander, far, KardashevAscent.Simulation.Resources.Material.Iron, 400.0 });
                    Log("heavy haul to tile " + far + " added: " + (book != null));
                    return true;
                }
                case "weather":
                {
                    // weather [days]: print the base's weather now; "weather storm" advances (hour steps) to the next storm at the base.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    if (argument == "storm") { for (int i = 0; i < 24 * 365 * 3 && !sim.StormAt(sim.BaseTileId); i++) sim.Tick(1.0); }
                    var w = sim.WeatherAt(sim.BaseTileId);
                    Log($"weather at base: cloud {w.CloudFactor:0.00} air {w.AirC:0.0} C pressure {w.PressurePa / 1000.0:0.00} kPa wind {w.WindMs:0.0} m/s dust tau {w.DustTau:0.00} storm {w.Storm} dust storm {w.DustStorm} (threshold {sim.StormThresholdMs(sim.BaseTileId):0.0}) day {sim.TimeHours / 24.0:0}");
                    return true;
                }
                case "weathermap":
                    Presentation.Planet.WeatherOverlay.Set(argument != "off");
                    return true;
                case "stormrobot":
                {
                    // stormrobot: put a robot on a far land tile, then step the colony until a wind storm is over that tile; the robot should shelter.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    sim.Debug.FullBattery = true;
                    int far = -1;
                    for (int t = 0; t < sim.TileCount && far < 0; t++) if (!sim.GetTile(t).IsWater && sim.Routes.HoursBetween(sim.BaseTileId, t) > 8.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 20.0) far = t;
                    int h = 0;
                    for (; h < 24 * 365 * 3 && !sim.StormAt(far); h++) sim.Tick(1.0);
                    var r = sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.General, far);   // the robot arrives as the storm does
                    sim.Tick(0.01);
                    Log($"storm robot {r.Id}: far tile {far} after {h} h storm {sim.StormAt(far)} hold {r.StormHold} target {r.ManualTarget} tile {r.Tile}");
                    return true;
                }
                case "dustwatch":
                {
                    // dustwatch: step the colony until a dust WATCH entry (dusty season or global storm) is in the log.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    int h = 0;
                    for (; h < 24 * 700 * 3 && !System.Linq.Enumerable.Any(sim.OperationsLog, r => r.Quantity == "Dusty season"); h++) sim.Tick(1.0);
                    Log("dustwatch after " + h + " h: " + System.Linq.Enumerable.FirstOrDefault(sim.OperationsLog, r => r.Quantity == "Dusty season")?.Text);
                    return true;
                }
                case "windturbine":
                {
                    // windturbine: learn the wind mast, put a turbine on an open land tile next to the lander, step to a windy day and log its status.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    // Internal to the Simulation assembly (visible to tests only), so reach it by reflection like "fitpuzzle" does.
                    sim.GetType().GetMethod("LearnExperimentForTests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(sim, new object[] { KardashevAscent.Simulation.Planet.ExperimentKind.WindMast });
                    sim.Debug.FreeBuilding = true;
                    int tile = -1;
                    foreach (int n in sim.Sphere.Neighbors[sim.BaseTileId]) if (!sim.GetTile(n).IsWater && sim.IsOpenGround(n)) { tile = n; break; }
                    bool ok = tile >= 0 && sim.PlaceWindTurbine(tile, 0);
                    sim.Debug.FreeBuilding = false;
                    if (argument == "idle") { for (int h = 0; h < 24 * 400 && sim.WindTurbinePowerAtW(tile) > 1.0; h++) sim.Tick(1.0); }
                    else for (int h = 0; h < 24 * 60 && sim.WindTurbinePowerAtW(tile) < 20000.0; h++) sim.Tick(1.0);
                    Log($"windturbine placed={ok} tile={tile} status={(tile >= 0 ? sim.WindTurbineStatus(tile) : "-")} windPowerW={sim.WindPowerW():0}");
                    return true;
                }
                case "knowdemo":
                {
                    // knowdemo: a first-measured report (air pressure, experiment and fit), a discrepancy (two measured gravity records far apart) and a stale value (a 40-day-old hub wind record).
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    sim.GetType().GetMethod("LearnExperimentForTests", flags).Invoke(sim, new object[] { KardashevAscent.Simulation.Planet.ExperimentKind.AirPressure });
                    sim.GetType().GetMethod("OnExperimentFinished", flags).Invoke(sim, new object[] { KardashevAscent.Simulation.Planet.ExperimentKind.AirPressure });
                    for (int h = 0; h < 26; h++) sim.Tick(1.0);
                    sim.AnswerFitPuzzle(KardashevAscent.Simulation.Planet.ExperimentKind.AirPressure, sim.PuzzleFor(KardashevAscent.Simulation.Planet.ExperimentKind.AirPressure).Best);
                    var kb = sim.KnowledgeStore; double t = sim.TimeHours;
                    var planet = KardashevAscent.Simulation.Knowledge.Subject.Planet;
                    kb.Add(new KardashevAscent.Simulation.Knowledge.Observation(planet, KardashevAscent.Simulation.Knowledge.Quantity.SurfaceGravity, 9.8, 0.05, KardashevAscent.Simulation.Knowledge.Confidence.High, new KardashevAscent.Simulation.Knowledge.Source(KardashevAscent.Simulation.Knowledge.SourceKind.MeasuredByColony), t, t), t);
                    kb.Add(new KardashevAscent.Simulation.Knowledge.Observation(planet, KardashevAscent.Simulation.Knowledge.Quantity.SurfaceGravity, 11.4, 0.05, KardashevAscent.Simulation.Knowledge.Confidence.High, new KardashevAscent.Simulation.Knowledge.Source(KardashevAscent.Simulation.Knowledge.SourceKind.Robot, 2), t + 0.5, t + 0.5), t + 0.5);
                    var baseTile = KardashevAscent.Simulation.Knowledge.Subject.Tile(sim.BaseTileId);
                    kb.Add(new KardashevAscent.Simulation.Knowledge.Observation(baseTile, KardashevAscent.Simulation.Knowledge.Quantity.HubWindSpeed, 6.1, 0.5, KardashevAscent.Simulation.Knowledge.Confidence.High, new KardashevAscent.Simulation.Knowledge.Source(KardashevAscent.Simulation.Knowledge.SourceKind.MeasuredByColony), t - 24.0 * 40.0, t), t);
                    for (int h = 0; h < 26; h++) sim.Tick(1.0);
                    Log("knowdemo: " + string.Join(" | ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(sim.OperationsLog, o => o.Priority >= KardashevAscent.Simulation.Operations.LogPriority.Info && (o.Quantity == "Measured" || o.Quantity == "Discrepancy" || o.Quantity == "Value stale" || o.Quantity == "Range narrowed")), o => o.Quantity + ": " + o.Text)));
                    return true;
                }
                case "soakdemo":
                {
                    // soakdemo: every other robot is run dry too, one is stranded away from the lander: no helper exists, so it hibernates and cold-soaks; each call steps 4 h and logs its body temperature.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    if (argument == "start")
                    {
                        sim.RescueEnabled = false;   // no helper at all
                        foreach (var o in System.Linq.Enumerable.ToList(sim.Workforce.Robots)) o.Battery.Draw(1e12, 1.0);
                        int tile = -1;
                        foreach (int n in sim.Sphere.Neighbors[sim.BaseTileId]) if (!sim.GetTile(n).IsWater && sim.IsOpenGround(n)) { tile = n; break; }
                        var r = sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.General, tile);
                        r.Battery.Draw(1e12, 1.0);
                    }
                    for (int h = 0; h < 8; h++) sim.Tick(0.5);
                    Log("soakdemo: " + string.Join(" | ", System.Linq.Enumerable.Select(sim.Workforce.Robots, x => "R" + x.Id + " tile " + x.Tile + " body " + x.BodyTempC.ToString("0.0") + " C hib=" + x.Hibernating + " left=" + x.HibernationHoursLeft.ToString("0.0") + " h :: " + sim.LadderOf(x.Id).Text)) + " destroyed=" + sim.RobotsDestroyed + " :: " + string.Join(" || ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(sim.OperationsLog, o => o.Quantity == "Robot destroyed" || o.Quantity == "Rescue impossible"), o => o.Text)));
                    return true;
                }
                case "rescuedemo":
                {
                    // rescuedemo [jump|tow|stage]: a General robot one tile from the lander is run dry; "jump" or "tow" forces the method; each call steps 1 h and logs the ladder.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    if (argument == "jump") sim.RescueMethodOverride = KardashevAscent.Simulation.Game.RescueMethod.Jump;
                    if (argument == "tow") sim.RescueMethodOverride = KardashevAscent.Simulation.Game.RescueMethod.Tow;
                    if (argument == "jump" || argument == "tow")
                    {
                        int tile = -1;
                        foreach (int n in sim.Sphere.Neighbors[sim.BaseTileId]) if (!sim.GetTile(n).IsWater && sim.IsOpenGround(n)) { tile = n; break; }
                        var r = sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.General, tile);
                        r.Battery.Draw(1e12, 1.0);
                    }
                    for (int h = 0; h < 12; h++) sim.Tick(0.5);
                    string states = string.Join(" | ", System.Linq.Enumerable.Select(sim.Workforce.Robots, x => "R" + x.Id + "@" + x.Tile + " " + (int)(x.Battery.StateOfCharge * 100) + "% " + sim.LadderOf(x.Id).Text));
                    Log($"rescuedemo requested={sim.RescuesRequested} done={sim.RescuesSucceeded} jump={sim.RescuesByJump} tow={sim.RescuesByTow} orders={sim.Rescues.Count} :: {states} :: " + string.Join(" || ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(sim.OperationsLog, o => o.Quantity.StartsWith("Rescue")), o => o.Text)));
                    return true;
                }
                case "chemlog":
                {
                    // chemlog: an iron pile on an open depot tile and sodium in a wreck, five days on: the chemistry log entries and the stores chain.
                    var sim = Runner()?.Simulation as KardashevAscent.Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    int tile = -1;
                    foreach (int n in sim.Sphere.Neighbors[sim.BaseTileId]) if (!sim.GetTile(n).IsWater) { tile = n; break; }
                    var inv = (KardashevAscent.Simulation.Logistics.InventoryBook)sim.GetType().GetProperty("InventoriesForTests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(sim);
                    inv.Add(KardashevAscent.Simulation.Logistics.PlaceId.Depot(tile), KardashevAscent.Simulation.Resources.Material.Iron, 3000.0);
                    var r = sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.General, tile);
                    var w = (KardashevAscent.Simulation.Game.Wreck)sim.GetType().GetMethod("DestroyForTests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(sim, new object[] { r.Id, 10.0 });
                    inv.Add(KardashevAscent.Simulation.Logistics.PlaceId.Wreck(w.Id), KardashevAscent.Simulation.Resources.Material.Sodium, 50.0);
                    for (int h = 0; h < 24 * 5; h++) sim.Tick(1.0);
                    Log("chemlog notes: " + string.Join("; ", sim.StorageNotes(tile)) + " | log: " + string.Join(" | ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(sim.OperationsLog, o => o.Quantity == "Reactive metal exposed" || o.Quantity == "Storage loss"), o => o.Quantity + ": " + o.Text)));
                    return true;
                }
                case "overlay":
                    Presentation.Planet.InstrumentOverlay.Set(argument != "off");
                    return true;
                case "uiscale":
                {
                    // uiscale <0.8..1.5>: set the UI size (saved like the player's own setting).
                    if (float.TryParse(argument, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sc)) Presentation.UI.UiScale.Set(sc);
                    return true;
                }
                case "coldrobot":
                {
                    // coldrobot: a General robot on a land tile away from the lander, battery emptied; logs its state. "coldrobot status" logs the robots.
                    var sim = Runner()?.Simulation;
                    if (sim == null) return true;
                    if (argument == "status")
                    {
                        foreach (var r in sim.Workforce.Robots)
                            Log($"robot {r.Id} tile={r.Tile} heater={r.HeaterW:0}W sheltered={r.Sheltered} usableWh={r.Battery.UsableWh:0.0} exposedEmptyH={r.ExposedEmptyHours:0.00}");
                        Log($"robots={sim.Workforce.Robots.Count} destroyed={((Simulation.Game.PlanetSimulation)sim).RobotsDestroyed}");
                        return true;
                    }
                    int far = -1;
                    for (int t = 0; t < sim.TileCount && far < 0; t++)
                        if (t != sim.BaseTileId && !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && System.Array.IndexOf(sim.Sphere.Neighbors[sim.BaseTileId], t) < 0
                            && System.Array.IndexOf(sim.Sphere.Neighbors[sim.BaseTileId], t) < 0)
                            far = t;
                    var added = sim.Workforce.AddRobot(Simulation.Robots.RobotKind.General, far);
                    if (argument != "warm") added.Battery.Draw(1e12, 1.0);
                    else added.Battery.Draw(added.Battery.UsableWh * 0.97 * 1.0, 1.0);   // nearly flat: it will not last the night
                    Log($"coldrobot added robot {added.Id} on tile {far}, battery {(argument == "warm" ? "nearly flat" : "emptied")}");
                    return true;
                }
                case "acstatus":
                {
                    var sim = Runner()?.Simulation;
                    if (sim == null) return true;
                    int gen = int.Parse(argument);
                    Log($"acstatus tile={gen} powered={sim.IsPowered(gen)} conversion={sim.ConversionEfficiencyFrom(gen):0.0000} grid={sim.GridEfficiency(gen):0.0000} converterLossW={sim.ConverterLossW:0}");
                    return true;
                }
                case "planet":
                    UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>()?.Select(-1);
                    return true;
                case "scrollto":
                {
                    // Scroll the window that holds the first visible label containing the argument so that label shows.
                    foreach (var doc in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsSortMode.None))
                    {
                        if (doc.rootVisualElement == null) continue;
                        UnityEngine.UIElements.Label hit = null;
                        UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(doc.rootVisualElement).ForEach(l =>
                        {
                            if (hit == null && l.resolvedStyle.display != UnityEngine.UIElements.DisplayStyle.None && l.text != null && l.text.IndexOf(argument, StringComparison.OrdinalIgnoreCase) >= 0)
                                hit = l;
                        });
                        if (hit == null) continue;
                        var sv = hit.GetFirstAncestorOfType<UnityEngine.UIElements.ScrollView>();
                        sv?.ScrollTo(hit);
                        return true;
                    }
                    Log("no label: " + argument);
                    return true;
                }
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
                        Log($"STATUS t={sim.TimeHours:0}h gen={sim.GenerationW:0}W demand={sim.DemandW:0}W battery={sim.BatteryStoredWh / 1e6:0.0}MWh brownout={sim.IsBrownout} K={sim.PlanetK:0.000} milestones={sim.MilestonesReached} iron={sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron]:0}kg queue={sim.ConstructionQueueCount} robots={sim.Workforce.Robots.Count} tidal={sim.TidalPowerW():0}W waves={sim.WavePowerW():0}W wired={sim.WiredGrid} poweredTiles={sim.PoweredTileCount} offGrid={sim.GeneratorsOffGrid} barrage={sim.BarragePowerW():0}W period={sim.TidePeriodHours:0.0}h moonM={sim.Moon.MassRatio:0.0000} moonD={sim.Moon.DistanceRadii:0}");
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
                case "robots":
                {
                    var sim = Runner()?.Simulation;
                    if (sim != null)
                        foreach (var r in sim.Workforce.Robots)
                            Log($"ROBOT {r.Id} {r.Kind} tile={r.Tile} activity={r.Activity} task={(r.Task.HasValue ? r.Task.Value.Kind + "@" + r.Task.Value.Tile : "-")} battery={r.Battery.StoredWh:0}Wh stranded={r.IsStranded} charger={r.ChargerExists} connected={r.Connected} cond={r.Condition:0.00} program={sim.Workforce.EffectiveProgram(r)} surveyPriority={sim.Workforce.ProgramPriority(r, KardashevAscent.Simulation.Robots.TaskKind.Survey)} buildPriority={sim.Workforce.ProgramPriority(r, KardashevAscent.Simulation.Robots.TaskKind.Build)} available={r.Available} shutdown={r.IsShutDown} milestones={sim.MilestonesReached}");
                    return true;
                }
                case "reach":
                {
                    var sim = Runner()?.Simulation;
                    int inRange = 0, far = 0, cut = 0, land = 0;
                    for (int t = 0; sim != null && t < sim.TileCount; t++)
                    {
                        if (sim.GetTile(t).IsWater) continue;
                        land++;
                        string why = sim.WorkReachProblem(t);
                        if (why == null) inRange++;
                        else if (why.StartsWith("Out of range")) far++;
                        else cut++;
                    }
                    Log($"REACH land={land} inRange={inRange} outOfRange={far} cutOff={cut} base={sim?.BaseTileId}");
                    return true;
                }
                case "wire":
                {
                    // wire <Terrain>: power lines along the shortest land path from the base to the nearest tile of that terrain; opens its site.
                    var sim = Runner()?.Simulation;
                    var picker = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>();
                    string[] wireArgs = argument.Split(' ');
                    var kind = (Simulation.Planet.TerrainType)Enum.Parse(typeof(Simulation.Planet.TerrainType), wireArgs[0]);
                    int maxLinks = wireArgs.Length > 1 ? int.Parse(wireArgs[1]) : 99;
                    if (sim == null) return true;
                    var prev = new Dictionary<int, int> { { sim.BaseTileId, -1 } };
                    var queue = new Queue<int>();
                    queue.Enqueue(sim.BaseTileId);
                    int goal = -1;
                    while (queue.Count > 0 && goal < 0)
                    {
                        int t = queue.Dequeue();
                        foreach (int nb in sim.Sphere.Neighbors[t])
                            if (!prev.ContainsKey(nb) && !sim.GetTile(nb).IsWater)
                            {
                                prev[nb] = t;
                                if (sim.GetTile(nb).Terrain == kind) { goal = nb; break; }
                                queue.Enqueue(nb);
                            }
                    }
                    int placed = 0;
                    var path = new List<int>();
                    for (int t = goal; t >= 0; t = prev[t]) path.Add(t);   // goal ... base
                    path.Reverse();                                        // base ... goal
                    for (int i = 1; i < path.Count && placed < maxLinks; i++)
                        if (sim.PlaceWire(path[i - 1], path[i], false)) placed++;
                    Log($"wire goal={goal} lines={placed} powered={sim.PoweredTileCount}");
                    if (goal >= 0 && picker != null) picker.Select(goal);
                    return true;
                }
                case "flows":
                {
                    var sim = Runner()?.Simulation;
                    if (sim == null) return true;
                    var snap = sim.FlowsheetSnapshot(7);
                    Log($"flows t={sim.TimeHours:0} processes={snap.Processes.Count} in={snap.TotalInKg:0.0} out={snap.TotalOutKg:0.0} heat={snap.WasteHeatKWh:0.0} blocked={snap.Blocked.Count}");
                    return true;
                }
                case "minedemo":
                {
                    // minedemo: survey everything, mine iron at the base for 60 hours, take 40 % of the deposit out, log the numbers.
                    var sim = Runner()?.Simulation as Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    int t = sim.BaseTileId;
                    var iron = Simulation.Resources.Material.Iron;
                    sim.DebugSurveyAll();
                    sim.OrderMining(t, iron);
                    for (int h = 0; h < 60; h++) sim.Tick(1.0);
                    sim.Deposits.Extract(t, iron, sim.Deposits.ReserveKg(t, iron) * 0.4);
                    sim.Tick(1.0);
                    Log($"minedemo reserve {sim.ReserveEstimate(t, iron).Say("0", "kg")}, ore grade {sim.OreGradeEstimate(t, iron).Say("0.0%", "")}, left {sim.DepositShareLeft(t, iron) * 100.0:0} %, rate {sim.MiningRateEstimate(t, iron).Say("0.#", "kg/h")}, rock moved {sim.RockMovedKg / 1000.0:0.0} t, tailings {sim.TailingsKg / 1000.0:0.0} t");
                    return true;
                }
                case "ironcell":
                {
                    // ironcell: a Workshop and an Electrolysis cell at the base, 200 kg of rust, one iron batch; steps 130 h and logs what came out.
                    var sim = Runner()?.Simulation as Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    int t = sim.BaseTileId;
                    sim.Debug.FreeBuilding = true;
                    for (int slot = 0; slot < sim.SlotCount(t); slot++)
                        if (sim.GetBuilding(t, slot) == Simulation.Game.BuildingType.None && sim.PlaceWorkshop(t, slot)) break;
                    for (int slot = 0; slot < sim.SlotCount(t); slot++)
                        if (sim.GetBuilding(t, slot) == Simulation.Game.BuildingType.None && sim.RefineryBlocker(Simulation.Game.BuildingType.Electrolyser, t, slot) == null) { sim.PlaceRefinery(Simulation.Game.BuildingType.Electrolyser, t, slot); break; }
                    sim.Debug.FreeBuilding = false;
                    sim.WireAllBuildings();
                    double ironBefore = sim.Materials[Simulation.Resources.Material.Iron];
                    sim.Materials.Add(Simulation.Resources.Material.Rust, 200.0);
                    string order = sim.OrderRefine(Simulation.Resources.RefineProcess.IronFromRust);
                    for (int h = 0; h < 900; h++) sim.Tick(1.0);
                    Log($"ironcell order={order ?? "ok"} iron {ironBefore:0.0} -> {sim.Materials[Simulation.Resources.Material.Iron]:0.0} kg, rust left {sim.Materials[Simulation.Resources.Material.Rust]:0.0} kg :: " + string.Join(" || ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(sim.OperationsLog, o => o.Text.Contains("Iron from rust")), o => o.Text)));
                    return true;
                }
                case "rebuilddemo":
                {
                    // rebuilddemo: a General robot dies on a cold night, its wreck is recovered, and the Workshop rebuilds it; logs each step.
                    var sim = Runner()?.Simulation as Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    sim.Debug.FreeBuilding = true;
                    for (int slot = 0; slot < sim.SlotCount(sim.BaseTileId); slot++)
                        if (sim.GetBuilding(sim.BaseTileId, slot) == Simulation.Game.BuildingType.None && sim.PlaceWorkshop(sim.BaseTileId, slot)) break;
                    sim.Debug.FreeBuilding = false;
                    sim.WireAllBuildings();
                    int tile = -1;
                    foreach (int n in sim.Sphere.Neighbors[sim.BaseTileId]) if (!sim.GetTile(n).IsWater) { tile = n; break; }
                    var r = sim.Workforce.AddRobot(Simulation.Robots.RobotKind.General, tile);
                    var w = (Simulation.Game.Wreck)sim.GetType().GetMethod("DestroyForTests", flags).Invoke(sim, new object[] { r.Id, -50.0 });
                    string before = sim.RebuildBlocker(w.Id);
                    var inv = (Simulation.Logistics.InventoryBook)sim.GetType().GetProperty("InventoriesForTests", flags).GetValue(sim);
                    inv.Remove(Simulation.Logistics.PlaceId.Wreck(w.Id));
                    sim.LogisticsEnabled = true; sim.Tick(1.0);
                    string after = sim.RebuildBlocker(w.Id);
                    foreach (var kv in Simulation.Robots.WreckModel.BillOf(Simulation.Robots.RobotKind.General, false)) sim.Materials.Add(kv.Key, kv.Value);   // the recovery brought the metals home
                    string order = sim.OrderRebuild(w.Id);
                    int robots = sim.Workforce.Robots.Count;
                    for (int h = 0; h < 12; h++) sim.Tick(1.0);
                    Log($"rebuilddemo before={before} | after recovery={after ?? "can rebuild"} | order={order ?? "ok"} | robots {robots} -> {sim.Workforce.Robots.Count} :: " + string.Join(" || ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(sim.OperationsLog, o => o.Quantity == "Robot rebuilt"), o => o.Text)));
                    return true;
                }
                case "refine":
                {
                    // refine: free-build a Workshop, an Alumina plant and an Electrolysis cell at the base, give them ore and salt, queue batches
                    var sim = Runner()?.Simulation as Simulation.Game.PlanetSimulation;
                    if (sim == null) return true;
                    int t = sim.BaseTileId;
                    sim.Debug.FreeBuilding = true;
                    for (int slot = 0; slot < sim.SlotCount(t); slot++)
                        if (sim.GetBuilding(t, slot) == Simulation.Game.BuildingType.None && sim.PlaceWorkshop(t, slot)) break;
                    foreach (var type in new[] { Simulation.Game.BuildingType.AluminaPlant, Simulation.Game.BuildingType.Electrolyser })
                        for (int slot = 0; slot < sim.SlotCount(t); slot++)
                            if (sim.GetBuilding(t, slot) == Simulation.Game.BuildingType.None && sim.RefineryBlocker(type, t, slot) == null) { sim.PlaceRefinery(type, t, slot); break; }
                    sim.Debug.FreeBuilding = false;
                    sim.WireAllBuildings();
                    sim.Materials.Add(Simulation.Resources.Material.Anorthosite, 600.0);
                    sim.Materials.Add(Simulation.Resources.Material.Salt, 200.0);
                    sim.OrderRefine(Simulation.Resources.RefineProcess.AluminaFromAnorthosite);
                    sim.OrderRefine(Simulation.Resources.RefineProcess.Aluminium);
                    sim.OrderRefine(Simulation.Resources.RefineProcess.SodiumFromSalt);
                    Log("refine set up");
                    return true;
                }
                case "target":
                {
                    // target <tile|neighbour> <Material> <kg>: a stock target at a depot ("n" = the first land neighbour of the base)
                    string[] w = argument.Split(' ');
                    var sim = Runner()?.Simulation;
                    if (sim == null) return true;
                    int tile = w[0] == "n" ? 0 : int.Parse(w[0]);
                    if (w[0] == "n")
                    {
                        var sphere = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.PlanetView>().Sphere;
                        foreach (int nb in sphere.Neighbors[sim.BaseTileId])
                            if (!sim.GetTile(nb).IsWater) { tile = nb; break; }
                    }
                    sim.SetStockTarget(tile, (Simulation.Resources.Material)Enum.Parse(typeof(Simulation.Resources.Material), w[1]), double.Parse(w[2], System.Globalization.CultureInfo.InvariantCulture));
                    Log("target tile " + tile);
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
                case "bay":
                {
                    // bay: open the site of a shallow-sea tile that can hold a barrage.
                    var sim = Runner()?.Simulation;
                    var picker = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>();
                    int found = -1;
                    for (int t = 0; sim != null && t < sim.TileCount && found < 0; t++)
                        if (sim.BarrageBlocker(t) == null) found = t;
                    if (found >= 0 && picker != null) picker.Select(found);
                    Log("bay tile " + found);
                    return true;
                }
                case "wave":
                {
                    // wave: open the site of the shallow-sea tile with the roughest sea.
                    var sim = Runner()?.Simulation;
                    var picker = UnityEngine.Object.FindFirstObjectByType<Presentation.Planet.TilePicker>();
                    int best = -1;
                    double most = -1.0;
                    for (int t = 0; sim != null && t < sim.TileCount; t++)
                        if (sim.GetTile(t).Terrain == Simulation.Planet.TerrainType.ShallowOcean && sim.MeanWaveHeightAtM(t) > most) { most = sim.MeanWaveHeightAtM(t); best = t; }
                    if (best >= 0 && picker != null) picker.Select(best);
                    Log("wave tile " + best + " height " + most);
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
                case "playtests":
                {
                    // A PlayMode run with script compile errors freezes the editor (Safe Mode prompt): refuse it, and say why.
                    if (EditorUtility.scriptCompilationFailed) { Log("playtests NOT started: scripts do not compile (see the Console)"); return true; }
                    // Run every PlayMode test (Play must be off); the result goes to the log (TESTS passed=... failed=...).
                    SessionState.SetBool("DevDriver.PlayTestsPending", true);
                    var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                    api.RegisterCallbacks(new TestLog(() => SessionState.SetBool("DevDriver.PlayTestsPending", false)));
                    api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode }));
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
            readonly Action onFinished;
            public TestLog(Action finished = null) { onFinished = finished; }
            public void RunStarted(ITestAdaptor testsToRun) { Log("tests started"); }
            public void RunFinished(ITestResultAdaptor result) { onFinished?.Invoke(); Log($"TESTS passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} inconclusive={result.InconclusiveCount}"); }
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

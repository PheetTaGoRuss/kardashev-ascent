using System;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using Material = KardashevAscent.Simulation.Resources.Material;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Tile information panel (#13), shown while a tile's site is open: where the tile is, its yearly sunlight,
    /// the best panel angle, a sun-path chart (the star's track on the solstices and the equinox, with where it is now
    /// and where the panel faces), the panel's energy month by month, and the formulas behind them.
    /// UI Toolkit: UI/TileInfo/TileInfo.uxml + .uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TileInfoView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] TilePicker picker;
        [SerializeField] PanelPlacer placer;
        [SerializeField] LightPotentialOverlay lightMap;
        [Tooltip("Seconds between refreshes of the live parts (star position, panel angles).")]
        [Range(0.05f, 1f)] [SerializeField] float refreshInterval = 0.2f;

        static readonly string[] MonthNames = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" };

        VisualElement panelRoot, monthly, monthLabels, groundActions;
        Label ground, landingText;
        VisualElement landingActions;
        string groundShape, landingShape;
        Label title, place, sunlight, bestLabel, bestCaption, sunPathCaption, monthlyCaption, monthlyHover, legend1, legend2;
        SunPathChart sunPath;
        // Short lines with the full story behind "i" (director 2026-11-15, after Oxygen Not Included), in two tabs.
        InfoTip placeTip, sunlightTip, bestTip, groundTip, sunTip;
        VisualElement groundChips, overviewTab, sunTab;
        Button overviewButton, sunButton;
        static bool showSunTab;
        int shownTile = -1;
        bool shownKnowsLatitude;
        double shownTilt = double.NaN, shownAzimuth = double.NaN;
        double[] months;
        float nextRefresh;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);

            panelRoot = root.Q("tile-info");
            PanelDrag.AddGrip(panelRoot);   // drag it anywhere (director 2026-11-14)
            title = root.Q<Label>("title");
            place = root.Q<Label>("place");
            sunlight = root.Q<Label>("sunlight");
            bestLabel = root.Q<Label>("best");
            bestCaption = root.Q<Label>("best-caption");
            sunPathCaption = root.Q<Label>("sunpath-caption");
            monthlyCaption = root.Q<Label>("monthly-caption");
            monthlyHover = root.Q<Label>("monthly-hover");
            legend1 = root.Q<Label>("legend-1");
            legend2 = root.Q<Label>("legend-2");
            monthly = root.Q("monthly");
            monthLabels = root.Q("month-labels");
            ground = root.Q<Label>("ground");
            landingText = root.Q<Label>("landing-text");
            landingActions = root.Q("landing-actions");
            landingShape = null;
            groundActions = root.Q("ground-actions");
            groundShape = null;

            BuildTabsAndTips(root);

            VisualElement holder = root.Q("sunpath");
            sunPath = new SunPathChart();
            sunPath.style.flexGrow = 1;
            holder.Add(sunPath);

            for (int m = 0; m < 12; m++)
            {
                int month = m;
                var bar = new VisualElement();
                bar.AddToClassList("month-bar");
                bar.RegisterCallback<PointerEnterEvent>(_ => ShowMonth(month));
                bar.RegisterCallback<PointerLeaveEvent>(_ => monthlyHover.text = " ");
                monthly.Add(bar);
                var label = new Label(MonthNames[m]);
                label.AddToClassList("month-label");
                monthLabels.Add(label);
            }
            shownTile = -1;
        }

        void BuildTabsAndTips(VisualElement root)
        {
            // Two tabs under the title: Overview (sunlight, best angle, ground) and Sun (path, months, formulas).
            var tabBar = new VisualElement();
            tabBar.style.flexDirection = FlexDirection.Row;
            tabBar.style.marginTop = 8;
            overviewButton = new Button(() => { showSunTab = false; ApplyTab(); }) { text = "Overview" };
            sunButton = new Button(() => { showSunTab = true; ApplyTab(); }) { text = "Sun" };
            foreach (Button b in new[] { overviewButton, sunButton })
            {
                b.AddToClassList("tab");
                b.style.flexGrow = 1;
                tabBar.Add(b);
            }
            overviewTab = new VisualElement();
            sunTab = new VisualElement();
            VisualElement parent = landingActions.parent;
            int at = parent.IndexOf(landingActions) + 1;
            parent.Insert(at, tabBar);
            parent.Insert(at + 1, overviewTab);
            parent.Insert(at + 2, sunTab);

            VisualElement statRow = root.Q(className: "stat-row");
            Label groundHead = null;
            root.Query<Label>().ForEach(l => { if (l.text == "GROUND") groundHead = l; });
            foreach (VisualElement e in new[] { statRow, groundHead, ground, groundActions })
                if (e != null) overviewTab.Add(e);
            groundChips = new VisualElement();
            groundChips.style.flexDirection = FlexDirection.Row;
            groundChips.style.flexWrap = Wrap.Wrap;
            overviewTab.Insert(overviewTab.IndexOf(ground) + 1, groundChips);

            // The formulas move behind the sun path's "i".
            var formulas = root.Q<Foldout>("formulas");
            string formulaText = "";
            if (formulas != null)
            {
                formulas.Query<Label>(className: "formula").ForEach(l => formulaText += l.text + "\n");
                formulas.RemoveFromHierarchy();
            }
            foreach (string name in new[] { "sunpath-fold", "monthly-fold" })
            {
                VisualElement e = root.Q(name);
                if (e != null) sunTab.Add(e);
            }

            // The "i" boxes.
            placeTip = InfoTip.After(place, "");
            if (statRow != null)
            {
                var heads = statRow.Query<Label>(className: "heading").ToList();
                if (heads.Count > 0)
                    sunlightTip = InfoTip.After(heads[0], "Yearly starlight on one flat square metre here, in kWh, after the air and " +
                                                 "the clouds (NASA climate records where there are some). A tilted panel gets more or less.");
                if (heads.Count > 1)
                    bestTip = InfoTip.After(heads[1], "The tilt β (from flat) and facing γ (from north) that catch the most energy over a " +
                                             "year here. It needs your latitude, measured in the sky view (V). Rule of thumb: tilt about " +
                                             "your latitude, facing the equator.");
            }
            if (groundHead != null)
                groundTip = InfoTip.After(groundHead, "");
            sunTip = InfoTip.After(sunPathCaption, "Looking straight up: the centre is overhead, the rim is the horizon, north at " +
                                   "the top. The lines are the star's path on the shortest day, the longest day and an equinox.\n\n" +
                                   "Formulas:\n" + formulaText.TrimEnd('\n'));
            ApplyTab();
        }

        void ApplyTab()
        {
            if (overviewTab == null) return;
            overviewTab.style.display = showSunTab ? DisplayStyle.None : DisplayStyle.Flex;
            sunTab.style.display = showSunTab ? DisplayStyle.Flex : DisplayStyle.None;
            overviewButton.EnableInClassList("selected", !showSunTab);
            sunButton.EnableInClassList("selected", showSunTab);
        }

        void OnDisable()
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        void Update()
        {
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            int tile = picker != null ? picker.SelectedTile : -1;
            if (panelRoot == null)
                return;
            panelRoot.EnableInClassList("hidden", sim == null || tile < 0);
            if (sim == null || tile < 0)
            {
                shownTile = -1;
                return;
            }

            // The tile's place and sun path appear once its latitude is measured (director 2026-11-14).
            bool knows = sim.Notebook != null && sim.Notebook.KnowsLatitude(tile);
            if (tile != shownTile || knows != shownKnowsLatitude)
            {
                shownKnowsLatitude = knows;
                shownTile = tile;
                shownTilt = shownAzimuth = double.NaN;
                ShowTile(sim, tile);
                nextRefresh = 0f;
            }
            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Refresh(sim, tile);
            RefreshGround(sim, tile);
            RefreshLanding(sim, tile);
        }

        /// <summary>
        /// While choosing where to land (time stopped), a button to land on this tile; once landed, where the colony is.
        /// The colony can't move after landing (decided 2026-11-14).
        /// </summary>
        void RefreshLanding(IPlanetSimulation sim, int tile)
        {
            if (landingText == null || landingActions == null)
                return;
            bool here = tile == sim.BaseTileId, water = sim.GetTile(tile).IsWater;
            bool landing = GameFlow.Phase == GamePhase.Landing;
            string shape = $"{tile}|{sim.BaseTileId}|{landing}";
            if (shape == landingShape)
                return;
            landingShape = shape;
            landingActions.Clear();
            string problem = Terrains.LandingProblem(sim.GetTile(tile).Terrain);
            if (landing && problem != null)
                landingText.text = problem + " Pick another tile.";
            else if (landing)
            {
                landingText.text = "Land here? The colony can't move later.";
                var b = new Button(() =>
                {
                    string why = runner.LandAt(tile, true);
                    landingShape = null;
                    if (why != null)
                    {
                        landingText.text = why;
                        return;
                    }
                    GameFlow.Landed(tile);
                }) { text = "LAND HERE" };
                // Big and bright so it can't be missed (director 2026-11-15).
                b.AddToClassList("primary-button");
                b.AddToClassList("land-here");
                b.style.flexGrow = 1;
                b.style.height = 48;
                b.style.fontSize = 20;
                b.style.marginTop = 6;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.letterSpacing = 2;
                landingActions.Add(b);
            }
            else if (here)
                landingText.text = "Landing site · R: fly back";
            else
                landingText.text = "";
            landingText.EnableInClassList("hidden", landingText.text.Length == 0);
        }

        /// <summary>
        /// What is known about the tile's ground (phase 3a): rock always; the rest after a robot surveys it. Buttons
        /// order a survey or keep robots mining a material here (click again to stop).
        /// </summary>
        static bool CoresWaiting(IPlanetSimulation sim, int tile)
        {
            foreach (KardashevAscent.Simulation.Resources.SurveyPuzzle p in sim.SurveyPuzzles)
                if (p.Tile == tile) return true;
            return false;
        }

        /// <summary>"42 t of 50 t left", "used up" or "never runs out" (director 2026-11-15: deposits run out).</summary>
        /// <summary>A grade as the colony knows it: a range ("0.31 ± 0.08") until the ore assay, exact after. Rock is always exact.</summary>
        static string GradeText(IPlanetSimulation sim, int tile, Deposit d, bool rough)
        {
            if (d.Material == Material.Rock) return d.Grade.ToString("0.00");
            var e = sim.GradeEstimate(tile, d.Material);
            if (e.IsExact) return (rough ? "±" : "") + d.Grade.ToString("0.00");
            return $"{e.Value:0.00} ± {e.HalfRange:0.00}";
        }

        static string Left(DepositMap map, int tile, Material m)
        {
            if (map.IsEndless(tile, m)) return "never runs out (sea water)";
            if (map.IsExhausted(tile, m)) return "used up";
            return $"{Tonnes(map.RemainingKg(tile, m))} of {Tonnes(map.ReserveKg(tile, m))} left";
        }

        static string Tonnes(double kg) => kg >= 10_000.0 ? $"{kg / 1000.0:0} t" : kg >= 1000.0 ? $"{kg / 1000.0:0.0} t" : $"{kg:0} kg";

        void RefreshGround(IPlanetSimulation sim, int tile)
        {
            if (ground == null || groundActions == null)
                return;
            DepositMap map = sim.Deposits;
            TileInfo info = sim.GetTile(tile);
            bool water = info.IsWater;
            bool surveyed = map.IsSurveyed(tile) || KardashevAscent.Presentation.Planet.GodMode.Active;   // god mode: view only
            string text;
            if (water)
                text = "Sea floor: robots can't reach it yet (copper and zinc ores form at sea-floor vents).";
            else if (!surveyed)
                text = sim.IsSurveyOrdered(tile)
                    ? $"Survey under way: {sim.SurveyProgress(tile) * 100.0:0} % (a robot takes cores and scans the ground).\nRock: yes, everywhere on land."
                    : CoresWaiting(sim, tile)
                    ? "Cores are in: estimate the grades in Research (T) to finish the survey.\nRock: yes, everywhere on land."
                    : "Not surveyed: only rock is known. Survey to find iron, water ice, copper or lithium.";
            else
            {
                text = "";
                bool rough = sim.IsRoughSurvey(tile);
                foreach (Deposit d in map.Known(tile))
                    text += $"{Materials.Name(d.Material)}: grade {GradeText(sim, tile, d, rough)}, {Left(map, tile, d.Material)}" +
                            $"{(sim.IsMiningOrdered(tile, d.Material) ? " · being mined" : "")}\n";
                text += "Deposits run out: what robots mine comes out of the ground. Sea water from a shore never runs out.\n";
                if (rough)
                    text += "± : surveyed from a rough estimate (the mine pays by the real grade).\n";
                if (map.Known(tile).Count <= 2)
                    text += "No water, copper or lithium here.\n";
                if (sim.KnowsCave(tile))
                    text += "A cave: it reaches deep rock (more rare earths), and a tunnel here takes half the work and steel.\n";
                if (KardashevAscent.Simulation.Resources.DepositMap.IsDeepGround(info.Terrain) && !map.DeepScanned)
                    text += "Deep ore (rare earths) can only be seen after a seismic survey (Research).\n";
                text += $"Grade 1 = an ordinary deposit on Earth. This planet: metals ×{map.Chemistry.MetalFactor:0.00} " +
                        $"(star [Fe/H] {map.Chemistry.MetallicityDex:+0.00;-0.00}), iron ×{map.Chemistry.IronFactor:0.00}" +
                        (map.Chemistry.IronMeasured ? " (measured iron fraction)" : "") +
                        (map.Chemistry.WaterHistory ? "" : ". No seas: no copper or lithium ores");
            }
            if (sim.IsBarrier(tile))
                text += "\n" + (sim.IsOpenGround(tile)
                    ? $"Opened: {KardashevAscent.Simulation.Resources.Passages.Name(sim.PassageAt(tile)).ToLowerInvariant()}. Robots can cross, build and mine here."
                    : sim.ClosedGroundReason(tile) + " Until then robots can't cross it, build or mine on it; they can survey it from the edge.");
            text += sim.LogisticsEnabled ? $"\nDepot on this tile: {DepotText(sim, tile)}" : $"\nOn hand: {Stock(sim)}";
            // Short line here; the whole story behind "i" (director 2026-11-15).
            if (groundTip != null)
                groundTip.Text = text.Trim();
            ground.text = water ? "Sea floor · can't reach yet"
                : !surveyed ? (sim.IsSurveyOrdered(tile) ? $"Surveying {sim.SurveyProgress(tile) * 100.0:0} %"
                               : CoresWaiting(sim, tile) ? "Cores waiting" : "Not surveyed · only rock known")
                : sim.IsRoughSurvey(tile) ? "Surveyed (rough ±)" : "Surveyed";

            string shape = $"{tile}|{water}|{surveyed}|{sim.IsSurveyOrdered(tile)}|{CoresWaiting(sim, tile)}|{map.DeepScanned}" +
                           $"|{sim.PassageAt(tile)}|{sim.PassageOrdered(tile)}|{(int)(sim.PassageProgress(tile) * 100.0)}|{(int)(sim.Materials[Material.Iron] / 100.0)}";
            foreach (Material m in Materials.Mined)
                shape += (sim.IsMiningOrdered(tile, m) ? "1" : "0") + (int)(map.RemainingShare(tile, m) * 100.0);
            if (shape == groundShape)
                return;
            groundShape = shape;
            groundActions.Clear();
            // Deposits as icons with their grade (1 = an ordinary deposit on Earth).
            groundChips.Clear();
            if (surveyed && !water)
                foreach (Deposit d in map.Known(tile))
                {
                    var chip = new VisualElement();
                    chip.style.flexDirection = FlexDirection.Row;
                    chip.style.alignItems = Align.Center;
                    chip.style.marginRight = 10;
                    chip.style.marginTop = 3;
                    chip.Add(MaterialChips.Icon(d.Material, 18f));
                    bool rough = sim.IsRoughSurvey(tile) && d.Material != Material.Rock;
                    bool empty = map.IsExhausted(tile, d.Material);
                    string share = map.IsEndless(tile, d.Material) ? "" : empty ? " · empty" : $" · {map.RemainingShare(tile, d.Material) * 100.0:0}%";
                    var g = new Label($"×{GradeText(sim, tile, d, rough)}{share}{(sim.IsMiningOrdered(tile, d.Material) ? " · mining" : "")}");
                    g.style.marginLeft = 4;
                    g.style.fontSize = 14;
                    if (empty) g.style.color = UiPalette.Muted;
                    chip.Add(g);
                    chip.tooltip = $"{Materials.Name(d.Material)}: grade {GradeText(sim, tile, d, rough)} (1 = an ordinary deposit on Earth); {Left(map, tile, d.Material)}";
                    groundChips.Add(chip);
                }
            if (water)
                return;
            if (!surveyed && CoresWaiting(sim, tile))
            {
                // The cores are in and the survey waits on the player (director 2026-11-15: say so, with a way there).
                var go = new Button(() =>
                {
                    PuzzleShelf.Focus(PuzzleShelf.SurveyKey(tile));
                    SystemTabs.Open("research");
                }) { text = "! Cores waiting: finish the survey (Research)" };
                go.AddToClassList("primary-button");
                go.style.flexGrow = 1;
                go.style.height = 30;
                go.style.fontSize = 14;
                go.style.marginTop = 4;
                go.tooltip = "A robot brought back rock cores. Work out each material's grade to finish surveying this tile.";
                groundActions.Add(go);
            }
            if (!surveyed && !CoresWaiting(sim, tile))
            {
                bool ordered = sim.IsSurveyOrdered(tile);
                AddGroundButton(ordered ? "Cancel survey" : "Survey (robot)", ordered,
                                () => { if (sim.IsSurveyOrdered(tile)) sim.CancelSurvey(tile); else sim.OrderSurvey(tile); groundShape = null; });
            }
            // A mountain or canyon: open it first (director 2026-11-15).
            if (sim.IsBarrier(tile) && !sim.IsOpenGround(tile))
            {
                KardashevAscent.Simulation.Resources.PassageKind ordered = sim.PassageOrdered(tile);
                if (ordered != KardashevAscent.Simulation.Resources.PassageKind.None)
                {
                    var o = ordered;
                    AddGroundButton($"Cancel {KardashevAscent.Simulation.Resources.Passages.Name(o).ToLowerInvariant()} · {sim.PassageProgress(tile) * 100.0:0} %", true,
                                    () => { sim.CancelPassage(tile); groundShape = null; });
                }
                else
                    foreach (var k in KardashevAscent.Simulation.Resources.Passages.For(info.Terrain))
                    {
                        var kind = k;
                        string why = sim.PassageBlocker(tile, kind);
                        var bill = sim.PassageBill(tile, kind);
                        var b = new Button(() => { sim.OrderPassage(tile, kind); groundShape = null; })
                        { text = $"{KardashevAscent.Simulation.Resources.Passages.Name(kind)} · {sim.PassageHours(tile, kind):0} h" };
                        b.AddToClassList("ground-button");
                        b.tooltip = KardashevAscent.Simulation.Resources.Passages.What(kind) + "\nTakes " + bill.Describe() + (why != null ? "\n" + why : "");
                        b.SetEnabled(why == null);
                        // The button and what it takes on one line.
                        var line = new VisualElement();
                        line.style.flexDirection = FlexDirection.Row;
                        line.style.alignItems = Align.Center;
                        line.style.width = Length.Percent(100);
                        line.Add(b);
                        var cost = new VisualElement();
                        MaterialChips.Fill(cost, bill, sim.Materials);
                        cost.style.marginLeft = 6;
                        line.Add(cost);
                        groundActions.Add(line);
                    }
            }
            foreach (Material m in Materials.Mined)
            {
                if (!map.IsKnown(tile, m) || map.GradeOf(tile, m) <= 0.0 || !sim.IsOpenGround(tile))
                    continue;
                Material material = m;
                bool on = sim.IsMiningOrdered(tile, m);
                var rate = sim.MiningRateEstimate(tile, m);
                string rateText = rate.IsExact ? $" · {rate.Value:0.#} kg/h" : $" · about {rate.Value:0.#} ± {rate.HalfRange:0.#} kg/h";
                AddGroundButton(on ? $"Stop mining {Materials.Name(m).ToLowerInvariant()}" : $"Mine {Materials.Name(m).ToLowerInvariant()}{rateText}", on,
                                () => { if (sim.IsMiningOrdered(tile, material)) sim.CancelMining(tile, material); else sim.OrderMining(tile, material); groundShape = null; });
            }
            // A storage yard can be given a charging bay: robots recharge at the depot and hauls can be staged through it (L2, 2026-11-24).
            if (sim.ChargingBayBlocker(tile) == null)
            {
                int bayTile = tile;
                AddGroundButton("Add charging bay to this depot (iron 60, rock 10, copper 12)", false, () =>
                {
                    for (int slot = 0; slot < sim.SlotCount(bayTile); slot++) if (sim.PlaceChargingBay(bayTile, slot)) break;
                    groundShape = null;
                });
            }
            // A mining or survey order out of a robot's one-charge range is never worked: say so (balance pass 2026-11-20).
            string reach = groundActions.childCount > 0 ? sim.WorkReachProblem(tile) : null;
            if (reach != null)
            {
                var warn = new Label(reach);
                warn.style.whiteSpace = WhiteSpace.Normal;
                warn.style.color = new Color(1f, 0.47f, 0.41f);
                warn.style.marginTop = 4;
                groundActions.Add(warn);
            }
        }

        void AddGroundButton(string text, bool on, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("ground-button");
            b.EnableInClassList("on", on);
            groundActions.Add(b);
        }

        /// <summary>What this tile's own depot holds and the stock targets set for it (mission operations MO-02).</summary>
        static string DepotText(IPlanetSimulation sim, int tile)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (Material m in Materials.All)
            {
                double kg = sim.KgAt(tile, m);
                if (kg >= 0.5) parts.Add($"{Materials.Name(m).ToLowerInvariant()} {kg:0} kg");
            }
            foreach (var t in sim.StockTargets())
                if (t.Tile == tile) parts.Add($"target {t.Kg:0} kg {Materials.Name(t.Material).ToLowerInvariant()}");
            return parts.Count == 0 ? "empty (set targets in Operations, I)" : string.Join(", ", parts);
        }

        static string Stock(IPlanetSimulation sim)
        {
            string s = "";
            foreach (Material m in Materials.All)
                if (sim.Materials[m] > 0.0)
                    s += (s.Length > 0 ? " · " : "") + $"{Materials.Name(m)} {FormatKg(sim.Materials[m])}";
            return s.Length > 0 ? s : "no materials yet";
        }

        static string FormatKg(double kg) => kg >= 1000.0 ? $"{kg / 1000.0:0.##} t" : $"{kg:0} kg";

        void ShowTile(IPlanetSimulation sim, int tile)
        {
            TileInfo info = sim.GetTile(tile);
            double latDeg = info.Latitude * Mathf.Rad2Deg, lonDeg = info.Longitude * Mathf.Rad2Deg;
            title.text = $"Tile #{tile} · {Pretty(info.Terrain)}";
            // Where the tile is stays unknown until the colony measures a latitude (sky view, V): the map then gives it.
            bool knowsLatitude = sim.Notebook != null && sim.Notebook.KnowsLatitude(tile);
            place.text = (knowsLatitude
                             ? $"{Math.Abs(latDeg):0.0}° {(latDeg >= 0 ? "N" : "S")}, {Math.Abs(lonDeg):0.0}° {(lonDeg >= 0 ? "E" : "W")}"
                             : "Latitude ?") +
                         (!info.IsBuildableLand ? (Terrains.AllowsFloating(info.Terrain) ? (info.Terrain == TerrainType.ShallowOcean ? " · floating, tidal, wave" + (sim.BarrageBlocker(tile) == null ? ", barrage bay" : "") + (sim.IsExperimentKnown(KardashevAscent.Simulation.Planet.ExperimentKind.TideGauge) ? $" · tide {sim.TidalRangeAtM(tile):0.0} m" : "") + (sim.IsExperimentKnown(KardashevAscent.Simulation.Planet.ExperimentKind.WaveBuoy) ? $" · waves {sim.MeanWaveHeightAtM(tile):0.0} m" : "") : " · floating only") : " · water") : $" · {sim.SlotCount(tile)} slots" + (sim.WiredGrid ? (sim.IsPowered(tile) ? (sim.HasLineOn(tile, true) ? $" · line {Format.Power(sim.LineFlowW(tile))} of {Format.Power(sim.LineCapacityW(tile))}" + (sim.IsLineOverloaded(tile) ? " (overloaded)" : "") : " · on grid") : " · off grid") : ""));
            if (placeTip != null)
                placeTip.Text = (knowsLatitude ? "Latitude and longitude of the tile's centre." :
                                    "Where the tile is stays unknown until you measure a latitude in the sky view (V); then the map gives every tile's.") +
                                (!info.IsBuildableLand
                                    ? (Terrains.AllowsFloating(info.Terrain) ? (info.Terrain == TerrainType.ShallowOcean ? $" Shallow sea: floating solar and tidal turbines can go here. {(sim.IsExperimentKnown(KardashevAscent.Simulation.Planet.ExperimentKind.TideGauge) ? $"Tide here: {sim.TidalRangeAtM(tile):0.0} m range, peak stream {sim.PeakStreamAtMs(tile):0.0} m/s (a turbine needs {KardashevAscent.Simulation.Planet.Tides.CutInMs:0.0} m/s)." : "Tide unknown: run the tide gauge experiment (Research, T).")}" : " Wetland: only floating solar can go here.") : " Deep water: nothing can be built here.")
                                    : $" A site has {sim.SlotCount(tile)} building slots; bigger buildings cover several.");

            PlanetSolarParams planet = runner.SolarParams;
            if (planet.TidallyLocked)
            {
                sunPathCaption.text = "Locked: the star stands still";
                sunPath.SetPaths(null, null, null, info.Latitude);
                return;
            }
            if (!knowsLatitude)
            {
                sunPathCaption.text = "Needs your latitude (V)";
                sunPath.SetPaths(null, null, null, 0.0);
                return;
            }
            // "June" solstice = the northern summer one: δ = +ε. Name them by what they are here.
            bool north = info.Latitude >= 0.0;
            legend1.text = north ? "Winter solstice" : "Summer solstice";
            legend2.text = north ? "Summer solstice" : "Winter solstice";
            sunPathCaption.text = $"Noon {SunPath.NoonElevationDeg(info.Latitude, planet.Obliquity):0}° summer · " +
                                  $"{SunPath.NoonElevationDeg(info.Latitude, -planet.Obliquity):0}° winter";
            double day = planet.DayLengthHours;
            sunPath.SetPaths(SunPath.Day(info.Latitude, -planet.Obliquity, day),
                             SunPath.Day(info.Latitude, planet.Obliquity, day),
                             SunPath.Day(info.Latitude, 0.0, day),
                             info.Latitude);
        }

        void Refresh(IPlanetSimulation sim, int tile)
        {
            TileInfo info = sim.GetTile(tile);

            double kwh = lightMap != null ? lightMap.ValueAt(tile) : double.NaN;
            sunlight.text = double.IsNaN(kwh) ? "–" : $"{kwh:N0}";

            if (placer != null && placer.Tile == tile && placer.TryGetBest(out PanelAngles best))
            {
                bestLabel.text = $"{best.Tilt * Mathf.Rad2Deg:0}° · {best.Azimuth * Mathf.Rad2Deg:0}°";
                bestCaption.text = "tilt · facing";
            }
            else if (!info.IsWater && placer != null && placer.Tile == tile && !placer.BestAngleUnlocked)
            {
                bestLabel.text = "?";
                bestCaption.text = "needs latitude (V)";
            }
            else
            {
                bestLabel.text = info.IsWater ? "–" : "…";
                bestCaption.text = !info.IsBuildableLand ? (Terrains.AllowsFloating(info.Terrain) ? "floating solar only" : "no panels on deep water") : "searching";
            }

            // Where the star is now, in this tile's sky.
            Double3 sun = PlanetFrame.ToLocal(sim.StarDirection, info.Latitude, info.Longitude);
            sunPath.SetNow(SunPosition.AzimuthDegrees(sun), 90.0 - SunPosition.ZenithDegrees(sun));

            // The panel being planned or selected: where it faces, and its months.
            if (placer != null && placer.Tile == tile && placer.TryGetAngles(out double tilt, out double azimuth))
            {
                sunPath.SetPanel(azimuth * Mathf.Rad2Deg, 90.0 - tilt * Mathf.Rad2Deg);
                if (tilt != shownTilt || azimuth != shownAzimuth)
                {
                    shownTilt = tilt;
                    shownAzimuth = azimuth;
                    months = sim.MonthlyEnergyWh(tile, tilt, azimuth);
                    monthlyCaption.text = $"One panel at tilt {tilt * Mathf.Rad2Deg:0}°, facing {azimuth * Mathf.Rad2Deg:0}°";
                    ShowMonths();
                }
            }
        }

        void ShowMonths()
        {
            double max = 0.0;
            if (months != null)
                foreach (double m in months)
                    max = Math.Max(max, m);
            for (int i = 0; i < monthly.childCount; i++)
            {
                double share = months != null && max > 0.0 ? months[i] / max : 0.0;
                monthly[i].style.height = Length.Percent((float)(share * 100.0));
            }
            if (months == null)
                monthlyCaption.text = "No panels here";
            // Months split this planet's own year; say how long that is when it isn't Earth's.
            double yearDays = runner != null ? runner.SolarParams.YearLengthHours / 24.0 : 365.256;
            string year = Math.Abs(yearDays - 365.256) < 1.0 ? "year" : $"planet year ({yearDays:0.#} days)";
            monthlyHover.text = months != null ? $"Best month {Format.Energy(max)} · {year} {Format.Energy(Sum(months))}" : " ";
        }

        void ShowMonth(int month)
        {
            if (months == null)
                return;
            monthlyHover.text = $"Month {month + 1}: {Format.Energy(months[month])}";
        }

        static double Sum(double[] values)
        {
            double sum = 0.0;
            foreach (double v in values)
                sum += v;
            return sum;
        }

        static string Pretty(TerrainType terrain) => Terrains.Name(terrain);
    }

    /// <summary>
    /// The sky seen from above (director 2026-11-15: the flat chart looked broken where the sun passes overhead): the
    /// centre is straight up, the rim is the horizon, rings every 30°, north at the top and east to the right. Three
    /// day-tracks, the star now (filled dot) and where the panel faces (open diamond). Any path draws as one smooth
    /// curve, tropics and midnight sun included.
    /// </summary>
    public sealed class SunPathChart : VisualElement
    {
        static readonly Color[] SeriesColors =
        {
            new Color32(57, 135, 229, 255),    // winter / first
            new Color32(217, 89, 38, 255),     // summer / second
            new Color32(25, 158, 112, 255),    // equinox
        };
        static readonly Color GridColor = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color HorizonColor = new Color(1f, 1f, 1f, 0.28f);
        static readonly Color NowColor = new Color32(255, 214, 120, 255);
        static readonly Color PanelColor = new Color32(230, 236, 255, 255);
        const float Margin = 16f;

        SunPathPoint[][] paths = new SunPathPoint[3][];
        double nowAz = double.NaN, nowEl = double.NaN, panelAz = double.NaN, panelEl = double.NaN;
        readonly Label[] compass = new Label[4];
        readonly Label[] rings = new Label[3];

        public SunPathChart()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
            string[] names = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                compass[i] = new Label(names[i]);
                compass[i].AddToClassList("axis-label");
                Add(compass[i]);
            }
            int[] els = { 0, 30, 60 };
            for (int i = 0; i < 3; i++)
            {
                rings[i] = new Label($"{els[i]}°");
                rings[i].AddToClassList("axis-label");
                rings[i].userData = els[i];
                Add(rings[i]);
            }
            RegisterCallback<GeometryChangedEvent>(_ => PlaceLabels());
        }

        public void SetPaths(SunPathPoint[] first, SunPathPoint[] second, SunPathPoint[] third, double latitude)
        {
            paths = new[] { first, second, third };
            MarkDirtyRepaint();
        }

        public void SetNow(double azimuthDeg, double elevationDeg)
        {
            nowAz = azimuthDeg;
            nowEl = elevationDeg;
            MarkDirtyRepaint();
        }

        public void SetPanel(double azimuthDeg, double elevationDeg)
        {
            panelAz = azimuthDeg;
            panelEl = elevationDeg;
            MarkDirtyRepaint();
        }

        static Vector2 Centre(Rect r) => r.center;
        static float Radius(Rect r) => Mathf.Max(10f, Mathf.Min(r.width, r.height) / 2f - Margin);

        /// <summary>A sky direction on the chart: the lower the elevation, the farther from the centre.</summary>
        static Vector2 At(double azimuthDeg, double elevationDeg, Rect r)
        {
            float d = (float)((90.0 - Math.Max(0.0, Math.Min(90.0, elevationDeg))) / 90.0) * Radius(r);
            double a = azimuthDeg * Math.PI / 180.0;
            return Centre(r) + new Vector2((float)Math.Sin(a) * d, -(float)Math.Cos(a) * d);
        }

        void PlaceLabels()
        {
            Rect r = contentRect;
            if (r.width <= 0f)
                return;
            float rad = Radius(r);
            Vector2 c = Centre(r);
            Vector2[] at = { new Vector2(-4f, -rad - 14f), new Vector2(rad + 4f, -7f), new Vector2(-4f, rad + 1f), new Vector2(-rad - 13f, -7f) };
            for (int i = 0; i < 4; i++)
            {
                compass[i].style.left = c.x + at[i].x;
                compass[i].style.top = c.y + at[i].y;
            }
            foreach (Label l in rings)
            {
                Vector2 p = At(135.0, (int)l.userData, r);   // along the south-east diagonal
                l.style.left = p.x + 2f;
                l.style.top = p.y - 2f;
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            Painter2D p = ctx.painter2D;
            Vector2 c = Centre(r);
            float rad = Radius(r);

            // Rings at 0° (the horizon), 30° and 60°, and the compass cross.
            p.lineWidth = 1f;
            for (int el = 0; el < 90; el += 30)
            {
                p.strokeColor = el == 0 ? HorizonColor : GridColor;
                p.BeginPath();
                p.Arc(c, rad * (90f - el) / 90f, 0f, 360f);
                p.Stroke();
            }
            p.strokeColor = GridColor;
            Line(p, c + new Vector2(0f, -rad), c + new Vector2(0f, rad));
            Line(p, c + new Vector2(-rad, 0f), c + new Vector2(rad, 0f));

            // Day tracks, only above the horizon.
            for (int s = 0; s < paths.Length; s++)
            {
                SunPathPoint[] path = paths[s];
                if (path == null)
                    continue;
                p.strokeColor = SeriesColors[s];
                p.lineWidth = 2f;
                p.lineJoin = LineJoin.Round;
                bool drawing = false;
                foreach (SunPathPoint pt in path)
                {
                    bool up = pt.ElevationDeg > 0.0;
                    Vector2 at = At(pt.AzimuthDeg, pt.ElevationDeg, r);
                    if (up && !drawing)
                    {
                        p.BeginPath();
                        p.MoveTo(at);
                        drawing = true;
                    }
                    else if (up)
                        p.LineTo(at);
                    else if (drawing)
                    {
                        p.LineTo(at);   // down to the horizon ring
                        p.Stroke();
                        drawing = false;
                    }
                }
                if (drawing)
                    p.Stroke();
            }

            // Where the panel faces: an open diamond.
            if (!double.IsNaN(panelAz))
            {
                Vector2 d = At(panelAz, panelEl, r);
                p.strokeColor = PanelColor;
                p.lineWidth = 2f;
                p.BeginPath();
                p.MoveTo(d + new Vector2(0f, -6f));
                p.LineTo(d + new Vector2(6f, 0f));
                p.LineTo(d + new Vector2(0f, 6f));
                p.LineTo(d + new Vector2(-6f, 0f));
                p.ClosePath();
                p.Stroke();
            }

            // The star now, when it is up: a filled dot with a dark ring so it reads over the lines.
            if (!double.IsNaN(nowAz) && nowEl > 0.0)
            {
                Vector2 n = At(nowAz, nowEl, r);
                p.fillColor = new Color(0.04f, 0.06f, 0.1f, 1f);
                p.BeginPath();
                p.Arc(n, 6f, 0f, 360f);
                p.Fill();
                p.fillColor = NowColor;
                p.BeginPath();
                p.Arc(n, 4f, 0f, 360f);
                p.Fill();
            }
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }
    }
}

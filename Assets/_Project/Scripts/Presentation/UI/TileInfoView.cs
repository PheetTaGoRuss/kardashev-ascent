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
        int shownTile = -1;
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

            if (tile != shownTile)
            {
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

        /// <summary>Where the colony landed, and (before anything is built) a button to land on this tile instead.</summary>
        void RefreshLanding(IPlanetSimulation sim, int tile)
        {
            if (landingText == null || landingActions == null)
                return;
            bool here = tile == sim.BaseTileId, water = sim.GetTile(tile).IsWater, canMove = runner.CanMoveLanding;
            string shape = $"{tile}|{sim.BaseTileId}|{canMove}";
            if (shape == landingShape)
                return;
            landingShape = shape;
            landingActions.Clear();
            if (here)
                landingText.text = canMove
                    ? "Your lander is here. Before you build, you can land somewhere else: pick a land tile and press Land here."
                    : "Your colony's landing site. Press R to fly back here.";
            else if (canMove && !water)
            {
                landingText.text = $"Nothing is built yet: the lander can still come down here instead (tile #{sim.BaseTileId} now).";
                var b = new Button(() =>
                {
                    string why = runner.LandAt(tile);
                    landingText.text = why ?? "Landed here.";
                    landingShape = null;
                }) { text = "Land here" };
                b.AddToClassList("ground-button");
                b.AddToClassList("on");
                landingActions.Add(b);
            }
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

        void RefreshGround(IPlanetSimulation sim, int tile)
        {
            if (ground == null || groundActions == null)
                return;
            DepositMap map = sim.Deposits;
            bool water = sim.GetTile(tile).IsWater;
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
                foreach (Deposit d in map.At(tile))
                    text += $"{Materials.Name(d.Material)}: grade {(rough && d.Material != Material.Rock ? "±" : "")}{d.Grade:0.00}" +
                            $"{(sim.IsMiningOrdered(tile, d.Material) ? " · being mined" : "")}\n";
                if (rough)
                    text += "± : surveyed from a rough estimate (the mine pays by the real grade).\n";
                if (map.At(tile).Count <= 2)
                    text += "No water, copper or lithium here.\n";
                text += $"Grade 1 = an ordinary deposit on Earth. This planet: metals ×{map.Chemistry.MetalFactor:0.00} " +
                        $"(star [Fe/H] {map.Chemistry.MetallicityDex:+0.00;-0.00}), iron ×{map.Chemistry.IronFactor:0.00}" +
                        (map.Chemistry.IronMeasured ? " (measured iron fraction)" : "") +
                        (map.Chemistry.WaterHistory ? "" : ". No seas: no copper or lithium ores");
            }
            text += $"\nOn hand: {Stock(sim)}";
            ground.text = text;

            string shape = $"{tile}|{water}|{surveyed}|{sim.IsSurveyOrdered(tile)}|{CoresWaiting(sim, tile)}";
            foreach (Material m in Materials.Mined)
                shape += sim.IsMiningOrdered(tile, m) ? "1" : "0";
            if (shape == groundShape)
                return;
            groundShape = shape;
            groundActions.Clear();
            if (water)
                return;
            if (!surveyed && !CoresWaiting(sim, tile))
            {
                bool ordered = sim.IsSurveyOrdered(tile);
                AddGroundButton(ordered ? "Cancel survey" : "Survey (robot)", ordered,
                                () => { if (sim.IsSurveyOrdered(tile)) sim.CancelSurvey(tile); else sim.OrderSurvey(tile); groundShape = null; });
            }
            foreach (Material m in Materials.Mined)
            {
                if (!map.IsKnown(tile, m) || map.GradeOf(tile, m) <= 0.0)
                    continue;
                Material material = m;
                bool on = sim.IsMiningOrdered(tile, m);
                AddGroundButton(on ? $"Stop mining {Materials.Name(m).ToLowerInvariant()}" : $"Mine {Materials.Name(m).ToLowerInvariant()}", on,
                                () => { if (sim.IsMiningOrdered(tile, material)) sim.CancelMining(tile, material); else sim.OrderMining(tile, material); groundShape = null; });
            }
        }

        void AddGroundButton(string text, bool on, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("ground-button");
            b.EnableInClassList("on", on);
            groundActions.Add(b);
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
            place.text = $"{Math.Abs(latDeg):0.0}° {(latDeg >= 0 ? "N" : "S")}, {Math.Abs(lonDeg):0.0}° {(lonDeg >= 0 ? "E" : "W")}" +
                         (!info.IsBuildableLand ? (Terrains.AllowsFloating(info.Terrain) ? " · floating solar only" : " · no building on water") : $" · {sim.SlotCount(tile)} building slots");

            PlanetSolarParams planet = runner.SolarParams;
            if (planet.TidallyLocked)
            {
                sunPathCaption.text = "Tidally locked: the star stands still in this tile's sky.";
                sunPath.SetPaths(null, null, null, info.Latitude);
                return;
            }
            // "June" solstice = the northern summer one: δ = +ε. Name them by what they are here.
            bool north = info.Latitude >= 0.0;
            legend1.text = north ? "Winter solstice" : "Summer solstice";
            legend2.text = north ? "Summer solstice" : "Winter solstice";
            sunPathCaption.text = $"Noon height: {SunPath.NoonElevationDeg(info.Latitude, planet.Obliquity):0}° in summer, " +
                                  $"{SunPath.NoonElevationDeg(info.Latitude, -planet.Obliquity):0}° in winter";
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
                bestCaption.text = "tilt · facing (from north)";
            }
            else if (!info.IsWater && placer != null && placer.Tile == tile && !placer.BestAngleUnlocked)
            {
                bestLabel.text = "?";
                bestCaption.text = "find your latitude first (V: sky view)";
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
    /// Elevation (0–90°, up) against azimuth (across, centred on the equator side: south in the north, north in the
    /// south). Three day-tracks, the star now (filled dot) and where the panel faces (open diamond).
    /// </summary>
    public sealed class SunPathChart : VisualElement
    {
        static readonly Color[] SeriesColors =
        {
            new Color32(57, 135, 229, 255),    // winter / first
            new Color32(217, 89, 38, 255),     // summer / second
            new Color32(25, 158, 112, 255),    // equinox
        };
        static readonly Color GridColor = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color NowColor = new Color32(255, 214, 120, 255);
        static readonly Color PanelColor = new Color32(230, 236, 255, 255);

        const float PadLeft = 22f, PadBottom = 14f, PadTop = 4f, PadRight = 6f;

        SunPathPoint[][] paths = new SunPathPoint[3][];
        double centreAzimuth = 180.0;
        double nowAz = double.NaN, nowEl = double.NaN, panelAz = double.NaN, panelEl = double.NaN;
        readonly Label[] xLabels = new Label[5];

        public SunPathChart()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 5; i++)
            {
                xLabels[i] = new Label();
                xLabels[i].AddToClassList("axis-label");
                Add(xLabels[i]);
            }
            foreach (int el in new[] { 30, 60, 90 })
            {
                var label = new Label($"{el}°");
                label.AddToClassList("axis-label");
                label.style.left = 2f;
                label.userData = el;
                Add(label);
            }
            RegisterCallback<GeometryChangedEvent>(_ => PlaceLabels());
        }

        public void SetPaths(SunPathPoint[] first, SunPathPoint[] second, SunPathPoint[] third, double latitude)
        {
            paths = new[] { first, second, third };
            centreAzimuth = latitude >= 0.0 ? 180.0 : 0.0;
            PlaceLabels();
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

        float X(double azimuth, Rect r)
        {
            double t = ((azimuth - centreAzimuth + 540.0) % 360.0) / 360.0;   // centre at 0.5
            return r.xMin + PadLeft + (float)t * (r.width - PadLeft - PadRight);
        }

        float Y(double elevation, Rect r) =>
            r.yMax - PadBottom - (float)(Math.Max(0.0, elevation) / 90.0) * (r.height - PadBottom - PadTop);

        void PlaceLabels()
        {
            Rect r = contentRect;
            if (r.width <= 0f)
                return;
            string[] names = { "N", "E", "S", "W" };
            for (int i = 0; i < 5; i++)
            {
                double az = centreAzimuth - 180.0 + 90.0 * i;
                int index = (int)Math.Round(((az % 360.0) + 360.0) % 360.0 / 90.0) % 4;
                xLabels[i].text = names[index];
                xLabels[i].style.left = X(az + (i == 4 ? -0.001 : 0.0), r) - 4f;
                xLabels[i].style.top = r.height - PadBottom + 1f;
            }
            foreach (VisualElement child in Children())
                if (child.userData is int el)
                    child.style.top = Y(el, r) - 6f;
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            Painter2D p = ctx.painter2D;

            // Grid: elevation every 30°, azimuth every 90°.
            p.strokeColor = GridColor;
            p.lineWidth = 1f;
            for (int el = 0; el <= 90; el += 30)
                Line(p, new Vector2(r.xMin + PadLeft, Y(el, r)), new Vector2(r.xMax - PadRight, Y(el, r)));
            for (int i = 0; i <= 4; i++)
            {
                float x = r.xMin + PadLeft + i / 4f * (r.width - PadLeft - PadRight);
                Line(p, new Vector2(x, Y(0, r)), new Vector2(x, Y(90, r)));
            }

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
                float lastX = 0f;
                for (int i = 0; i < path.Length; i++)
                {
                    SunPathPoint pt = path[i];
                    float x = X(pt.AzimuthDeg, r);
                    bool up = pt.ElevationDeg > 0.0;
                    // Break the line where it wraps around the chart's edge (midnight sun crossing the far side).
                    bool jump = drawing && Math.Abs(x - lastX) > (r.width - PadLeft - PadRight) * 0.5f;
                    if (up && (!drawing || jump))
                    {
                        if (drawing) p.Stroke();
                        p.BeginPath();
                        p.MoveTo(new Vector2(x, Y(pt.ElevationDeg, r)));
                        drawing = true;
                    }
                    else if (up)
                    {
                        p.LineTo(new Vector2(x, Y(pt.ElevationDeg, r)));
                    }
                    else if (drawing)
                    {
                        p.Stroke();
                        drawing = false;
                    }
                    lastX = x;
                }
                if (drawing)
                    p.Stroke();
            }

            // Where the panel faces: an open diamond.
            if (!double.IsNaN(panelAz))
            {
                Vector2 c = new Vector2(X(panelAz, r), Y(panelEl, r));
                p.strokeColor = PanelColor;
                p.lineWidth = 2f;
                p.BeginPath();
                p.MoveTo(c + new Vector2(0f, -6f));
                p.LineTo(c + new Vector2(6f, 0f));
                p.LineTo(c + new Vector2(0f, 6f));
                p.LineTo(c + new Vector2(-6f, 0f));
                p.ClosePath();
                p.Stroke();
            }

            // The star now, when it is up: a filled dot with a dark ring so it reads over the lines.
            if (!double.IsNaN(nowAz) && nowEl > 0.0)
            {
                Vector2 c = new Vector2(X(nowAz, r), Y(nowEl, r));
                p.fillColor = new Color(0.04f, 0.06f, 0.1f, 1f);
                p.BeginPath();
                p.Arc(c, 6f, 0f, 360f);
                p.Fill();
                p.fillColor = NowColor;
                p.BeginPath();
                p.Arc(c, 4f, 0f, 360f);
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

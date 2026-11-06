using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The colony overview (C): how big the colony is and why it is or isn't growing, a week of power made and needed
    /// with the battery level and brownouts, energy since landing, and what has been built where.
    /// UI Toolkit: UI/Colony/Colony.uxml + .uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ColonyView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] KeyCode toggleKey = KeyCode.C;
        [Tooltip("Seconds between refreshes while open.")]
        [Range(0.1f, 2f)] [SerializeField] float refreshInterval = 0.5f;
        [Tooltip("Tiles listed under Buildings, busiest first.")]
        [Range(1, 10)] [SerializeField] int tilesListed = 5;

        VisualElement panelRoot;
        Label subtitle, demand, demandCaption, growth, growthCaption, supply, supplyCaption, chartCaption, energy, buildings, tiles;
        PowerHistoryChart chart;
        float nextRefresh;

        /// <summary>True while the overview is shown.</summary>
        public bool Visible { get; private set; }
        public KeyCode ToggleKey => toggleKey;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);

            panelRoot = root.Q("colony");
            subtitle = root.Q<Label>("subtitle");
            demand = root.Q<Label>("demand");
            demandCaption = root.Q<Label>("demand-caption");
            growth = root.Q<Label>("growth");
            growthCaption = root.Q<Label>("growth-caption");
            supply = root.Q<Label>("supply");
            supplyCaption = root.Q<Label>("supply-caption");
            chartCaption = root.Q<Label>("chart-caption");
            energy = root.Q<Label>("energy");
            buildings = root.Q<Label>("buildings");
            tiles = root.Q<Label>("tiles");
            Button close = root.Q<Button>("close");
            if (close != null)
                close.clicked += () => SetVisible(false);

            VisualElement holder = root.Q("chart");
            chart = new PowerHistoryChart();
            chart.style.flexGrow = 1;
            holder?.Add(chart);
            SetVisible(Visible);
        }

        void OnDisable()
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            panelRoot?.EnableInClassList("hidden", !visible);
            nextRefresh = 0f;
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetVisible(!Visible);
            if (!Visible || panelRoot == null || Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim != null)
                Refresh(sim);
        }

        void Refresh(IPlanetSimulation sim)
        {
            double dayHours = sim.DayLengthHours > 0.0 ? sim.DayLengthHours : 24.0;
            int day = (int)(sim.TimeHours / dayHours) + 1;
            subtitle.text = $"Day {day} since landing · {toggleKey}: close";

            demand.text = Format.Power(sim.DemandW);
            demandCaption.text = $"target {Format.Power(sim.DemandTargetW)}";

            if (sim.IsColonyGrowing)
            {
                growth.text = "Growing";
                growthCaption.text = $"toward {Format.Power(sim.DemandTargetW)}";
            }
            else if (sim.HoursSinceBrownout < dayHours)
            {
                growth.text = "Held back";
                growthCaption.text = $"needs a full day with no brownout ({Math.Max(0.0, dayHours - sim.HoursSinceBrownout):0} h to go)";
            }
            else
            {
                growth.text = "At target";
                growthCaption.text = "more panels raise the target";
            }
            growth.EnableInClassList("good", growth.text == "Growing");
            growth.EnableInClassList("warn", growth.text == "Held back");

            PowerHistory h = sim.History;
            supply.text = $"{h.ServedShare * 100.0:0.0} %";
            supplyCaption.text = h.BrownoutHours > 0.0
                ? $"of demand met · {FormatHours(h.BrownoutHours, dayHours)} of brownout"
                : "of demand met · no brownouts";

            chart.SetHistory(h, sim.TimeHours, dayHours);
            double spanHours = h.Capacity * h.SampleHours;
            chartCaption.text = $"Last {spanHours / dayHours:0} days, one point per {FormatHours(h.SampleHours, dayHours)}";

            double curtailed = Math.Max(0.0, h.TotalGeneratedWh - h.TotalServedWh - h.TotalBuildWh);
            energy.text =
                $"Generated  {Format.Energy(h.TotalGeneratedWh)}\n" +
                $"Used by the colony  {Format.Energy(h.TotalServedWh)}\n" +
                $"Made into build parts  {Format.Energy(h.TotalBuildWh)}\n" +
                $"Stored or lost in the battery  {Format.Energy(curtailed)}";

            UpdateBuildings(sim);
        }

        void UpdateBuildings(IPlanetSimulation sim)
        {
            int panels = 0, panelSites = 0, yards = 0, yardSites = 0;
            var perTile = new Dictionary<int, (int Panels, int Yards)>();
            foreach (BuildingRecord r in sim.CaptureState().Buildings)
            {
                perTile.TryGetValue(r.Tile, out var n);
                if (r.Type == BuildingType.SolarPanel)
                {
                    panels++;
                    if (r.UnderConstruction) panelSites++;
                    n.Panels++;
                }
                else if (r.Type == BuildingType.Battery)
                {
                    yards++;
                    if (r.UnderConstruction) yardSites++;
                    n.Yards++;
                }
                perTile[r.Tile] = n;
            }

            buildings.text =
                $"Solar panels  {panels}{Sites(panelSites)}\n" +
                $"Battery yards  {yards}{Sites(yardSites)} · storage {Format.Energy(sim.BatteryCapacityWh)}\n" +
                $"Build parts  {Format.Energy(sim.BuildStockWh)}" +
                (sim.ConstructionQueueCount > 0 ? $" · {Format.Energy(sim.ConstructionRemainingWh)} still needed" : "");

            if (perTile.Count == 0)
            {
                tiles.text = "Nothing built yet. Click a tile to open its site.";
                return;
            }
            var list = new List<KeyValuePair<int, (int Panels, int Yards)>>(perTile);
            list.Sort((a, b) =>
            {
                int bySize = (b.Value.Panels + b.Value.Yards).CompareTo(a.Value.Panels + a.Value.Yards);
                return bySize != 0 ? bySize : a.Key.CompareTo(b.Key);
            });
            var lines = new List<string> { $"Built on {perTile.Count} tile{(perTile.Count == 1 ? "" : "s")}" };
            for (int i = 0; i < list.Count && i < tilesListed; i++)
            {
                var e = list[i];
                string where = e.Key == sim.BaseTileId ? " (base)" : "";
                lines.Add($"  Tile #{e.Key}{where}: {e.Value.Panels} panels, {e.Value.Yards} yards");
            }
            if (list.Count > tilesListed)
                lines.Add($"  … and {list.Count - tilesListed} more");
            tiles.text = string.Join("\n", lines);
        }

        static string Sites(int n) => n > 0 ? $" ({n} under construction)" : "";

        static string FormatHours(double hours, double dayHours) =>
            hours >= 2.0 * dayHours ? $"{hours / dayHours:0.#} days" : $"{hours:0.#} h";
    }

    /// <summary>
    /// A week of colony power: generation and demand (left axis, W) and battery charge (right axis, %),
    /// with brownout hours shaded.
    /// </summary>
    public sealed class PowerHistoryChart : VisualElement
    {
        static readonly Color GenerationColor = new Color32(143, 179, 255, 255);
        static readonly Color DemandColor = new Color32(242, 190, 80, 255);
        static readonly Color BatteryColor = new Color32(95, 211, 141, 200);
        static readonly Color BrownoutColor = new Color32(255, 120, 105, 60);
        static readonly Color GridColor = new Color(1f, 1f, 1f, 0.08f);

        const float PadLeft = 44f, PadRight = 30f, PadTop = 6f, PadBottom = 14f;

        PowerSample[] samples = Array.Empty<PowerSample>();
        double nowHours, spanHours = 1.0, dayHours = 24.0, maxW = 1.0;
        readonly Label topLabel, zeroLabel, fullLabel, emptyLabel, startLabel, endLabel;

        public PowerHistoryChart()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
            topLabel = AxisLabel();
            zeroLabel = AxisLabel("0");
            fullLabel = AxisLabel("100%");
            emptyLabel = AxisLabel("0%");
            startLabel = AxisLabel();
            endLabel = AxisLabel("now");
            RegisterCallback<GeometryChangedEvent>(_ => PlaceLabels());
        }

        Label AxisLabel(string text = "")
        {
            var label = new Label(text);
            label.AddToClassList("axis-label");
            Add(label);
            return label;
        }

        public void SetHistory(PowerHistory history, double timeHours, double planetDayHours)
        {
            if (samples.Length != history.Count)
                samples = new PowerSample[history.Count];
            double peak = 0.0;
            for (int i = 0; i < history.Count; i++)
            {
                samples[i] = history[i];
                peak = Math.Max(peak, Math.Max(samples[i].GenerationW, samples[i].DemandW));
            }
            nowHours = timeHours;
            dayHours = planetDayHours;
            spanHours = history.Capacity * history.SampleHours;
            maxW = NiceCeiling(Math.Max(peak, 1.0) * 1.05);
            topLabel.text = Format.Power(maxW);
            startLabel.text = $"−{spanHours / dayHours:0} d";
            PlaceLabels();
            MarkDirtyRepaint();
        }

        float X(double t, Rect r)
        {
            double share = 1.0 - (nowHours - t) / spanHours;
            return r.xMin + PadLeft + (float)Math.Max(0.0, Math.Min(1.0, share)) * (r.width - PadLeft - PadRight);
        }

        float Y(double share, Rect r) =>
            r.yMax - PadBottom - (float)Math.Max(0.0, Math.Min(1.0, share)) * (r.height - PadBottom - PadTop);

        void PlaceLabels()
        {
            Rect r = contentRect;
            if (r.width <= 0f)
                return;
            Place(topLabel, 2f, Y(1.0, r) - 6f);
            Place(zeroLabel, PadLeft - 12f, Y(0.0, r) - 6f);
            Place(fullLabel, r.width - PadRight + 3f, Y(1.0, r) - 6f);
            Place(emptyLabel, r.width - PadRight + 3f, Y(0.0, r) - 6f);
            Place(startLabel, PadLeft, r.height - PadBottom + 1f);
            Place(endLabel, r.width - PadRight - 18f, r.height - PadBottom + 1f);
        }

        static void Place(Label label, float left, float top)
        {
            label.style.left = left;
            label.style.top = top;
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            Painter2D p = ctx.painter2D;

            // Grid: quarters of the power axis, one line per planet day.
            p.strokeColor = GridColor;
            p.lineWidth = 1f;
            for (int i = 0; i <= 4; i++)
                Line(p, new Vector2(r.xMin + PadLeft, Y(i / 4.0, r)), new Vector2(r.xMax - PadRight, Y(i / 4.0, r)));
            for (double t = Math.Floor(nowHours / dayHours) * dayHours; t > nowHours - spanHours; t -= dayHours)
                Line(p, new Vector2(X(t, r), Y(0.0, r)), new Vector2(X(t, r), Y(1.0, r)));

            if (samples.Length == 0)
                return;

            // Brownouts: shade each sample where the colony got less than it asked for.
            p.fillColor = BrownoutColor;
            double step = samples.Length > 1 ? samples[1].TimeHours - samples[0].TimeHours : spanHours / 168.0;
            foreach (PowerSample s in samples)
            {
                if (s.ServedW >= s.DemandW * 0.999)
                    continue;
                float x0 = X(s.TimeHours - step, r), x1 = X(s.TimeHours, r);
                p.BeginPath();
                p.MoveTo(new Vector2(x0, Y(0.0, r)));
                p.LineTo(new Vector2(x1, Y(0.0, r)));
                p.LineTo(new Vector2(x1, Y(1.0, r)));
                p.LineTo(new Vector2(x0, Y(1.0, r)));
                p.ClosePath();
                p.Fill();
            }

            Series(p, r, BatteryColor, 1.5f, s => s.BatteryFraction);
            Series(p, r, DemandColor, 2f, s => s.DemandW / maxW);
            Series(p, r, GenerationColor, 2f, s => s.GenerationW / maxW);
        }

        void Series(Painter2D p, Rect r, Color color, float width, Func<PowerSample, double> value)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < samples.Length; i++)
            {
                var pt = new Vector2(X(samples[i].TimeHours, r), Y(value(samples[i]), r));
                if (i == 0) p.MoveTo(pt);
                else p.LineTo(pt);
            }
            p.Stroke();
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        /// <summary>1, 2 or 5 × a power of ten, at or above <paramref name="w"/>.</summary>
        static double NiceCeiling(double w)
        {
            double scale = Math.Pow(10.0, Math.Floor(Math.Log10(w)));
            foreach (double m in new[] { 1.0, 2.0, 5.0, 10.0 })
                if (m * scale >= w)
                    return m * scale;
            return 10.0 * scale;
        }
    }
}

using System.Linq;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>Power (O): what makes power now, where it goes, the battery and the grid's buildings.</summary>
    public sealed class PowerWindow : SystemWindow
    {
        public override string Id => "power";
        public override string Title => "Power";
        public override KeyCode Key => KeyCode.O;
        public override int Order => 10;

        Label now, sources, uses, battery, buildings;
        InfoTip nowTip;
        VisualElement lineRows;
        readonly System.Collections.Generic.Dictionary<int, (Label Text, Button Meter)> lineRowOf = new System.Collections.Generic.Dictionary<int, (Label, Button)>();
        string lineShape = "";
        Label failedText, ordersText;
        VisualElement orderRows;
        string orderShape = "";
        VisualElement failedRows;
        string failedShape = "";
        const int MaxLineRows = 12;

        // One line per section; what each means is behind "i" (director 2026-11-15, after Oxygen Not Included).
        protected override void BuildBody()
        {
            now = Text();
            nowTip = InfoTip.After(now, "");
            Heading("MADE");
            sources = Text();
            InfoTip.After(sources, "Solar: all your panels now (0 at night). RTG: the lander's nuclear battery, steady day and night. " +
                                   "Wasted: power made with nowhere to go (battery full, demand met).");
            Heading("USED");
            uses = Text();
            InfoTip.After(uses, "Colony: what the colony draws now; it grows toward its target after a day without brownout. " +
                                "Workshop: jobs being made. Charging: robots topping up.");
            Heading("BATTERY");
            battery = Text();
            InfoTip.After(battery, "Charges from surplus by day, carries the colony through the night. Until the surface " +
                                   "temperature experiment is done, it only charges to 80 % (safety margin).");
            Heading("GRID");
            var wire = MakeButton("⚡  Wire tool (X)", () => KardashevAscent.Presentation.Planet.WireTool.Toggle());
            wire.tooltip = "Click a tile, then the next tile along, to run a wire between them. Generators and stations only work on tiles the wires join to the lander tile.";
            Body.Add(wire);
            Heading("LINES");
            var lineNote = Text("caption");
            lineNote.text = "A line's load is only known with a meter or a fresh inspection (a day). Unmetered lines show their rating and a range.";
            lineRows = new VisualElement();
            Body.Add(lineRows);
            ordersText = Text("caption");
            orderRows = new VisualElement();
            Body.Add(orderRows);
            failedText = Text("message");
            failedRows = new VisualElement();
            Body.Add(failedRows);
            Heading("BUILDINGS");
            buildings = Text();
            InfoTip.After(buildings, "The week chart of power made and needed is in Overview, Power tab (C).");
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            double tidal = sim.TidalPowerW();
            double waves = sim.WavePowerW();
            double barrage = sim.BarragePowerW();
            double solar = sim.GenerationW - sim.RtgW - tidal - waves - barrage;
            now.text = (sim.IsBrownout ? "BROWNOUT · " : "") +
                       $"Making {Format.Power(sim.GenerationW)} · need {Format.Power(sim.DemandW)}";
            if (nowTip != null)
                nowTip.Text = $"Colony target {Format.Power(sim.DemandTargetW)}: demand grows toward it after a full day without " +
                              "brownout. A brownout is when power made plus the battery can't meet demand; growth stops until it ends.";
            now.EnableInClassList("message", sim.IsBrownout);
            sources.text = $"Solar {Format.Power(System.Math.Max(0.0, solar))} · " + (tidal > 0.0 ? $"Tidal {Format.Power(tidal)} · " : "") + (waves > 0.0 ? $"Waves {Format.Power(waves)} · " : "") + (barrage > 0.0 ? $"Barrage {Format.Power(barrage)} · " : "") + (sim.GeneratorsOffGrid > 0 ? $"{sim.GeneratorsOffGrid} off grid · " : "") + (sim.TransmissionLossW > 1.0 ? $"lines lose {Format.Power(sim.TransmissionLossW)} · " : "") + (sim.OverloadLossW > 1.0 ? $"OVERLOAD {Format.Power(sim.OverloadLossW)} lost · " : "") + $"RTG {Format.Power(sim.RtgW)} · wasted {Format.Power(sim.CurtailedW)}";
            uses.text = $"Colony {Format.Power(sim.DemandW)} · workshop {Format.Power(sim.WorkshopPowerW)} · charging {Format.Power(sim.RobotChargingW)}";
            double share = sim.BatteryCapacityWh > 0.0 ? sim.BatteryStoredWh / sim.BatteryCapacityWh : 0.0;
            string flow = sim.BatteryPowerW > 1.0 ? $"charging {Format.Power(sim.BatteryPowerW)}"
                        : sim.BatteryPowerW < -1.0 ? $"discharging {Format.Power(-sim.BatteryPowerW)}" : "idle";
            battery.text = $"{share * 100.0:0} % of {Format.Energy(sim.BatteryCapacityWh)} · {flow}";

            int panels = 0, yards = 0;
            for (int t = 0; t < sim.TileCount; t++)
            {
                panels += sim.PanelCount(t);
                yards += sim.BatteryCount(t);
            }
            buildings.text = $"{panels} panel{(panels == 1 ? "" : "s")} · {yards} battery yard{(yards == 1 ? "" : "s")}";
            RefreshLines(sim);
            RefreshFailed(sim);
            RefreshOrders(sim);
        }

        void RefreshOrders(IPlanetSimulation sim)
        {
            var orders = sim.ElectricalOrders();
            ordersText.text = orders.Count == 0 ? "" : $"{orders.Count} electrical job{(orders.Count == 1 ? "" : "s")} waiting for a robot:" + (string.IsNullOrEmpty(sim.LastElectricalProblem) ? "" : "\nLast job dropped: " + sim.LastElectricalProblem);
            string shape = string.Join(",", orders.Select(o => o.Action + ":" + o.Tile + "." + o.Slot));
            if (shape == orderShape) return;
            orderShape = shape;
            orderRows.Clear();
            foreach (var o in orders)
            {
                var order = o;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap } };
                var name = order.Action == ElectricalAction.InspectLine ? "Inspect line" : order.Action == ElectricalAction.RepairLine ? "Repair line" : order.Action == ElectricalAction.InstallMeter ? "Install meter" : order.Action == ElectricalAction.InstallProtection ? "Fit fuse or breaker" : "Install circuit";
                var text = Label($"{name} · tile {order.Tile}", "caption");
                text.style.flexGrow = 1;
                row.Add(text);
                row.Add(MakeButton("Cancel", () => SystemTabsSim()?.CancelElectricalOrder(order.Action, order.Tile, order.Slot)));
                orderRows.Add(row);
            }
        }

        void RefreshFailed(IPlanetSimulation sim)
        {
            var failed = sim.WiredGrid ? sim.FailedLines().ToList() : new System.Collections.Generic.List<(int Tile, int Slot, double Condition)>();
            failedText.text = failed.Count == 0 ? "" : $"{failed.Count} line{(failed.Count == 1 ? "" : "s")} failed: the grid is split there until repaired.";
            failedText.style.display = failed.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            string shape = string.Join(",", failed.Select(f => f.Tile + "." + f.Slot));
            if (shape == failedShape) return;
            failedShape = shape;
            failedRows.Clear();
            foreach (var f in failed)
            {
                int tile = f.Tile, slot = f.Slot;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Column, marginBottom = 4 } };
                var text = Label($"Line {tile}.{slot}: failed", "caption");
                text.AddToClassList("wrap");
                row.Add(text);
                row.Add(MakeButton("Send robot to repair", () => { var s = SystemTabsSim(); string why = s?.OrderLineRepair(tile, slot); text.text = why != null ? $"Line {tile}.{slot}: {why}" : $"Line {tile}.{slot}: a robot is on its way"; }));
                failedRows.Add(row);
            }
        }

        void RefreshLines(IPlanetSimulation sim)
        {
            var tiles = new System.Collections.Generic.List<int>();
            if (sim.WiredGrid)
                foreach (int t in sim.LineTiles())
                    if (sim.LineReadout(t).HasLine) tiles.Add(t);
            tiles.Sort();
            int hidden = System.Math.Max(0, tiles.Count - MaxLineRows);
            if (hidden > 0) tiles.RemoveRange(MaxLineRows, hidden);
            string shape = string.Join(",", tiles) + "/" + hidden;
            if (shape != lineShape)
            {
                lineShape = shape;
                lineRows.Clear();
                lineRowOf.Clear();
                foreach (int tile in tiles)
                {
                    int t = tile;
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Column, marginBottom = 4 } };
                    var text = Label("", "caption");
                    text.AddToClassList("wrap");
                    row.Add(text);
                    var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
                    row.Add(buttonRow);
                    Button meter = null;
                    meter = MakeButton("Meter", () =>
                    {
                        var s = SystemTabsSim();
                        if (s == null) return;
                        if (s.HasLineMeter(t)) s.RemoveLineMeter(t); else s.OrderLineMeter(t);
                    });
                    buttonRow.Add(meter);
                    buttonRow.Add(MakeButton("Inspect", () => SystemTabsSim()?.OrderLineInspection(t)));
                    buttonRow.Add(MakeButton("Fuse", () => SystemTabsSim()?.OrderLineProtection(t, ProtectionKind.Fuse)));
                    buttonRow.Add(MakeButton("Breaker", () => SystemTabsSim()?.OrderLineProtection(t, ProtectionKind.Breaker)));
                    lineRows.Add(row);
                    lineRowOf[t] = (text, meter);
                }
                if (hidden > 0) lineRows.Add(Label($"… and {hidden} more lines", "caption"));
            }
            foreach (int t in tiles)
            {
                LineReadout r = sim.LineReadout(t);
                var (text, meter) = lineRowOf[t];
                string cap = Format.Power(r.CapacityW);
                string guard = sim.LineProtectionOf(t) == ProtectionKind.None ? "" : " · " + sim.LineProtectionOf(t).ToString().ToLowerInvariant();
                text.text = r.Measured
                    ? $"Tile {t}: carries {Format.Power(r.FlowW)} of {cap}" + (r.Overloaded ? " · OVERLOADED" : "") + (r.Source == ReadingSource.Meter ? " · meter" : $" · inspected {r.AgeHours:0} h ago")
                    : $"Tile {t}: rated {cap} · load not measured (0 to {cap})";
                text.text += guard;
                meter.text = sim.HasLineMeter(t) ? "Remove meter" : "Meter";
            }
        }

        static IPlanetSimulation SystemTabsSim()
        {
            var runner = Object.FindFirstObjectByType<KardashevAscent.Presentation.Game.SimulationRunner>();
            return runner != null ? runner.Simulation : null;
        }
    }

    /// <summary>
    /// How each solar value looks on screen: a symbol in a coloured badge, a short name and what measures it
    /// (director 2026-11-14: less text, symbols and pictures).
    /// </summary>
    public static class ParameterLook
    {
        public static string Symbol(SolarParameter p)
        {
            switch (p)
            {
                case SolarParameter.Latitude: return "φ";
                case SolarParameter.OpticalDepth: return "τ";
                case SolarParameter.DiffuseShare: return "k";
                case SolarParameter.Clouds: return "f";
                case SolarParameter.Albedo: return "ρ";
                case SolarParameter.AirTemperature: return "T";
                case SolarParameter.Dust: return "d";
                default: return "λ";
            }
        }

        public static string Short(SolarParameter p)
        {
            switch (p)
            {
                case SolarParameter.Latitude: return "Latitude";
                case SolarParameter.OpticalDepth: return "Air haze";
                case SolarParameter.DiffuseShare: return "Sky glow";
                case SolarParameter.Clouds: return "Clouds";
                case SolarParameter.Albedo: return "Ground shine";
                case SolarParameter.AirTemperature: return "Air heat";
                case SolarParameter.Dust: return "Dust";
                default: return "Star colour";
            }
        }

        /// <summary>The instrument, in a few words.</summary>
        public static string Tool(SolarParameter p)
        {
            switch (p)
            {
                case SolarParameter.Latitude: return "noon shadow";
                case SolarParameter.OpticalDepth: return "sun photometer";
                case SolarParameter.DiffuseShare: return "shaded sensor";
                case SolarParameter.Clouds: return "light log";
                case SolarParameter.Albedo: return "up/down sensors";
                case SolarParameter.AirTemperature: return "weather station";
                case SolarParameter.Dust: return "test cells";
                default: return "spectrometer";
            }
        }

        public static Color Colour(SolarParameter p)
        {
            switch (p)
            {
                case SolarParameter.Latitude: return new Color(0.56f, 0.70f, 1f);
                case SolarParameter.OpticalDepth: return new Color(0.75f, 0.62f, 1f);
                case SolarParameter.DiffuseShare: return new Color(0.45f, 0.85f, 0.95f);
                case SolarParameter.Clouds: return new Color(0.80f, 0.84f, 0.90f);
                case SolarParameter.Albedo: return new Color(0.95f, 0.78f, 0.45f);
                case SolarParameter.AirTemperature: return new Color(1f, 0.55f, 0.45f);
                case SolarParameter.Dust: return new Color(0.80f, 0.68f, 0.52f);
                default: return new Color(1f, 0.85f, 0.35f);
            }
        }

        /// <summary>A square badge with the value's symbol.</summary>
        public static VisualElement Badge(SolarParameter p, float size = 26f)
        {
            var b = new Label(Symbol(p));
            b.style.width = size; b.style.height = size;
            b.style.minWidth = size;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 0;
            b.style.backgroundColor = Colour(p);
            b.style.color = new Color(0.05f, 0.06f, 0.10f);
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.fontSize = UiType.Px(size * 0.55f);
            b.style.marginRight = 8;
            b.style.paddingLeft = b.style.paddingRight = b.style.paddingTop = b.style.paddingBottom = 0;
            return b;
        }

        /// <summary>A thin bar: <paramref name="fill"/> of it in <paramref name="colour"/> over a dark track.</summary>
        public static VisualElement Bar(out VisualElement fill, Color colour, float height = 6f)
        {
            var track = new VisualElement();
            track.style.height = height;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            track.style.borderTopLeftRadius = track.style.borderTopRightRadius = track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 0;
            fill = new VisualElement();
            fill.style.height = height;
            fill.style.width = Length.Percent(0);
            fill.style.backgroundColor = colour;
            fill.style.borderTopLeftRadius = fill.style.borderTopRightRadius = fill.style.borderBottomLeftRadius = fill.style.borderBottomRightRadius = 0;
            track.Add(fill);
            return track;
        }
    }

    /// <summary>
    /// Research: what a solar panel on the selected tile depends on, how sure the colony is of each value, and a button to
    /// send a robot to measure it. Puzzles (now and then) wait at the top. A short picture tutorial opens the first time
    /// (and with "?").
    /// </summary>
    public sealed class ResearchWindow : SystemWindow
    {
        // Wide enough for the puzzle cards' bigger text, steps and worked examples (director 2026-11-14).
        protected override float Width => 680f;   // the research tree: three columns of nodes
        public override string Id => "research";
        public override string Title => "Research";
        public override KeyCode Key => KeyCode.T;
        public override int Order => 40;

        const string TutorialSeenKey = "kardashev.tutorial.measuring";

        Label where, total, puzzleHead, solvedText, totalKnown, stationNote, marginNote;
        VisualElement totalFill;
        VisualElement puzzles, tutorial;
        Label tutorialStep, tutorialText, tutorialCount;
        VisualElement tutorialPicture;
        Button tutorialNext;
        int tutorialAt;
        int shownPuzzles = -1;
        readonly Dictionary<SolarParameter, Label> status = new Dictionary<SolarParameter, Label>();
        readonly Dictionary<SolarParameter, Label> spreadText = new Dictionary<SolarParameter, Label>();
        readonly Dictionary<SolarParameter, VisualElement> spreadFill = new Dictionary<SolarParameter, VisualElement>();
        readonly Dictionary<SolarParameter, Button> buttons = new Dictionary<SolarParameter, Button>();
        List<ParameterEffect> effects;
        int effectsTile = -2;
        float nextEffects;
        KardashevAscent.Presentation.Planet.TilePicker picker;

        static readonly (string Title, string Text, SolarParameter Picture)[] Steps =
        {
            ("Unknowns cost power", "Each value changes what a panel makes. The longer its bar, the less sure we are.", SolarParameter.Clouds),
            ("Send a robot", "Build a Research Station first. Then press ► next to a value: a robot measures there for a few hours.", SolarParameter.OpticalDepth),
            ("Sometimes a puzzle", "Usually the robot works the value out. Now and then it brings readings: solve them at the top.", SolarParameter.Spectrum),
            ("Bars shrink", "Measured values drop to zero. Know your latitude φ to unlock the best panel angle (B).", SolarParameter.Latitude),
        };

        protected override void BuildBody()
        {
            BuildTutorial();

            // The panel here, and how much of its year is still unknown.
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            total = Label("", "value");
            total.style.flexGrow = 1;
            head.Add(total);
            var help = MakeButton("?", () => ShowTutorial(0));
            help.tooltip = "How measuring works";
            head.Add(help);
            Body.Add(head);
            Body.Add(ParameterLook.Bar(out totalFill, new Color(1f, 0.45f, 0.35f), 8f));
            totalKnown = Label("", "caption");
            Body.Add(totalKnown);
            where = Label("", "caption");
            Body.Add(where);
            // Measuring and experiments happen at a Research Station (director 2026-11-14).
            stationNote = Label("", "body");
            stationNote.AddToClassList("wrap");
            stationNote.style.color = UiPalette.Warning;
            stationNote.style.marginTop = 4;
            Body.Add(stationNote);
            marginNote = Label("", "caption");
            marginNote.AddToClassList("wrap");
            marginNote.style.marginTop = 2;
            Body.Add(marginNote);
            InfoTip.After(marginNote, "Until a value is measured, buildings are designed for the cautious end of its range. " +
                                      "Panels deliver 1 minus the combined spread of the unknown light values (never below half). " +
                                      "Batteries charge only to 80 % until the surface temperature experiment says how hot or cold the cells get.");

            puzzleHead = Heading("PUZZLES");
            puzzles = new VisualElement();
            Body.Add(puzzles);
            solvedText = Text();
            solvedText.AddToClassList("good");

            // Experiments as a research tree (director 2026-11-15): survey the station's tile, then each experiment
            // after the one it needs.
            Heading("EXPERIMENTS · WHAT'S SAFE HERE");   // first: the experiments are what the player acts on (director 2026-11-20: could not find the tide gauge below the values)
            tree = new ResearchTreeView(ToggleExperiment, ToggleStationSurvey);
            Body.Add(tree);

            Heading("WHAT THE COLONY BELIEVES · UNTIL MEASURED");
            beliefs = Text();

            Heading("VALUES");
            foreach (SolarParameter p in (SolarParameter[])System.Enum.GetValues(typeof(SolarParameter)))
            {
                SolarParameter param = p;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 5;
                row.Add(ParameterLook.Badge(p));

                var mid = new VisualElement();
                mid.style.flexGrow = 1; mid.style.flexShrink = 1;
                var top = new VisualElement();
                top.style.flexDirection = FlexDirection.Row;
                Label name = Label(ParameterLook.Short(p), "robot-name");
                name.style.flexGrow = 1;
                top.Add(name);
                Label sp = Label("", "job-text");
                sp.style.flexGrow = 0;
                top.Add(sp);
                mid.Add(top);
                mid.Add(ParameterLook.Bar(out VisualElement fill, ParameterLook.Colour(p)));
                Label st = Label("", "job-text");
                mid.Add(st);
                mid.tooltip = $"{SolarUnknowns.Name(p)} · measured with {SolarUnknowns.Instrument(p)}";
                row.Add(mid);

                Button b = MakeButton("►", () => Toggle(param));
                b.style.width = 34;
                b.style.marginLeft = 8;
                row.Add(b);
                Body.Add(row);
                status[p] = st;
                spreadText[p] = sp;
                spreadFill[p] = fill;
                buttons[p] = b;
            }

        }

        ResearchTreeView tree;
        Label beliefs;

        void ToggleStationSurvey()
        {
            if (lastSim == null) return;
            int station = lastSim.FinishedStationTile(Tile(lastSim));
            if (station < 0) return;
            if (lastSim.IsSurveyOrdered(station)) lastSim.CancelSurvey(station);
            else lastSim.OrderSurvey(station);
        }

        void ToggleExperiment(KardashevAscent.Simulation.Planet.ExperimentKind k)
        {
            if (lastSim == null) return;
            if (lastSim.IsExperimentOrdered(k)) lastSim.CancelExperiment(k);
            else lastSim.OrderExperiment(k, Tile(lastSim));
        }

        // ---- Tutorial ----

        void BuildTutorial()
        {
            tutorial = new VisualElement();
            tutorial.style.marginBottom = 8;
            tutorial.style.paddingLeft = tutorial.style.paddingRight = 8;
            tutorial.style.paddingTop = tutorial.style.paddingBottom = 6;
            tutorial.style.backgroundColor = UiPalette.AccentA(0.10f);
            tutorial.style.borderTopLeftRadius = tutorial.style.borderTopRightRadius = tutorial.style.borderBottomLeftRadius = tutorial.style.borderBottomRightRadius = 0;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            tutorialPicture = new VisualElement();
            row.Add(tutorialPicture);
            var words = new VisualElement();
            words.style.flexGrow = 1; words.style.flexShrink = 1;
            tutorialStep = Label("", "robot-name");
            words.Add(tutorialStep);
            tutorialText = Label("", "job-text");
            words.Add(tutorialText);
            row.Add(words);
            tutorial.Add(row);
            var buttonsRow = new VisualElement();
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.alignItems = Align.Center;
            buttonsRow.style.marginTop = 4;
            tutorialCount = Label("", "caption");
            tutorialCount.style.flexGrow = 1;
            buttonsRow.Add(tutorialCount);
            buttonsRow.Add(MakeButton("Skip", CloseTutorial));
            tutorialNext = MakeButton("Next ►", () => ShowTutorial(tutorialAt + 1));
            buttonsRow.Add(tutorialNext);
            tutorial.Add(buttonsRow);
            Body.Add(tutorial);
            bool seen = false;
            try { seen = PlayerPrefs.GetInt(TutorialSeenKey, 0) == 1; } catch { }
            if (seen) tutorial.style.display = DisplayStyle.None;
            else ShowTutorial(0);
        }

        void ShowTutorial(int step)
        {
            if (step >= Steps.Length)
            {
                CloseTutorial();
                return;
            }
            tutorialAt = step;
            tutorial.style.display = DisplayStyle.Flex;
            var s = Steps[step];
            tutorialStep.text = $"{step + 1}. {s.Title}";
            tutorialText.text = s.Text;
            tutorialCount.text = $"{step + 1} / {Steps.Length}";
            tutorialNext.text = step == Steps.Length - 1 ? "Got it" : "Next ►";
            tutorialPicture.Clear();
            tutorialPicture.Add(Picture(step, s.Picture));
        }

        /// <summary>A small picture per step: a bar, the measure button, a puzzle card, a bar going to zero.</summary>
        static VisualElement Picture(int step, SolarParameter p)
        {
            var box = new VisualElement();
            box.style.width = 70;
            box.style.marginRight = 8;
            box.style.alignItems = Align.Center;
            box.Add(ParameterLook.Badge(p, 30f));
            if (step == 1)
            {
                var play = new Label("►");
                play.AddToClassList(UiType.Body);
                play.style.color = ParameterLook.Colour(p);
                box.Add(play);
                return box;
            }
            var bar = ParameterLook.Bar(out VisualElement fill, ParameterLook.Colour(p));
            bar.style.width = 60;
            bar.style.marginTop = 4;
            fill.style.width = Length.Percent(step == 0 ? 80 : step == 2 ? 45 : 4);
            box.Add(bar);
            if (step == 2)
            {
                var q = new Label("? = ?");
                q.AddToClassList(UiType.Secondary);
                q.style.color = ParameterLook.Colour(p);
                box.Add(q);
            }
            return box;
        }

        void CloseTutorial()
        {
            tutorial.style.display = DisplayStyle.None;
            try { PlayerPrefs.SetInt(TutorialSeenKey, 1); PlayerPrefs.Save(); } catch { }
        }

        // ---- Values ----

        IPlanetSimulation lastSim;
        int Tile(IPlanetSimulation sim)
        {
            if (picker == null) picker = Object.FindFirstObjectByType<KardashevAscent.Presentation.Planet.TilePicker>();
            int t = picker != null ? picker.SelectedTile : -1;
            return t >= 0 && !sim.GetTile(t).IsWater ? t : sim.BaseTileId;
        }

        static bool HasLightPuzzle(IPlanetSimulation sim, SolarParameter p)
        {
            foreach (ParameterPuzzle q in sim.ParameterPuzzles)
                if (q.Parameter == p) return true;
            return false;
        }

        static bool HasPuzzle(IPlanetSimulation sim, int tile)
        {
            foreach (LatitudePuzzle q in sim.LatitudePuzzles)
                if (q.Tile == tile) return true;
            return false;
        }

        void Toggle(SolarParameter p)
        {
            if (lastSim == null) return;
            if (lastSim.IsMeasureOrdered(p)) lastSim.CancelMeasure(p);
            else lastSim.OrderMeasure(p, Tile(lastSim));
            effectsTile = -2;
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            lastSim = sim;
            int puzzleShape = sim.LatitudePuzzles.Count + sim.ParameterPuzzles.Count * 100 + sim.SurveyPuzzles.Count * 10000 + sim.PendingPuzzles.Count * 1000000;
            if (puzzleShape != shownPuzzles)
            {
                shownPuzzles = puzzleShape;
                puzzles.Clear();
                // Each card can be folded away and done later (director 2026-11-15).
                foreach (LatitudePuzzle p in sim.LatitudePuzzles)
                    puzzles.Add(PuzzleShelf.Wrap(new LatitudePuzzleView(sim, p, result => { solvedText.text = result; effectsTile = -2; }),
                                                 PuzzleShelf.LatitudeKey(p.Tile), $"Latitude of tile #{p.Tile}"));
                foreach (ParameterPuzzle p in sim.ParameterPuzzles)
                    puzzles.Add(PuzzleShelf.Wrap(new LightPuzzleView(sim, p, result => { solvedText.text = result; effectsTile = -2; }),
                                                 PuzzleShelf.LightKey(p.Parameter), SolarUnknowns.Name(p.Parameter)));
                foreach (SurveyPuzzle p in sim.SurveyPuzzles)
                    puzzles.Add(PuzzleShelf.Wrap(new SurveyPuzzleView(sim, p, result => { solvedText.text = result; effectsTile = -2; }),
                                                 PuzzleShelf.SurveyKey(p.Tile), $"Ground survey of tile #{p.Tile}"));
                foreach (KardashevAscent.Simulation.Planet.ExperimentKind k in sim.PendingPuzzles)
                {
                    var kind = k;
                    puzzles.Add(PuzzleShelf.Wrap(new FitPuzzleView(sim, kind, result => { solvedText.text = result; effectsTile = -2; shownPuzzles = -1; }),
                                                 "fit-" + kind, KardashevAscent.Simulation.Planet.Experiments.Name(kind) + " · fit the readings"));
                }
                puzzleHead.style.display = shownPuzzles > 0 || solvedText.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            PuzzleShelf.ShowFocused(puzzles);
            int tile = Tile(sim);
            double lat = sim.Sphere.Latitude(tile);
            if (tile != effectsTile || Time.unscaledTime >= nextEffects)
            {
                // A 30° panel facing the equator: a typical panel, so the spreads compare across values.
                effects = sim.ParameterEffects(tile, 30.0 * Mathf.Deg2Rad, lat >= 0.0 ? Mathf.PI : 0.0);
                effectsTile = tile;
                nextEffects = Time.unscaledTime + 3f;
            }
            where.text = $"Tile #{tile}{(tile == sim.BaseTileId ? " · base" : "")} · click another tile to compare";
            double baseWh = 0.0, sq = 0.0, worst = 0.0;
            int known = 0;
            foreach (ParameterEffect e in effects)
            {
                baseWh = System.Math.Max(baseWh, (e.LowWh + e.HighWh) / 2.0);
                sq += e.HalfRangeWh * e.HalfRangeWh;
                worst = System.Math.Max(worst, e.HalfRangeWh);
                if (e.Known) known++;
            }
            double spread = System.Math.Sqrt(sq);
            double share = baseWh > 0.0 ? spread / baseWh : 0.0;
            total.text = baseWh > 0.0 ? $"Panel here ≈ {Format.Energy(baseWh)}/yr ± {share * 100.0:0.0} %" : "No sunlight model here";
            totalFill.style.width = Length.Percent((float)System.Math.Min(100.0, share * 400.0));   // 25 % fills the bar
            totalKnown.text = $"{known} of {effects.Count} values known";
            foreach (ParameterEffect e in effects)
            {
                SolarParameter p = e.Parameter;
                bool ordered = sim.IsMeasureOrdered(p);
                bool waitingPuzzle = p == SolarParameter.Latitude ? HasPuzzle(sim, tile) : HasLightPuzzle(sim, p);
                double pct = baseWh > 0.0 ? e.HalfRangeWh / baseWh * 100.0 : 0.0;
                spreadText[p].text = e.Known ? "known" : $"± {pct:0.0} %";
                spreadFill[p].style.width = Length.Percent(e.Known || worst <= 0.0 ? 0f : (float)(100.0 * e.HalfRangeWh / worst));
                status[p].text = e.Known
                    ? (p == SolarParameter.Clouds && !sim.Parameters.IsMeasured(p) ? "● no clouds here" : "● measured")
                    : ordered ? $"● measuring {sim.MeasureProgress(p) * 100.0:0} % · {ParameterLook.Tool(p)}"
                    : waitingPuzzle ? "● puzzle waiting ↑"
                    : e.Rough ? $"● rough · {ParameterLook.Tool(p)}"
                    : $"○ estimate · {ParameterLook.Tool(p)}";
                status[p].style.color = e.Known ? new Color(0.45f, 0.85f, 0.55f)
                    : ordered ? UiPalette.Accent
                    : waitingPuzzle ? new Color(1f, 0.78f, 0.35f)
                    : new Color(0.75f, 0.78f, 0.85f);
                Button b = buttons[p];
                string blocker = e.Known || ordered || waitingPuzzle ? null : sim.MeasureBlocker(p, tile);
                b.text = ordered ? "■" : waitingPuzzle ? "↑" : e.Rough ? "↻" : "►";
                b.tooltip = ordered ? "Stop measuring" : waitingPuzzle ? "Solve the puzzle above" : blocker ?? (e.Rough ? "Measure again" : "Send a robot to measure at the Research Station");
                b.SetEnabled(!e.Known && !waitingPuzzle && blocker == null);
                if (blocker != null && !e.Known)
                    status[p].text += " · needs a Research Station";
            }

            // Where measuring happens, and what not knowing costs.
            stationNote.text = sim.HasResearchStation ? ""
                : "No Research Station · build one (Stations)";
            stationNote.style.display = sim.HasResearchStation ? DisplayStyle.None : DisplayStyle.Flex;
            double design = sim.SolarDesignShare, battery = sim.BatteryUsableShare;
            marginNote.text = $"Margins · panels {design * 100.0:0} % · batteries {battery * 100.0:0} %";

            tree?.Refresh(sim);
            if (beliefs != null)
                beliefs.text =
                    "Air pressure: " + sim.PressureEstimate.Say("0.#", "kPa", 0.001) + (sim.PressureEstimate.IsExact ? "" : " (cables are rated at " + (sim.CableDesignShare * 100.0).ToString("0") + " % until measured)") + "\n" +
                    "Mean ground temperature: " + sim.TemperatureEstimate.Say("0", "C") + (sim.TemperatureEstimate.IsExact ? "" : " (robot warnings take the worse end)") + "\n" +
                    "Highest point: " + sim.HighestPointEstimate.Say("0.#", "km", 0.001) + "\n" +
                    "Open-sea tide: " + sim.TideRangeEstimate.Say("0.##", "m");
        }
    }

    /// <summary>Network (N): workforce v2 stage E. Shows what it will do until it is built.</summary>
    public sealed class NetworkWindow : SystemWindow
    {
        public override string Id => "network";
        public override string Title => "Network";
        public override KeyCode Key => KeyCode.N;
        public override int Order => 50;

        Label status, coverage, power, computing, robots;

        protected override void BuildBody()
        {
            status = Text();
            Heading("COVERAGE");
            coverage = Text();
            Heading("POWER");
            power = Text();
            Heading("COLONY COMPUTING");
            computing = Text();
            Heading("ROBOTS");
            robots = Text();
            Text("caption").text =
                "The lander is the network hub. A robot within range of the hub or a powered relay is connected: it hears " +
                "new orders, rule changes and the colony's needs, and hands in what it found. A robot out of range keeps " +
                "working on the program and priorities it last synced, and can't react to the colony's needs until it is " +
                "back in range. The hub and relays need grid power: in a brownout the whole network is down. " +
                "Build relays from the build bar to extend coverage.\n" +
                "AI levels: a Processor upgrade (Workshop) gives level 2: cut off, the robot keeps working by the colony's " +
                "needs as it last knew them. An Advanced processor gives level 3: it also reaches another robot 2 tiles away " +
                "instead of 1. A cut-off robot near a connected one syncs through it and hands over what it found; " +
                "otherwise its survey and measurement results wait until it is back in range. Levels 2 and 3 need colony " +
                "computing, the first load cut when power is short. Ranges, draws and unlocks are placeholder values.";
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            var net = sim.Network;
            var w = sim.Workforce;
            int off = w.DisconnectedCount;
            status.text = net.Powered
                ? (off == 0 ? "Network up · every robot connected." : $"Network up · {off} robot{(off == 1 ? "" : "s")} out of range.")
                : "NETWORK DOWN: the grid is in brownout, so the hub and relays are unpowered. Robots work on cached rules.";
            status.EnableInClassList("message", !net.Powered || off > 0);
            int relays = net.Relays.Count;
            coverage.text = $"{net.Coverage.Count} tiles covered (range {KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles} tiles from each node) · " +
                            $"hub at tile #{net.HubTile} · {relays} relay{(relays == 1 ? "" : "s")}" +
                            (relays == 0 ? ".\nNo relays yet: robots working more than " + KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles + " tiles from the lander drop off the network." : ".");
            power.text = $"Hub {Format.Power(KardashevAscent.Simulation.Robots.ColonyNetwork.HubPowerW)} · relays {Format.Power(relays * KardashevAscent.Simulation.Robots.ColonyNetwork.RelayPowerW)} · " +
                         $"total {Format.Power(net.PowerW)}, served before the colony's other loads.";
            double request = sim.ComputingRequestW;
            computing.text = request <= 0.0
                ? "Not needed: no robot has a processor yet (Workshop upgrade)."
                : sim.ComputingOn
                    ? $"Running · {Format.Power(request)} for the robots' processors."
                    : $"CUT: power is short, so the {Format.Power(request)} for processors is off. Robots work at AI level 1.";
            computing.EnableInClassList("message", request > 0.0 && !sim.ComputingOn);
            var sb = new System.Text.StringBuilder();
            foreach (var r in w.Robots)
            {
                int level = w.EffectiveAILevel(r);
                sb.Append(r.Name).Append($" (AI {level}{(level < r.AILevel ? $" of {r.AILevel}" : "")}): ");
                if (r.Connected) sb.Append("connected");
                else
                {
                    sb.Append(net.Powered ? $"out of range for {r.HoursOffline:0.0} h on tile #{r.Tile}"
                                          : $"cut off for {r.HoursOffline:0.0} h (network down) on tile #{r.Tile}");
                    var via = r.ExchangeVia.HasValue ? w.GetRobot(r.ExchangeVia.Value) : null;
                    sb.Append(via != null ? $" · syncing through {via.Name} nearby" : $" · running cached rules ({r.CachedProgram})");
                }
                if (r.UnsyncedFindings > 0)
                    sb.Append($" · carrying {r.UnsyncedFindings} result{(r.UnsyncedFindings == 1 ? "" : "s")} not yet handed in");
                sb.Append('\n');
            }
            robots.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "No robots.";
        }
    }
}

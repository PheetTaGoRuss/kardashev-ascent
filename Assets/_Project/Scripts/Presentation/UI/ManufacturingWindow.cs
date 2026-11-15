using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Manufacturing (U), remade (director 2026-11-15): big, with tabs (Robots, Refining, Queue, Reports). Every recipe
    /// is a card: a picture, a big name with "i" for the whole story, what it needs as material icons (red = not
    /// enough), what it makes, how long and how much power, and its buttons. Refining has Queue and Auto (keep a
    /// chosen amount in stock).
    /// </summary>
    public sealed class ManufacturingWindow : SystemWindow
    {
        public override string Id => "manufacturing";
        public override string Title => "Manufacturing";
        public override KeyCode Key => KeyCode.U;
        public override int Order => 30;
        protected override float Width => 840f;

        enum Tab { Robots, Refining, Queue, Reports }

        static readonly RobotKind[] Buildable =
            { RobotKind.General, RobotKind.Construction, RobotKind.Mining, RobotKind.Research, RobotKind.Maintenance };

        /// <summary>Auto refining keeps this many batches' worth in stock, cycling with each press (0 = off).</summary>
        static readonly int[] AutoSteps = { 0, 1, 3, 10 };

        Tab tab = Tab.Refining;
        Label status, message;
        VisualElement stock, content;
        readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        string shownShape;
        float nextRebuild;

        protected override void BuildBody()
        {
            status = Label("", "value");
            status.style.fontSize = 16;
            status.style.whiteSpace = WhiteSpace.Normal;
            Body.Add(status);
            InfoTip.After(status, "Robot Workshops build robots and upgrades. Refinery modules (Alumina plant, Electrolysis cell) go in " +
                                  "slots touching a Workshop; each runs one batch at a time on grid power, so more modules make more at once. " +
                                  "Batches take their input when they start and pause in a brownout.");
            stock = new VisualElement();
            stock.style.marginTop = 4;
            Body.Add(stock);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginTop = 8;
            tabs.style.marginBottom = 6;
            foreach (Tab t in new[] { Tab.Robots, Tab.Refining, Tab.Queue, Tab.Reports })
            {
                Tab chosen = t;
                var b = new Button(() => { tab = chosen; shownShape = null; }) { text = t.ToString() };
                b.AddToClassList("tab");
                b.style.fontSize = 15;
                b.style.height = 30;
                b.style.minWidth = 110;
                tabs.Add(b);
                tabButtons[t] = b;
            }
            Body.Add(tabs);
            message = Label("", "caption");
            message.AddToClassList("message");
            Body.Add(message);
            content = new VisualElement();
            Body.Add(content);
        }

        public override string LockedReason(IPlanetSimulation sim) =>
            sim != null && sim.WorkshopCount == 0 ? "build a Robot Workshop on a site first (build bar → Industry → Workshop)." : null;

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            int modules = sim.RefineryCount(RefineryKind.AluminaPlant) + sim.RefineryCount(RefineryKind.Electrolyser);
            status.text = $"{sim.WorkshopCount} Workshop{(sim.WorkshopCount == 1 ? "" : "s")} · {modules} refinery module{(modules == 1 ? "" : "s")} · " +
                          $"industry {Format.Power(sim.WorkshopPowerW)}";
            foreach (var kv in tabButtons)
                kv.Value.EnableInClassList("selected", kv.Key == tab);

            string shape = Shape(sim);
            if (shape == shownShape || Time.unscaledTime < nextRebuild)
                return;
            shownShape = shape;
            nextRebuild = Time.unscaledTime + 0.4f;
            var held = new List<Material>();
            foreach (Material m in Materials.All)
                if (sim.Materials[m] > 0.0) held.Add(m);
            MaterialChips.FillStock(stock, sim.Materials, held.ToArray(), 18f);
            content.Clear();
            switch (tab)
            {
                case Tab.Robots: BuildRobots(sim); break;
                case Tab.Refining: BuildRefining(sim); break;
                case Tab.Queue: BuildQueue(sim); break;
                default: BuildReports(sim); break;
            }
        }

        string Shape(IPlanetSimulation sim)
        {
            var s = new System.Text.StringBuilder();
            s.Append(tab).Append('|').Append(sim.MilestonesReached).Append('|').Append(sim.WorkshopCount).Append('|').Append(sim.Jobs.Count);
            foreach (Material m in Materials.All) s.Append('|').Append((int)sim.Materials[m]);
            s.Append('|').Append(sim.RefineQueue.Count);
            foreach (RefineryLine l in sim.RefineryLines()) s.Append('|').Append(l.Process).Append((int)(l.Progress * 100)).Append(l.Built);
            foreach (RefineProcess p in Refining.Processes) s.Append('|').Append((int)sim.AutoRefineTarget(p));
            if (tab == Tab.Queue)
                for (int i = 0; i < sim.Jobs.Count; i++) s.Append('|').Append((int)(sim.Jobs[i].Progress * 100));
            if (tab == Tab.Reports) s.Append('|').Append(sim.IndustrialReports.Count);
            return s.ToString();
        }

        // ---- cards ----

        /// <summary>A recipe card: picture, big name with "i", needs → makes, time and power, a reason in red, buttons.</summary>
        VisualElement Card(VisualElement picture, string name, string info, MaterialBill needs, MaterialStock have,
                           MaterialBill makes, string facts, string blocker, params Button[] actions)
        {
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            card.style.marginBottom = 6;
            card.style.paddingLeft = card.style.paddingRight = 8;
            card.style.paddingTop = card.style.paddingBottom = 6;
            card.style.backgroundColor = new Color(0.05f, 0.08f, 0.15f, 0.92f);
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = blocker == null ? UiPalette.Accent : UiPalette.Border;
            if (picture != null)
            {
                picture.style.marginRight = 10;
                card.Add(picture);
            }
            var mid = new VisualElement();
            mid.style.flexGrow = 1;
            mid.style.flexShrink = 1;
            var title = new Label(name);
            title.style.fontSize = 17;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            mid.Add(title);
            InfoTip.After(title, info);
            var flow = new VisualElement();
            flow.style.flexDirection = FlexDirection.Row;
            flow.style.alignItems = Align.Center;
            flow.style.flexWrap = Wrap.Wrap;
            flow.style.marginTop = 3;
            var needRow = new VisualElement();
            MaterialChips.Fill(needRow, needs, have, 20f);
            flow.Add(needRow);
            if (makes != null && !makes.IsEmpty)
            {
                var arrow = new Label("→");
                arrow.style.fontSize = 18;
                arrow.style.marginLeft = 6;
                arrow.style.marginRight = 6;
                flow.Add(arrow);
                var makeRow = new VisualElement();
                MaterialChips.Fill(makeRow, makes, null, 20f);
                flow.Add(makeRow);
            }
            mid.Add(flow);
            var factLine = new Label(facts);
            factLine.style.fontSize = 13;
            factLine.style.color = UiPalette.Muted;
            factLine.style.marginTop = 2;
            mid.Add(factLine);
            if (blocker != null)
            {
                var why = new Label(blocker);
                why.style.fontSize = 13;
                why.style.color = new Color(1f, 0.45f, 0.4f);
                why.style.whiteSpace = WhiteSpace.Normal;
                mid.Add(why);
            }
            card.Add(mid);
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Column;
            buttons.style.marginLeft = 8;
            foreach (Button b in actions)
            {
                b.style.minWidth = 130;
                b.style.height = 30;
                b.style.fontSize = 14;
                b.style.marginBottom = 3;
                buttons.Add(b);
            }
            card.Add(buttons);
            return card;
        }

        static Button Action(string text, System.Action onClick, bool primary = true, string tip = null)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList(primary ? "primary-button" : "small-button");
            if (tip != null) b.tooltip = tip;
            return b;
        }

        Label Section(string text)
        {
            var h = Label(text.ToUpperInvariant(), "heading section-head");
            h.style.marginTop = 8;
            content.Add(h);
            return h;
        }

        // ---- robots ----

        void BuildRobots(IPlanetSimulation sim)
        {
            Section("New robots · one Workshop job each");
            foreach (RobotKind kind in Buildable)
            {
                RobotKind k = kind;
                Recipe r = RobotTech.Robot(kind, false, false);
                var bill = new MaterialBill();
                foreach (var (m, kg) in r.Inputs) bill = bill.Add(m, kg);
                int unlock = RobotTech.UnlockMilestone(kind);
                string blocker = sim.MilestonesReached < unlock ? (unlock == int.MaxValue ? "Not available yet." : $"Unlocks at milestone {unlock}.")
                               : !bill.CoveredBy(sim.Materials) ? "Not enough materials (red)." : null;
                Button go = Action("Build", () => Say(sim, sim.OrderRobot(k), $"{k} robot ordered."), true,
                                   "Queue it at the Workshop. Copper can be aluminium and lithium sodium (stand-ins) if that is what you have.");
                go.SetEnabled(sim.MilestonesReached >= unlock);
                content.Add(Card(null, $"{kind} robot", RobotInfo(kind), bill, sim.Materials, null,
                                 $"{r.WorkHours:0} h of Workshop work · {r.EnergyWh / 1000.0:0} kWh", blocker, go));
            }
            var note = Label("Upgrades for a robot are ordered from the Robots window (G).", "caption");
            note.style.marginTop = 4;
            content.Add(note);
        }

        static string RobotInfo(RobotKind k)
        {
            switch (k)
            {
                case RobotKind.General: return "Does every basic job at an ordinary pace: build, mine, survey, research, run the Workshop.";
                case RobotKind.Construction: return "Builds and takes down twice as fast, and works the Workshop.";
                case RobotKind.Mining: return "Mines twice as fast; heavier tools.";
                case RobotKind.Research: return "Surveys, measures and runs experiments twice as fast (better sensors).";
                default: return "Repairs and turns panels; keeps the others running.";
            }
        }

        // ---- refining ----

        void BuildRefining(IPlanetSimulation sim)
        {
            foreach (RefineryKind kind in new[] { RefineryKind.AluminaPlant, RefineryKind.Electrolyser })
            {
                int n = sim.RefineryCount(kind);
                Section($"{Refining.ModuleName(kind)}s · {n} built");
                foreach (RefineryLine l in sim.RefineryLines())
                    if (l.Kind == kind) content.Add(LineBar(l));
                if (n == 0)
                {
                    var hint = Label($"Build an {Refining.ModuleName(kind)} in a slot touching a Robot Workshop (build bar → Industry).", "caption");
                    hint.style.whiteSpace = WhiteSpace.Normal;
                    content.Add(hint);
                }
                foreach (RefineProcess p in Refining.Processes)
                {
                    RefineRecipe r = Refining.Of(p);
                    if (r.Module != kind) continue;
                    RefineProcess process = p;
                    int queued = 0;
                    foreach (RefineProcess q in sim.RefineQueue) if (q == p) queued++;
                    double target = sim.AutoRefineTarget(p);
                    int step = 0;
                    for (int i = 0; i < AutoSteps.Length; i++) if (target >= AutoSteps[i] * r.OutputKg - 1e-6) step = i;
                    int next = AutoSteps[(step + 1) % AutoSteps.Length];
                    Button queue = Action(queued > 0 ? $"Queue 1 more ({queued})" : "Queue 1 batch", () => Say(sim, sim.OrderRefine(process), "Batch queued."));
                    Button auto = Action(target > 0.0 ? $"Auto: keep {MaterialChips.Kg(target)}" : "Auto: off",
                                         () => { sim.SetAutoRefine(process, next * r.OutputKg); shownShape = null; }, target > 0.0,
                                         "Auto starts batches by itself whenever the stock is below this amount. Press to change.");
                    string facts = $"{Days(r.Hours)} a batch · {r.EnergyWh / 1000.0:0} kWh ({Format.Power(r.PowerW)} while it runs)" +
                                   (r.VentedKg > 0.0 ? $" · {r.VentedKg:0} kg {r.VentedName} vented" : "") +
                                   (r.SlagKg > 0.0 ? $" · {r.SlagKg:0} kg slag" : "");
                    string blocker = sim.RefineryCount(kind) == 0 ? null : sim.RefineBlocker(p);
                    content.Add(Card(new GuidePicture(r.Output, 52f), r.Name, r.What + Chemistry(p),
                                     new MaterialBill().Add(r.Input, r.InputKg), sim.Materials, new MaterialBill().Add(r.Output, r.OutputKg),
                                     facts, blocker, queue, auto));
                }
            }
        }

        static string Chemistry(RefineProcess p)
        {
            switch (p)
            {
                case RefineProcess.AluminaFromAnorthosite:
                    return $" Anorthosite is about {Refining.AnorthositeAluminaShare * 100:0} % alumina; about {Refining.AluminaRecovery * 100:0} % of it is recovered.";
                case RefineProcess.AluminaFromRock:
                    return $" Ordinary rock is only about {Refining.RockAluminaShare * 100:0} % alumina: mine anorthosite in highlands for twice the yield.";
                case RefineProcess.Aluminium:
                    return $" {Refining.AluminaPerAluminium:0.00} kg alumina and about {Refining.SmeltWhPerKg / 1000.0:0.#} kWh per kg of aluminium (real smelters: 13–15 kWh/kg). Grade: 99.7 %, fine for wiring.";
                case RefineProcess.PureAluminium:
                    return " Grade 99.99 %: needed for advanced processors (chip wiring).";
                case RefineProcess.SodiumFromSalt:
                    return $" {Refining.SaltPerSodium:0.00} kg salt per kg of sodium (NaCl → Na + ½ Cl₂), about {Refining.SodiumWhPerKg / 1000.0:0} kWh/kg.";
                default:
                    return " Only where there is no salt (a planet that never had seas).";
            }
        }

        VisualElement LineBar(RefineryLine l)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            string what = !l.Built ? "being built" : l.Process.HasValue ? Refining.Of(l.Process.Value).Name : "idle";
            var name = new Label($"Tile {l.Tile} · slot {l.Slot}: {what}");
            name.style.width = 330;
            name.style.fontSize = 14;
            row.Add(name);
            var bar = new VisualElement();
            bar.style.flexGrow = 1;
            bar.style.height = 10;
            bar.style.backgroundColor = new Color(0.2f, 0.2f, 0.23f);
            var fill = new VisualElement();
            fill.style.height = 10;
            fill.style.width = Length.Percent((float)(l.Progress * 100.0));
            fill.style.backgroundColor = UiPalette.Accent;
            bar.Add(fill);
            row.Add(bar);
            var left = new Label(l.Process.HasValue ? $" {Days(l.HoursLeft)} left" : "");
            left.style.width = 110;
            left.style.fontSize = 13;
            row.Add(left);
            return row;
        }

        // ---- queue and reports ----

        void BuildQueue(IPlanetSimulation sim)
        {
            Section("Workshop jobs");
            if (sim.Jobs.Count == 0) content.Add(Label("No jobs queued.", "caption"));
            for (int i = 0; i < sim.Jobs.Count; i++)
            {
                int index = i;
                ManufacturingJob j = sim.Jobs[i];
                string where = i < sim.WorkshopCount ? $"{j.Progress * 100.0:0} % of {j.Recipe.WorkHours:0} h" : "waiting for a free Workshop";
                content.Add(Row($"{i + 1}. {j.Describe()} · {where}", Action("Cancel", () => { sim.CancelJob(index); Changed(sim); }, false)));
            }
            Section("Refinery orders");
            if (sim.RefineQueue.Count == 0) content.Add(Label("None waiting.", "caption"));
            for (int i = 0; i < sim.RefineQueue.Count; i++)
            {
                int index = i;
                RefineProcess p = sim.RefineQueue[i];
                string why = sim.RefineBlocker(p);
                content.Add(Row($"{i + 1}. {Refining.Of(p).Name}" + (why != null ? $" · waiting: {why}" : " · next free module"),
                                Action("Cancel", () => { sim.CancelRefineOrder(index); shownShape = null; }, false)));
            }
            Section("Running");
            foreach (RefineryLine l in sim.RefineryLines()) content.Add(LineBar(l));
        }

        VisualElement Row(string text, Button button)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            var l = new Label(text);
            l.style.flexGrow = 1;
            l.style.fontSize = 14;
            l.style.whiteSpace = WhiteSpace.Normal;
            row.Add(l);
            row.Add(button);
            return row;
        }

        void BuildReports(IPlanetSimulation sim)
        {
            var list = sim.IndustrialReports;
            if (list.Count == 0) content.Add(Label("No reports yet.", "caption"));
            for (int i = list.Count - 1; i >= 0 && i >= list.Count - 12; i--)
            {
                var l = new Label($"Day {(int)(list[i].TimeHours / 24.0) + 1} · {list[i].Facility}: {list[i].Message}");
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.fontSize = 14;
                l.style.marginBottom = 3;
                if (list[i].IsProblem) l.style.color = new Color(1f, 0.6f, 0.45f);
                content.Add(l);
            }
        }

        static string Days(double hours) => hours >= 48.0 ? $"{hours / 24.0:0.#} days" : $"{hours:0} h";

        void Say(IPlanetSimulation sim, string why, string ok)
        {
            message.text = why ?? ok;
            Changed(sim);
        }

        void Changed(IPlanetSimulation sim)
        {
            sim.RefreshWork();
            shownShape = null;
            nextRebuild = 0f;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Operations (I): how this expedition is run (mission operations 6, MO-15). The light-time banner, the uplink (which
    /// robots run which rulebook version), the rulebook with a sentence-builder editor, plant cards (buffers, byproduct
    /// policy, blocker, heat), depots with stock targets, and the Operations log with filters. Reports and settings: what a
    /// cut-off robot saw shows as "not known yet" until it syncs. It computes nothing; the simulation does.
    /// </summary>
    public sealed class OperationsWindow : SystemWindow
    {
        public override string Id => "operations";
        public override string Title => "Operations";
        public override KeyCode Key => KeyCode.I;
        public override int Order => 35;
        protected override float Width => 760f;

        enum LogFilter { All, ActNow, Watch, Rules, Syncs, Plants, Notices }

        Label banner, uplink, plants, depots, history, message;
        VisualElement rulebook, editor, logList, targetRow;
        readonly List<Rule> draft = new List<Rule>();
        int loadedVersion = -1;
        Rule editing;
        string note = "";
        LogFilter filter = LogFilter.All;
        string shownLog, shownPlants, shownDepots, shownWhy, shownStores, shownDiscrepancy;
        WhyChain storesWhy, discrepancyWhy;
        VisualElement depotsWhy;
        VisualElement plantRows;
        float nextRebuild;
        int targetTile;
        Material targetMaterial = Material.Iron;
        float targetKg = 100f;

        protected override void BuildBody()
        {
            banner = Text("value");
            banner.AddToClassList(UiType.Body);
            InfoTip.After(banner, "Real expeditions cannot be run from home: the light from Earth takes years. So this colony runs on a written rulebook " +
                                  "of decisions made in advance, each with its reason, and reports what happened. The distance is from the NASA Exoplanet Archive.");
            Heading("UPLINK");
            uplink = Text();
            InfoTip.After(uplink, "A robot follows the rulebook version the colony network last gave it. One that is cut off keeps its last version " +
                                  "and syncs the moment it is back in contact.");
            Heading("RULEBOOK");
            message = Text("message");
            rulebook = new VisualElement();
            Body.Add(rulebook);
            editor = new VisualElement();
            Body.Add(editor);
            history = Text("caption");
            Heading("PLANTS");
            plantRows = new VisualElement();
            Body.Add(plantRows);
            Heading("DEPOTS AND STOCK TARGETS");
            depots = Text();
            depotsWhy = new VisualElement();
            Body.Add(depotsWhy);
            targetRow = new VisualElement();
            targetRow.style.flexDirection = FlexDirection.Row;
            targetRow.style.flexWrap = Wrap.Wrap;
            Body.Add(targetRow);
            Heading("OPERATIONS LOG");
            var filters = new VisualElement();
            filters.style.flexDirection = FlexDirection.Row;
            foreach (LogFilter f in Enum.GetValues(typeof(LogFilter)))
            {
                LogFilter chosen = f;
                var b = new Button(() => { filter = chosen; shownLog = null; }) { text = f.ToString() };
                b.AddToClassList("small-button");
                filters.Add(b);
            }
            Body.Add(filters);
            logList = new VisualElement();
            Body.Add(logList);
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            RefreshBanner();
            RefreshUplink(sim);
            if (loadedVersion != sim.RulebookVersion && editing == null)
                LoadDraft(sim);
            RefreshHistory(sim);
            RefreshPlants(sim);
            RefreshDepots(sim);
            RefreshLog(sim);
        }

        // ---- light time ----

        void RefreshBanner()
        {
            SimulationRunner runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            double pc = runner != null && runner.Record != null && runner.Record.star != null ? runner.Record.star.distancePc : 0.0;
            if (pc <= 0.0) { banner.text = "A practice or home world: no distance in the NASA archive. On a real exoplanet the light from Earth takes years, so the colony runs on its rulebook."; return; }
            double ly = HaulPhysics.LightYearsFromParsecs(pc);
            banner.text = $"Earth is {ly:0.##} light-years away: a question and its answer take {2.0 * ly:0.#} years. This expedition runs on its rulebook. (NASA Exoplanet Archive)";
        }

        // ---- uplink ----

        void RefreshUplink(IPlanetSimulation sim)
        {
            int current = sim.RulebookVersion;
            if (current == 0) { uplink.text = "No rulebook yet. Robots follow their group's settings."; return; }
            var robots = sim.Workforce.Robots;
            int onCurrent = robots.Count(r => sim.RobotRulebookVersion(r.Id) == current);
            string text = $"Rulebook v{current}: on {onCurrent} of {robots.Count} robots · plants on v{sim.PlantRulebookVersion}";
            foreach (Robot r in robots)
            {
                int v = sim.RobotRulebookVersion(r.Id);
                if (v != current) text += $"\n  {r.Name} still on v{v}" + (r.Connected ? "" : $" · cut off {r.HoursOffline:0} h");
            }
            uplink.text = text;
        }

        // ---- rulebook ----

        void LoadDraft(IPlanetSimulation sim)
        {
            draft.Clear();
            RulebookVersion v = sim.RulebookHistory.Count > 0 ? sim.RulebookHistory[sim.RulebookHistory.Count - 1] : null;
            if (v != null) foreach (Rule r in v.Rules) draft.Add(r.Copy());
            loadedVersion = sim.RulebookVersion;
            RebuildRulebook(sim);
        }

        void RefreshHistory(IPlanetSimulation sim)
        {
            var lines = sim.RulebookHistory.Reverse().Take(6).Select(v => $"v{v.Number} · hour {v.EditedAtHours:0} · {v.Rules.Count} rule(s)" + (string.IsNullOrEmpty(v.Note) ? "" : " · " + v.Note));
            history.text = "History: " + string.Join("   |   ", lines);
        }

        void RebuildRulebook(IPlanetSimulation sim)
        {
            rulebook.Clear();
            editor.Clear();
            for (int i = 0; i < draft.Count; i++)
            {
                int index = i;
                Rule rule = draft[i];
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                var on = new Toggle { value = rule.Enabled };
                on.RegisterValueChangedCallback(e => rule.Enabled = e.newValue);
                row.Add(on);
                var name = Label($"{i + 1}. {rule.Name}", "body");
                name.style.flexGrow = 1;
                name.tooltip = rule.Rationale;
                row.Add(name);
                row.Add(Small("▲", () => { if (index > 0) { draft.RemoveAt(index); draft.Insert(index - 1, rule); RebuildRulebook(sim); } }));
                row.Add(Small("▼", () => { if (index < draft.Count - 1) { draft.RemoveAt(index); draft.Insert(index + 1, rule); RebuildRulebook(sim); } }));
                row.Add(Small(editing == rule ? "close" : "edit", () => { editing = editing == rule ? null : rule; RebuildRulebook(sim); }));
                row.Add(Small("✕", () => { draft.RemoveAt(index); if (editing == rule) editing = null; RebuildRulebook(sim); }));
                rulebook.Add(row);
                var sentence = Label(Sentence(rule), "caption");
                sentence.AddToClassList("wrap");
                rulebook.Add(sentence);
            }
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.flexWrap = Wrap.Wrap;
            bar.style.marginTop = 4;
            bar.Add(Small("+ rule", () =>
            {
                var r = new Rule { Name = "New rule", Rationale = "", Conditions = new List<RuleCondition> { new RuleCondition { Kind = ConditionKind.RobotBatteryBelow, Value = 25.0 } }, Response = new RuleResponse { Kind = ResponseKind.SafeMode } };
                draft.Add(r); editing = r; RebuildRulebook(sim);
            }));
            bar.Add(Small("discard changes", () => { editing = null; LoadDraft(sim); }));
            var noteField = new TextField { value = note };
            noteField.style.minWidth = 220;
            noteField.RegisterValueChangedCallback(e => note = e.newValue);
            bar.Add(noteField);
            bar.Add(Small($"Publish v{sim.RulebookVersion + 1}", () =>
            {
                string problem = sim.PublishRulebook(draft, note);
                message.text = problem ?? $"Published v{sim.RulebookVersion}: robots receive it as the network reaches them.";
                if (problem == null) { editing = null; note = ""; LoadDraft(sim); }
            }));
            rulebook.Add(bar);
            if (editing != null) BuildEditor(sim);
        }

        static string Sentence(Rule r)
        {
            string scope = r.Scope == RuleScopeKind.AllRobots ? "Every robot" : r.Scope == RuleScopeKind.Group ? $"Group {r.ScopeId}"
                         : r.Scope == RuleScopeKind.RobotKind ? $"Every {(RobotKind)r.ScopeId} robot" : "Every plant";
            string when = string.Join(" and ", r.Conditions.Select(Condition));
            return $"{scope}: when {when}, then {Response(r.Response)}." + (r.CooldownHours > 0.0 ? $" (not again within {r.CooldownHours:0.#} h)" : "") + (string.IsNullOrWhiteSpace(r.Rationale) ? "  [needs a rationale]" : $"  Because: {r.Rationale}");
        }

        static string Condition(RuleCondition c)
        {
            switch (c.Kind)
            {
                case ConditionKind.RobotBatteryBelow: return $"its battery is below {c.Value:0} %";
                case ConditionKind.RobotBatteryAbove: return $"its battery is above {c.Value:0} %";
                case ConditionKind.RobotConditionBelow: return $"its condition is below {c.Value:0} %";
                case ConditionKind.GridBatteryBelow: return $"the grid battery is below {c.Value:0} %";
                case ConditionKind.BrownoutNow: return "there is a brownout";
                case ConditionKind.IsNight: return "it is night";
                case ConditionKind.IsDay: return "it is day";
                case ConditionKind.CutOffForHours: return $"it has been cut off for over {c.Value:0.#} h";
                case ConditionKind.DepotStockBelow: return $"depot {c.Tile} holds under {c.Value:0} kg {Materials.Name(c.Material).ToLowerInvariant()}";
                case ConditionKind.DepotStockAbove: return $"depot {c.Tile} holds over {c.Value:0} kg {Materials.Name(c.Material).ToLowerInvariant()}";
                case ConditionKind.PlantBlockedBy: return $"a plant is blocked ({c.Blocker}){(c.Value > 0.0 ? $" over {c.Value:0.#} h" : "")}";
                case ConditionKind.PlantOutputBufferAbove: return $"a plant's output buffer is over {c.Value:0} % full";
                case ConditionKind.SurfaceTemperatureOutside: return $"the measured surface temperature is outside {c.Value:0} to {c.Value2:0} °C";
                case ConditionKind.StellarFlareActive: return "the star is flaring";
                default: return c.Kind.ToString();
            }
        }

        static string Response(RuleResponse r)
        {
            switch (r.Kind)
            {
                case ResponseKind.SetTaskPriority: return $"treat {r.Task} work as {r.Priority}";
                case ResponseKind.SetProgram: return $"switch to the {r.Program} program";
                case ResponseKind.SafeMode: return "go to the nearest charger and wait (safe mode)";
                case ResponseKind.HoldPosition: return "hold position";
                case ResponseKind.SetStockTarget: return $"keep {r.Kg:0} kg {Materials.Name(r.Material).ToLowerInvariant()} in depot {r.Tile}";
                case ResponseKind.RequestHaul: return $"haul {r.Kg:0} kg {Materials.Name(r.Material).ToLowerInvariant()} from tile {r.Tile} to tile {r.ToTile}";
                case ResponseKind.PausePlant: return "pause the plant";
                case ResponseKind.ResumePlant: return "resume the plant";
                case ResponseKind.SetByproductPolicy: return $"{(r.Policy == ByproductPolicy.Vent ? "vent" : "store")} the plant's gas";
                case ResponseKind.Notify: return $"tell me: \"{r.Text}\"";
                default: return r.Kind.ToString();
            }
        }

        // ---- the sentence builder ----

        void BuildEditor(IPlanetSimulation sim)
        {
            Rule r = editing;
            editor.Clear();
            var box = new VisualElement();
            box.AddToClassList("panel");
            box.style.paddingLeft = box.style.paddingRight = 6;
            box.style.paddingTop = box.style.paddingBottom = 6;
            box.style.marginTop = 4;
            editor.Add(box);
            box.Add(Field("Name", new TextField { value = r.Name }, f => r.Name = f));
            var why = new TextField { value = r.Rationale, multiline = true };
            why.style.minHeight = 40;
            box.Add(Field("Because (required)", why, f => r.Rationale = f));
            var scope = new EnumField(r.Scope);
            scope.RegisterValueChangedCallback(e => { r.Scope = (RuleScopeKind)e.newValue; RebuildRulebook(sim); });
            box.Add(Row("Applies to", scope));
            if (r.Scope == RuleScopeKind.RobotKind)
            {
                var kind = new EnumField((RobotKind)r.ScopeId);
                kind.RegisterValueChangedCallback(e => { r.ScopeId = (int)(RobotKind)e.newValue; });
                box.Add(Row("Robot kind", kind));
            }
            else if (r.Scope == RuleScopeKind.Group)
            {
                var g = new IntegerField { value = r.ScopeId };
                g.RegisterValueChangedCallback(e => r.ScopeId = e.newValue);
                box.Add(Row("Group number", g));
            }
            box.Add(Label("WHEN (up to three, all must hold)", "caption"));
            for (int i = 0; i < r.Conditions.Count; i++)
            {
                RuleCondition c = r.Conditions[i];
                int index = i;
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.flexWrap = Wrap.Wrap;
                var kind = new EnumField(c.Kind);
                kind.RegisterValueChangedCallback(e => { c.Kind = (ConditionKind)e.newValue; RebuildRulebook(sim); });
                line.Add(kind);
                line.Add(Num(c.Value, v => c.Value = v));
                if (c.Kind == ConditionKind.SurfaceTemperatureOutside) line.Add(Num(c.Value2, v => c.Value2 = v));
                if (c.Kind == ConditionKind.DepotStockBelow || c.Kind == ConditionKind.DepotStockAbove)
                {
                    var tile = new IntegerField { value = c.Tile }; tile.style.width = 60; tile.RegisterValueChangedCallback(e => c.Tile = e.newValue); line.Add(tile);
                    var mat = new EnumField(c.Material); mat.RegisterValueChangedCallback(e => c.Material = (Material)e.newValue); line.Add(mat);
                }
                if (c.Kind == ConditionKind.PlantBlockedBy)
                {
                    var blocker = new EnumField(c.Blocker); blocker.RegisterValueChangedCallback(e => c.Blocker = (IndustrialBlocker)e.newValue); line.Add(blocker);
                }
                line.Add(Small("✕", () => { if (r.Conditions.Count > 1) { r.Conditions.RemoveAt(index); RebuildRulebook(sim); } }));
                box.Add(line);
            }
            if (r.Conditions.Count < Rule.MaxConditions)
                box.Add(Small("+ condition", () => { r.Conditions.Add(new RuleCondition { Kind = ConditionKind.IsNight }); RebuildRulebook(sim); }));
            box.Add(Label("THEN", "caption"));
            RuleResponse resp = r.Response;
            var rk = new EnumField(resp.Kind);
            rk.RegisterValueChangedCallback(e => { resp.Kind = (ResponseKind)e.newValue; RebuildRulebook(sim); });
            box.Add(rk);
            var pr = new VisualElement();
            pr.style.flexDirection = FlexDirection.Row;
            pr.style.flexWrap = Wrap.Wrap;
            switch (resp.Kind)
            {
                case ResponseKind.SetTaskPriority:
                    var tk = new EnumField(resp.Task); tk.RegisterValueChangedCallback(e => resp.Task = (TaskKind)e.newValue); pr.Add(tk);
                    var tp = new EnumField(resp.Priority); tp.RegisterValueChangedCallback(e => resp.Priority = (TaskPriority)e.newValue); pr.Add(tp);
                    break;
                case ResponseKind.SetProgram:
                    var pg = new EnumField(resp.Program); pg.RegisterValueChangedCallback(e => resp.Program = (OperatingProgram)e.newValue); pr.Add(pg);
                    break;
                case ResponseKind.SetStockTarget:
                case ResponseKind.RequestHaul:
                    var tile = new IntegerField { value = resp.Tile }; tile.style.width = 60; tile.RegisterValueChangedCallback(e => resp.Tile = e.newValue); pr.Add(tile);
                    if (resp.Kind == ResponseKind.RequestHaul)
                    {
                        var to = new IntegerField { value = resp.ToTile }; to.style.width = 60; to.RegisterValueChangedCallback(e => resp.ToTile = e.newValue); pr.Add(to);
                    }
                    var mat = new EnumField(resp.Material); mat.RegisterValueChangedCallback(e => resp.Material = (Material)e.newValue); pr.Add(mat);
                    pr.Add(Num(resp.Kg, v => resp.Kg = v));
                    break;
                case ResponseKind.SetByproductPolicy:
                    var po = new EnumField(resp.Policy); po.RegisterValueChangedCallback(e => resp.Policy = (ByproductPolicy)e.newValue); pr.Add(po);
                    break;
                case ResponseKind.Notify:
                    var tx = new TextField { value = resp.Text }; tx.style.minWidth = 260; tx.RegisterValueChangedCallback(e => resp.Text = e.newValue); pr.Add(tx);
                    break;
            }
            box.Add(pr);
            box.Add(Row("Not again within (hours)", Num(r.CooldownHours, v => r.CooldownHours = v)));
        }

        static VisualElement Row(string label, VisualElement field)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var l = Label(label, "caption");
            l.style.minWidth = 150;
            row.Add(l);
            row.Add(field);
            return row;
        }

        static VisualElement Field(string label, TextField f, Action<string> set)
        {
            f.RegisterValueChangedCallback(e => set(e.newValue));
            f.style.flexGrow = 1;
            return Row(label, f);
        }

        static FloatField Num(double value, Action<double> set)
        {
            var f = new FloatField { value = (float)value };
            f.style.width = 70;
            f.RegisterValueChangedCallback(e => set(e.newValue));
            return f;
        }

        static Button Small(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("small-button");
            return b;
        }

        // ---- plants ----

        void RefreshPlants(IPlanetSimulation sim)
        {
            var lines = new List<(int Tile, int Slot, string Text, ByproductPolicy Policy, string Blocker)>();
            foreach (RefineryLine line in sim.RefineryLines())
            {
                if (!line.Built) continue;
                PlantReport rep = sim.PlantReportAt(line.Tile, line.Slot);
                if (rep == null) continue;
                string state = sim.IsPlantPaused(line.Tile, line.Slot) ? "PAUSED by a rule" : rep.Blocker != IndustrialBlocker.None ? "BLOCKED: " + rep.Reason
                             : line.Process.HasValue ? $"running {line.Progress * 100.0:0} %" : "idle";
                lines.Add((line.Tile, line.Slot, $"{Refining.ModuleName(line.Kind)} {line.Tile}.{line.Slot} · {state} · buffers in {rep.InputBufferKg:0} / out {rep.OutputBufferKg:0} kg"
                                                 + (rep.WasteHeatW > 0.0 ? $" · heat {Format.Power(rep.WasteHeatW)}" : ""), rep.Policy, rep.Blocker.ToString()));
            }
            string shape = string.Join("|", lines.Select(l => l.Text + l.Policy + l.Blocker));
            if (shape == shownPlants) return;
            shownPlants = shape;
            plantRows.Clear();
            if (lines.Count == 0) plantRows.Add(Label("No refinery modules yet. Build them next to a Robot Workshop.", "caption"));
            foreach (var l in lines)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                var t = Label(l.Text, "body");
                t.AddToClassList("wrap");
                t.style.flexGrow = 1;
                row.Add(t);
                int tile = l.Tile, slot = l.Slot;
                ByproductPolicy policy = l.Policy;
                var toggle = Small(policy == ByproductPolicy.Vent ? "gas: vent" : "gas: store", () =>
                {
                    sim.SetByproductPolicy(tile, slot, policy == ByproductPolicy.Vent ? ByproductPolicy.Store : ByproductPolicy.Vent);
                    shownPlants = null;
                });
                toggle.tooltip = "Store: the gas goes to a gas tank and a full store stops the plant. Vent: it is released (logged; chlorine is toxic).";
                row.Add(toggle);
                plantRows.Add(row);
                var (plantSteps, window, actionText) = WhyChain.PlantSteps(l.Blocker);
                if (plantSteps.Count > 0)
                {
                    var chain = new WhyChain();
                    chain.Set($"Plant {l.Tile}.{l.Slot} is blocked", plantSteps, actionText, () => SystemTabs.Open(window));
                    plantRows.Add(chain);
                }
            }
        }

        // ---- depots ----

        void RefreshDepots(IPlanetSimulation sim)
        {
            var lines = new List<string>();
            foreach (var g in sim.Holdings().Where(h => h.Kind == PlaceKind.Depot).GroupBy(h => h.Id).OrderBy(g => g.Key))
                lines.Add($"Tile {g.Key}: " + string.Join(", ", g.Where(h => h.Kg >= 0.5).Select(h => $"{Materials.Name(h.Material).ToLowerInvariant()} {h.Kg:0}")));
            var targets = sim.StockTargets();
            foreach (var t in targets) lines.Add($"Target: tile {t.Tile} keeps {t.Kg:0} kg {Materials.Name(t.Material).ToLowerInvariant()} (now {sim.KgAt(t.Tile, t.Material):0})");
            var haul = sim.HaulOrders();
            foreach (var h in haul) lines.Add($"Haul {h.Id}: {h.Kg:0} kg {Materials.Name(h.Material).ToLowerInvariant()} from tile {h.From} to {h.To}: {sim.HaulCostText(h.From, h.To, h.Kg)}" + (sim.HaulBlockedReason(h.Id) != null ? " · WAITING: " + sim.HaulBlockedReason(h.Id) : ""));
            foreach (var site in sim.DepotSites(3))
            {
                double have = sim.Materials[KardashevAscent.Simulation.Resources.Material.Copper];
                string need = site.CopperKg > have ? $" · needs {site.CopperKg:0} kg copper, you have {have:0}: mine first" : "";
                lines.Add($"Depot advice: build a depot (storage yard) with a charging bay at tile {site.Tile}" + (site.LineTiles > 0 ? $" and a power line of {site.LineTiles} tile(s)" : "") +
                          $": {site.CopperKg:0} kg copper, {site.BuildHours:0} h of building; it would save or make possible about {site.SavedWhPerDay:0} Wh a day of hauling (advice only: build where you choose){need}");
            }
            lines.Add($"◆ rules: climbing costs {HaulPhysics.ClimbEffortShare * 100.0:0.#} % of m·g·Δh; a leg may use {(1.0 - KardashevAscent.Simulation.Robots.RobotSpecs.ReturnToChargeFraction) * 100.0:0} % of a battery; a load that does not fit is split to a quarter.");
            foreach (string wl in sim.WreckLines()) lines.Add(wl);
            lines.Add($"{haul.Count} haul(s) under way · {sim.HaulTotals.KgMoved:0} kg moved so far, {sim.HaulTotals.Wh / 1000.0:0.0} kWh spent hauling");
            string shape = string.Join("\n", lines);
            if (shape != shownDepots)
            {
                shownDepots = shape;
                depots.text = shape;
            }
            // why? chains for hauls that are waiting, from the simulation's structured reason (UI information design, stage 5)
            var waiting = haul.Where(h => sim.HaulBlockedReason(h.Id) != null).Select(h => (h.Id, h.From, h.To, Why: sim.HaulWhyOf(h.Id))).ToList();
            var exhaustedNow = sim.ExhaustedDeposits();
            var outposts = sim.OpenWork().Where(w => w.Kind == KardashevAscent.Simulation.Robots.TaskKind.Build).Select(w => w.Tile).Distinct().Select(t => (Tile: t, Text: sim.OutpostText(t))).Where(o => o.Text.Length > 0).ToList();
            string whyShape = string.Join("|", waiting.Select(w => $"{w.Id}:{w.Why.Kind}:{w.Why.StopTile}:{w.Why.NeedWh:0}")) + "#" + string.Join(",", exhaustedNow.Select(e => e.Tile + ":" + e.Material)) + "#" + string.Join(",", outposts.Select(o => o.Tile + ":" + o.Text + sim.OutpostStages(o.Tile))); 
            if (whyShape != shownWhy)
            {
                shownWhy = whyShape;
                depotsWhy.Clear();
                foreach (var w in waiting)
                {
                    var chain = new WhyChain();
                    int hid = w.Id;
                    if (sim.HaulWaitsForStorm(hid))
                        chain.Set($"Haul {w.Id} waits (tile {w.From} to {w.To})", WhyChain.HaulSteps(w.Why.Kind, w.Why.NeedWh, w.Why.BudgetWh, w.Why.StopTile),
                                  "Go anyway", () => { sim.GoAnywayHaul(hid); shownWhy = null; });
                    else
                        chain.Set($"Haul {w.Id} is waiting (tile {w.From} to {w.To})", WhyChain.HaulSteps(w.Why.Kind, w.Why.NeedWh, w.Why.BudgetWh, w.Why.StopTile),
                                  "Show depot advice", () => depots.parent?.Q<ScrollView>()?.ScrollTo(depots));
                    depotsWhy.Add(chain);
                }
                // a leapfrog outpost: whether a robot may go one way, and why (decided 2026-11-25)
                foreach (var o in outposts)
                {
                    var oc = new WhyChain();
                    oc.Set($"Outpost on tile {o.Tile}", new System.Collections.Generic.List<string>
                    {
                        sim.OutpostStages(o.Tile),
                        o.Text,
                        "the kit goes to the outpost's depot by ordinary round-trip hauls; the builder then goes one way with no cargo, only if its battery covers the outbound leg and the first stages at the cold capacity and the outpost's own panels will charge it to its return reserve",
                        "otherwise the round-trip rule stands: it must be able to drive back to a charger",
                    });
                    depotsWhy.Add(oc);
                }
                // a mine whose deposit is used up: why it stopped, and where to look next (B3)
                foreach (var ex in exhaustedNow.Take(3))
                {
                    var chain = new WhyChain();
                    var best = sim.BestDeposits(ex.Material, 5);
                    var m = ex.Material; int exTile = ex.Tile;
                    string name = Materials.Name(m).ToLowerInvariant();
                    var steps = new System.Collections.Generic.List<string>
                    {
                        $"the {name} deposit on tile {exTile} held about {ex.HeldKg / 1000.0:0} t and it is all mined",
                        "a mine stops when nothing is left; its order was removed",
                        Comminution.NeedsConcentrating(m, DepositMap.TypicalOreGrade(m) * sim.DepositShareLeft(exTile, m)) || DepositMap.TypicalOreGrade(m) < 0.5 ? $"mining {name} also paid {Comminution.KWhPerTonneProduct(m, DepositMap.TypicalOreGrade(m)):0} kWh of crushing and grinding per tonne at a typical ore grade (Bond's law): a poorer deposit costs more" : "this ore needs no grinding",
                    best.Count > 0 ? "other known deposits: " + string.Join(", ", best.Select(b => $"tile {b.Tile} ({b.OreGrade.Value * 100.0:0.##} % ore, {b.ShareLeft * 100.0:0} % left)")) : "no other known deposit: survey more tiles",
                    };
                    chain.Set($"Mine on tile {exTile} stopped: {name} used up", steps, "Find another deposit", () => { if (best.Count > 0) sim.OrderSurvey(best[0].Tile); SystemTabs.Open("research"); });
                    depotsWhy.Add(chain);
                }
            }
            // the latest discrepancy between what the colony showed and a new record, as a why? chain (K5)
            {
                var d = sim.RecentDiscrepancies.Count > 0 ? (KardashevAscent.Simulation.Knowledge.KnowledgeEvent?)sim.RecentDiscrepancies[sim.RecentDiscrepancies.Count - 1] : null;
                bool fresh = d.HasValue && sim.TimeHours - d.Value.New.KnownAt < 24.0 * 7.0;
                string dShape = fresh ? d.Value.Quantity + "|" + d.Value.New.Value + "|" + d.Value.OldValue : "";
                if (dShape != shownDiscrepancy)
                {
                    shownDiscrepancy = dShape;
                    if (discrepancyWhy == null) { discrepancyWhy = new WhyChain(); depotsWhy.parent.Insert(depotsWhy.parent.IndexOf(depotsWhy) + 1, discrepancyWhy); }
                    if (!fresh) discrepancyWhy.Set("Discrepancy", null);
                    else
                    {
                        var e = d.Value; string unit = e.New.Unit.Length > 0 ? " " + e.New.Unit : "";
                        discrepancyWhy.Set($"Discrepancy: {KardashevAscent.Simulation.Knowledge.Quantities.Name(e.Quantity)} ({e.Subject})", new System.Collections.Generic.List<string>
                        {
                            $"a new record from {e.New.Source} reads {e.New.Value:0.###} ±{e.New.Uncertainty:0.###}{unit}",
                            $"the colony showed {e.OldValue:0.###} ±{e.OldUncertainty:0.###}{unit}: the two ranges do not overlap",
                            "both records are kept; the display uses the most recent of the highest confidence",
                        }, "Open Knowledge", () => KnowledgeWindow.Focus(e.Subject, e.Quantity));
                    }
                }
            }
            // stored materials that are losing mass or reacting, as a why? chain ending in a button to the stores (M4)
            {
                var losing = new System.Collections.Generic.List<string>();
                for (int t = 0; t < sim.TileCount && losing.Count < 4; t++)
                    foreach (string note in sim.StorageNotes(t)) if (note.Contains("losing")) { losing.Add($"tile {t}: {note}"); if (losing.Count >= 4) break; }
                string stoShape = string.Join("|", losing);
                if (stoShape != shownStores)
                {
                    shownStores = stoShape;
                    if (storesWhy == null) { storesWhy = new WhyChain(); depotsWhy.parent.Insert(depotsWhy.parent.IndexOf(depotsWhy) + 1, storesWhy); }
                    storesWhy.Set("Stored materials are changing", losing.Count == 0 ? null : losing, "Show stores", () => depots.parent?.Q<ScrollView>()?.ScrollTo(depots));
                }
            }
            if (targetRow.childCount == 0)
            {
                targetRow.Add(Label("Keep", "caption"));
                var tile = new IntegerField { value = targetTile }; tile.style.width = 60; tile.RegisterValueChangedCallback(e => targetTile = e.newValue); targetRow.Add(tile);
                var mat = new EnumField(targetMaterial); mat.RegisterValueChangedCallback(e => targetMaterial = (Material)e.newValue); targetRow.Add(mat);
                var kg = new FloatField { value = targetKg }; kg.style.width = 70; kg.RegisterValueChangedCallback(e => targetKg = e.newValue); targetRow.Add(kg);
                targetRow.Add(Small("set target (0 clears)", () => { sim.SetStockTarget(targetTile, targetMaterial, targetKg); shownDepots = null; }));
            }
        }

        // ---- log ----

        bool Shown(OperationRecord r)
        {
            switch (filter)
            {
                case LogFilter.ActNow: return r.Priority == LogPriority.ActNow;
                case LogFilter.Watch: return r.Priority >= LogPriority.Watch;
                case LogFilter.Rules: return r.Quantity == "Rule fired" || r.Quantity == "Player order wins";
                case LogFilter.Syncs: return r.Quantity == "Rulebook synced" || r.Quantity == "Rulebook published";
                case LogFilter.Plants: return r.Subject.StartsWith("Plant");
                case LogFilter.Notices: return r.Quantity == "Notice";
                default: return true;
            }
        }

        void RefreshLog(IPlanetSimulation sim)
        {
            // ranked into three priorities, newest first within each; a quiet colony shows its info lines, a troubled one shows what to do first
            var records = sim.OperationsLog.Where(Shown).Reverse().Take(60).OrderByDescending(r => r.Priority).ThenByDescending(r => r.Id).Take(40).ToList();
            string shape = filter + "|" + string.Join("|", records.Select(r => r.Id + (r.IsKnown ? "k" : "u")));
            if (shape == shownLog) return;
            shownLog = shape;
            logList.Clear();
            if (records.Count == 0) logList.Add(Label("Nothing logged yet.", "caption"));
            LogPriority? lastPriority = null;
            foreach (OperationRecord r in records)
            {
                if (lastPriority != r.Priority)
                {
                    lastPriority = r.Priority;
                    var head = Label(r.Priority == LogPriority.ActNow ? "▲ ACT NOW" : r.Priority == LogPriority.Watch ? "● WATCH" : "· INFO", "heading");
                    head.style.marginTop = 6;
                    if (r.Priority == LogPriority.ActNow) head.style.color = UiPalette.Critical;
                    else if (r.Priority == LogPriority.Watch) head.style.color = UiPalette.Warning;
                    logList.Add(head);
                }
                string known = r.IsKnown ? (r.KnownAtHours > r.ObservedAtHours + 0.01 ? $" · known at hour {r.KnownAtHours:0}" : "") : " · not known yet (the robot is cut off)";
                var l = Label($"hour {r.ObservedAtHours:0} · {r.Subject} · {r.Text}{known}", r.IsKnown ? "body" : "caption");
                l.AddToClassList("wrap");
                logList.Add(l);
            }
        }
    }
}

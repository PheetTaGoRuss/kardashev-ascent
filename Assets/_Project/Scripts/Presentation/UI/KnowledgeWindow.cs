using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Knowledge;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Knowledge (F7, K4, decided 2026-11-25): what the colony knows, by subject and quantity, from the knowledge store. Each entry shows the value with its range, the mark, the
    /// source, the confidence, its age (stale ones flagged) and a history sparkline; conflicting records are listed; unknown or stale entries offer Measure. Records a robot holds
    /// and has not synchronised are listed apart (they are not known yet). It only reads the store.
    /// </summary>
    public sealed class KnowledgeWindow : SystemWindow
    {
        public override string Id => "knowledge";
        public override string Title => "Knowledge";
        public override KeyCode Key => KeyCode.F7;
        public override int Order => 42;
        protected override float Width => 900f;

        static (Subject Subject, Quantity Quantity)? pendingFocus;

        /// <summary>Open the window on one entry (the "History ›" link of a value).</summary>
        public static void Focus(Subject subject, Quantity quantity)
        {
            pendingFocus = (subject, quantity);
            SystemTabs.Open("knowledge");
        }

        TextField search;
        DropdownField subjectFilter;
        VisualElement list;
        Label summary;
        string shown = "";
        (Subject, Quantity)? focused;

        protected override void BuildBody()
        {
            summary = Text("caption");
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            search = new TextField("Search") { value = "" };
            search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(_ => shown = null);
            subjectFilter = new DropdownField(new List<string> { "All subjects", "Planet", "Tile", "Building", "Robot" }, 0);
            subjectFilter.RegisterValueChangedCallback(_ => shown = null);
            subjectFilter.style.marginLeft = 8; subjectFilter.style.minWidth = 140;
            row.Add(search); row.Add(subjectFilter);
            Body.Add(row);
            list = new VisualElement();
            Body.Add(list);
        }

        static string Sparkline(IReadOnlyList<Observation> history, int width = 24)
        {
            var known = history.Where(h => h.IsKnown).ToList();
            if (known.Count < 2) return "";
            var take = known.Skip(Math.Max(0, known.Count - width)).Select(h => h.Value).ToList();
            double lo = take.Min(), hi = take.Max();
            const string bars = "▁▂▃▄▅▆▇█";
            return string.Concat(take.Select(v => bars[hi - lo < 1e-12 ? 3 : Math.Min(7, (int)((v - lo) / (hi - lo) * 7.999))]));
        }

        public static string Describe(Observation o, double now)
        {
            string unit = o.Unit.Length > 0 ? " " + o.Unit : "";
            string value = o.Uncertainty > 0 ? $"{o.Value:0.###} ±{o.Uncertainty:0.###}{unit}" : $"{o.Value:0.###}{unit}";
            double age = KnowledgeBase.AgeHours(o, now);
            return $"{value} · {o.Source} · {o.Confidence.ToString().ToLowerInvariant()} confidence · {Quantities.Age(age)}" + (KnowledgeBase.IsStale(o, now) ? " · ⚠ stale" : "");
        }

        public static Mark MarkOf(Observation o) => o.Source.Kind == SourceKind.Derived || o.Source.Kind == SourceKind.Assumed || o.Source.Kind == SourceKind.Dataset || o.Uncertainty > 0.0 && o.Confidence == Confidence.Low ? Mark.Estimated : o.Uncertainty > 0.0 && o.Source.Kind == SourceKind.MeasuredByColony && o.Confidence == Confidence.Medium ? Mark.Estimated : Mark.Measured;

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            var kb = sim.KnowledgeStore;
            if (pendingFocus.HasValue)
            {
                focused = pendingFocus; pendingFocus = null; shown = null;
                search.SetValueWithoutNotify(""); subjectFilter.SetValueWithoutNotify("All subjects");
            }
            double now = sim.TimeHours;
            string q = (search.value ?? "").Trim().ToLowerInvariant();
            string sf = subjectFilter.value;
            var keys = kb.Keys().Where(k =>
                (sf == "All subjects" || k.Subject.Kind.ToString() == sf) &&
                (focused.HasValue ? k == focused.Value : (q.Length == 0 || (Quantities.Name(k.Quantity) + " " + k.Subject).ToLowerInvariant().Contains(q)))).Take(150).ToList();
            string shape = now.ToString("0") + "|" + kb.TotalRecords() + "|" + q + "|" + sf + "|" + (focused.HasValue ? "f" : "") + "|" + keys.Count;
            if (shape == shown) return;
            shown = shape;
            summary.text = $"{kb.TotalRecords()} records · {kb.Keys().Count} entries · what the colony knows, with its range, source and age. A robot that is cut off holds records the colony does not have yet." + (focused.HasValue ? "  (showing one entry; clear the search to see all)" : "");
            list.Clear();
            if (focused.HasValue) { var clear = MakeButton("Show all", () => { focused = null; shown = null; }); list.Add(clear); }
            if (keys.Count == 0) list.Add(Label("Nothing known matches. Run an experiment in Research to learn more.", "caption"));
            Subject? last = null;
            foreach (var key in keys)
            {
                if (last != key.Subject) { last = key.Subject; var head = Label(key.Subject.ToString().ToUpperInvariant(), "heading section"); head.style.marginTop = 8; list.Add(head); }
                Observation? shownRec = kb.Shown(key.Subject, key.Quantity);
                var box = new VisualElement { style = { marginBottom = 4 } };
                string name = Quantities.Name(key.Quantity);
                if (!shownRec.HasValue)
                {
                    box.Add(Label($"○ {name}: unknown. Nothing has been observed that reached the colony.", "body"));
                }
                else
                {
                    Observation o = shownRec.Value;
                    var line = new ValueLabel();
                    bool stale = KnowledgeBase.IsStale(o, now);
                    line.Set(MarkOf(o), $"{name}: " + Describe(o, now), null);
                    box.Add(line);
                    string spark = Sparkline(kb.History(key.Subject, key.Quantity));
                    if (spark.Length > 0) { var sp = Label($"history  {spark}  ({kb.History(key.Subject, key.Quantity).Count} records)", "caption"); sp.style.color = UiPalette.Muted; box.Add(sp); }
                    foreach (Observation c in kb.Conflicts(key.Subject, key.Quantity)) { var cl = Label($"⚠ conflict: {Describe(c, now)} (kept; not the one shown)", "caption"); cl.style.color = UiPalette.Warning; box.Add(cl); }
                    if (stale || MarkOf(o) == Mark.Estimated)
                    {
                        var b = MakeButton("Measure ›", () => SystemTabs.Open("research"));
                        b.style.alignSelf = Align.FlexStart;
                        box.Add(b);
                    }
                }
                list.Add(box);
            }
            // held by robots, not known yet
            var held = sim.Workforce.Robots.SelectMany(r => kb.Unsynced(r.Id)).ToList();
            if (held.Count > 0)
            {
                var head = Label("HELD BY ROBOTS (NOT KNOWN YET)", "heading section"); head.style.marginTop = 10; list.Add(head);
                foreach (Observation o in held.Take(30)) list.Add(Label($"{Quantities.Name(o.Quantity)} ({o.Subject}) · {o.Source} · observed {Quantities.Age(now - o.ObservedAt)} · reaches the colony when the robot synchronises", "caption"));
            }
        }
    }
}

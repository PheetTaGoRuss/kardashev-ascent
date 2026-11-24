using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Flowsheet (Z): a Sankey of the last week of refining (mission operations 6, U8): what went into each process, what came
    /// out as product, slag, stored gas and vented gas, with the mass closure readout ("Mass in = out: 100.0 %"), waste heat and
    /// the plants that are blocked as red notes. It reads <see cref="FlowsheetSnapshot"/> only and computes nothing.
    /// </summary>
    public sealed class FlowsheetWindow : SystemWindow
    {
        public override string Id => "flowsheet";
        public override string Title => "Flowsheet";
        public override KeyCode Key => KeyCode.Z;
        public override int Order => 36;
        protected override float Width => 760f;

        Label closure, heat, flows, blocked;
        SankeyElement sankey;
        string shown;

        protected override void BuildBody()
        {
            closure = Label("", "value");
            closure.AddToClassList(UiType.Section);
            Body.Add(closure);
            InfoTip.After(closure, "Everything a plant takes in comes out as product, slag, gas it keeps or gas it vents: nothing is created or lost. " +
                                   "A batch is counted when it finishes, input and outputs together, so any figure other than 100 % would mean mass was lost.");
            heat = Text();
            InfoTip.After(heat, "Electricity in = chemical energy kept in the product + waste heat out (first law). The heat is counted here; the planet does not warm yet.");
            sankey = new SankeyElement();
            sankey.style.height = 330;
            sankey.style.marginTop = 6;
            Body.Add(sankey);
            Heading("MINED AND HAULED THIS WEEK");
            flows = Text();
            Heading("STOPPED PLANTS");
            blocked = Text("message");
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            FlowsheetSnapshot snap = sim.FlowsheetSnapshot(7);
            closure.text = snap.Processes.Count == 0 ? "No refining in the last week." : $"Mass in = out: {snap.ClosurePct:0.0} %  ({snap.TotalInKg:0} kg in, {snap.TotalOutKg:0} kg out)";
            heat.text = $"Waste heat: {Format.Power(snap.WasteHeatNowW)} now · {snap.WasteHeatKWh:0} kWh so far";
            flows.text = "Mined: " + (snap.Mined.Count == 0 ? "nothing" : string.Join(", ", snap.Mined.Select(m => $"{Materials.Name(m.Material).ToLowerInvariant()} {m.Kg:0} kg")))
                       + "\nHauled: " + (snap.Hauled.Count == 0 ? "nothing" : string.Join(", ", snap.Hauled.Select(m => $"{Materials.Name(m.Material).ToLowerInvariant()} {m.Kg:0} kg")));
            blocked.text = snap.Blocked.Count == 0 ? "" : string.Join("\n", snap.Blocked.Select(b => $"Plant {b.Tile}.{b.Slot}: {b.Reason}"));
            blocked.style.display = snap.Blocked.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            string shape = string.Join("|", snap.Processes.Select(p => $"{p.Process}:{p.InKg:0.#}:{p.ProductKg:0.#}:{p.SlagKg:0.#}:{p.GasStoredKg:0.#}:{p.VentedKg:0.#}"));
            if (shape == shown) return;
            shown = shape;
            sankey.Show(snap);
        }

        /// <summary>The Sankey: inputs by material on the left, processes in the middle, outputs on the right; ribbons as wide as the mass.</summary>
        sealed class SankeyElement : VisualElement
        {
            struct Node { public string Name; public Color Colour; public float Y, H; }
            struct Link { public int From, To; public float FromY, ToY, H; public Color Colour; }

            readonly List<Node> left = new List<Node>(), middle = new List<Node>(), right = new List<Node>();
            readonly List<Link> links = new List<Link>();

            public SankeyElement()
            {
                generateVisualContent += Draw;
            }

            static Color ColourOf(Material m)
            {
                switch (m)
                {
                    case Material.Rock: return new Color(0.62f, 0.58f, 0.52f);
                    case Material.Anorthosite: return new Color(0.80f, 0.82f, 0.86f);
                    case Material.Alumina: return new Color(0.93f, 0.90f, 0.78f);
                    case Material.Aluminium: case Material.PureAluminium: return new Color(0.70f, 0.78f, 0.90f);
                    case Material.Salt: return new Color(0.95f, 0.95f, 0.95f);
                    case Material.Sodium: return new Color(0.95f, 0.72f, 0.30f);
                    case Material.Slag: return new Color(0.45f, 0.42f, 0.40f);
                    case Material.Oxygen: return new Color(0.45f, 0.80f, 0.95f);
                    case Material.Chlorine: return new Color(0.70f, 0.90f, 0.35f);
                    case Material.Iron: return new Color(0.75f, 0.45f, 0.35f);
                    default: return new Color(0.7f, 0.7f, 0.7f);
                }
            }

            public void Show(FlowsheetSnapshot snap)
            {
                Clear();
                left.Clear(); middle.Clear(); right.Clear(); links.Clear();
                if (snap.Processes.Count == 0) { MarkDirtyRepaint(); return; }
                // columns: input materials, processes, output kinds
                var inputs = snap.Processes.GroupBy(p => p.Input).Select(g => (Key: g.Key, Kg: g.Sum(p => p.InKg))).OrderBy(x => (int)x.Key).ToList();
                var outputs = new List<(string Name, Color C, float Kg, string Key)>();
                void AddOut(string key, string name, Color c, double kg)
                {
                    if (kg <= 0.0) return;
                    int i = outputs.FindIndex(o => o.Key == key);
                    if (i >= 0) outputs[i] = (outputs[i].Name, outputs[i].C, outputs[i].Kg + (float)kg, key);
                    else outputs.Add((name, c, (float)kg, key));
                }
                foreach (ProcessFlow p in snap.Processes)
                {
                    AddOut("p" + p.Product, Materials.Name(p.Product), ColourOf(p.Product), p.ProductKg);
                    AddOut("slag", "Slag", ColourOf(Material.Slag), p.SlagKg);
                    if (p.Gas.HasValue)
                    {
                        AddOut("s" + p.Gas.Value, Materials.Name(p.Gas.Value) + " stored", ColourOf(p.Gas.Value), p.GasStoredKg);
                        AddOut("v" + p.Gas.Value, Materials.Name(p.Gas.Value) + " vented", Color.Lerp(ColourOf(p.Gas.Value), Color.red, 0.35f), p.VentedKg);
                    }
                }
                float total = Mathf.Max(1f, Mathf.Max(inputs.Sum(i => (float)i.Kg), outputs.Sum(o => o.Kg), (float)snap.Processes.Sum(p => p.InKg)));
                const float gap = 8f;
                float height = 330f - 24f;
                float scale = (height - gap * 6f) / total;
                float y = 12f;
                foreach (var i in inputs) { left.Add(new Node { Name = $"{Materials.Name(i.Key)} {i.Kg:0} kg", Colour = ColourOf(i.Key), Y = y, H = (float)i.Kg * scale }); y += (float)i.Kg * scale + gap; }
                y = 12f;
                foreach (ProcessFlow p in snap.Processes) { middle.Add(new Node { Name = $"{p.Name} · {p.ClosurePct:0} %", Colour = new Color(0.30f, 0.42f, 0.58f), Y = y, H = (float)p.InKg * scale }); y += (float)p.InKg * scale + gap; }
                y = 12f;
                foreach (var o in outputs) { right.Add(new Node { Name = $"{o.Name} {o.Kg:0} kg", Colour = o.C, Y = y, H = o.Kg * scale }); y += o.Kg * scale + gap; }
                // ribbons: each input into its processes, each process out to its outputs
                var leftFill = left.Select(n => n.Y).ToArray();
                var midInFill = middle.Select(n => n.Y).ToArray();
                for (int pi = 0; pi < snap.Processes.Count; pi++)
                {
                    ProcessFlow p = snap.Processes[pi];
                    int li = inputs.FindIndex(x => x.Key == p.Input);
                    float h = (float)p.InKg * scale;
                    links.Add(new Link { From = li, To = pi, FromY = leftFill[li], ToY = midInFill[pi], H = h, Colour = ColourOf(p.Input) });
                    leftFill[li] += h; midInFill[pi] += h;
                }
                var midOutFill = middle.Select(n => n.Y).ToArray();
                var rightFill = right.Select(n => n.Y).ToArray();
                for (int pi = 0; pi < snap.Processes.Count; pi++)
                {
                    ProcessFlow p = snap.Processes[pi];
                    void Out(string key, double kg, Color c)
                    {
                        if (kg <= 0.0) return;
                        int ri = outputs.FindIndex(o => o.Key == key);
                        float h = (float)kg * scale;
                        links.Add(new Link { From = 100 + pi, To = ri, FromY = midOutFill[pi], ToY = rightFill[ri], H = h, Colour = c });
                        midOutFill[pi] += h; rightFill[ri] += h;
                    }
                    Out("p" + p.Product, p.ProductKg, ColourOf(p.Product));
                    Out("slag", p.SlagKg, ColourOf(Material.Slag));
                    if (p.Gas.HasValue) { Out("s" + p.Gas.Value, p.GasStoredKg, ColourOf(p.Gas.Value)); Out("v" + p.Gas.Value, p.VentedKg, Color.Lerp(ColourOf(p.Gas.Value), Color.red, 0.35f)); }
                }
                foreach (var n in left) AddLabel(n, 0f, false);
                foreach (var n in middle) AddLabel(n, 0.5f, true);
                foreach (var n in right) AddLabel(n, 1f, false);
                MarkDirtyRepaint();
            }

            void AddLabel(Node n, float column, bool centre)
            {
                var l = new Label(n.Name);
                l.AddToClassList("caption");
                l.pickingMode = PickingMode.Ignore;
                l.style.position = Position.Absolute;
                l.style.top = n.Y + Mathf.Max(0f, n.H / 2f - 8f);
                l.AddToClassList(UiType.Secondary);
                if (column == 0f) l.style.left = 12;
                else if (column == 1f) l.style.right = 12;
                else { l.style.left = new Length(50, LengthUnit.Percent); l.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0); }
                Add(l);
            }

            void Draw(MeshGenerationContext mgc)
            {
                Rect r = contentRect;
                if (r.width < 10f || (left.Count == 0 && middle.Count == 0)) return;
                Painter2D p = mgc.painter2D;
                float w = r.width;
                float xL = w * 0.10f, xM = w * 0.45f, xR = w * 0.80f, bar = 12f;
                float xLe = xL + bar, xMe = xM + bar, xRe = xR + bar;
                foreach (Link k in links)
                {
                    bool fromMiddle = k.From >= 100;
                    float x0 = fromMiddle ? xMe : xLe, x1 = fromMiddle ? xR : xM;
                    float cx = (x0 + x1) / 2f;
                    Color c = k.Colour; c.a = 0.55f;
                    p.fillColor = c;
                    p.BeginPath();
                    p.MoveTo(new Vector2(x0, k.FromY));
                    p.BezierCurveTo(new Vector2(cx, k.FromY), new Vector2(cx, k.ToY), new Vector2(x1, k.ToY));
                    p.LineTo(new Vector2(x1, k.ToY + k.H));
                    p.BezierCurveTo(new Vector2(cx, k.ToY + k.H), new Vector2(cx, k.FromY + k.H), new Vector2(x0, k.FromY + k.H));
                    p.ClosePath();
                    p.Fill();
                }
                void Bars(List<Node> nodes, float x)
                {
                    foreach (Node n in nodes)
                    {
                        p.fillColor = n.Colour;
                        p.BeginPath();
                        p.MoveTo(new Vector2(x, n.Y));
                        p.LineTo(new Vector2(x + bar, n.Y));
                        p.LineTo(new Vector2(x + bar, n.Y + Mathf.Max(2f, n.H)));
                        p.LineTo(new Vector2(x, n.Y + Mathf.Max(2f, n.H)));
                        p.ClosePath();
                        p.Fill();
                    }
                }
                Bars(left, xL); Bars(middle, xM); Bars(right, xR);
            }
        }
    }
}

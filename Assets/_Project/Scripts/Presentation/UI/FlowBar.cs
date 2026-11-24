using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The energy flow bar (UI information design, stage 4): two stacked rows on one scale, what is made (sources) above what it goes to (loads, losses,
    /// curtailed). Every segment is a value the simulation reports; whatever the two rows do not account for is shown as its own "not accounted" segment
    /// rather than hidden, so the bar never claims a balance the numbers do not have.
    /// </summary>
    public sealed class FlowBar : VisualElement
    {
        public readonly struct Segment
        {
            public readonly string Name;
            public readonly double Watts;
            public readonly Color Colour;
            public Segment(string name, double watts, Color colour) { Name = name; Watts = watts; Colour = colour; }
        }

        readonly VisualElement top = Row(), bottom = Row();
        readonly Label topLegend = new Label(), bottomLegend = new Label();

        public FlowBar()
        {
            style.marginTop = 4; style.marginBottom = 6;
            var madeLabel = new Label("MADE"); madeLabel.AddToClassList("caption");
            var usedLabel = new Label("WHERE IT GOES"); usedLabel.AddToClassList("caption");
            foreach (Label l in new[] { topLegend, bottomLegend })
            {
                l.AddToClassList(UiType.Secondary);
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.color = UiPalette.Muted;
                l.style.marginBottom = 4;
            }
            Add(madeLabel); Add(top); Add(topLegend); Add(usedLabel); Add(bottom); Add(bottomLegend);
        }

        static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.height = 18;
            r.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            return r;
        }

        public static readonly Color SolarColour = new Color(1f, 0.78f, 0.36f), RtgColour = new Color(0.7f, 0.55f, 1f), SeaColour = new Color(0.4f, 0.75f, 1f),
            BatteryColour = new Color(0.43f, 0.86f, 0.51f), LoadColour = new Color(0.47f, 0.75f, 1f), LossColour = new Color(1f, 0.47f, 0.41f),
            CurtailColour = new Color(0.6f, 0.64f, 0.7f), UnaccountedColour = new Color(0.9f, 0.9f, 0.9f);

        /// <summary>Show both rows on one scale (the larger total).</summary>
        public void Set(IList<Segment> made, IList<Segment> used)
        {
            double scale = System.Math.Max(made.Sum(s => s.Watts), used.Sum(s => s.Watts));
            Fill(top, topLegend, made, scale);
            Fill(bottom, bottomLegend, used, scale);
        }

        static void Fill(VisualElement row, Label legend, IList<Segment> segments, double scale)
        {
            row.Clear();
            var parts = new List<string>();
            foreach (Segment s in segments)
            {
                if (s.Watts <= 0.0 || scale <= 0.0) continue;
                var block = new VisualElement();
                block.style.flexGrow = (float)(s.Watts / scale * 1000.0);
                block.style.flexBasis = 0;
                block.style.backgroundColor = s.Colour;
                block.tooltip = $"{s.Name}: {Format.Power(s.Watts)}";
                row.Add(block);
                parts.Add($"{s.Name} {Format.Power(s.Watts)}");
            }
            // empty space to the scale end when this row is shorter
            double total = segments.Sum(s => System.Math.Max(0.0, s.Watts));
            if (scale > 0.0 && total < scale - 1e-9)
            {
                var gap = new VisualElement();
                gap.style.flexGrow = (float)((scale - total) / scale * 1000.0);
                gap.style.flexBasis = 0;
                row.Add(gap);
            }
            legend.text = parts.Count == 0 ? "nothing" : string.Join(" · ", parts);
        }
    }
}

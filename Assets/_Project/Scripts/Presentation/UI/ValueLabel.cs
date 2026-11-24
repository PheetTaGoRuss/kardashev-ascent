using System;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// What the colony knows about a value (UI information design, stage 2, decided 2026-11-24): every number wears one of five marks, each a shape
    /// and a word, never colour alone. A mark is shown only for a state the simulation has: measured and estimated come from
    /// <see cref="KnowledgeEstimate"/> (a half range of 0 is measured), unknown from a value nobody has observed, game rule from a tuning constant,
    /// outside model from a regime the model does not cover.
    /// </summary>
    public enum Mark { Measured, Estimated, Unknown, GameRule, OutsideModel }

    public static class Marks
    {
        public static string Glyph(Mark m) => m == Mark.Measured ? "●" : m == Mark.Estimated ? "◐" : m == Mark.Unknown ? "○" : m == Mark.GameRule ? "◆" : "⚠";
        public static string Word(Mark m) => m == Mark.Measured ? "measured" : m == Mark.Estimated ? "estimate" : m == Mark.Unknown ? "unknown" : m == Mark.GameRule ? "rule" : "outside model";
        public static string Tip(Mark m) =>
            m == Mark.Measured ? "Measured: an instrument read it; the range is the instrument's precision."
            : m == Mark.Estimated ? "Estimated: inferred, with a range, until it is measured."
            : m == Mark.Unknown ? "Unknown: nothing has been observed yet."
            : m == Mark.GameRule ? "Game rule: a tuning value of the game, not a claim about nature."
            : "Outside the model: the formula is not valid here.";

        public static Color Colour(Mark m) => m == Mark.Measured ? UiPalette.Good : m == Mark.Estimated ? UiPalette.Warning : m == Mark.Unknown ? UiPalette.Muted : m == Mark.GameRule ? UiPalette.Cool : UiPalette.Critical;

        /// <summary>Measured when the range is gone, estimated while there is one.</summary>
        public static Mark Of(KnowledgeEstimate e) => e.IsExact ? Mark.Measured : Mark.Estimated;
    }

    /// <summary>A value with its mark, its range and, while it is only an estimate, a "Measure ›" way out. One element, used wherever a number needs its state of knowledge.</summary>
    public sealed class ValueLabel : VisualElement
    {
        readonly Label mark = new Label(), value = new Label(), range = new Label();
        readonly Button measure = new Button();
        Action measureAction;

        public ValueLabel()
        {
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            style.alignItems = Align.Center;
            mark.AddToClassList(UiType.Secondary);
            mark.style.marginRight = 6;
            value.AddToClassList(UiType.Body);
            range.AddToClassList(UiType.Secondary);
            range.style.color = UiPalette.Muted;
            range.style.marginLeft = 6;
            measure.AddToClassList("small-button");
            measure.AddToClassList(UiType.Secondary);
            measure.style.marginLeft = 8;
            measure.clicked += () => measureAction?.Invoke();
            Add(mark); Add(value); Add(range); Add(measure);
            measure.style.display = DisplayStyle.None;
        }

        /// <summary>Show a value. <paramref name="rangeText"/> is the range or the unit note; <paramref name="measureText"/> and <paramref name="onMeasure"/> give the "Measure ›" link.</summary>
        public void Set(Mark m, string valueText, string rangeText = null, string measureText = null, Action onMeasure = null, string measureTip = null)
        {
            mark.text = Marks.Glyph(m) + " " + Marks.Word(m);
            mark.style.color = Marks.Colour(m);
            mark.tooltip = Marks.Tip(m);
            value.text = valueText;
            value.style.color = m == Mark.Estimated || m == Mark.Unknown ? UiPalette.Muted : UiPalette.Text;
            range.text = rangeText ?? "";
            range.style.display = string.IsNullOrEmpty(rangeText) ? DisplayStyle.None : DisplayStyle.Flex;
            measureAction = onMeasure;
            bool link = onMeasure != null && !string.IsNullOrEmpty(measureText);
            measure.text = link ? measureText + " ›" : "";
            measure.tooltip = measureTip ?? "";
            measure.style.display = link ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Show a <see cref="KnowledgeEstimate"/>: "93.9 kPa" measured, "about 78 kPa" with its range while estimated.</summary>
        public void SetEstimate(KnowledgeEstimate e, string format, string unit, double scale = 1.0, string measureText = null, Action onMeasure = null, string measureTip = null)
        {
            string u = unit.Length > 0 ? " " + unit : "";
            if (e.IsExact) Set(Mark.Measured, (e.Value * scale).ToString(format) + u, null);
            else Set(Mark.Estimated, "about " + (e.Value * scale).ToString(format) + u, $"({(e.Low * scale).ToString(format)} to {(e.High * scale).ToString(format)})", measureText, onMeasure, measureTip);
        }

        /// <summary>The text on screen, for tests.</summary>
        public string Shown => mark.text + " " + value.text + " " + range.text + (measure.style.display == DisplayStyle.Flex ? " " + measure.text : "");
    }
}

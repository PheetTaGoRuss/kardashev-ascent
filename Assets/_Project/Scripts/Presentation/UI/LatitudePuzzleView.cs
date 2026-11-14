using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// One latitude puzzle (stage C): a drawing of the rod, its noon shadow and the sun's ray; the readings; boxes for
    /// the sun's altitude h (a check on the way) and the latitude φ; and the formulas behind a hint button.
    /// </summary>
    public sealed class LatitudePuzzleView : VisualElement
    {
        readonly IPlanetSimulation sim;
        readonly LatitudePuzzle puzzle;
        readonly TextField altitudeField, latitudeField;
        readonly Label message, hints;

        public LatitudePuzzleView(IPlanetSimulation sim, LatitudePuzzle puzzle, Action<string> solved)
        {
            this.sim = sim;
            this.puzzle = puzzle;
            style.marginTop = 6;
            style.paddingLeft = 6;
            style.paddingTop = 4;
            style.paddingBottom = 6;
            style.borderLeftWidth = 2;
            style.borderLeftColor = UiPalette.Accent;

            Add(PuzzleGuide.Text($"Your latitude from a noon shadow · tile #{puzzle.Tile}", bold: true, size: PuzzleGuide.TitleSize));
            var drawing = new ShadowDrawing(puzzle);
            Add(drawing);
            string shadowWay = puzzle.SunSouth ? "north (so the sun is in the south)" : "south (so the sun is in the north)";
            Add(PuzzleGuide.Text(
                $"At noon a robot stood a rod H = {puzzle.RodHeightM:0.00} m tall. Its shadow was L = {puzzle.ShadowM:0.00} m long, " +
                $"pointing {shadowWay}. Today's declination δ = {puzzle.DeclinationDeg:+0.00;−0.00}° (how far north of the equator the sun is today)."));
            Add(CalculatorPad.Values("H", $"{puzzle.RodHeightM:0.00}", "L", $"{puzzle.ShadowM:0.00}",
                                     "δ", (puzzle.DeclinationDeg).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            Add(PuzzleGuide.Steps(
                "Divide the rod by the shadow: H / L.",
                "Press arctan (shift + tan) on that, calculator in degrees. That is the sun's height h.",
                "Shadow pointing north: φ = 90° − h + δ.   Shadow pointing south: φ = h − 90° + δ.",
                "Positive φ means north of the equator, negative means south."));
            Add(PuzzleGuide.Example(
                "Rod 1.00 m, shadow 0.58 m pointing north, δ = +10°:",
                "1.00 / 0.58 = 1.724  →  arctan 1.724 = 59.9°  (that's h)",
                "φ = 90° − 59.9° + 10° = 40.1°  (40° north)"));

            altitudeField = Field("Sun's height h (°), optional check:");
            latitudeField = Field("Latitude φ (°, south is −):");

            var buttons = new VisualElement();
            buttons.AddToClassList("button-row");
            var check = new Button(Submit) { text = "Check" };
            check.AddToClassList("small-button");
            check.style.fontSize = PuzzleGuide.BodySize;
            buttons.Add(check);
            buttons.Add(CalculatorPad.OpenButton());
            Add(buttons);

            hints = PuzzleGuide.Text("");
            hints.style.display = DisplayStyle.None;
            Add(hints);
            message = PuzzleGuide.Text("");
            message.AddToClassList("message");
            Add(message);
            this.solved = solved;
        }

        readonly Action<string> solved;

        TextField Field(string label)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            Label l = PuzzleGuide.Text(label, bold: true);
            l.style.flexGrow = 1;
            row.Add(l);
            var f = new TextField { maxLength = 12 };
            CalculatorPad.TrackAnswer(f);
            f.AddToClassList("puzzle-field");
            f.style.width = 110;
            f.style.fontSize = PuzzleGuide.BodySize;
            row.Add(f);
            Add(row);
            return f;
        }

        void Submit()
        {
            string notes = "";
            if (TryParse(altitudeField.value, out double h))
                notes = Math.Abs(h - puzzle.AltitudeDeg) <= 0.5
                    ? $"h = {h:0.0}° is right. "
                    : $"h = {h:0.0}° is off: check atan(H / L) (in degrees). ";
            if (!TryParse(latitudeField.value, out double phi))
            {
                message.text = notes + "Enter the latitude φ in degrees.";
                return;
            }
            string result = sim.AnswerLatitudePuzzle(puzzle.Tile, phi, out LatitudePuzzle.Grade grade);
            message.text = notes + (grade == LatitudePuzzle.Grade.Wrong ? result
                : grade == LatitudePuzzle.Grade.Precise ? "Precise! " + result : "Close enough (rough fix). " + result);
            if (grade != LatitudePuzzle.Grade.Wrong)
                solved?.Invoke(message.text);
        }

        static bool TryParse(string text, out double value) =>
            double.TryParse((text ?? "").Trim().Replace('−', '-'), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out value);

        static Label MakeLabel(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            l.AddToClassList("wrap");
            return l;
        }

        /// <summary>The rod, its shadow on the ground and the sun's ray over the rod's tip, to scale.</summary>
        sealed class ShadowDrawing : VisualElement
        {
            readonly LatitudePuzzle p;

            public ShadowDrawing(LatitudePuzzle p)
            {
                this.p = p;
                style.height = 90;
                style.marginTop = 4;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                Rect r = contentRect;
                if (r.width <= 0f)
                    return;
                var g = ctx.painter2D;
                float ground = r.height - 12f;
                // Scale so rod and shadow fit.
                float scale = Mathf.Min((r.height - 24f) / (float)p.RodHeightM, (r.width - 60f) / Mathf.Max(0.1f, (float)p.ShadowM));
                float rodX = p.SunSouth ? 40f : r.width - 40f;   // the shadow points away from the sun
                float dir = p.SunSouth ? 1f : -1f;
                float tipY = ground - (float)p.RodHeightM * scale;
                float shadowEnd = rodX + dir * (float)p.ShadowM * scale;

                g.lineWidth = 1.5f;
                g.strokeColor = new Color(1f, 1f, 1f, 0.35f);
                g.BeginPath(); g.MoveTo(new Vector2(4f, ground)); g.LineTo(new Vector2(r.width - 4f, ground)); g.Stroke();
                g.lineWidth = 4f;
                g.strokeColor = new Color(0.1f, 0.1f, 0.12f, 0.9f);
                g.BeginPath(); g.MoveTo(new Vector2(rodX, ground)); g.LineTo(new Vector2(shadowEnd, ground)); g.Stroke();
                g.lineWidth = 3f;
                g.strokeColor = new Color(0.85f, 0.85f, 0.9f);
                g.BeginPath(); g.MoveTo(new Vector2(rodX, ground)); g.LineTo(new Vector2(rodX, tipY)); g.Stroke();
                g.lineWidth = 1f;
                g.strokeColor = new Color(1f, 0.85f, 0.35f, 0.9f);
                // The ray from the shadow's tip past the rod's tip toward the sun, stopped inside the drawing.
                float dx = rodX - shadowEnd, dy = tipY - ground;
                float back = 1.6f;
                if (tipY + dy * back < 8f) back = (8f - tipY) / dy;
                float sx = rodX + dx * back;
                if (sx < 8f) back = Mathf.Min(back, (8f - rodX) / dx);
                if (sx > r.width - 8f) back = Mathf.Min(back, (r.width - 8f - rodX) / dx);
                var sun = new Vector2(rodX + dx * back, tipY + dy * back);
                g.BeginPath();
                g.MoveTo(new Vector2(shadowEnd, ground));
                g.LineTo(sun);
                g.Stroke();
                g.fillColor = new Color(1f, 0.85f, 0.35f);
                g.BeginPath(); g.Arc(sun, 6f, Angle.Degrees(0f), Angle.Degrees(360f)); g.Fill();
            }
        }
    }
}

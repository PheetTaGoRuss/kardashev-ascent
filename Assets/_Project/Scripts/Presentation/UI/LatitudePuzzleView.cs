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
            style.borderLeftColor = new Color(0.56f, 0.70f, 1f);

            Add(MakeLabel($"Latitude of tile #{puzzle.Tile}: noon shadow", "robot-name"));
            var drawing = new ShadowDrawing(puzzle);
            Add(drawing);
            string shadowWay = puzzle.SunSouth ? "north (the sun is to the south)" : "south (the sun is to the north)";
            Add(MakeLabel(
                $"A robot stood a rod H = {puzzle.RodHeightM:0.00} m upright and marked its shortest shadow of the day " +
                $"(local noon): L = {puzzle.ShadowM:0.00} m, pointing {shadowWay}.\n" +
                $"Today's declination (from the date): δ = {puzzle.DeclinationDeg:+0.00;−0.00}°.", "job-text"));

            altitudeField = Field("Sun's altitude h (°), optional check:");
            latitudeField = Field("Latitude φ (°, + north, − south):");

            var buttons = new VisualElement();
            buttons.AddToClassList("button-row");
            var check = new Button(Submit) { text = "Submit" };
            check.AddToClassList("small-button");
            buttons.Add(check);
            var hint = new Button(() => hints.style.display = DisplayStyle.Flex) { text = "Show the formulas" };
            hint.AddToClassList("small-button");
            buttons.Add(hint);
            Add(buttons);

            hints = MakeLabel(
                "1. The rod and its shadow make a right triangle: tan h = H / L, so h = atan(H / L).\n" +
                "2. At noon the sun is 90° − h from straight overhead, along the meridian.\n" +
                "   Shadow pointing north (sun to the south): φ = 90° − h + δ.\n" +
                "   Shadow pointing south (sun to the north): φ = h − 90° + δ.", "job-text");
            hints.style.display = DisplayStyle.None;
            Add(hints);
            message = MakeLabel("", "job-text");
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
            Label l = MakeLabel(label, "job-text");
            l.style.flexGrow = 1;
            row.Add(l);
            var f = new TextField { maxLength = 8 };
            f.AddToClassList("puzzle-field");
            f.style.width = 70;
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

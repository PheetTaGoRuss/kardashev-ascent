using System;
using System.Text;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// How every puzzle card reads (director 2026-11-14: bigger, simpler, with a worked example, for a student who has
    /// never seen the method before): larger text, numbered "how to solve it" steps, and an example with other numbers.
    /// </summary>
    public static class PuzzleGuide
    {
        public const float BodySize = 14f;
        public const float TitleSize = 18f;
        public const float SmallSize = 14f;

        static readonly Color ExampleTint = UiPalette.Good;

        public static Label Text(string text, bool bold = false, float size = BodySize)
        {
            var l = new Label(text);
            l.style.fontSize = UiType.Px(size);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 2;
            l.style.paddingLeft = l.style.paddingRight = 0;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        /// <summary>"How to solve it" with numbered steps.</summary>
        public static VisualElement Steps(params string[] steps)
        {
            var box = new VisualElement();
            box.style.marginTop = 8;
            box.Add(Text("How to solve it", bold: true));
            for (int i = 0; i < steps.Length; i++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 2;
                var n = Text($"{i + 1}.", bold: true);
                n.style.width = 22;
                n.style.flexShrink = 0;
                row.Add(n);
                var s = Text(steps[i]);
                s.style.flexShrink = 1;
                s.style.flexGrow = 1;
                row.Add(s);
                box.Add(row);
            }
            return box;
        }

        /// <summary>A worked example with different numbers, in a green box.</summary>
        public static VisualElement Example(params string[] lines)
        {
            var box = new VisualElement();
            box.style.marginTop = 8;
            box.style.paddingLeft = box.style.paddingRight = 8;
            box.style.paddingTop = box.style.paddingBottom = 5;
            box.style.backgroundColor = new Color(UiPalette.Good.r, UiPalette.Good.g, UiPalette.Good.b, 0.10f);
            box.style.borderLeftWidth = 3;
            box.style.borderLeftColor = ExampleTint;
            var h = Text("Example (other numbers)", bold: true);
            h.style.color = ExampleTint;
            box.Add(h);
            foreach (string line in lines)
                box.Add(Text(line));
            return box;
        }
    }

    /// <summary>
    /// One light puzzle (stage D): the robot's readings as a table (and, for haze, the Langley plot), a box for the
    /// answer, and the formulas behind a hint button.
    /// </summary>
    public sealed class LightPuzzleView : VisualElement
    {
        readonly IPlanetSimulation sim;
        readonly ParameterPuzzle puzzle;
        readonly TextField answer;
        readonly Label message, hints;
        readonly Action<string> solved;

        public LightPuzzleView(IPlanetSimulation sim, ParameterPuzzle puzzle, Action<string> solved)
        {
            this.sim = sim;
            this.puzzle = puzzle;
            this.solved = solved;
            style.marginTop = 6;
            style.paddingLeft = 6;
            style.paddingTop = 4;
            style.paddingBottom = 6;
            style.borderLeftWidth = 2;
            style.borderLeftColor = UiPalette.Accent;

            string title, intro, ask;
            string[] steps, example;
            var head = new System.Collections.Generic.List<string>();
            var rows = new System.Collections.Generic.List<string[]>();
            switch (puzzle.Parameter)
            {
                case SolarParameter.OpticalDepth:
                    title = "Air haze τ: how much light the air blocks";
                    intro = $"A robot measured the sun's direct light at different sun heights. Above the air the light is G₀ = {puzzle.G0:0} W/m². " +
                            "Air mass m = how many layers of air the light passed through (1 = sun straight up).";
                    head.AddRange(new[] { "sun height", "air mass m", "light reaching ground" });
                    for (int i = 0; i < puzzle.Dni.Length; i++)
                        rows.Add(new[] { $"{puzzle.AltitudeDeg[i]:0}°", $"{puzzle.AirMass[i]:0.000}", $"{puzzle.Dni[i]:0} W/m²" });
                    ask = "Air haze τ:";
                    steps = new[]
                    {
                        "Pick one row of the table.",
                        "Divide the light above the air by the light that reached the ground: G₀ / light.",
                        "Take the natural log of that number (the ln key).",
                        "Divide by that row's air mass m. That is τ. (Do two rows and average them to be more exact.)",
                    };
                    example = new[]
                    {
                        "G₀ = 1361 W/m², a row with m = 2.000 and light 742 W/m²:",
                        "1361 / 742 = 1.834  →  ln 1.834 = 0.607  →  0.607 / 2.000 = 0.30",
                        "So τ ≈ 0.30.",
                    };
                    break;
                case SolarParameter.DiffuseShare:
                    title = "Sky glow k: how much light the sky scatters down";
                    intro = $"A robot read two sensors: one in shade (only the sky's glow, DHI) and one aimed at the sun (direct light, DNI). " +
                            $"Above the air: G₀ = {puzzle.G0:0} W/m².";
                    head.AddRange(new[] { "sun height h", "direct DNI", "sky glow DHI" });
                    for (int i = 0; i < puzzle.Dhi.Length; i++)
                        rows.Add(new[] { $"{puzzle.AltitudeDeg[i]:0}°", $"{puzzle.Dni[i]:0} W/m²", $"{puzzle.Dhi[i]:0.0} W/m²" });
                    ask = "Sky glow share k:";
                    steps = new[]
                    {
                        "Pick one row.",
                        "How much of the beam the air took away: 1 − DNI / G₀.",
                        "How much light would land on flat ground: G₀ × sin h (calculator in degrees).",
                        "Multiply the answers of steps 2 and 3.",
                        "k = DHI / that number. (Average a few rows.)",
                    };
                    example = new[]
                    {
                        "G₀ = 1361, h = 40°, DNI = 800, DHI = 90:",
                        "1 − 800 / 1361 = 0.412   ·   1361 × sin 40° = 875   ·   875 × 0.412 = 360",
                        "k = 90 / 360 = 0.25",
                    };
                    break;
                case SolarParameter.Albedo:
                    title = "Ground shine ρ: how much light the ground reflects";
                    intro = "A robot pointed one light sensor up (light coming in) and one down (light the ground bounces back), three times in a day.";
                    head.AddRange(new[] { "reading", "light in (up)", "light back (down)" });
                    for (int i = 0; i < puzzle.Incoming.Length; i++)
                        rows.Add(new[] { $"{i + 1}", $"{puzzle.Incoming[i]:0} W/m²", $"{puzzle.Reflected[i]:0.0} W/m²" });
                    ask = "Ground shine ρ (0 to 1):";
                    steps = new[]
                    {
                        "Add up all the 'light in' readings.",
                        "Add up all the 'light back' readings.",
                        "ρ = light back / light in. (0 = black ground, 1 = a perfect mirror.)",
                    };
                    example = new[]
                    {
                        "Light in: 500 + 800 + 650 = 1950   ·   light back: 100 + 160 + 130 = 390",
                        "ρ = 390 / 1950 = 0.20",
                    };
                    break;
                case SolarParameter.AirTemperature:
                    title = "Air heat: the day's average temperature";
                    intro = "A robot read a thermometer every 3 hours for one whole day.";
                    head.AddRange(new[] { "time", "temperature", "" });
                    for (int i = 0; i < puzzle.TemperatureC.Length; i++)
                        rows.Add(new[] { $"{puzzle.HourOfDay[i]:00}:00", $"{puzzle.TemperatureC[i]:0.0} °C", "" });
                    ask = "Average temperature (°C):";
                    steps = new[]
                    {
                        "Add up all 8 temperatures (minus signs count: −3 makes the sum smaller).",
                        "Divide by 8. That is the day's average.",
                    };
                    example = new[]
                    {
                        "12 + 10 + 9 + 14 + 20 + 24 + 21 + 16 = 126",
                        "126 / 8 = 15.8 °C",
                    };
                    break;
                case SolarParameter.Dust:
                    title = "Dust: how much light the dust blocks";
                    intro = "A robot left two small test panels side by side for a week: one wiped clean every day, one left dusty.";
                    head.AddRange(new[] { "reading", "clean panel", "dusty panel" });
                    for (int i = 0; i < puzzle.CleanW.Length; i++)
                        rows.Add(new[] { $"{i + 1}", $"{puzzle.CleanW[i]:0.0} W", $"{puzzle.DustyW[i]:0.0} W" });
                    ask = "Share the dust blocks (0 to 1):";
                    steps = new[]
                    {
                        "Add up the clean panel's readings.",
                        "Add up the dusty panel's readings.",
                        "Divide dusty / clean: the share that still gets through.",
                        "Dust share = 1 − that number.",
                    };
                    example = new[]
                    {
                        "Clean: 50 + 60 + 40 = 150   ·   dusty: 47 + 56.4 + 37.6 = 141",
                        "141 / 150 = 0.94   →   1 − 0.94 = 0.06 (the dust blocks 6 %)",
                    };
                    break;
                case SolarParameter.Spectrum:
                    title = "Star colour: the star's temperature";
                    intro = "A robot measured how bright the star is at several colours (wavelengths, in nm). 100 = the brightest one. " +
                            "Hot stars are brightest at blue (short) wavelengths, cool stars at red (long) ones.";
                    head.AddRange(new[] { "wavelength", "brightness", "" });
                    for (int i = 0; i < puzzle.WavelengthNm.Length; i++)
                        rows.Add(new[] { $"{puzzle.WavelengthNm[i]:0} nm", $"{puzzle.Brightness[i]:0.0}", "" });
                    ask = "Star temperature (K):";
                    steps = new[]
                    {
                        "Find the brightest reading (the top of the hump). Its wavelength is λ_peak.",
                        "Change nm to µm: divide by 1000 (500 nm = 0.5 µm).",
                        $"Wien's law: T = {ParameterPuzzle.WienMicronK:0} / λ_peak (in µm). The answer is in kelvin (K).",
                    };
                    example = new[]
                    {
                        "Brightest at 800 nm  →  0.8 µm",
                        $"T = {ParameterPuzzle.WienMicronK:0} / 0.8 = 3622 K (a cool, orange star). The Sun peaks near 500 nm: about 5800 K.",
                    };
                    break;
                default:
                    title = "Clouds: how much sunlight gets through";
                    intro = "A robot logged the sunlight on flat ground for five days, next to what a clear sky would have given.";
                    head.AddRange(new[] { "day", "clear sky", "measured" });
                    for (int d = 0; d < puzzle.ClearKwh.Length; d++)
                        rows.Add(new[] { $"{d + 1}", $"{puzzle.ClearKwh[d]:0.00} kWh/m²", $"{puzzle.MeasuredKwh[d]:0.00} kWh/m²" });
                    ask = "Share that got through (0 to 1):";
                    steps = new[]
                    {
                        "Add up the 'measured' column.",
                        "Add up the 'clear sky' column.",
                        "Divide measured / clear sky. (1 = no clouds; 0.7 = clouds took 30 %.)",
                    };
                    example = new[]
                    {
                        "Measured: 4.5 + 3.0 + 5.1 = 12.6   ·   clear sky: 6.0 + 6.2 + 5.8 = 18.0",
                        "12.6 / 18.0 = 0.70",
                    };
                    break;
            }

            // A symbol badge and the title, then what the robot did in plain words (director 2026-11-14: readable by a
            // student who has never seen this before: steps and a worked example).
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.Add(ParameterLook.Badge(puzzle.Parameter, 28f));
            Label t = PuzzleGuide.Text(title, bold: true, size: PuzzleGuide.TitleSize);
            t.style.flexGrow = 1;
            titleRow.Add(t);
            Add(titleRow);
            Add(PuzzleGuide.Text(intro));
            if (puzzle.Parameter == SolarParameter.OpticalDepth)
                Add(new LangleyPlot(puzzle));
            if (puzzle.Parameter == SolarParameter.Spectrum)
                Add(new SpectrumPlot(puzzle));
            if (puzzle.Parameter == SolarParameter.OpticalDepth || puzzle.Parameter == SolarParameter.DiffuseShare)
                Add(CalculatorPad.Values("G₀", $"{puzzle.G0:0}"));
            if (puzzle.Parameter == SolarParameter.Spectrum)
                Add(CalculatorPad.Values("Wien's number", $"{ParameterPuzzle.WienMicronK:0}"));
            Add(Table(head, rows));
            Add(PuzzleGuide.Steps(steps));
            Add(PuzzleGuide.Example(example));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 6;
            Label l = PuzzleGuide.Text(ask, bold: true);
            l.style.flexGrow = 1;
            row.Add(l);
            answer = new TextField { maxLength = 12 };
            CalculatorPad.TrackAnswer(answer);
            answer.AddToClassList("puzzle-field");
            answer.style.width = 110;
            answer.style.fontSize = UiType.Px(PuzzleGuide.BodySize);
            row.Add(answer);
            var submit = new Button(Submit) { text = "Check" };
            submit.AddToClassList("small-button");
            submit.style.fontSize = UiType.Px(PuzzleGuide.BodySize);
            row.Add(submit);
            row.Add(CalculatorPad.OpenButton());
            Add(row);

            hints = PuzzleGuide.Text("");
            hints.style.display = DisplayStyle.None;
            Add(hints);
            message = PuzzleGuide.Text("");
            message.AddToClassList("message");
            Add(message);
        }

        void Submit()
        {
            if (!double.TryParse((answer.value ?? "").Trim().Replace('−', '-').Replace(',', '.'),
                                 System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                message.text = "Enter a number (for clouds, a share like 0.7).";
                return;
            }
            string result = sim.AnswerParameterPuzzle(puzzle.Parameter, v, out LatitudePuzzle.Grade grade);
            KardashevAscent.Presentation.Audio.GameAudio.Play(grade != LatitudePuzzle.Grade.Wrong ? KardashevAscent.Presentation.Audio.Sfx.Correct : KardashevAscent.Presentation.Audio.Sfx.Wrong);
            message.text = grade == LatitudePuzzle.Grade.Precise ? "Precise! " + result : result;
            if (grade != LatitudePuzzle.Grade.Wrong)
                solved?.Invoke(message.text);
        }

        /// <summary>A small table with fixed-width columns (labels don't keep spaces lined up).</summary>
        static VisualElement Table(System.Collections.Generic.List<string> head, System.Collections.Generic.List<string[]> rows)
        {
            var table = new VisualElement();
            table.style.marginTop = 4;
            // A first column that only counts readings (1, 2, 3 or the hour) isn't a number to work with.
            bool countColumn = head.Count > 0 && (head[0] == "reading" || head[0] == "day" || head[0] == "time");
            VisualElement Row(string[] cells, bool header)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                for (int i = 0; i < cells.Length; i++)
                {
                    var l = new Label(cells[i]);
                    l.style.fontSize = UiType.Px(PuzzleGuide.BodySize - 1f);
                    l.style.whiteSpace = WhiteSpace.Normal;
                    l.style.width = 140;
                    l.style.flexGrow = 0;
                    if (header)
                    {
                        l.style.unityFontStyleAndWeight = FontStyle.Bold;
                        l.style.color = UiPalette.Muted;
                    }
                    else if (i > 0 || !countColumn)
                        CalculatorPad.MakeClickable(l);   // click a reading: it goes into the calculator
                    row.Add(l);
                }
                return row;
            }
            table.Add(Row(head.ToArray(), true));
            foreach (string[] r in rows)
                table.Add(Row(r, false));
            return table;
        }

        static Label MakeLabel(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            l.AddToClassList("wrap");
            return l;
        }

        /// <summary>Brightness against wavelength: a hump whose top is λ_peak.</summary>
        sealed class SpectrumPlot : VisualElement
        {
            readonly ParameterPuzzle p;

            public SpectrumPlot(ParameterPuzzle p)
            {
                this.p = p;
                style.height = 140;
                style.marginTop = 4;
                generateVisualContent += Draw;
                Add(new Label("brightness ↑") { style = { position = Position.Absolute, left = 0, top = 0, fontSize = UiType.Px(12) } });
                Add(new Label("wavelength →") { style = { position = Position.Absolute, right = 0, bottom = 0, fontSize = UiType.Px(12) } });
            }

            void Draw(MeshGenerationContext ctx)
            {
                Rect r = contentRect;
                if (r.width <= 0f || p.WavelengthNm.Length == 0)
                    return;
                float left = 30f, right = r.width - 20f, top = 22f, bottom = r.height - 22f;
                double xMin = p.WavelengthNm[0], xMax = p.WavelengthNm[p.WavelengthNm.Length - 1];
                Vector2 At(double x, double y) => new Vector2(
                    left + (float)((x - xMin) / Math.Max(1e-9, xMax - xMin)) * (right - left),
                    bottom - (float)(y / 100.0) * (bottom - top));

                var g = ctx.painter2D;
                g.lineWidth = 1f;
                g.strokeColor = new Color(1f, 1f, 1f, 0.35f);
                g.BeginPath(); g.MoveTo(new Vector2(left, top)); g.LineTo(new Vector2(left, bottom)); g.LineTo(new Vector2(right, bottom)); g.Stroke();
                g.lineWidth = 1.5f;
                g.strokeColor = new Color(1f, 0.85f, 0.35f, 0.6f);
                g.BeginPath();
                for (int i = 0; i < p.WavelengthNm.Length; i++)
                {
                    Vector2 v = At(p.WavelengthNm[i], p.Brightness[i]);
                    if (i == 0) g.MoveTo(v); else g.LineTo(v);
                }
                g.Stroke();
                g.fillColor = new Color(1f, 0.85f, 0.35f);
                for (int i = 0; i < p.WavelengthNm.Length; i++)
                {
                    g.BeginPath(); g.Arc(At(p.WavelengthNm[i], p.Brightness[i]), 3.5f, Angle.Degrees(0f), Angle.Degrees(360f)); g.Fill();
                }
            }
        }

        /// <summary>ln DNI against air mass: the readings fall on a straight line whose slope is −τ.</summary>
        sealed class LangleyPlot : VisualElement
        {
            readonly ParameterPuzzle p;

            public LangleyPlot(ParameterPuzzle p)
            {
                this.p = p;
                style.height = 140;
                style.marginTop = 4;
                generateVisualContent += Draw;
                Add(new Label("ln DNI ↑") { style = { position = Position.Absolute, left = 0, top = 0, fontSize = UiType.Px(12) } });
                Add(new Label("air mass m →") { style = { position = Position.Absolute, right = 0, bottom = 0, fontSize = UiType.Px(12) } });
                Add(new Label("yellow: readings · blue: G₀ at m = 0") { style = { position = Position.Absolute, right = 0, top = 0, fontSize = UiType.Px(12) } });
            }

            void Draw(MeshGenerationContext ctx)
            {
                Rect r = contentRect;
                if (r.width <= 0f || p.AirMass.Length == 0)
                    return;
                float left = 30f, right = r.width - 20f, top = 22f, bottom = r.height - 22f;
                double mMax = 0.0, yMin = double.MaxValue, yMax = double.MinValue;
                foreach (double m in p.AirMass) mMax = Math.Max(mMax, m);
                foreach (double d in p.Dni) { double y = Math.Log(d); yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y); }
                yMax = Math.Max(yMax, Math.Log(p.G0));
                Vector2 At(double m, double y) => new Vector2(
                    left + (float)(m / Math.Max(1e-9, mMax)) * (right - left),
                    bottom - (float)((y - yMin) / Math.Max(1e-9, yMax - yMin)) * (bottom - top));

                var g = ctx.painter2D;
                g.lineWidth = 1f;
                g.strokeColor = new Color(1f, 1f, 1f, 0.35f);
                g.BeginPath(); g.MoveTo(new Vector2(left, top)); g.LineTo(new Vector2(left, bottom)); g.LineTo(new Vector2(right, bottom)); g.Stroke();
                g.fillColor = new Color(1f, 0.85f, 0.35f);
                for (int i = 0; i < p.AirMass.Length; i++)
                {
                    g.BeginPath(); g.Arc(At(p.AirMass[i], Math.Log(p.Dni[i])), 3.5f, Angle.Degrees(0f), Angle.Degrees(360f)); g.Fill();
                }
                // G₀ sits at m = 0 (no air): the line starts there.
                g.fillColor = new Color(0.56f, 0.70f, 1f);
                g.BeginPath(); g.Arc(At(0.0, Math.Log(p.G0)), 3.5f, Angle.Degrees(0f), Angle.Degrees(360f)); g.Fill();
            }
        }
    }
}

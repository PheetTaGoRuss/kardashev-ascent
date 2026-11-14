using System;
using System.Text;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
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
            style.borderLeftColor = new Color(0.56f, 0.70f, 1f);

            string title, intro, ask, formulas;
            var head = new System.Collections.Generic.List<string>();
            var rows = new System.Collections.Generic.List<string[]>();
            switch (puzzle.Parameter)
            {
                case SolarParameter.OpticalDepth:
                    title = "Air haze τ: the Langley plot";
                    intro = $"Sun photometer through the day · above the air G₀ = {puzzle.G0:0} W/m² · m = thicknesses of air crossed";
                    head.AddRange(new[] { "sun height h", "air mass m", "direct DNI" });
                    for (int i = 0; i < puzzle.Dni.Length; i++)
                        rows.Add(new[] { $"{puzzle.AltitudeDeg[i]:0}°", $"{puzzle.AirMass[i]:0.000}", $"{puzzle.Dni[i]:0} W/m²" });
                    ask = "Optical depth τ:";
                    formulas = "Beer–Lambert: DNI = G₀ · e^(−τ·m).\nSo ln DNI = ln G₀ − τ·m: plot ln DNI against m, and the line falls with slope −τ.\n" +
                               "From one row: τ = ln(G₀ / DNI) / m. Using several rows (the slope) is more exact.";
                    break;
                case SolarParameter.DiffuseShare:
                    title = "Diffuse light share k";
                    intro = $"Shaded sensor (sky glow DHI) + tracker (direct beam DNI) · G₀ = {puzzle.G0:0} W/m²";
                    head.AddRange(new[] { "sun height h", "direct DNI", "diffuse DHI" });
                    for (int i = 0; i < puzzle.Dhi.Length; i++)
                        rows.Add(new[] { $"{puzzle.AltitudeDeg[i]:0}°", $"{puzzle.Dni[i]:0} W/m²", $"{puzzle.Dhi[i]:0.0} W/m²" });
                    ask = "Diffuse share k:";
                    formulas = "The air takes 1 − DNI / G₀ of the beam out. A share k of that comes down as diffuse light, spread over " +
                               "the ground, which the sun meets at sin h:\n  DHI = k · G₀ · sin h · (1 − DNI / G₀)\n" +
                               "So k = DHI / (G₀ · sin h · (1 − DNI / G₀)). Work it out for each row and average.";
                    break;
                case SolarParameter.Albedo:
                    title = "Ground reflectance ρ_g";
                    intro = "Sensor facing up (light in) and down (light the ground throws back)";
                    head.AddRange(new[] { "reading", "incoming (up)", "reflected (down)" });
                    for (int i = 0; i < puzzle.Incoming.Length; i++)
                        rows.Add(new[] { $"{i + 1}", $"{puzzle.Incoming[i]:0} W/m²", $"{puzzle.Reflected[i]:0.0} W/m²" });
                    ask = "Reflectance ρ (0–1):";
                    formulas = "Reflectance is the share of the light that bounces back: ρ = reflected / incoming.\n" +
                               "Add up all readings first (single readings carry small sensor errors): ρ = Σ reflected / Σ incoming.\n" +
                               "Tilted panels catch some of this light from the ground in front of them.";
                    break;
                case SolarParameter.AirTemperature:
                    title = "Air temperature: the weather station";
                    intro = "Weather station, read every 3 hours for a day";
                    head.AddRange(new[] { "time", "air temperature", "" });
                    for (int i = 0; i < puzzle.TemperatureC.Length; i++)
                        rows.Add(new[] { $"{puzzle.HourOfDay[i]:00}:00", $"{puzzle.TemperatureC[i]:0.0} °C", "" });
                    ask = "Daily mean temperature (°C):";
                    formulas = "The readings are evenly spaced through the whole day, so the daily mean is their average:\n" +
                               "  T_mean = (sum of the 8 readings) / 8.\n" +
                               "Panels run hotter than the air; each °C over 25 °C costs a silicon cell about 0.4 % of its output.";
                    break;
                case SolarParameter.Dust:
                    title = "Dust on panels";
                    intro = "Two test cells for a week: one wiped daily, one left dusty";
                    head.AddRange(new[] { "reading", "clean cell", "dusty cell" });
                    for (int i = 0; i < puzzle.CleanW.Length; i++)
                        rows.Add(new[] { $"{i + 1}", $"{puzzle.CleanW[i]:0.0} W", $"{puzzle.DustyW[i]:0.0} W" });
                    ask = "Share of light the dust blocks (0–1):";
                    formulas = "The dusty cell loses a share of its light: f_dust = 1 − (dusty / clean).\n" +
                               "Use the sums of all readings: f_dust = 1 − Σ dusty / Σ clean. 0 means the dust does nothing.";
                    break;
                case SolarParameter.Spectrum:
                    title = "The star's spectrum";
                    intro = "Spectrometer on the star, 10 nm steps (100 = brightest) · hotter stars peak bluer";
                    head.AddRange(new[] { "wavelength", "brightness", "" });
                    for (int i = 0; i < puzzle.WavelengthNm.Length; i++)
                        rows.Add(new[] { $"{puzzle.WavelengthNm[i]:0} nm", $"{puzzle.Brightness[i]:0.0}", "" });
                    ask = "Star temperature (K):";
                    formulas = "Find the wavelength where the brightness peaks, λ_peak. Wien's displacement law:\n" +
                               $"  T = b / λ_peak, with b = {ParameterPuzzle.WienMicronK:0} µm·K (so divide by λ in µm: 500 nm = 0.5 µm).\n" +
                               "The Sun peaks near 500 nm → about 5800 K. A red dwarf peaks past 1000 nm → under 2900 K.\n" +
                               "The star's temperature sets how well the cell material matches its light (f_spec).";
                    break;
                default:
                    title = "Clouds: the light log";
                    intro = "Five days of light on flat ground next to a clear sky";
                    head.AddRange(new[] { "day", "clear sky", "measured" });
                    for (int d = 0; d < puzzle.ClearKwh.Length; d++)
                        rows.Add(new[] { $"{d + 1}", $"{puzzle.ClearKwh[d]:0.00} kWh/m²", $"{puzzle.MeasuredKwh[d]:0.00} kWh/m²" });
                    ask = "Share that got through f:";
                    formulas = "Single days swing with the weather, so add them up:\n  f = (sum of measured) / (sum of clear sky).\n" +
                               "f = 1 means no clouds; 0.7 means clouds took 30 %.";
                    break;
            }

            // A symbol badge and the title, then one line on what the robot brought (director 2026-11-14: less text).
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.Add(ParameterLook.Badge(puzzle.Parameter, 22f));
            Label t = MakeLabel(title, "robot-name");
            t.style.flexGrow = 1;
            titleRow.Add(t);
            Add(titleRow);
            Add(MakeLabel(intro, "job-text"));
            if (puzzle.Parameter == SolarParameter.OpticalDepth)
                Add(new LangleyPlot(puzzle));
            if (puzzle.Parameter == SolarParameter.Spectrum)
                Add(new SpectrumPlot(puzzle));
            Add(Table(head, rows));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            Label l = MakeLabel(ask, "job-text");
            l.style.flexGrow = 1;
            row.Add(l);
            answer = new TextField { maxLength = 10 };
            answer.AddToClassList("puzzle-field");
            answer.style.width = 70;
            row.Add(answer);
            Add(row);

            var buttons = new VisualElement();
            buttons.AddToClassList("button-row");
            var submit = new Button(Submit) { text = "Check" };
            submit.AddToClassList("small-button");
            buttons.Add(submit);
            var hint = new Button(() => hints.style.display = DisplayStyle.Flex) { text = "? Formula" };
            hint.AddToClassList("small-button");
            buttons.Add(hint);
            Add(buttons);

            hints = MakeLabel(formulas, "job-text");
            hints.style.display = DisplayStyle.None;
            Add(hints);
            message = MakeLabel("", "job-text");
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
            message.text = grade == LatitudePuzzle.Grade.Precise ? "Precise! " + result : result;
            if (grade != LatitudePuzzle.Grade.Wrong)
                solved?.Invoke(message.text);
        }

        /// <summary>A small table with fixed-width columns (labels don't keep spaces lined up).</summary>
        static VisualElement Table(System.Collections.Generic.List<string> head, System.Collections.Generic.List<string[]> rows)
        {
            var table = new VisualElement();
            table.style.marginTop = 4;
            VisualElement Row(string[] cells, bool header)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                foreach (string c in cells)
                {
                    var l = new Label(c);
                    l.AddToClassList("job-text");
                    l.style.width = 110;
                    l.style.flexGrow = 0;
                    if (header)
                    {
                        l.style.unityFontStyleAndWeight = FontStyle.Bold;
                        l.style.color = new Color(0.62f, 0.68f, 0.8f);
                    }
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
                style.height = 110;
                style.marginTop = 4;
                generateVisualContent += Draw;
                Add(new Label("brightness ↑") { style = { position = Position.Absolute, left = 0, top = 0, fontSize = 10 } });
                Add(new Label("wavelength →") { style = { position = Position.Absolute, right = 0, bottom = 0, fontSize = 10 } });
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
                style.height = 110;
                style.marginTop = 4;
                generateVisualContent += Draw;
                Add(new Label("ln DNI ↑") { style = { position = Position.Absolute, left = 0, top = 0, fontSize = 10 } });
                Add(new Label("air mass m →") { style = { position = Position.Absolute, right = 0, bottom = 0, fontSize = 10 } });
                Add(new Label("yellow: readings · blue: G₀ at m = 0") { style = { position = Position.Absolute, right = 0, top = 0, fontSize = 10 } });
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

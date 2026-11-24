using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The "fit the data" puzzle at the end of an experiment (RS-13): the instrument's readings as dots, the player's model as a curve
    /// moved by one to three sliders, the residuals as short lines. No pass or fail: the fit sets how narrow the result's range gets.
    /// It only draws and calls <see cref="IPlanetSimulation.AnswerFitPuzzle"/>; the quality of the fit is computed in the simulation.
    /// </summary>
    public sealed class FitPuzzleView : VisualElement
    {
        readonly FitPuzzle puzzle;
        readonly double[] handles;
        readonly Label[] valueLabels;
        readonly Label share, result;
        readonly Plot plot;

        public FitPuzzleView(IPlanetSimulation sim, ExperimentKind kind, Action<string> done)
        {
            puzzle = sim.PuzzleFor(kind);
            handles = (double[])puzzle.Start.Clone();
            style.paddingLeft = style.paddingRight = 8; style.paddingTop = style.paddingBottom = 6;
            style.backgroundColor = UiPalette.AccentA(0.08f);

            var title = new Label(Experiments.Name(kind) + " · fit the readings");
            title.AddToClassList("robot-name");
            Add(title);
            var physics = new Label(puzzle.Physics);
            physics.AddToClassList("job-text"); physics.AddToClassList("wrap");
            Add(physics);

            plot = new Plot(this) { style = { height = 160, marginTop = 4, marginBottom = 4 } };
            Add(plot);

            valueLabels = new Label[handles.Length];
            for (int i = 0; i < handles.Length; i++)
            {
                int k = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                var name = new Label(puzzle.ParamNames[i]); name.AddToClassList("job-text"); name.style.width = 190;
                row.Add(name);
                var slider = new Slider((float)puzzle.Min[i], (float)puzzle.Max[i]) { value = (float)handles[i] };
                slider.style.flexGrow = 1;
                slider.RegisterValueChangedCallback(e => { handles[k] = e.newValue; Update(); });
                row.Add(slider);
                valueLabels[i] = new Label(); valueLabels[i].AddToClassList("job-text"); valueLabels[i].style.width = 70;
                row.Add(valueLabels[i]);
                Add(row);
            }
            share = new Label(); share.AddToClassList("job-text");
            Add(share);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            var submit = new Button(() => { string r = sim.AnswerFitPuzzle(kind, handles); done?.Invoke(r); }) { text = "Use this fit" };
            submit.AddToClassList("small-button");
            var auto = new Button(() => { string r = sim.AutoFitPuzzle(kind); done?.Invoke(r); }) { text = "Fit for me" };
            auto.AddToClassList("small-button");
            buttons.Add(submit); buttons.Add(auto);
            Add(buttons);
            result = new Label(); result.AddToClassList("job-text");
            Add(result);
            Update();
        }

        void Update()
        {
            for (int i = 0; i < handles.Length; i++) valueLabels[i].text = handles[i].ToString("0.###");
            double s = puzzle.Share(handles);
            share.text = puzzle.ExtraHours != null
                ? $"Range left: {s * 100.0:0} % of the original · robot time for these samples: {puzzle.ExtraHours(handles):0.#} h"
                : $"Range left: {s * 100.0:0} % of the original (lower is better; ◆ rule: {FitPuzzle.PrecisionShare * 100.0:0} % is the instrument's precision, Fit for me leaves {FitPuzzle.AutoFitShare * 100.0:0} %)";
            plot.MarkDirtyRepaint();
        }

        sealed class Plot : VisualElement
        {
            readonly FitPuzzleView v;
            public Plot(FitPuzzleView view)
            {
                v = view;
                style.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.9f);
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext mgc)
            {
                FitPuzzle p = v.puzzle;
                Rect r = contentRect;
                if (r.width < 10f || r.height < 10f) return;
                double xmin = p.X[0], xmax = p.X[p.X.Length - 1];
                double ymin = double.MaxValue, ymax = double.MinValue;
                foreach (double y in p.Y) { ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y); }
                if (p.Curve != null)
                    foreach (double x in p.X) { double c = p.Curve(v.handles, x); ymin = Math.Min(ymin, c); ymax = Math.Max(ymax, c); }
                if (ymax - ymin < 1e-9) ymax = ymin + 1.0;
                double pad = 0.08 * (ymax - ymin); ymin -= pad; ymax += pad;
                Vector2 Map(double x, double y) => new Vector2((float)((x - xmin) / (xmax - xmin) * (r.width - 8) + 4), (float)(r.height - 4 - (y - ymin) / (ymax - ymin) * (r.height - 8)));
                Painter2D g = mgc.painter2D;
                if (p.Curve != null)
                {
                    g.strokeColor = new Color(0.3f, 0.9f, 0.95f); g.lineWidth = 2f;
                    g.BeginPath();
                    for (int i = 0; i <= 80; i++)
                    {
                        double x = xmin + (xmax - xmin) * i / 80.0;
                        Vector2 q = Map(x, p.Curve(v.handles, x));
                        if (i == 0) g.MoveTo(q); else g.LineTo(q);
                    }
                    g.Stroke();
                    g.strokeColor = new Color(1f, 0.55f, 0.45f, 0.7f); g.lineWidth = 1f;
                    for (int i = 0; i < p.X.Length; i++)
                    {
                        g.BeginPath(); g.MoveTo(Map(p.X[i], p.Y[i])); g.LineTo(Map(p.X[i], p.Curve(v.handles, p.X[i]))); g.Stroke();
                    }
                }
                else if (p.Kind == ExperimentKind.ReliefSurvey)
                {
                    double line = p.Thresholds.Mean + v.handles[0] * p.Thresholds.Sd;
                    g.strokeColor = new Color(0.3f, 0.9f, 0.95f); g.lineWidth = 2f;
                    g.BeginPath(); g.MoveTo(Map(xmin, line)); g.LineTo(Map(xmax, line)); g.Stroke();
                }
                g.fillColor = new Color(1f, 0.85f, 0.35f);
                int shown = p.Curve == null && p.Kind == ExperimentKind.OreAssay ? Math.Max(1, (int)Math.Round(v.handles[0])) : p.X.Length;
                for (int i = 0; i < p.X.Length; i++)
                {
                    bool rejected = p.Kind == ExperimentKind.ReliefSurvey && p.Y[i] > p.Thresholds.Mean + v.handles[0] * p.Thresholds.Sd;
                    g.fillColor = i >= shown || rejected ? new Color(0.5f, 0.5f, 0.55f, 0.5f) : new Color(1f, 0.85f, 0.35f);
                    g.BeginPath(); g.Arc(Map(p.X[i], p.Y[i]), 2.5f, 0f, 360f); g.Fill();
                }
            }
        }
    }
}

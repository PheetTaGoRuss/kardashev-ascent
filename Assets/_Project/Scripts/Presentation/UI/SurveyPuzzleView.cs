using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// One ground-survey puzzle (stage F): the cores a robot drilled, assayed per material, one answer box per
    /// material for the tile's grade, and the rule behind a hint button.
    /// </summary>
    public sealed class SurveyPuzzleView : VisualElement
    {
        readonly IPlanetSimulation sim;
        readonly SurveyPuzzle puzzle;
        readonly List<TextField> answers = new List<TextField>();
        readonly Label message, hints;
        readonly Action<string> solved;

        public SurveyPuzzleView(IPlanetSimulation sim, SurveyPuzzle puzzle, Action<string> solved)
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

            Add(PuzzleGuide.Text($"What's in the ground of tile #{puzzle.Tile}?", bold: true, size: PuzzleGuide.TitleSize));
            Add(PuzzleGuide.Text($"A robot drilled {puzzle.Rows.Count} rock samples (cores) across the tile and tested each one. " +
                                 "The numbers are grades: 1 = an ordinary deposit on Earth, 2 = twice as rich, 'none' = nothing found."));

            var head = new List<string> { "core (depth)" };
            foreach (Material m in puzzle.Materials)
                head.Add(Materials.Name(m));
            var rows = new List<string[]>();
            for (int i = 0; i < puzzle.Rows.Count; i++)
            {
                SurveyPuzzle.CoreRow r = puzzle.Rows[i];
                var cells = new string[1 + r.Grades.Length];
                cells[0] = $"{i + 1} ({r.DepthM:0.0} m)";
                for (int m = 0; m < r.Grades.Length; m++)
                    cells[1 + m] = r.Grades[m] <= 0.0 ? "none" : $"{r.Grades[m]:0.00}";
                rows.Add(cells);
            }
            Add(Table(head, rows));
            Add(PuzzleGuide.Steps(
                "Pick one material (one column).",
                "Add up its grades from every core. A core with 'none' counts as 0.",
                "Divide by the number of cores. That average is the tile's grade.",
                $"Within {SurveyPuzzle.PreciseShare * 100:0} % counts as exact; within {SurveyPuzzle.RoughShare * 100:0} % as rough (shown ±)."));
            Add(PuzzleGuide.Example(
                "Iron in 5 cores: 1.2, none, 0.9, 1.5, 0.4",
                "1.2 + 0 + 0.9 + 1.5 + 0.4 = 4.0   →   4.0 / 5 = 0.80"));

            for (int m = 0; m < puzzle.Materials.Count; m++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 3;
                Label l = PuzzleGuide.Text($"{Materials.Name(puzzle.Materials[m])}: average grade", bold: true);
                l.style.flexGrow = 1;
                row.Add(l);
                var f = new TextField { maxLength = 8 };
                f.AddToClassList("puzzle-field");
                f.style.width = 110;
                f.style.fontSize = PuzzleGuide.BodySize;
                row.Add(f);
                answers.Add(f);
                Add(row);
            }

            var buttons = new VisualElement();
            buttons.AddToClassList("button-row");
            var submit = new Button(Submit) { text = "Check" };
            submit.AddToClassList("small-button");
            submit.style.fontSize = PuzzleGuide.BodySize;
            buttons.Add(submit);
            Add(buttons);

            hints = PuzzleGuide.Text("");
            hints.style.display = DisplayStyle.None;
            Add(hints);
            message = PuzzleGuide.Text("");
            message.AddToClassList("message");
            Add(message);
        }

        void Submit()
        {
            var values = new List<double>();
            foreach (TextField f in answers)
            {
                if (!double.TryParse((f.value ?? "").Trim().Replace('−', '-').Replace(',', '.'),
                                     System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                {
                    message.text = "Enter a grade for every material (a number like 0.85).";
                    return;
                }
                values.Add(v);
            }
            string result = sim.AnswerSurveyPuzzle(puzzle.Tile, values, out LatitudePuzzle.Grade grade);
            message.text = grade == LatitudePuzzle.Grade.Precise ? "Precise! " + result : result;
            if (grade != LatitudePuzzle.Grade.Wrong)
                solved?.Invoke(message.text);
        }

        static VisualElement Table(List<string> head, List<string[]> rows)
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
    }
}

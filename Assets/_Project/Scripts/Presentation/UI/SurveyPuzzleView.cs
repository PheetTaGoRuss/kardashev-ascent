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
    /// One ground-survey puzzle (stage F): the cores a robot drilled, assayed per material, and one answer box with
    /// its own Check button per material (director 2026-11-15: answer them one at a time, now or later).
    /// </summary>
    public sealed class SurveyPuzzleView : VisualElement
    {
        readonly IPlanetSimulation sim;
        readonly SurveyPuzzle puzzle;
        readonly List<TextField> answers = new List<TextField>();
        readonly List<Button> checks = new List<Button>();
        readonly List<Label> states = new List<Label>();
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
            style.borderLeftColor = UiPalette.Accent;

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
                var f = new TextField { maxLength = 12 };
                CalculatorPad.TrackAnswer(f);
                f.AddToClassList("puzzle-field");
                f.style.width = 110;
                f.style.fontSize = PuzzleGuide.BodySize;
                row.Add(f);
                answers.Add(f);
                int index = m;
                var check = new Button(() => CheckOne(index)) { text = "Check" };
                check.AddToClassList("small-button");
                check.style.fontSize = PuzzleGuide.BodySize;
                check.style.marginLeft = 4;
                row.Add(check);
                checks.Add(check);
                Label state = PuzzleGuide.Text("", size: PuzzleGuide.SmallSize);
                state.style.width = 44;
                state.style.marginLeft = 4;
                row.Add(state);
                states.Add(state);
                Add(row);
            }

            var buttons = new VisualElement();
            buttons.AddToClassList("button-row");
            buttons.Add(CalculatorPad.OpenButton());
            Add(buttons);
            ShowSolved();

            hints = PuzzleGuide.Text("");
            hints.style.display = DisplayStyle.None;
            Add(hints);
            message = PuzzleGuide.Text("");
            message.AddToClassList("message");
            Add(message);
        }

        /// <summary>Check one material's answer; a right one is kept and its row locks.</summary>
        void CheckOne(int m)
        {
            TextField f = answers[m];
            if (!double.TryParse((f.value ?? "").Trim().Replace('−', '-').Replace(',', '.'),
                                 System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                message.text = $"Enter a grade for {Materials.Name(puzzle.Materials[m]).ToLowerInvariant()} (a number like 0.85).";
                return;
            }
            string result = sim.AnswerSurveyMaterial(puzzle.Tile, m, v, out LatitudePuzzle.Grade grade);
            KardashevAscent.Presentation.Audio.GameAudio.Play(grade != LatitudePuzzle.Grade.Wrong ? KardashevAscent.Presentation.Audio.Sfx.Correct : KardashevAscent.Presentation.Audio.Sfx.Wrong);
            message.text = result;
            ShowSolved();
            if (puzzle.AllSolved)
                solved?.Invoke(message.text);
        }

        /// <summary>Rows already worked out show as done and can't be changed.</summary>
        void ShowSolved()
        {
            for (int m = 0; m < answers.Count; m++)
            {
                bool done = puzzle.IsSolved(m);
                bool rough = done && puzzle.RoughSolved != null && puzzle.RoughSolved.Contains(m);
                answers[m].SetEnabled(!done);
                checks[m].style.display = done ? DisplayStyle.None : DisplayStyle.Flex;
                states[m].text = done ? (rough ? "close" : "done") : "";
                states[m].style.color = rough ? UiPalette.Warning : UiPalette.Good;
            }
        }

        static VisualElement Table(List<string> head, List<string[]> rows)
        {
            var table = new VisualElement();
            table.style.marginTop = 4;
            VisualElement Row(string[] cells, bool header)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                for (int i = 0; i < cells.Length; i++)
                {
                    var l = new Label(cells[i]);
                    l.AddToClassList("job-text");
                    l.style.fontSize = PuzzleGuide.BodySize - 1f;
                    l.style.width = 110;
                    l.style.flexGrow = 0;
                    if (header)
                    {
                        l.style.unityFontStyleAndWeight = FontStyle.Bold;
                        l.style.color = UiPalette.Muted;
                    }
                    else if (i > 0)
                        CalculatorPad.MakeClickable(l);   // a grade ("none" = 0) goes into the calculator
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

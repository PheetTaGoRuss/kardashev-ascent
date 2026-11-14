using System.Text.RegularExpressions;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The floating science calculator for the puzzles (director 2026-11-14). Click a number on a puzzle card to put
    /// it in (it is copied to the clipboard too); type or click keys; "Put in answer" fills the answer box you last
    /// clicked. Angles are in degrees. The maths lives in <see cref="ScienceCalculator"/>.
    /// </summary>
    public static class CalculatorPad
    {
        static readonly Color Accent = new Color(0.56f, 0.70f, 1f);
        static readonly Regex NumberIn = new Regex(@"[-−+]?\d+(?:[.,]\d+)?");

        static VisualElement window;
        static TextField display;
        static Label result;
        static Label status;
        static bool justEvaluated;
        static TextField lastAnswer;

        // ---- what the puzzle cards call ----

        /// <summary>A button that opens or closes the calculator.</summary>
        public static Button OpenButton()
        {
            Button b = null;
            b = new Button(() => Toggle(b)) { text = "Calculator" };
            b.AddToClassList("small-button");
            b.style.fontSize = PuzzleGuide.BodySize;
            return b;
        }

        /// <summary>Make a label's number clickable: a click puts it in the calculator and copies it.</summary>
        public static void MakeClickable(Label label, string number = null)
        {
            number ??= FirstNumber(label.text);
            if (number == null)
                return;
            label.style.color = Accent;
            label.tooltip = $"Click: put {number} in the calculator";
            label.RegisterCallback<PointerEnterEvent>(_ => label.style.backgroundColor = new Color(0.56f, 0.70f, 1f, 0.15f));
            label.RegisterCallback<PointerLeaveEvent>(_ => label.style.backgroundColor = StyleKeyword.Null);
            label.RegisterCallback<ClickEvent>(e => { Insert(label, number); e.StopPropagation(); });
        }

        /// <summary>"Click a number" chips: the values a puzzle gives, each one clickable.</summary>
        public static VisualElement Values(params string[] nameAndNumber)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 6;
            row.Add(PuzzleGuide.Text("Numbers (click one):", bold: true, size: PuzzleGuide.SmallSize + 1));
            for (int i = 0; i + 1 < nameAndNumber.Length; i += 2)
            {
                string name = nameAndNumber[i], number = nameAndNumber[i + 1];
                var chip = PuzzleGuide.Text($"{name} = {number}", size: PuzzleGuide.BodySize);
                chip.style.marginLeft = 6;
                chip.style.paddingLeft = chip.style.paddingRight = 6;
                chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius =
                    chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 4;
                chip.style.borderTopWidth = chip.style.borderBottomWidth = chip.style.borderLeftWidth = chip.style.borderRightWidth = 1;
                chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor =
                    new Color(0.56f, 0.70f, 1f, 0.5f);
                MakeClickable(chip, number);
                row.Add(chip);
            }
            return row;
        }

        /// <summary>Remember this field as the one "Put in answer" fills, once the player clicks into it.</summary>
        public static void TrackAnswer(TextField field)
        {
            field.RegisterCallback<FocusInEvent>(_ => lastAnswer = field);
            if (lastAnswer == null || lastAnswer.panel == null)
                lastAnswer = field;
        }

        public static string FirstNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Trim() == "none") return "0";
            Match m = NumberIn.Match(text);
            return m.Success ? m.Value.Replace('−', '-').Replace(',', '.').TrimStart('+') : null;
        }

        // ---- the window ----

        static void Toggle(VisualElement from)
        {
            if (Ensure(from) && window.style.display == DisplayStyle.None)
                window.style.display = DisplayStyle.Flex;
            else if (window != null)
                window.style.display = DisplayStyle.None;
        }

        static void Insert(VisualElement from, string number)
        {
            GUIUtility.systemCopyBuffer = number;
            if (!Ensure(from)) return;
            window.style.display = DisplayStyle.Flex;
            string s = display.value ?? "";
            if (justEvaluated)
                s = "";
            else
            {
                // A number right after another one replaces it, like a pocket calculator; after an operator it joins.
                Match tail = Regex.Match(s, @"[\d.]+$");
                if (tail.Success) s = s.Substring(0, tail.Index);
            }
            Set(s + number);
        }

        static bool Ensure(VisualElement from)
        {
            if (window != null && window.panel != null)
                return true;
            VisualElement root = from?.panel?.visualTree;
            if (root == null)
                return false;
            window = Build();
            root.Add(window);
            return true;
        }

        static void Set(string text)
        {
            display.SetValueWithoutNotify(text);
            justEvaluated = false;
            Preview();
        }

        static void Preview()
        {
            if (ScienceCalculator.TryEvaluate(display.value, out double v, out string error))
            {
                result.text = "= " + ScienceCalculator.Format(v);
                status.text = "";
            }
            else
            {
                result.text = "= ?";
                status.text = string.IsNullOrWhiteSpace(display.value) ? "Click a number on the puzzle, or type." : error;
            }
        }

        static void Key(string k)
        {
            string s = display.value ?? "";
            switch (k)
            {
                case "C": Set(""); return;
                case "Del": Set(s.Length > 0 ? s.Substring(0, s.Length - 1) : ""); return;
                case "=":
                    if (ScienceCalculator.TryEvaluate(s, out double v, out _))
                    {
                        display.SetValueWithoutNotify(ScienceCalculator.Format(v));
                        Preview();
                        justEvaluated = true;
                    }
                    return;
            }
            bool isOperator = k == "+" || k == "-" || k == "×" || k == "/" || k == "^";
            if (justEvaluated && !isOperator && k != ")")
                s = "";
            Set(s + k);
        }

        static VisualElement Build()
        {
            var w = new VisualElement();
            w.pickingMode = PickingMode.Position;
            w.style.position = Position.Absolute;
            w.style.left = 330;
            w.style.top = 230;
            w.style.width = 300;
            w.style.paddingLeft = w.style.paddingRight = 10;
            w.style.paddingTop = w.style.paddingBottom = 8;
            w.style.backgroundColor = new Color(0.04f, 0.055f, 0.095f, 0.98f);
            w.style.borderTopWidth = w.style.borderBottomWidth = w.style.borderLeftWidth = w.style.borderRightWidth = 1;
            w.style.borderTopColor = w.style.borderBottomColor = w.style.borderLeftColor = w.style.borderRightColor = new Color(0.56f, 0.70f, 1f, 0.6f);
            w.style.borderTopLeftRadius = w.style.borderTopRightRadius = w.style.borderBottomLeftRadius = w.style.borderBottomRightRadius = 6;

            // Title bar: drag it to move the calculator.
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            Label title = PuzzleGuide.Text("Calculator · degrees (DEG)", bold: true);
            title.style.flexGrow = 1;
            bar.Add(title);
            var close = new Button(() => w.style.display = DisplayStyle.None) { text = "×" };
            close.AddToClassList("close-button");
            close.style.fontSize = 16;
            bar.Add(close);
            w.Add(bar);
            Drag(bar, w);

            display = new TextField { maxLength = 80 };
            display.AddToClassList("puzzle-field");
            display.style.fontSize = PuzzleGuide.TitleSize;
            display.style.marginTop = 4;
            display.RegisterValueChangedCallback(_ => { justEvaluated = false; Preview(); });
            display.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Key("="); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            w.Add(display);

            result = PuzzleGuide.Text("= ?", bold: true, size: PuzzleGuide.TitleSize + 3);
            result.style.unityTextAlign = TextAnchor.MiddleRight;
            result.tooltip = "Click: copy the result";
            result.RegisterCallback<ClickEvent>(_ => GUIUtility.systemCopyBuffer = ResultText());
            w.Add(result);
            status = PuzzleGuide.Text("Click a number on the puzzle, or type.", size: PuzzleGuide.SmallSize);
            status.style.color = new Color(0.7f, 0.74f, 0.82f);
            w.Add(status);

            string[][] keys =
            {
                new[] { "sin(", "cos(", "tan(", "ln(", "C" },
                new[] { "asin(", "acos(", "atan(", "log(", "Del" },
                new[] { "7", "8", "9", "/", "(" },
                new[] { "4", "5", "6", "×", ")" },
                new[] { "1", "2", "3", "-", "^" },
                new[] { "0", ".", "pi", "+", "=" },
                new[] { "sqrt(", "e" },
            };
            foreach (string[] line in keys)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                foreach (string k in line)
                {
                    string key = k;
                    var b = new Button(() => Key(key)) { text = key.TrimEnd('(') };
                    b.style.flexGrow = 1;
                    b.style.flexBasis = 0;
                    b.style.height = 30;
                    b.style.fontSize = PuzzleGuide.BodySize;
                    b.style.marginLeft = b.style.marginRight = 2;
                    b.style.marginTop = b.style.marginBottom = 2;
                    if (key == "=") b.style.backgroundColor = new Color(0.56f, 0.70f, 1f, 0.55f);
                    row.Add(b);
                }
                w.Add(row);
            }

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginTop = 4;
            var copy = new Button(() => GUIUtility.systemCopyBuffer = ResultText()) { text = "Copy result" };
            var put = new Button(() =>
            {
                string r = ResultText();
                if (r.Length == 0) { status.text = "Nothing to put in yet."; return; }
                if (lastAnswer == null || lastAnswer.panel == null) { status.text = "Click an answer box on the puzzle first."; return; }
                lastAnswer.value = r;
                status.text = "Put in the answer box. Press Check on the puzzle.";
            }) { text = "Put in answer" };
            foreach (Button b in new[] { copy, put })
            {
                b.AddToClassList("small-button");
                b.style.flexGrow = 1;
                b.style.fontSize = PuzzleGuide.BodySize;
                actions.Add(b);
            }
            w.Add(actions);
            return w;
        }

        static string ResultText() =>
            ScienceCalculator.TryEvaluate(display?.value, out double v, out _) ? ScienceCalculator.Format(v) : "";

        static void Drag(VisualElement handle, VisualElement target)
        {
            bool dragging = false;
            Vector2 start = default, origin = default;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target is Button) return;
                dragging = true;
                start = e.position;
                origin = new Vector2(target.resolvedStyle.left, target.resolvedStyle.top);
                handle.CapturePointer(e.pointerId);
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                Vector2 d = (Vector2)e.position - start;
                target.style.left = origin.x + d.x;
                target.style.top = origin.y + d.y;
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                handle.ReleasePointer(e.pointerId);
            });
        }
    }
}

using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The small "i" button (director 2026-11-15, after Oxygen Not Included): screens show short titles and short lines;
    /// the full method and background sit behind an "i" that opens a box under its line and closes again.
    /// </summary>
    public sealed class InfoTip
    {
        public Button Button { get; }
        public Label Detail { get; }

        /// <summary>The full text (can change while the box is open).</summary>
        public string Text
        {
            get => Detail.text;
            set
            {
                Detail.text = value ?? "";
                Button.tooltip = string.IsNullOrEmpty(value) ? "" : "How it works";
                Button.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        InfoTip(string text)
        {
            Detail = new Label();
            Detail.style.whiteSpace = WhiteSpace.Normal;
            Detail.style.fontSize = 13;
            Detail.style.color = UiPalette.Muted;
            Detail.style.marginTop = 3;
            Detail.style.marginBottom = 3;
            Detail.style.paddingLeft = Detail.style.paddingRight = 8;
            Detail.style.paddingTop = Detail.style.paddingBottom = 5;
            Detail.style.backgroundColor = UiPalette.AccentA(0.07f);
            Detail.style.borderLeftWidth = 2;
            Detail.style.borderLeftColor = UiPalette.AccentA(0.6f);
            Detail.style.display = DisplayStyle.None;

            Button = new Button(Toggle) { text = "i" };
            Button.style.width = 18;
            Button.style.height = 18;
            Button.style.minWidth = 18;
            Button.style.paddingLeft = Button.style.paddingRight = Button.style.paddingTop = Button.style.paddingBottom = 0;
            Button.style.marginLeft = 6;
            Button.style.marginRight = 0;
            Button.style.fontSize = 12;
            Button.style.unityFontStyleAndWeight = FontStyle.Bold;   // upright: italic "i" reads as "/"
            Button.style.color = UiPalette.Accent;
            Button.style.borderTopWidth = Button.style.borderBottomWidth = Button.style.borderLeftWidth = Button.style.borderRightWidth = 1;
            Button.style.borderTopColor = Button.style.borderBottomColor = Button.style.borderLeftColor = Button.style.borderRightColor = UiPalette.AccentA(0.6f);
            Text = text;
        }

        void Toggle()
        {
            bool open = Detail.style.display == DisplayStyle.None;
            Detail.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            Button.style.backgroundColor = open ? UiPalette.AccentA(0.25f) : StyleKeyword.Null;
        }

        /// <summary>
        /// Put an "i" at the end of <paramref name="row"/> (a row element, or a label that is wrapped into one) and its
        /// box right after it.
        /// </summary>
        public static InfoTip After(VisualElement row, string text)
        {
            var tip = new InfoTip(text);
            if (row is Label && row.parent != null)
            {
                // One block holds the line (label + "i") and its box, so whatever shows or hides the line (a window's
                // tabs) shows or hides them together and never opens the box by itself.
                VisualElement parent = row.parent;
                int at = parent.IndexOf(row);
                var block = new VisualElement();
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                parent.Insert(at, block);
                block.Add(line);
                line.Add(row);
                row.style.flexShrink = 1;
                line.Add(tip.Button);
                block.Add(tip.Detail);
                return tip;
            }
            row.Add(tip.Button);
            if (row.parent != null)
                row.parent.Insert(row.parent.IndexOf(row) + 1, tip.Detail);
            return tip;
        }
    }
}

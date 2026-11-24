using System.Linq;
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

        /// <summary>The Guide page this tip points to, if any: shows a "Guide ›" link.</summary>
        public string GuidePage { get; set; }

        /// <summary>A tip shows at most this many lines until "More ›" is pressed (UI information design, stage 3); the rest is never removed.</summary>
        public const int ClampLines = 2;
        const int ClampChars = 150;
        readonly VisualElement box = new VisualElement();
        readonly Label more = new Label("More ›"), guide = new Label("Guide ›");
        bool expanded;

        /// <summary>The full text (can change while the box is open).</summary>
        public string Text
        {
            get => Detail.text;
            set
            {
                full = value ?? "";
                Detail.text = Shown();
                RefreshLinks();
                Button.tooltip = string.IsNullOrEmpty(value) ? "" : "How it works";
                Button.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        string full = "";

        string Shown() => expanded || full.Length <= ClampChars ? full : full.Substring(0, ClampChars).TrimEnd() + "…";

        static System.Collections.Generic.List<string> guideTitles;

        /// <summary>The Guide page a tip is about, when its text names one (a page title of 6 or more letters): every tip with a matching page gets a link.</summary>
        static string MatchingPage(string text)
        {
            guideTitles ??= KardashevAscent.Simulation.Game.GuideBook.All().Select(p => p.Title).Where(t => t.Length >= 6).OrderByDescending(t => t.Length).ToList();
            foreach (string t in guideTitles)
                if (text.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }

        void RefreshLinks()
        {
            if (string.IsNullOrEmpty(GuidePage) && !string.IsNullOrEmpty(full)) GuidePage = MatchingPage(full);
            more.style.display = full.Length > ClampChars ? DisplayStyle.Flex : DisplayStyle.None;
            more.text = expanded ? "Less ‹" : "More ›";
            guide.style.display = string.IsNullOrEmpty(GuidePage) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        InfoTip(string text)
        {
            foreach (Label l in new[] { more, guide })
            {
                l.AddToClassList(UiType.Secondary);
                l.style.color = UiPalette.Accent;
                l.style.marginRight = 12;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            more.RegisterCallback<ClickEvent>(_ => { expanded = !expanded; Detail.text = Shown(); RefreshLinks(); });
            guide.RegisterCallback<ClickEvent>(_ => { if (!string.IsNullOrEmpty(GuidePage)) GuideWindow.Open(GuidePage); });
            var links = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            links.Add(more); links.Add(guide);
            box.Add(Detail); box.Add(links);
            box.style.display = DisplayStyle.None;
            Detail = new Label();
            Detail.style.whiteSpace = WhiteSpace.Normal;
            Detail.AddToClassList(UiType.Secondary);
            Detail.style.color = UiPalette.Muted;
            Detail.style.marginTop = 3;
            Detail.style.marginBottom = 3;
            Detail.style.paddingLeft = Detail.style.paddingRight = 8;
            Detail.style.paddingTop = Detail.style.paddingBottom = 5;
            Detail.style.backgroundColor = UiPalette.AccentA(0.07f);
            Detail.style.borderLeftWidth = 2;
            Detail.style.borderLeftColor = UiPalette.AccentA(0.6f);
            Detail.style.display = DisplayStyle.Flex;

            Button = new Button(Toggle) { text = "i" };
            Button.style.width = 18;
            Button.style.height = 18;
            Button.style.minWidth = 18;
            Button.style.paddingLeft = Button.style.paddingRight = Button.style.paddingTop = Button.style.paddingBottom = 0;
            Button.style.marginLeft = 6;
            Button.style.marginRight = 0;
            Button.AddToClassList(UiType.Secondary);
            Button.style.unityFontStyleAndWeight = FontStyle.Bold;   // upright: italic "i" reads as "/"
            Button.style.color = UiPalette.Accent;
            Button.style.borderTopWidth = Button.style.borderBottomWidth = Button.style.borderLeftWidth = Button.style.borderRightWidth = 1;
            Button.style.borderTopColor = Button.style.borderBottomColor = Button.style.borderLeftColor = Button.style.borderRightColor = UiPalette.AccentA(0.6f);
            Text = text;
        }

        void Toggle()
        {
            bool open = box.style.display == DisplayStyle.None;
            box.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshLinks();
            Button.style.backgroundColor = open ? UiPalette.AccentA(0.25f) : StyleKeyword.Null;
        }

        /// <summary>
        /// Put an "i" at the end of <paramref name="row"/> (a row element, or a label that is wrapped into one) and its
        /// box right after it.
        /// </summary>
        public static InfoTip After(VisualElement row, string text, string guidePage = null)
        {
            var tip = new InfoTip(text) { GuidePage = guidePage };
            tip.RefreshLinks();
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
                block.Add(tip.box);
                return tip;
            }
            row.Add(tip.Button);
            if (row.parent != null)
                row.parent.Insert(row.parent.IndexOf(row) + 1, tip.box);
            return tip;
        }
    }
}

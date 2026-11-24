using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Guide (J): the guide book (director 2026-11-15). Topic tabs and a search box at the top, the pages of the
    /// topic on the left, the open page on the right: a one-line summary, then short headed facts. The facts come
    /// from <see cref="GuideBook"/>, built from the game's own rules; the Controls pages are the keys.
    /// </summary>
    public sealed class GuideWindow : SystemWindow
    {
        public override string Id => "guide";
        public override string Title => "Guide";
        public override KeyCode Key => KeyCode.J;
        public override int Order => 60;
        protected override float Width => 760f;

        const string Controls = "Controls";
        static readonly string[] Topics = GuideBook.Categories.Concat(new[] { Controls }).ToArray();

        static string pendingPage;

        /// <summary>Open the Guide at a page by its title (from an InfoTip, a card or another page's Related links).</summary>
        public static void Open(string title)
        {
            pendingPage = title;
            SystemTabs.Open("guide");
        }

        List<GuidePage> pages;
        string topic = GuideBook.Materials;
        GuidePage open;
        string search = "";
        VisualElement topicBar, list, page;
        readonly Dictionary<string, Button> topicButtons = new Dictionary<string, Button>();

        protected override void BuildBody()
        {
            pages = GuideBook.All();
            pages.AddRange(ControlPages());

            // Topics and search.
            topicBar = new VisualElement();
            topicBar.style.flexDirection = FlexDirection.Row;
            topicBar.style.flexWrap = Wrap.Wrap;
            foreach (string t in Topics)
            {
                string chosen = t;
                var b = new Button(() => { topic = chosen; search = ""; searchField?.SetValueWithoutNotify(""); open = null; Rebuild(); }) { text = t };
                b.AddToClassList("tab");
                b.style.marginBottom = 4;
                topicBar.Add(b);
                topicButtons[t] = b;
            }
            Body.Add(topicBar);
            searchField = new TextField { maxLength = 40 };
            searchField.AddToClassList("puzzle-field");
            searchField.style.marginTop = 2;
            searchField.AddToClassList(UiType.Secondary);
            searchField.RegisterValueChangedCallback(e => { search = e.newValue ?? ""; open = null; Rebuild(); });
            var searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.alignItems = Align.Center;
            var searchLabel = new Label("Search");
            searchLabel.AddToClassList("caption");
            searchLabel.style.marginRight = 6;
            searchRow.Add(searchLabel);
            searchField.style.flexGrow = 1;
            searchRow.Add(searchField);
            Body.Add(searchRow);

            // List on the left, page on the right.
            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.marginTop = 8;
            list = new VisualElement();
            list.style.width = 220;
            list.style.flexShrink = 0;
            list.style.marginRight = 12;
            columns.Add(list);
            page = new VisualElement();
            page.style.flexGrow = 1;
            page.style.flexShrink = 1;
            columns.Add(page);
            Body.Add(columns);
            Rebuild();
        }

        TextField searchField;

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            if (pendingPage == null) return;
            GuidePage target = pages.FirstOrDefault(p => p.Title == pendingPage);
            pendingPage = null;
            if (target == null) return;
            topic = target.Category; search = ""; searchField?.SetValueWithoutNotify(""); open = target;
            Rebuild();
        }

        void Rebuild()
        {
            bool searching = !string.IsNullOrWhiteSpace(search);
            foreach (var kv in topicButtons)
                kv.Value.EnableInClassList("selected", !searching && kv.Key == topic);
            List<GuidePage> shown = pages.Where(p => searching ? p.Mentions(search) : p.Category == topic).ToList();
            if (open == null || !shown.Contains(open))
                open = shown.FirstOrDefault();

            list.Clear();
            if (shown.Count == 0)
                list.Add(Line("Nothing found.", muted: true));
            foreach (GuidePage p in shown)
            {
                GuidePage target = p;
                var item = new Button(() => { open = target; Rebuild(); });
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                item.style.justifyContent = Justify.FlexStart;
                item.style.minHeight = 28;
                item.style.marginBottom = 3;
                item.style.paddingLeft = 6;
                VisualElement thumb = p.Material.HasValue ? new GuidePicture(p.Material.Value, 24f)
                                    : p.Ground.HasValue ? new GuidePicture(p.Ground.Value, 24f) : null;
                if (thumb != null)
                {
                    thumb.style.marginRight = 6;
                    item.Add(thumb);
                }
                var name = new Label(searching ? $"{p.Title} · {p.Category}" : p.Title);
                name.AddToClassList(UiType.Secondary);
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                name.style.whiteSpace = WhiteSpace.Normal;
                name.style.flexShrink = 1;
                name.pickingMode = PickingMode.Ignore;
                item.Add(name);
                if (p == open)
                {
                    item.style.borderTopColor = item.style.borderBottomColor = item.style.borderLeftColor = item.style.borderRightColor = UiPalette.Accent;
                    name.style.color = UiPalette.Accent;
                }
                list.Add(item);
            }

            page.Clear();
            if (open == null)
                return;
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            // A picture of the material or the ground (director 2026-11-15).
            VisualElement picture = open.Material.HasValue ? new GuidePicture(open.Material.Value, 88f)
                                  : open.Ground.HasValue ? new GuidePicture(open.Ground.Value, 88f) : null;
            if (picture != null)
            {
                picture.style.marginRight = 12;
                head.Add(picture);
            }
            if (open.Material.HasValue)
            {
                VisualElement chip = MaterialChips.Icon(open.Material.Value, 22f);
                chip.style.marginRight = 8;
                head.Add(chip);
            }
            var title = new Label(open.Title);
            title.AddToClassList(UiType.Value);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(title);
            page.Add(head);
            Label summary = Line(open.Summary);
            summary.AddToClassList(UiType.Body);
            summary.style.marginTop = 4;
            page.Add(summary);
            foreach (var (heading, lines) in open.Sections)
            {
                var h = new Label(heading.ToUpperInvariant());
                h.AddToClassList("heading");
                h.style.marginTop = 12;
                h.AddToClassList(UiType.Secondary);
                page.Add(h);
                foreach (string line in lines)
                {
                    GuidePage linked = heading == "Related" ? pages.FirstOrDefault(p => p.Title == line) : null;
                    if (linked == null) { page.Add(Line(line)); continue; }
                    var link = new Button(() => { open = linked; topic = linked.Category; Rebuild(); }) { text = linked.Title + " ›" };
                    link.AddToClassList("small-button");
                    link.AddToClassList(UiType.Secondary);
                    link.style.alignSelf = Align.FlexStart;
                    link.style.marginTop = 2;
                    page.Add(link);
                }
            }
        }

        static Label Line(string text, bool muted = false)
        {
            var l = new Label(text);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.AddToClassList(UiType.Secondary);
            l.style.marginTop = 2;
            if (muted) l.style.color = UiPalette.Muted;
            return l;
        }

        static IEnumerable<GuidePage> ControlPages()
        {
            yield return new GuidePage(Controls, "Camera", "Move around the planet and the sites.")
                .Add("Keys", "Drag or WASD / arrows: turn", "Mouse wheel or Q / E: zoom", "R: back to the colony", "F: flashlight", "M: light-potential map");
            yield return new GuidePage(Controls, "Building", "Place and change buildings on a site.")
                .Add("Keys", "Click a tile: open its site", "Drag from the build bar: place", "P: place on the selected slot", "Del: remove",
                     "Tab: next building", "B: best angle", "Ctrl+A: select all slots", "V: sky view over a panel");
            yield return new GuidePage(Controls, "Windows", "Open the colony's windows.")
                .Add("Keys", "C: Overview", "O: Power", "G: Robots", "U: Manufacturing", "I: Operations (flight rules, plants, depots, log)", "Z: Flowsheet", "T: Research", "N: Network", "J: Guide", "K: Calculator", "H: all controls");
            yield return new GuidePage(Controls, "Game", "Time, saving and the menu.")
                .Add("Keys", "Esc: pause menu (sound, save, load)", "F5: save · F9: load", "Time buttons at the top right: pause, real, 1 h/s, 1 d/s");
        }
    }
}

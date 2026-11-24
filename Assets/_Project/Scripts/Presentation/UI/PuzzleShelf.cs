using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Puzzle cards can be put aside for later (director 2026-11-15: minimise a research task and do it later): a card
    /// folds down to a one-line bar with "Open"; "Later" folds it again. Which cards are folded is remembered for the
    /// session. Another screen can ask for a card to be opened and shown (the tile panel's survey alert).
    /// </summary>
    public static class PuzzleShelf
    {
        static readonly HashSet<string> later = new HashSet<string>();
        static string focus;

        public static string LatitudeKey(int tile) => "latitude:" + tile;
        public static string LightKey(Simulation.Solar.SolarParameter p) => "light:" + p;
        public static string SurveyKey(int tile) => "survey:" + tile;

        /// <summary>Open this card and scroll to it the next time the Research window draws its puzzles.</summary>
        public static void Focus(string key)
        {
            later.Remove(key);
            focus = key;
        }

        /// <summary>The card inside a foldable frame: a header bar with its title and a Later / Open button.</summary>
        public static VisualElement Wrap(VisualElement card, string key, string title)
        {
            var frame = new VisualElement { name = key };
            frame.style.marginTop = 6;
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = bar.style.paddingRight = 6;
            bar.style.paddingTop = bar.style.paddingBottom = 3;
            bar.style.backgroundColor = UiPalette.AccentA(0.10f);
            Label name = PuzzleGuide.Text(title, bold: true);
            name.style.flexGrow = 1;
            bar.Add(name);
            Label waiting = PuzzleGuide.Text("waiting for you", size: PuzzleGuide.SmallSize);
            waiting.style.color = UiPalette.Accent;
            waiting.style.marginRight = 8;
            bar.Add(waiting);
            Button toggle = null;
            toggle = new Button(() =>
            {
                if (!later.Add(key)) later.Remove(key);
                Apply(card, waiting, toggle, key);
            });
            toggle.AddToClassList("small-button");
            toggle.style.fontSize = UiType.Px(PuzzleGuide.BodySize);
            bar.Add(toggle);
            frame.Add(bar);
            frame.Add(card);
            Apply(card, waiting, toggle, key);
            return frame;
        }

        static void Apply(VisualElement card, Label waiting, Button toggle, string key)
        {
            bool folded = later.Contains(key);
            card.style.display = folded ? DisplayStyle.None : DisplayStyle.Flex;
            waiting.style.display = folded ? DisplayStyle.Flex : DisplayStyle.None;
            toggle.text = folded ? "Open" : "Later";
            toggle.tooltip = folded ? "Work on it now" : "Fold it away and do it later; it waits here";
        }

        /// <summary>If a card was asked for, scroll the window to it (call after the puzzles are built).</summary>
        public static void ShowFocused(VisualElement puzzles)
        {
            if (focus == null || puzzles == null)
                return;
            VisualElement frame = puzzles.Q(focus);
            if (frame == null)
                return;
            focus = null;
            ScrollView scroll = frame.GetFirstAncestorOfType<ScrollView>();
            frame.schedule.Execute(() => scroll?.ScrollTo(frame)).StartingIn(50);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// A why? chain (UI information design, stage 5): a warning's headline, then two to four steps joined by arrows, ending in one action button. The
    /// steps are built from the simulation's structured reasons (a reason enum and its numbers), never from parsing text. A chain with no steps is hidden.
    /// </summary>
    public sealed class WhyChain : VisualElement
    {
        readonly Label headline = new Label(), steps = new Label();
        readonly Button action = new Button();
        Action onAction;

        public WhyChain()
        {
            style.marginTop = 4; style.marginBottom = 4;
            style.paddingLeft = 8; style.paddingTop = 4; style.paddingBottom = 4;
            style.borderLeftWidth = 3; style.borderLeftColor = UiPalette.Warning;
            style.backgroundColor = new Color(UiPalette.Warning.r, UiPalette.Warning.g, UiPalette.Warning.b, 0.08f);
            headline.AddToClassList(UiType.Secondary);
            headline.style.unityFontStyleAndWeight = FontStyle.Bold;
            headline.style.color = UiPalette.Warning;
            steps.AddToClassList(UiType.Secondary);
            steps.style.whiteSpace = WhiteSpace.Normal;
            action.AddToClassList("small-button");
            action.AddToClassList(UiType.Secondary);
            action.style.alignSelf = Align.FlexStart;
            action.style.marginLeft = 10;
            action.clicked += () => onAction?.Invoke();
            // the action sits on the header line, so it is always in view when the chain is
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
            header.Add(headline); header.Add(action);
            Add(header); Add(steps);
            style.display = DisplayStyle.None;
        }

        /// <summary>The text shown, for tests: headline, then the steps, then the action.</summary>
        public string Shown => headline.text + " | " + steps.text + " | " + (action.style.display == DisplayStyle.Flex ? action.text : "");

        public void Set(string headlineText, IList<string> chain, string actionText = null, Action act = null)
        {
            bool any = chain != null && chain.Count > 0;
            style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            if (!any) return;
            headline.text = "⚠ " + headlineText;
            steps.text = "→ " + string.Join("\n→ ", chain);
            onAction = act;
            bool button = !string.IsNullOrEmpty(actionText) && act != null;
            action.text = button ? "[" + actionText + "]" : "";
            action.style.display = button ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---- the reasons we have, as chains ----

        /// <summary>The steps behind a haul's structured reason (0 ok, 1 no route, 2 no start stop, 3 no end stop, 4 leg too long, 5 stop lost).</summary>
        public static List<string> HaulSteps(int kind, double needWh, double budgetWh, int stopTile)
        {
            switch (kind)
            {
                case 1: return new List<string> { "there is no way over land between the two tiles" };
                case 2: return new List<string> { "no powered charger or shelter within one charge of the pick-up (" + $"one charge gives {budgetWh:0} Wh)" };
                case 3: return new List<string> { "no powered charger or shelter within one charge of the drop (" + $"one charge gives {budgetWh:0} Wh)" };
                case 4: return new List<string> { $"the haul needs {needWh:0} Wh in one leg, one charge gives {budgetWh:0} Wh", "no powered charger within one charge of the route splits it" };
                case 5: return new List<string> { $"the charger at tile {stopTile} lost power or was removed", "the staged route no longer holds" };
                case 6: return new List<string> { $"◐ a storm is forecast in {stopTile} day(s) on the route: planning wind {needWh:0} m/s against a storm threshold of {budgetWh:0} m/s (one sigma pessimistic)", "the haul waits until the forecast clears; the energy check is separate and stays" };
                default: return new List<string>();
            }
        }

        /// <summary>The steps behind a plant's blocker (the simulation's <c>IndustrialBlocker</c>), with the window that fixes it.</summary>
        public static (List<string> Steps, string Window, string Action) PlantSteps(string blocker)
        {
            switch (blocker)
            {
                case "MissingInput": return (new List<string> { "its input buffer holds less than a batch needs", "hauls fill it from a depot" }, "operations", "Open Operations (I)");
                case "InsufficientElectricity": return (new List<string> { "the grid cannot serve its power now" }, "power", "Open Power (O)");
                case "InsufficientProcessHeat": return (new List<string> { "the process heat is short" }, "flowsheet", "Open Flowsheet (Z)");
                case "InsufficientCooling": return (new List<string> { "the cooling is short" }, "flowsheet", "Open Flowsheet (Z)");
                case "EnvironmentUnavailable": return (new List<string> { "this planet's environment does not allow the process" }, "flowsheet", "Open Flowsheet (Z)");
                case "OutputStorageUnavailable": return (new List<string> { "its output buffer is full", "a store or tank has no room for the product" }, "operations", "Open Operations (I)");
                default: return (new List<string>(), null, null);
            }
        }
    }
}

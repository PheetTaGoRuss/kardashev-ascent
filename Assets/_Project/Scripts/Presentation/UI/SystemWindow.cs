using System;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// A system window built in code (power, manufacturing, research, network...): a title row with a close button and
    /// a scrolling body, styled like the robots window. <see cref="SystemsUI"/> hosts them and the tab bar.
    /// </summary>
    public abstract class SystemWindow
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract KeyCode Key { get; }
        public abstract int Order { get; }

        /// <summary>Null when the system is available; otherwise why it is locked.</summary>
        public virtual string LockedReason(IPlanetSimulation sim) => null;

        protected VisualElement Body { get; private set; }
        protected Label Subtitle { get; private set; }
        VisualElement panel;
        Label locked;

        public bool Visible { get; private set; }

        public VisualElement Build()
        {
            panel = new VisualElement();
            panel.AddToClassList("workforce");
            panel.AddToClassList("panel");
            panel.AddToClassList("hidden");

            var titleRow = new VisualElement();
            titleRow.AddToClassList("title-row");
            var titleText = new VisualElement();
            titleText.AddToClassList("title-text");
            titleText.Add(Label(Title, "value"));
            Subtitle = Label($"{Key}: close", "caption");
            titleText.Add(Subtitle);
            titleRow.Add(titleText);
            var close = new Button(() => SystemTabs.Toggle(Id)) { text = "×" };
            close.AddToClassList("close-button");
            titleRow.Add(close);
            panel.Add(titleRow);

            locked = Label("", "body");
            locked.AddToClassList("wrap");
            panel.Add(locked);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("workforce-scroll");
            Body = scroll.contentContainer;
            panel.Add(scroll);
            BuildBody();
            return panel;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            panel?.EnableInClassList("hidden", !visible);
        }

        public void Refresh(IPlanetSimulation sim)
        {
            string why = LockedReason(sim);
            locked.text = why != null ? "Locked: " + why : "";
            locked.EnableInClassList("hidden", why == null);
            if (sim != null)
                RefreshBody(sim);
        }

        protected abstract void BuildBody();
        protected abstract void RefreshBody(IPlanetSimulation sim);

        // ---- helpers ----

        protected static Label Label(string text, string cls)
        {
            var l = new Label(text);
            foreach (string c in cls.Split(' '))
                if (c.Length > 0) l.AddToClassList(c);
            return l;
        }

        protected Label Heading(string text)
        {
            Label l = Label(text, "heading section-head");
            Body.Add(l);
            return l;
        }

        protected Label Text(string cls = "body")
        {
            Label l = Label("", cls);
            l.AddToClassList("wrap");
            Body.Add(l);
            return l;
        }

        protected static Button MakeButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("small-button");
            return b;
        }
    }
}

using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The system tab bar (top centre, under the HUD) and the windows built in code (power, manufacturing, research,
    /// network). The overview (C) and robots (G) windows register their own tabs. Sets itself up after the scene
    /// loads: copies the panel settings and style sheets of the robots window, so no scene changes are needed.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SystemsUI : MonoBehaviour
    {
        const float RefreshInterval = 0.5f;

        readonly List<SystemWindow> windows = new List<SystemWindow>
            { new PowerWindow(), new ManufacturingWindow(), new ResearchWindow(), new NetworkWindow() };

        SimulationRunner runner;
        VisualElement bar;
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        float nextRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            WorkforceView robots = FindFirstObjectByType<WorkforceView>();
            if (robots == null || FindFirstObjectByType<SystemsUI>() != null)
                return;
            UIDocument source = robots.GetComponent<UIDocument>();
            var go = new GameObject("Systems UI");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = source.panelSettings;
            doc.sortingOrder = source.sortingOrder;
            go.AddComponent<SystemsUI>();
            go.SetActive(true);
        }

        void OnEnable()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            CopyStyles(root);
            ScreenInput.Register(root.panel);

            bar = new VisualElement();
            bar.AddToClassList("panel");
            bar.style.position = Position.Absolute;
            bar.style.top = 78;
            bar.style.left = new Length(50, LengthUnit.Percent);
            bar.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.paddingLeft = bar.style.paddingRight = 4;
            bar.style.paddingTop = bar.style.paddingBottom = 3;
            root.Add(bar);

            foreach (SystemWindow w in windows)
            {
                root.Add(w.Build());
                SystemWindow window = w;
                SystemTabs.Register(new SystemTabs.Tab
                {
                    Id = w.Id, Title = w.Title, Key = w.Key, Order = w.Order,
                    IsOpen = () => window.Visible,
                    SetOpen = open => { window.SetVisible(open); nextRefresh = 0f; },
                    LockedReason = () => window.LockedReason(runner != null ? runner.Simulation : null),
                });
            }
            SystemTabs.Changed += RebuildBar;
            RebuildBar();
        }

        void OnDisable()
        {
            SystemTabs.Changed -= RebuildBar;
            foreach (SystemWindow w in windows)
                SystemTabs.Unregister(w.Id);
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        /// <summary>The robots and overview windows' style sheets, so code-built windows look the same.</summary>
        static void CopyStyles(VisualElement root)
        {
            foreach (MonoBehaviour view in new MonoBehaviour[] { FindFirstObjectByType<WorkforceView>(), FindFirstObjectByType<ColonyView>() })
            {
                UIDocument d = view != null ? view.GetComponent<UIDocument>() : null;
                if (d == null || d.rootVisualElement == null)
                    continue;
                foreach (VisualElement e in new[] { d.rootVisualElement }.Concat(d.rootVisualElement.Children()))
                    for (int i = 0; i < e.styleSheets.count; i++)
                        if (!root.styleSheets.Contains(e.styleSheets[i]))
                            root.styleSheets.Add(e.styleSheets[i]);
            }
        }

        void RebuildBar()
        {
            if (bar == null)
                return;
            bar.Clear();
            buttons.Clear();
            foreach (SystemTabs.Tab tab in SystemTabs.All)
            {
                string id = tab.Id;
                var b = new Button(() => SystemTabs.Toggle(id)) { text = $"{tab.Title} ({tab.Key})" };
                b.AddToClassList("tab");
                buttons[id] = b;
                bar.Add(b);
            }
        }

        void Update()
        {
            foreach (SystemWindow w in windows)
                if (ScreenInput.KeyDown(w.Key))
                    SystemTabs.Toggle(w.Id);

            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            foreach (SystemTabs.Tab tab in SystemTabs.All)
                if (buttons.TryGetValue(tab.Id, out Button b))
                {
                    b.EnableInClassList("selected", tab.IsOpen());
                    b.EnableInClassList("locked", tab.LockedReason != null && tab.LockedReason() != null);
                }
            foreach (SystemWindow w in windows)
                if (w.Visible)
                    w.Refresh(sim);
        }
    }

    static class EnumerableExtensions
    {
        public static IEnumerable<T> Concat<T>(this IEnumerable<T> a, IEnumerable<T> b)
        {
            foreach (T x in a) yield return x;
            foreach (T x in b) yield return x;
        }
    }
}

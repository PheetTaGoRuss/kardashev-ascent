using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// A description card under the mouse (director 2026-11-15, after Oxygen Not Included): hover any button, chip or
    /// line that has a description (its <c>tooltip</c>, which runtime UI Toolkit never shows by itself) and a card with
    /// it appears beside the pointer; hover a building on a site and the card says what it is and how it is doing.
    /// Made at start-up on its own UI document drawn above every other panel.
    /// </summary>
    public sealed class HoverCard : MonoBehaviour
    {
        const float Delay = 0.25f;   // seconds before a card shows (no flicker while the mouse crosses things)
        const float MaxWidth = 360f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<HoverCard>() != null) return;
            new GameObject("Hover Card").AddComponent<HoverCard>();
        }

        UIDocument document;
        VisualElement card;
        Label title, body;
        readonly HashSet<VisualElement> hooked = new HashSet<VisualElement>();
        VisualElement hoveredUi;
        string shownText;
        float since;
        Vector2 pointer;

        void Start() => TryBuild();

        bool TryBuild()
        {
            if (card != null) return true;
            PanelSettings settings = null;
            int top = 0;
            foreach (UIDocument d in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                if (d == document || d.panelSettings == null) continue;
                settings ??= d.panelSettings;
                top = Mathf.Max(top, (int)d.sortingOrder);
            }
            if (settings == null) return false;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.sortingOrder = top + 50;
            VisualElement root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;

            card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.position = Position.Absolute;
            card.style.maxWidth = MaxWidth;
            card.style.paddingLeft = card.style.paddingRight = 10;
            card.style.paddingTop = card.style.paddingBottom = 7;
            card.style.backgroundColor = new Color(0.09f, 0.09f, 0.11f, 0.97f);
            card.style.borderTopWidth = 3;
            card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopColor = UiPalette.Accent;
            card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = UiPalette.Border;
            title = new Label { pickingMode = PickingMode.Ignore };
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = UiPalette.Accent;
            title.style.whiteSpace = WhiteSpace.Normal;
            body = new Label { pickingMode = PickingMode.Ignore };
            body.style.fontSize = 14;
            body.style.color = new Color(0.92f, 0.92f, 0.93f);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginTop = 2;
            card.Add(title);
            card.Add(body);
            card.style.display = DisplayStyle.None;
            root.Add(card);
            return true;
        }

        /// <summary>Listen to every other document's pointer movement (new documents are picked up as they appear).</summary>
        void Hook()
        {
            foreach (UIDocument d in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                if (d == document) continue;
                VisualElement root = d.rootVisualElement;
                if (root == null || hooked.Contains(root)) continue;
                hooked.Add(root);
                root.RegisterCallback<PointerOverEvent>(e => hoveredUi = e.target as VisualElement, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerOutEvent>(e => { if (e.target == hoveredUi) hoveredUi = null; }, TrickleDown.TrickleDown);
            }
        }

        float nextHook;

        void Update()
        {
            if (!TryBuild()) return;
            if (Time.unscaledTime >= nextHook)
            {
                Hook();
                nextHook = Time.unscaledTime + 1f;
            }
            pointer = Input.mousePosition;

            string head = null, text = null;
            anchor = null;
            // UI first: the nearest element (or its parent) with a description.
            if (hoveredUi != null && !UnderPointer(hoveredUi))
                hoveredUi = null;   // it closed or moved away without a pointer-out (a window shut under the mouse)
            for (VisualElement e = hoveredUi; e != null; e = e.parent)
            {
                if (e.panel == null) break;
                if (!string.IsNullOrEmpty(e.tooltip))
                {
                    text = e.tooltip;
                    anchor = e;
                    head = e is Button b && !string.IsNullOrEmpty(b.text) && b.text.Length < 40 ? b.text : null;
                    break;
                }
            }
            if (text == null && hoveredUi == null && !ScreenInput.PointerOverUi)
                WorldText(out head, out text);

            string key = head + "|" + text;
            if (text == null)
            {
                card.style.display = DisplayStyle.None;
                shownText = null;
                return;
            }
            if (key != shownText)
            {
                shownText = key;
                since = Time.unscaledTime;
                title.text = head ?? "";
                title.style.display = string.IsNullOrEmpty(head) ? DisplayStyle.None : DisplayStyle.Flex;
                body.text = text;
            }
            if (Time.unscaledTime - since < Delay)
            {
                card.style.display = DisplayStyle.None;
                return;
            }
            card.style.display = DisplayStyle.Flex;
            Place();
        }

        VisualElement anchor;

        /// <summary>True while the element is shown and the pointer is over it.</summary>
        bool UnderPointer(VisualElement e)
        {
            if (e.panel == null) return false;
            for (VisualElement p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.ClassListContains("hidden")) return false;
            Vector2 at = RuntimePanelUtils.ScreenToPanel(e.panel, new Vector2(pointer.x, Screen.height - pointer.y));
            return e.worldBound.Contains(at);
        }

        /// <summary>Under the hovered element (or beside the pointer over the world), kept on screen.</summary>
        void Place()
        {
            IPanel panel = document.rootVisualElement.panel;
            if (panel == null) return;
            Rect root = document.rootVisualElement.layout;
            float w = card.layout.width > 1f ? card.layout.width : MaxWidth, h = card.layout.height > 1f ? card.layout.height : 60f;
            float x, y;
            if (anchor != null && anchor.panel != null)
            {
                // The other documents use the same panel settings, so their coordinates line up with this one's.
                Rect b = anchor.worldBound;
                x = b.xMin;
                y = b.yMax + 6f;
                if (y + h > root.height - 4f) y = b.yMin - h - 6f;
            }
            else
            {
                Vector2 at = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(pointer.x, Screen.height - pointer.y));
                x = at.x + 18f;
                y = at.y + 18f;
                if (y + h > root.height - 4f) y = at.y - h - 12f;
            }
            card.style.left = Mathf.Clamp(x, 4f, Mathf.Max(4f, root.width - w - 4f));
            card.style.top = Mathf.Clamp(y, 4f, Mathf.Max(4f, root.height - h - 4f));
        }

        SimulationRunner runner;
        SiteView site;

        /// <summary>A building under the pointer on the open site: what it is and how it is doing.</summary>
        void WorldText(out string head, out string text)
        {
            head = text = null;
            runner ??= FindAnyObjectByType<SimulationRunner>();
            site ??= FindAnyObjectByType<SiteView>();
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim == null || site == null || site.Site == null || site.HoveredSlot < 0) return;
            int tile = site.Site.Tile, slot = site.HoveredSlot;
            BuildingType type = sim.GetBuilding(tile, slot);
            if (type == BuildingType.None) return;
            head = Capital(BuildingCatalog.NameOf(type));
            text = BuildInfo.Short(type);
            if (sim.IsUnderConstruction(tile, slot))
            {
                var (done, total) = sim.ConstructionWork(tile, slot);
                text += $"\nBuilding {sim.ConstructionProgress(tile, slot) * 100.0:0} % · {done:0}/{total:0} robot hours" +
                        (sim.IsBeingBuilt(tile, slot) ? "" : " · waiting for a robot or materials");
            }
            else if (sim.IsSalvageOrdered(tile, slot))
                text += "\nTo be taken down.";
            if (type == BuildingType.AluminaPlant || type == BuildingType.Electrolyser)
                foreach (RefineryLine l in sim.RefineryLines())
                    if (l.Tile == tile && l.Slot == sim.AnchorOf(tile, slot))
                        text += l.Process.HasValue
                            ? $"\n{KardashevAscent.Simulation.Resources.Refining.Of(l.Process.Value).Name}: {l.Progress * 100.0:0} %, {l.HoursLeft / 24.0:0.#} days left"
                            : l.Built ? "\nIdle: queue a batch in Manufacturing (U)." : "";
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

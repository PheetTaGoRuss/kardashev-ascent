using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// One place decides where the permanent interface sits (director 2026-11-20: "top bar sometimes complicates with the
    /// Overview bar, the hint bar with the building bar; permanent interface should never overlap other UI unless it is
    /// draggable"). It measures the real size of the top bar and the build bar every few frames and places everything else
    /// around them: the system tabs under the top bar, the system windows under the tabs, the hints and the building info
    /// above the build bar, and keeps tall windows from running into the bottom row. Sets itself up after the scene loads.
    /// </summary>
    public sealed class UiLayout : MonoBehaviour
    {
        /// <summary>Panel units from the top edge to just under the top bar.</summary>
        public static float Top { get; private set; } = 82f;

        /// <summary>Panel units from the top edge to just under the system tabs (where a window may start).</summary>
        public static float WindowTop { get; private set; } = 118f;

        /// <summary>Panel units from the bottom edge to the top of the build bar (or a margin when it is hidden).</summary>
        public static float Bottom { get; private set; } = 12f;

        /// <summary>Panel units from the bottom edge to the top of the hints (the lowest line anything free-floating may use).</summary>
        public static float ClearBottom { get; private set; } = 12f;

        /// <summary>Screen pixels per panel unit.</summary>
        public static float PixelsPerUnit { get; private set; } = 1f;

        /// <summary>Screen y (pixels from the top) of the top of the build bar and hints, for IMGUI notes that must stay clear of them.</summary>
        public static float ClearBottomY => Screen.height - ClearBottom * PixelsPerUnit;

        /// <summary>Screen y (pixels from the top) just under the top bar.</summary>
        public static float TopY => Top * PixelsPerUnit;

        float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<UiLayout>() == null)
                new GameObject("UI Layout").AddComponent<UiLayout>();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + 0.1f;
            VisualElement top = null, tabs = null, build = null;
            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);
            float panelHeight = 0f, panelWidth = 0f;
            foreach (UIDocument d in documents)
            {
                VisualElement root = d.rootVisualElement;
                if (root == null) continue;
                top ??= root.Q("top-bar");
                tabs ??= root.Q("system-tabs");
                build ??= root.Q("build-bar");
                if (root.worldBound.height > panelHeight) { panelHeight = root.worldBound.height; panelWidth = root.worldBound.width; }
            }
            if (panelHeight <= 0f)
                return;
            PixelsPerUnit = panelWidth > 0f ? Screen.width / panelWidth : 1f;

            // the top bar sets the line everything else hangs from
            if (top != null && Shown(top) && top.worldBound.yMax > 1f)
                Top = Mathf.Max(70f, top.worldBound.yMax + 4f);
            WindowTop = Top;
            if (tabs != null)
            {
                tabs.style.top = Top;
                if (Shown(tabs) && tabs.worldBound.yMax > Top)
                    WindowTop = tabs.worldBound.yMax + 6f;
                else if (Shown(tabs))
                    WindowTop = Top + 38f;
            }

            // the build bar sets the line the hints and the info card sit on
            Bottom = 12f;
            if (build != null && Shown(build) && build.worldBound.height > 1f)
                Bottom = Mathf.Max(12f, panelHeight - build.worldBound.yMin + 8f);
            float hintsHeight = 0f;
            foreach (UIDocument d in documents)
            {
                VisualElement root = d.rootVisualElement;
                if (root == null) continue;
                VisualElement hints = root.Q("hints");
                if (hints != null)
                {
                    // At a large UI size the hints bar would sit over the foot of a floating window: hide it while a window is open.
                    hints.style.display = UiScale.Value > 1.3f && SystemTabs.AnyOpen ? DisplayStyle.None : StyleKeyword.Null;
                    hints.style.bottom = Bottom;
                    if (Shown(hints)) hintsHeight = Mathf.Max(hintsHeight, hints.worldBound.height + 8f);
                }
                VisualElement info = root.Q("build-info");
                if (info != null)
                    info.style.bottom = Bottom;
                VisualElement left = root.Q("left");
                if (left != null)
                    left.style.top = WindowTop - 2f;   // under the tabs: a wider tab strip (bigger type, larger UI size) would otherwise cover the panel title
            }
            ClearBottom = Bottom + hintsHeight;

            // windows start under the tabs and end above the bottom row
            float maxHeight = Mathf.Max(160f, panelHeight - WindowTop - ClearBottom - 6f);
            foreach (UIDocument d in documents)
            {
                VisualElement root = d.rootVisualElement;
                if (root == null) continue;
                // Floating windows sit between the two side panels (the building info on the left, the tile panel on the right) and are narrowed to fit,
                // so at a large UI size they do not cover them. A window the player has dragged stays where it was put (UI information design, finish).
                // the side panels live in other documents than the windows; all share one panel space
                float lo = 12f, hi = root.worldBound.width - 12f;
                foreach (UIDocument other in documents)
                {
                    VisualElement otherRoot = other.rootVisualElement;
                    if (otherRoot == null) continue;
                    otherRoot.Query(className: "tile-info").ForEach(r => { if (Shown(r) && r.worldBound.width > 1f) hi = Mathf.Min(hi, r.worldBound.xMin - 8f); });
                    VisualElement l = otherRoot.Q("left");
                    if (l != null && Shown(l) && l.worldBound.width > 1f) lo = Mathf.Max(lo, l.worldBound.xMax + 8f);
                }
                float room = hi - lo;
                void Fit(VisualElement w)
                {
                    if (w.ClassListContains("drag-pinned") || room < 320f) return;
                    w.style.maxWidth = room;
                    w.style.left = lo + room / 2f - root.worldBound.x;   // the stylesheet's centring translate puts the window's middle here
                }
                root.Query(className: "workforce").ForEach(Fit);
                root.Query(className: "workforce").ForEach(Place);
                VisualElement colony = root.Q("colony");
                if (colony != null) Place(colony);
                // the tile panel hangs from the top bar on the right and ends above the build bar
                VisualElement tileInfo = root.Q(className: "tile-info");
                if (tileInfo != null)
                {
                    tileInfo.style.top = WindowTop - 2f;
                    tileInfo.style.maxHeight = Mathf.Max(160f, panelHeight - Top - Bottom - 12f);
                }
            }

            void Place(VisualElement window)
            {
                window.style.top = WindowTop;
                window.style.maxHeight = maxHeight;
            }
        }

        static bool Shown(VisualElement e) =>
            e.resolvedStyle.display != DisplayStyle.None && !e.ClassListContains("hidden") && e.resolvedStyle.opacity > 0.01f;
    }
}

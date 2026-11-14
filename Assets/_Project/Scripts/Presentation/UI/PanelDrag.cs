using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Lets the player move a floating panel (director 2026-11-14: inspect and info panels drag like the calculator).
    /// A panel gets a grip strip along its top; dragging the grip moves the panel, kept on screen. The panel stays
    /// where it was left while it is hidden and shown again; a double-click on the grip puts it back.
    /// </summary>
    public static class PanelDrag
    {
        public const string GripClass = "drag-grip";

        /// <summary>Put a grip at the top of <paramref name="panel"/> that drags <paramref name="target"/> (the panel itself by default).</summary>
        public static VisualElement AddGrip(VisualElement panel, VisualElement target = null)
        {
            if (panel == null) return null;
            VisualElement existing = panel.Q(className: GripClass);
            if (existing != null && existing.parent == panel) return existing;
            var grip = new VisualElement();
            grip.AddToClassList(GripClass);
            grip.tooltip = "Drag to move · double-click: put back";
            grip.style.height = 10;
            grip.style.marginTop = -4;
            grip.style.marginBottom = 4;
            grip.style.flexDirection = FlexDirection.Row;
            grip.style.justifyContent = Justify.Center;
            grip.style.alignItems = Align.Center;
            for (int i = 0; i < 3; i++)
            {
                var bar = new VisualElement();
                bar.pickingMode = PickingMode.Ignore;
                bar.style.width = 18;
                bar.style.height = 3;
                bar.style.marginLeft = bar.style.marginRight = 2;
                bar.style.backgroundColor = UiPalette.Border;
                grip.Add(bar);
            }
            grip.RegisterCallback<PointerEnterEvent>(_ => SetGripColour(grip, UiPalette.Accent));
            grip.RegisterCallback<PointerLeaveEvent>(_ => SetGripColour(grip, UiPalette.Border));
            panel.Insert(0, grip);
            Attach(grip, target ?? panel);
            return grip;
        }

        static void SetGripColour(VisualElement grip, Color c)
        {
            foreach (VisualElement bar in grip.Children())
                bar.style.backgroundColor = c;
        }

        /// <summary>Dragging <paramref name="handle"/> moves <paramref name="target"/>.</summary>
        public static void Attach(VisualElement handle, VisualElement target)
        {
            bool dragging = false;
            Vector2 start = default, origin = default;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.target is Button) return;
                if (e.clickCount == 2) { Reset(target); return; }
                Pin(target);
                dragging = true;
                start = e.position;
                origin = new Vector2(target.resolvedStyle.left, target.resolvedStyle.top);
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                Vector2 d = (Vector2)e.position - start;
                Vector2 p = Clamp(target, origin + d);
                target.style.left = p.x;
                target.style.top = p.y;
                // A tall panel moved down gets shorter (and scrolls) rather than running off the bottom.
                if (target.parent != null)
                    target.style.maxHeight = Mathf.Max(120f, target.parent.contentRect.height - p.y - 8f);
                e.StopPropagation();
            });
            void End(int pointerId)
            {
                dragging = false;
                if (handle.HasPointerCapture(pointerId)) handle.ReleasePointer(pointerId);
            }
            handle.RegisterCallback<PointerUpEvent>(e => End(e.pointerId));
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }

        // From stylesheet placement (right/bottom anchors, centring translate) to plain left/top, keeping it in place.
        static void Pin(VisualElement target)
        {
            if (target.ClassListContains("drag-pinned") || target.parent == null) return;
            Rect r = target.worldBound, parent = target.parent.worldBound;
            target.AddToClassList("drag-pinned");
            target.style.translate = new Translate(0, 0);
            target.style.right = StyleKeyword.Auto;
            target.style.bottom = StyleKeyword.Auto;
            target.style.left = r.x - parent.x;
            target.style.top = r.y - parent.y;
        }

        static void Reset(VisualElement target)
        {
            target.RemoveFromClassList("drag-pinned");
            target.style.translate = StyleKeyword.Null;
            target.style.left = StyleKeyword.Null;
            target.style.top = StyleKeyword.Null;
            target.style.right = StyleKeyword.Null;
            target.style.bottom = StyleKeyword.Null;
            target.style.maxHeight = StyleKeyword.Null;
        }

        // Keep at least a strip of the panel (and its grip) on screen.
        static Vector2 Clamp(VisualElement target, Vector2 p)
        {
            if (target.parent == null) return p;
            Rect area = target.parent.contentRect;
            float w = target.resolvedStyle.width, keep = 60f;
            p.x = Mathf.Clamp(p.x, keep - w, Mathf.Max(0f, area.width - keep));
            p.y = Mathf.Clamp(p.y, 0f, Mathf.Max(0f, area.height - 30f));
            return p;
        }
    }
}

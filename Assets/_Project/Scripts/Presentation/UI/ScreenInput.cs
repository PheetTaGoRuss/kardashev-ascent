using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Lets on-screen panels claim the mouse, so a click or drag on them does not also pick a tile or turn the
    /// camera. UI Toolkit panels <see cref="Register"/> once and are hit-tested with their own picking;
    /// IMGUI windows call <see cref="Block"/> from OnGUI each frame they are shown.
    /// World input asks <see cref="PointerOverUi"/> before acting.
    /// </summary>
    public static class ScreenInput
    {
        /// <summary>Height reserved at the top of the screen for the HUD bar; IMGUI panels start below it.</summary>
        public const float TopInset = 72f;

        // Rects arrive in OnGUI (after Update), so last frame's rects are still in force this frame.
        static readonly List<(Rect Rect, int Frame)> rects = new List<(Rect, int)>();
        static readonly List<IPanel> panels = new List<IPanel>();

        /// <summary>Claim a screen area this frame, in GUI coordinates (origin top-left).</summary>
        public static void Block(Rect guiRect) => rects.Add((guiRect, Time.frameCount));

        /// <summary>Hit-test a UI Toolkit panel for the mouse from now on.</summary>
        public static void Register(IPanel panel)
        {
            if (panel != null && !panels.Contains(panel))
                panels.Add(panel);
        }

        public static void Unregister(IPanel panel) => panels.Remove(panel);

        /// <summary>True while a text box on screen has the keyboard (typing a name must not trigger hotkeys).</summary>
        public static bool TypingInTextField
        {
            get
            {
                foreach (IPanel panel in panels)
                {
                    Focusable focused = panel.focusController?.focusedElement;
                    if (focused is TextField || (focused is VisualElement element && element.GetFirstAncestorOfType<TextField>() != null))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Like <c>Input.GetKeyDown</c>, but false while the player is typing in a text box.</summary>
        public static bool KeyDown(KeyCode key) => !TypingInTextField && Input.GetKeyDown(key);

        /// <summary>Like <c>Input.GetKey</c>, but false while the player is typing in a text box.</summary>
        public static bool Key(KeyCode key) => !TypingInTextField && Input.GetKey(key);

        /// <summary>True while the mouse is over a UI Toolkit element or an IMGUI area claimed this frame or last.</summary>
        public static bool PointerOverUi
        {
            get
            {
                Vector3 mouse = Input.mousePosition;
                var guiPoint = new Vector2(mouse.x, Screen.height - mouse.y);

                foreach (IPanel panel in panels)
                {
                    Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, guiPoint);
                    VisualElement picked = panel.Pick(panelPoint);
                    if (picked != null && picked != panel.visualTree && !(picked is TemplateContainer))
                        return true;
                }

                int frame = Time.frameCount;
                rects.RemoveAll(r => r.Frame < frame - 1);
                foreach (var r in rects)
                    if (r.Rect.Contains(guiPoint))
                        return true;
                return false;
            }
        }
    }
}

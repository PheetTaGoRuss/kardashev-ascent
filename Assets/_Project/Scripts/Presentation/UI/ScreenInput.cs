using System.Collections.Generic;
using UnityEngine;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Lets on-screen panels (IMGUI windows) claim the mouse, so a click or drag on them does not also
    /// pick a tile or turn the camera. A panel calls <see cref="Block"/> from OnGUI each frame it is shown;
    /// world input asks <see cref="PointerOverUi"/> before acting.
    /// </summary>
    public static class ScreenInput
    {
        // Rects arrive in OnGUI (after Update), so last frame's rects are still in force this frame.
        static readonly List<(Rect Rect, int Frame)> rects = new List<(Rect, int)>();

        /// <summary>Claim a screen area this frame, in GUI coordinates (origin top-left).</summary>
        public static void Block(Rect guiRect) => rects.Add((guiRect, Time.frameCount));

        /// <summary>True while the mouse is over an area claimed this frame or last frame.</summary>
        public static bool PointerOverUi
        {
            get
            {
                int frame = Time.frameCount;
                rects.RemoveAll(r => r.Frame < frame - 1);
                Vector3 mouse = Input.mousePosition;
                var guiPoint = new Vector2(mouse.x, Screen.height - mouse.y);
                foreach (var r in rects)
                    if (r.Rect.Contains(guiPoint))
                        return true;
                return false;
            }
        }
    }
}

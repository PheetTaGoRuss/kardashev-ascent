using UnityEngine;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The UI colours for elements built in code, matching the variables in <c>UI/Common.uss</c>. The look
    /// (director 2026-11-15: epic and its own): a sci-fi mission-control look, deep navy glass panels with a thin cool-blue
    /// frame and an amber command strip, light text.
    /// </summary>
    public static class UiPalette
    {
        /// <summary>Amber: titles, selected things, clickable numbers.</summary>
        public static readonly Color Accent = new Color(1f, 0.78f, 0.36f);
        public static readonly Color AccentDark = new Color(0.47f, 0.36f, 0.16f);
        public static readonly Color PanelSolid = new Color(0.035f, 0.051f, 0.094f, 0.95f);
        public static readonly Color Border = new Color(0.36f, 0.59f, 0.86f, 0.45f);
        /// <summary>Cool blue: secondary highlights, the frames of panels.</summary>
        public static readonly Color Cool = new Color(0.47f, 0.75f, 1f);
        public static readonly Color Text = new Color(0.91f, 0.93f, 0.97f);
        public static readonly Color Muted = new Color(0.59f, 0.64f, 0.71f);
        public static readonly Color Good = new Color(0.43f, 0.86f, 0.51f);
        public static readonly Color Warning = new Color(0.95f, 0.75f, 0.31f);
        public static readonly Color Critical = new Color(1f, 0.47f, 0.41f);
        /// <summary>Dark text on an amber button.</summary>
        public static readonly Color OnAccent = new Color(0.10f, 0.08f, 0.04f);

        public static Color AccentA(float alpha) => new Color(Accent.r, Accent.g, Accent.b, alpha);
    }
}

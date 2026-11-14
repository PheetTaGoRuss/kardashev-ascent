using UnityEngine;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The UI colours for elements built in code, matching the variables in <c>UI/Common.uss</c>. The look
    /// (director 2026-11-14, inspired by factory games such as Mindustry, not copied): flat, square, nearly opaque
    /// dark-grey panels with a warm amber accent and plain light text.
    /// </summary>
    public static class UiPalette
    {
        /// <summary>Amber: titles, selected things, clickable numbers.</summary>
        public static readonly Color Accent = new Color(1f, 0.78f, 0.36f);
        public static readonly Color AccentDark = new Color(0.47f, 0.36f, 0.16f);
        public static readonly Color PanelSolid = new Color(0.086f, 0.086f, 0.10f, 0.97f);
        public static readonly Color Border = new Color(0.27f, 0.27f, 0.30f);
        public static readonly Color Text = new Color(0.95f, 0.95f, 0.95f);
        public static readonly Color Muted = new Color(0.65f, 0.65f, 0.67f);
        public static readonly Color Good = new Color(0.43f, 0.86f, 0.51f);
        public static readonly Color Warning = new Color(0.95f, 0.75f, 0.31f);
        public static readonly Color Critical = new Color(1f, 0.47f, 0.41f);
        /// <summary>Dark text on an amber button.</summary>
        public static readonly Color OnAccent = new Color(0.10f, 0.08f, 0.04f);

        public static Color AccentA(float alpha) => new Color(Accent.r, Accent.g, Accent.b, alpha);
    }
}

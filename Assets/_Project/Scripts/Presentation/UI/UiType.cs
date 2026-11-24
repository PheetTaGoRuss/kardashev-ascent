using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The type scale (UI information design, stage 1, decided 2026-11-24): six sizes, all in USS classes (<c>Common.uss</c>), nothing below
    /// 14 px. Code says which size a piece of text is (<see cref="ClassFor"/>), never a pixel number; <see cref="Px"/> is for the few sizes
    /// that are computed (icons, board labels) and keeps them at 14 or more.
    /// </summary>
    public static class UiType
    {
        public const float Minimum = 14f;
        public const string Hero = "t-hero", Title = "t-title", Section = "t-section", Value = "t-value", Body = "t-body", Secondary = "t-secondary";

        /// <summary>The scale class nearest an old pixel size: 14 and below secondary, 16 body, 18 section, 20 value, 24 title, 32 hero.</summary>
        public static string ClassFor(float px) =>
            px <= 14.5f ? Secondary : px <= 16.5f ? Body : px <= 19f ? Section : px <= 22.5f ? Value : px <= 27f ? Title : Hero;

        /// <summary>Set the size of a text element from the scale.</summary>
        public static T Size<T>(this T element, string scaleClass) where T : VisualElement
        {
            element.AddToClassList(scaleClass);
            return element;
        }

        /// <summary>A computed size, never below the minimum.</summary>
        public static float Px(float px) => Mathf.Max(Minimum, px);
    }
}

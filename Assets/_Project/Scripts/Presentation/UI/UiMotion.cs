using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Smooth interface motion. Every element with the "panel" class (windows, cards, hint boxes) fades and slides in
    /// when it appears, and the screens that go through <see cref="Show"/> fade out as well. It watches the UI documents
    /// itself, so panels need no code of their own. Sets itself up after the scene loads.
    /// </summary>
    public sealed class UiMotion : MonoBehaviour
    {
        const float InSeconds = 0.22f;
        const float OutSeconds = 0.18f;
        const float SlidePx = 10f;
        const float ScanEvery = 0f;   // every frame: a panel that just closed is shown again for its fade-out within one frame

        readonly Dictionary<VisualElement, bool> seen = new Dictionary<VisualElement, bool>();
        readonly List<VisualElement> found = new List<VisualElement>();
        readonly HashSet<VisualElement> fading = new HashSet<VisualElement>();
        static readonly HashSet<VisualElement> screens = new HashSet<VisualElement>();   // faded by Show, not by the scan
        float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<UiMotion>() == null)
                new GameObject("UI Motion").AddComponent<UiMotion>();
        }

        void Update()
        {
            if (Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + ScanEvery;
            found.Clear();
            foreach (UIDocument doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
                if (doc.rootVisualElement != null)
                    doc.rootVisualElement.Query(className: "panel").ForEach(found.Add);
            foreach (VisualElement e in found)
            {
                if (fading.Contains(e) || screens.Contains(e))
                    continue;
                bool shown = IsShown(e);
                if (seen.TryGetValue(e, out bool was))
                {
                    if (shown && !was)
                        FadeIn(e);
                    else if (!shown && was && e.resolvedStyle.display == DisplayStyle.None)
                    {
                        FadeOutClosed(e);   // it switched itself off: show it again just long enough to fade
                        continue;
                    }
                }
                seen[e] = shown;
            }
            if (seen.Count > found.Count + 64)
                seen.Clear();   // elements that were rebuilt: forget the old ones
        }

        static bool IsShown(VisualElement e) =>
            e.resolvedStyle.display != DisplayStyle.None && !float.IsNaN(e.layout.width) && e.layout.width > 0f && e.layout.height > 0f;

        /// <summary>Fade an element in and slide it up a little. Safe to call on any element.</summary>
        public static void FadeIn(VisualElement e)
        {
            e.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("translate") };
            e.style.transitionDuration = new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) };
            e.style.opacity = 0f;
            e.style.translate = new Translate(0f, SlidePx);
            e.schedule.Execute(() =>
            {
                e.style.transitionDuration = new List<TimeValue> { new TimeValue(InSeconds, TimeUnit.Second) };
                e.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };
                e.style.opacity = 1f;
                e.style.translate = new Translate(0f, 0f);
            }).StartingIn(16);
        }

        void FadeOutClosed(VisualElement e)
        {
            fading.Add(e);
            StyleEnum<DisplayStyle> displayBefore = e.style.display;
            e.style.display = DisplayStyle.Flex;   // inline style beats the class that hid it
            e.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("translate") };
            e.style.transitionDuration = new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) };
            e.style.opacity = 1f;
            e.style.translate = new Translate(0f, 0f);
            e.schedule.Execute(() =>
            {
                e.style.transitionDuration = new List<TimeValue> { new TimeValue(OutSeconds, TimeUnit.Second) };
                e.style.opacity = 0f;
                e.style.translate = new Translate(0f, SlidePx * 0.5f);
            }).StartingIn(16);
            e.schedule.Execute(() =>
            {
                e.style.display = displayBefore;   // back to what it was: the class, or an inline switch-off
                e.style.opacity = StyleKeyword.Null;
                e.style.translate = StyleKeyword.Null;
                seen[e] = false;
                fading.Remove(e);
            }).StartingIn((long)(OutSeconds * 1000f) + 60);
        }

        /// <summary>Show or hide a whole screen with a fade (hiding turns it off once it has faded out).</summary>
        public static void Show(VisualElement e, bool visible)
        {
            if (e == null)
                return;
            screens.Add(e);
            bool on = e.style.display != DisplayStyle.None;
            if (visible == on)
                return;
            if (visible)
            {
                e.style.display = DisplayStyle.Flex;
                FadeIn(e);
            }
            else
            {
                e.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
                e.style.transitionDuration = new List<TimeValue> { new TimeValue(OutSeconds, TimeUnit.Second) };
                e.style.opacity = 0f;
                e.schedule.Execute(() =>
                {
                    if (e.style.opacity.value <= 0.01f)
                        e.style.display = DisplayStyle.None;
                }).StartingIn((long)(OutSeconds * 1000f) + 30);
            }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The UI size setting (UI information design, stage 1): 80 to 150 %, kept between games. It scales the whole panel through
    /// <c>PanelSettings.scale</c> and puts the asset back on exit.
    /// </summary>
    public static class UiScale
    {
        public const float Min = 0.8f, Max = 1.5f;
        const string Key = "kardashev.settings.uiscale";
        public static event Action Changed;

        public static float Value
        {
            get { try { return Mathf.Clamp(PlayerPrefs.GetFloat(Key, 1f), Min, Max); } catch { return 1f; } }
        }

        public static void Set(float scale)
        {
            scale = Mathf.Clamp(scale, Min, Max);
            try { PlayerPrefs.SetFloat(Key, scale); PlayerPrefs.Save(); } catch { }
            Apply();
            Changed?.Invoke();
        }

        /// <summary>Set the saved scale on the panel settings the UI documents share. The asset's own value is put back when the game exits, so a play session never changes the file.</summary>
        public static void Apply()
        {
            foreach (UIDocument doc in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                PanelSettings ps = doc.panelSettings;
                if (ps == null) continue;
                if (!originals.ContainsKey(ps)) { originals[ps] = (ps.scaleMode, ps.scale); Application.quitting += Restore; }
                ps.scaleMode = PanelScaleMode.ConstantPixelSize;
                ps.scale = Value;
            }
        }

        static readonly System.Collections.Generic.Dictionary<PanelSettings, (PanelScaleMode Mode, float Scale)> originals = new System.Collections.Generic.Dictionary<PanelSettings, (PanelScaleMode, float)>();

        static void Restore()
        {
            foreach (var kv in originals)
                if (kv.Key != null) { kv.Key.scaleMode = kv.Value.Mode; kv.Key.scale = kv.Value.Scale; }
            originals.Clear();
            Application.quitting -= Restore;
        }
    }
}

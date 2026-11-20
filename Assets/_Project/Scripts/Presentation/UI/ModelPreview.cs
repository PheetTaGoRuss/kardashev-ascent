using System.Collections.Generic;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Picture of a building's model for the build palette (director 2026-11-20: "use symbol and model preview"). Each model is
    /// drawn once, far from the planet, by a short-lived camera into a small texture, then cached.
    /// </summary>
    public static class ModelPreview
    {
        const int Size = 96;
        static readonly Dictionary<BuildingType, Texture2D> cache = new Dictionary<BuildingType, Texture2D>();

        public static Texture Of(BuildingType type, Material lit)
        {
            if (cache.TryGetValue(type, out Texture2D have) && have != null)
                return have;
            var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.DontSave, antiAliasing = 4 };
            var root = new GameObject("Model Preview") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0f, -4000f, 0f);
            var holder = new GameObject("Model") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(root.transform, false);
            holder.transform.localRotation = Quaternion.Euler(0f, -35f, 0f);
            var parts = new List<(Renderer Renderer, Color Colour)>();
            BuildingModels.Build(holder, type, lit, parts);
            foreach (var (r, c) in parts)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", c);
                r.SetPropertyBlock(block);
            }
            var camGo = new GameObject("Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(root.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.13f, 0.18f, 1f);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.targetTexture = rt;
            cam.transform.localPosition = new Vector3(0f, 1.15f, -2.6f);
            cam.transform.LookAt(root.transform.position + new Vector3(0f, 0.22f, 0f));
            var lightGo = new GameObject("Preview Light") { hideFlags = HideFlags.HideAndDontSave };
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            cam.Render();
            // keep a plain picture, not the render texture (the panel draws it reliably)
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var picture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear };
            picture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            rt.Release();
            Object.Destroy(rt);
            Object.DestroyImmediate(root);
            cache[type] = picture;
            return picture;
        }
    }
}

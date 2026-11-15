using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.Planet;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The turning 3D preview of a world in the planet chooser (director 2026-11-15): the planet's own tiles, made by
    /// the same surface generator (<see cref="PlanetView.BuildPreview"/>), lit by a light in its star's colour, drawn by
    /// a second camera into a texture with a clear background. It sits far below the scene and only runs while the
    /// chooser is open.
    /// </summary>
    public sealed class PlanetPreview
    {
        const int Size = 768;
        const float FieldOfView = 30f;
        const float Fill = 0.80f;
        const float DegreesPerSecond = 9f;
        static readonly Vector3 Place = new Vector3(0f, -4000f, 0f);

        readonly PlanetView planet;
        readonly Camera camera;
        readonly RenderTexture texture;
        readonly Light sun;
        readonly GameObject body;
        readonly Image image;
        readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        readonly List<Light> dimmed = new List<Light>();
        float spin, tilt;
        bool locked;
        static readonly Vector3 LightTravel = new Vector3(0.75f, -0.4f, 0.55f).normalized;
        bool active;

        public VisualElement Element => image;

        public PlanetPreview(PlanetView planet)
        {
            this.planet = planet;
            texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Planet preview", antiAliasing = 4 };
            texture.Create();

            body = new GameObject("Planet Preview") { hideFlags = HideFlags.DontSave };
            body.transform.position = Place;
            body.AddComponent<MeshFilter>();
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = planet != null ? planet.TerrainMaterial : null;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.SetActive(false);

            var cam = new GameObject("Planet Preview Camera") { hideFlags = HideFlags.DontSave };
            camera = cam.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.fieldOfView = FieldOfView;
            camera.targetTexture = texture;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;
            camera.depth = -11;
            float half = Mathf.Atan(Fill * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad));
            float distance = 1.03f / Mathf.Sin(half);
            cam.transform.position = Place + new Vector3(0f, 0f, -distance);
            cam.transform.LookAt(Place, Vector3.up);
            camera.enabled = false;

            var light = new GameObject("Planet Preview Star") { hideFlags = HideFlags.DontSave };
            sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.None;
            // From the upper left and a little in front, so a lit face and a terminator both show.
            sun.transform.rotation = Quaternion.LookRotation(LightTravel, Vector3.up);
            sun.enabled = false;

            image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        }

        /// <summary>
        /// Show a world: its mesh is made once per key and kept. A tidally locked world does not turn: the point under
        /// its star (latitude 0, longitude 0, the +Z axis of the tiles) faces the light.
        /// </summary>
        public void Show(int key, Func<PlanetView, Mesh> build, Color starColour, float axialTiltDeg, bool tidallyLocked = false)
        {
            if (planet == null) return;
            if (!meshes.TryGetValue(key, out Mesh mesh))
            {
                mesh = build(planet);
                meshes[key] = mesh;
            }
            body.GetComponent<MeshFilter>().sharedMesh = mesh;
            tilt = Mathf.Clamp(axialTiltDeg, 0f, 60f);
            locked = tidallyLocked;
            Turn();
            sun.color = Color.Lerp(Color.white, starColour, 0.55f);
        }

        public void SetActive(bool on)
        {
            on &= planet != null;
            if (on == active) return;
            active = on;
            camera.enabled = on;
            body.SetActive(on);
            if (on) { Camera.onPreCull += BeforeRender; Camera.onPostRender += AfterRender; }
            else { Camera.onPreCull -= BeforeRender; Camera.onPostRender -= AfterRender; }
        }

        // Only the preview's own star lights the preview, and only while its camera draws: the scene's lights are
        // off for that one render and back right after, so nothing else in the game changes.
        void BeforeRender(Camera c)
        {
            if (c != camera) return;
            dimmed.Clear();
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l != sun && l.enabled && l.type == LightType.Directional) { l.enabled = false; dimmed.Add(l); }
            sun.enabled = true;
        }

        void AfterRender(Camera c)
        {
            if (c != camera) return;
            sun.enabled = false;
            foreach (Light l in dimmed) if (l != null) l.enabled = true;
            dimmed.Clear();
        }

        public void Update(float dt)
        {
            if (!active) return;
            spin += DegreesPerSecond * dt;
            Turn();
        }

        void Turn() => body.transform.rotation = locked
            ? Quaternion.FromToRotation(Vector3.forward, -LightTravel) * Quaternion.Euler(0f, Mathf.Sin(spin * 0.05f) * 12f, 0f)
            : Quaternion.Euler(0f, 0f, -tilt) * Quaternion.Euler(0f, spin, 0f);

        public void Dispose()
        {
            SetActive(false);
            foreach (Mesh m in meshes.Values) if (m != null) UnityEngine.Object.Destroy(m);
            meshes.Clear();
            if (camera != null) UnityEngine.Object.Destroy(camera.gameObject);
            if (sun != null) UnityEngine.Object.Destroy(sun.gameObject);
            if (body != null) UnityEngine.Object.Destroy(body);
            if (texture != null) { texture.Release(); UnityEngine.Object.Destroy(texture); }
        }

        /// <summary>
        /// A star's colour on screen from its temperature: a fit to the blackbody colour (Tanner Helland 2012,
        /// "How to convert temperature (K) to RGB"). Display only; the physics uses the spectrum itself.
        /// </summary>
        public static Color StarColour(double teffK)
        {
            double t = Math.Max(1000.0, Math.Min(40000.0, teffK)) / 100.0;
            double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
            double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
            double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            return new Color((float)Clamp01(r / 255.0), (float)Clamp01(g / 255.0), (float)Clamp01(b / 255.0));
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}

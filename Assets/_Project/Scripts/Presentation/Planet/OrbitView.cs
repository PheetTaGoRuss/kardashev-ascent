using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Star;
using KardashevAscent.Presentation.UI;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Orbit view (key Y, director 2026-11-19): the planet goes round its star, not the star round the planet. A second
    /// camera looks down on a small scene far below the game: the star in the middle, the orbit ring, and the colony's
    /// planet moving along it. A tidally locked planet turns once per orbit, so one face always looks at the star; any
    /// other planet spins on its tilted axis (the spin is slowed so it can be followed, and the orbit runs as a time lapse).
    /// Sizes are not to scale. It sets itself up after the scene loads.
    /// </summary>
    public sealed class OrbitView : MonoBehaviour
    {
        static readonly Vector3 Place = new Vector3(0f, -6000f, 0f);
        const float OrbitRadius = 13f, StarRadius = 2.6f, PlanetScale = 0.55f;
        const float SecondsPerOrbit = 30f;
        const float SpinTurnsPerOrbit = 12f;   // slowed spin for planets that are not locked (not to scale)

        SimulationRunner runner;
        Camera cam;
        Transform root, star, planetBody, glow;
        LineRenderer ring;
        Light starLight;
        Material starMaterial;
        readonly System.Collections.Generic.List<Light> dimmed = new System.Collections.Generic.List<Light>();
        bool open;
        float phase;      // radians along the orbit
        float spin;       // radians of spin (planets that are not locked)
        GUIStyle label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<OrbitView>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Orbit View").AddComponent<OrbitView>();
        }

        /// <summary>Whether the orbit view is showing (the Y key toggles it).</summary>
        public bool Open { get => open; set => SetOpen(value); }

        void Start() => runner = FindFirstObjectByType<SimulationRunner>();

        void Update()
        {
            if (ScreenInput.KeyDown(KeyCode.Y) && GameFlow.IsPlaying)
                SetOpen(!open);
            if (!open) return;
            if (runner == null || runner.Simulation == null) { SetOpen(false); return; }
            var sim = runner.Simulation;
            float dt = Time.unscaledDeltaTime;
            phase += dt * Mathf.PI * 2f / SecondsPerOrbit;
            bool locked = sim.DayLengthHours <= 0.0;
            if (!locked) spin += dt * Mathf.PI * 2f * SpinTurnsPerOrbit / SecondsPerOrbit;
            Place3D(sim, locked);
        }

        void Place3D(Simulation.Game.IPlanetSimulation sim, bool locked)
        {
            Vector3 offset = new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase)) * OrbitRadius;
            planetBody.localPosition = offset;
            Vector3 toStar = -offset.normalized;
            if (locked)
            {
                // The point under the star stays under it: carry the game's own star direction (planet frame) onto the real one.
                var s = sim.StarDirection;
                Vector3 inPlanet = new Vector3(s.X, s.Y, s.Z).normalized;
                planetBody.localRotation = Quaternion.FromToRotation(inPlanet, toStar);
            }
            else
            {
                // A tilted axis that keeps its direction while the planet goes round; the planet turns about it.
                float tilt = (float)sim.ObliquityRad;
                Vector3 axis = (Quaternion.Euler(0f, 0f, -tilt * Mathf.Rad2Deg) * Vector3.up).normalized;
                planetBody.localRotation = Quaternion.AngleAxis(spin * Mathf.Rad2Deg, axis) * Quaternion.FromToRotation(Vector3.up, axis);
            }
            glow.rotation = cam.transform.rotation;
        }

        void SetOpen(bool on)
        {
            if (on == open) return;
            if (on && !Build()) return;
            open = on;
            cam.enabled = on;
            root.gameObject.SetActive(on);
            if (on) { Camera.onPreCull += BeforeRender; Camera.onPostRender += AfterRender; phase = Mathf.PI; }
            else { Camera.onPreCull -= BeforeRender; Camera.onPostRender -= AfterRender; }
        }

        bool Build()
        {
            if (root != null) return true;
            PlanetView planet = runner != null ? runner.Planet : null;
            MeshFilter source = planet != null ? planet.GetComponent<MeshFilter>() : null;
            if (source == null || source.sharedMesh == null) return false;

            root = new GameObject("Orbit View Scene") { hideFlags = HideFlags.DontSave }.transform;
            root.position = Place;

            starMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            Color colour = StarColor.FromTemperature(runner.StarTemperatureK);
            starMaterial.color = Color.Lerp(colour, Color.white, 0.35f);

            star = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(star.GetComponent<Collider>());
            star.SetParent(root, false);
            star.localScale = Vector3.one * StarRadius * 2f;
            star.GetComponent<MeshRenderer>().sharedMaterial = starMaterial;

            var glowObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(glowObject.GetComponent<Collider>());
            glow = glowObject.transform;
            glow.SetParent(root, false);
            glow.localScale = Vector3.one * StarRadius * 7f;
            var glowMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            glowMaterial.mainTexture = RadialGlow(colour);
            glowObject.GetComponent<MeshRenderer>().sharedMaterial = glowMaterial;

            var planetObject = new GameObject("Orbit Planet");
            planetBody = planetObject.transform;
            planetBody.SetParent(root, false);
            planetObject.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            planetObject.AddComponent<MeshRenderer>().sharedMaterial = planet.TerrainMaterial;
            planetBody.localScale = Vector3.one * PlanetScale / Mathf.Max(0.01f, planet.transform.lossyScale.x);

            var ringObject = new GameObject("Orbit Ring");
            ringObject.transform.SetParent(root, false);
            ring = ringObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.widthMultiplier = 0.07f;
            ring.positionCount = 128;
            ring.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave, color = new Color(1f, 1f, 1f, 0.35f) };
            for (int i = 0; i < 128; i++)
            {
                float a = i / 128f * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * OrbitRadius);
            }

            var lightObject = new GameObject("Orbit Star Light");
            lightObject.transform.SetParent(root, false);
            starLight = lightObject.AddComponent<Light>();
            starLight.type = LightType.Point;
            starLight.range = 80f;
            starLight.intensity = 6f;
            starLight.shadows = LightShadows.None;
            starLight.color = Color.Lerp(Color.white, colour, 0.5f);
            starLight.enabled = false;

            var camObject = new GameObject("Orbit View Camera") { hideFlags = HideFlags.DontSave };
            cam = camObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.012f, 0.03f);
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.depth = 50;   // over the game camera; the interface panels draw above every camera
            camObject.transform.position = Place + new Vector3(0f, 21f, -34f);
            camObject.transform.LookAt(Place + new Vector3(0f, -1f, 0f), Vector3.up);
            cam.enabled = false;
            return true;
        }

        static Texture2D RadialGlow(Color c)
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * a;
                    t.SetPixel(x, y, new Color(c.r, c.g, c.b, a * 0.85f));
                }
            t.Apply();
            return t;
        }

        // Only the orbit scene's star lights it, and only while its camera draws.
        void BeforeRender(Camera c)
        {
            if (c != cam) return;
            dimmed.Clear();
            foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l != starLight && l.enabled && l.type == LightType.Directional) { l.enabled = false; dimmed.Add(l); }
            starLight.enabled = true;
        }

        void AfterRender(Camera c)
        {
            if (c != cam) return;
            starLight.enabled = false;
            foreach (Light l in dimmed) if (l != null) l.enabled = true;
            dimmed.Clear();
        }

        void OnGUI()
        {
            if (!open || runner == null || runner.Simulation == null) return;
            label ??= new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bool locked = runner.Simulation.DayLengthHours <= 0.0;
            string text = locked
                ? "ORBIT VIEW · the planet goes round its star. Tidally locked: it turns once per orbit, so one face always looks at the star. Sizes not to scale · Y: close"
                : "ORBIT VIEW · the planet goes round its star on a tilted axis. Spin slowed and orbit sped up so you can follow them. Sizes not to scale · Y: close";
            GUI.Box(new Rect(Screen.width / 2f - 340f, UiLayout.ClearBottomY - 58f, 680f, 48f), text, label);
        }

        void OnDestroy()
        {
            Camera.onPreCull -= BeforeRender;
            Camera.onPostRender -= AfterRender;
            if (root != null) Destroy(root.gameObject);
            if (cam != null) Destroy(cam.gameObject);
        }
    }
}

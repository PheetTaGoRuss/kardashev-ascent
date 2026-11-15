using KardashevAscent.Presentation.Planet;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The real 3D planet on the main menu (director 2026-11-15): a second camera circles the game's own planet slowly
    /// and renders it with a clear background into a texture; the menu shows that texture over its drawn starfield, so
    /// the planet the player is about to land on turns behind the title. The camera only runs while the menu shows.
    /// </summary>
    public sealed class MenuPlanet
    {
        const int Size = 1024;
        const float FieldOfView = 30f;
        /// <summary>Share of the texture's width the planet fills.</summary>
        public const float Fill = 0.86f;
        const float DegreesPerSecond = 3f;

        readonly Camera camera;
        readonly RenderTexture texture;
        readonly PlanetView planet;
        readonly Image image;
        float angle = 200f;

        public VisualElement Element => image;

        public MenuPlanet(PlanetView planet)
        {
            this.planet = planet;
            texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Menu planet", antiAliasing = 4 };
            texture.Create();
            var go = new GameObject("Menu Planet Camera") { hideFlags = HideFlags.DontSave };
            camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // see-through around the planet
            camera.fieldOfView = FieldOfView;
            camera.targetTexture = texture;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            camera.depth = -10;
            camera.enabled = false;

            image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.position = Position.Absolute;
        }

        /// <summary>Run the camera while the menu shows; stop it otherwise.</summary>
        public void SetActive(bool on)
        {
            if (camera != null) camera.enabled = on && planet != null;
            image.style.display = on && planet != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Circle the planet slowly, looking at it from a little above its equator.</summary>
        public void Update(float dt)
        {
            if (planet == null || camera == null || !camera.enabled) return;
            angle += DegreesPerSecond * dt;
            float r = planet.Radius * planet.transform.lossyScale.x * 1.03f;   // a little relief above the radius
            // Distance so the planet fills Fill of the view: tan(half-angle) = Fill · tan(fov / 2).
            float half = Mathf.Atan(Fill * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad));
            float distance = r / Mathf.Sin(half);
            Vector3 centre = planet.transform.position;
            Quaternion around = Quaternion.Euler(18f, angle, 0f);
            camera.transform.position = centre + around * (Vector3.back * distance);
            camera.transform.LookAt(centre, Vector3.up);
        }

        public void Dispose()
        {
            if (camera != null) Object.Destroy(camera.gameObject);
            if (texture != null) { texture.Release(); Object.Destroy(texture); }
        }
    }
}

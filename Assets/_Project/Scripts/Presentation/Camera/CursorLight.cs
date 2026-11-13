using KardashevAscent.Presentation.Planet;
using KardashevAscent.Presentation.UI;
using UnityEngine;

namespace KardashevAscent.Presentation.CameraControl
{
    /// <summary>
    /// A flashlight that follows the mouse over the planet, so the night side can be looked at.
    /// F turns it on and off. It stays where it was while the mouse is off the planet or over a panel.
    /// Purely visual: the simulation's light comes from the star alone.
    /// The point light lives on a hidden child made at runtime, so the editor draws no light icon for it.
    /// </summary>
    public sealed class CursorLight : MonoBehaviour
    {
        [SerializeField] TilePicker picker;
        [SerializeField] Camera viewCamera;
        [SerializeField] KeyCode toggleKey = KeyCode.F;
        [SerializeField] bool startOn = true;

        [Header("Light")]
        [SerializeField] Color color = Color.white;
        [Min(0f)] [SerializeField] float intensity = 5f;
        [Tooltip("Reach of the light, in planet radii.")]
        [Range(0.05f, 2f)] [SerializeField] float range = 0.4f;
        [Tooltip("Height above the ground, in planet radii.")]
        [Range(0.01f, 1f)] [SerializeField] float height = 0.2f;

        Light cursorLight;
        PlanetView planet;

        public KeyCode ToggleKey => toggleKey;
        public bool On => cursorLight != null && cursorLight.enabled;

        void Awake()
        {
            var go = new GameObject("Cursor Light (runtime)") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            cursorLight = go.AddComponent<Light>();
            cursorLight.type = LightType.Point;
            cursorLight.shadows = LightShadows.None;
            cursorLight.enabled = startOn;
            if (picker != null)
                planet = picker.GetComponent<PlanetView>();
        }

        void OnDestroy()
        {
            if (cursorLight != null)
                Destroy(cursorLight.gameObject);
        }

        void Update()
        {
            if (ScreenInput.KeyDown(toggleKey))
                cursorLight.enabled = !cursorLight.enabled;
            if (!cursorLight.enabled || picker == null || planet == null)
                return;

            float radius = picker.transform.lossyScale.x * planet.Radius;
            cursorLight.color = color;
            cursorLight.intensity = intensity;
            cursorLight.range = range * radius;

            if (ScreenInput.PointerOverUi)
                return;
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam != null && picker.TryGetSurfacePoint(cam, Input.mousePosition, out Vector3 point, out Vector3 normal))
                transform.position = point + normal * (height * radius);
        }
    }
}

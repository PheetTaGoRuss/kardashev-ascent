using KardashevAscent.Presentation.Planet;
using KardashevAscent.Presentation.UI;
using UnityEngine;

namespace KardashevAscent.Presentation.CameraControl
{
    /// <summary>
    /// A flashlight that follows the mouse over the planet, so the night side can be looked at.
    /// F turns it on and off. It stays where it was while the mouse is off the planet or over a panel.
    /// Purely visual: the simulation's light comes from the star alone.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class CursorLight : MonoBehaviour
    {
        [SerializeField] TilePicker picker;
        [SerializeField] Camera viewCamera;
        [SerializeField] KeyCode toggleKey = KeyCode.F;
        [Tooltip("Height above the ground, in planet radii.")]
        [Range(0.01f, 1f)] [SerializeField] float height = 0.2f;

        Light cursorLight;

        public KeyCode ToggleKey => toggleKey;
        public bool On => cursorLight != null && cursorLight.enabled;

        void Awake() => cursorLight = GetComponent<Light>();

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                cursorLight.enabled = !cursorLight.enabled;
            if (!cursorLight.enabled || picker == null || ScreenInput.PointerOverUi)
                return;
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null)
                return;
            if (picker.TryGetSurfacePoint(cam, Input.mousePosition, out Vector3 point, out Vector3 normal))
            {
                float radius = picker.transform.lossyScale.x * picker.GetComponent<PlanetView>().Radius;
                transform.position = point + normal * (height * radius);
            }
        }
    }
}

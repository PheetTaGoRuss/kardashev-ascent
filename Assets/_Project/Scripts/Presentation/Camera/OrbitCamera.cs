using KardashevAscent.Presentation.Planet;
using KardashevAscent.Presentation.UI;
using UnityEngine;

namespace KardashevAscent.Presentation.CameraControl
{
    /// <summary>
    /// Orbits a planet: drag (left or right mouse) or WASD/arrows to rotate, mouse wheel or Q/E to zoom, R to reset.
    /// Moves slower near the surface so close-up control stays precise.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [Tooltip("Used when the target has no PlanetView.")]
        [Min(0.01f)] [SerializeField] float fallbackRadius = 5f;

        [Header("Distance (in planet radii from the centre)")]
        [Min(1.01f)] [SerializeField] float minDistance = 1.15f;
        [Min(1.1f)] [SerializeField] float maxDistance = 6f;
        [Min(1.1f)] [SerializeField] float startDistance = 3f;

        [Header("Speed")]
        [Tooltip("Degrees per pixel of mouse drag, from the start distance.")]
        [SerializeField] float dragSpeed = 0.25f;
        [Tooltip("Degrees per second with the keyboard, from the start distance.")]
        [SerializeField] float keySpeed = 70f;
        [Tooltip("Zoom per mouse-wheel notch.")]
        [Range(0.01f, 0.5f)] [SerializeField] float wheelZoom = 0.15f;
        [Tooltip("Zoom per second with Q/E.")]
        [Range(0.1f, 3f)] [SerializeField] float keyZoom = 1.2f;
        [Tooltip("Seconds to settle after input; 0 = instant.")]
        [Range(0f, 0.5f)] [SerializeField] float smoothTime = 0.08f;

        [Header("Start view")]
        [SerializeField] float startYaw = 20f;
        [Range(-89f, 89f)] [SerializeField] float startPitch = 15f;

        [Header("Help overlay")]
        [SerializeField] bool showHelp = true;

        float yaw, pitch, altitude;          // altitude: distance above the surface, in radii
        float goalYaw, goalPitch, goalAltitude;
        float yawVel, pitchVel, altitudeVel;
        Camera cam;
        GUIStyle helpStyle;

        float Radius
        {
            get
            {
                if (target != null && target.TryGetComponent(out PlanetView planet))
                    return planet.Radius * target.lossyScale.x;
                return fallbackRadius;
            }
        }

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        void Awake() => cam = GetComponent<Camera>();

        void Start()
        {
            ResetView(true);
        }

        public void ResetView(bool instant)
        {
            goalYaw = startYaw;
            goalPitch = startPitch;
            goalAltitude = startDistance - 1f;
            if (!instant)
                return;
            yaw = goalYaw;
            pitch = goalPitch;
            altitude = goalAltitude;
            Apply();
        }

        void LateUpdate()
        {
            ReadInput();

            if (smoothTime > 0f)
            {
                yaw = Mathf.SmoothDampAngle(yaw, goalYaw, ref yawVel, smoothTime);
                pitch = Mathf.SmoothDamp(pitch, goalPitch, ref pitchVel, smoothTime);
                altitude = Mathf.SmoothDamp(altitude, goalAltitude, ref altitudeVel, smoothTime);
            }
            else
            {
                yaw = goalYaw;
                pitch = goalPitch;
                altitude = goalAltitude;
            }
            Apply();
        }

        bool dragStartedOnUi;

        void ReadInput()
        {
            // Closer to the surface = slower, so a drag moves the ground about the same amount on screen.
            float speedScale = Mathf.Clamp(goalAltitude / (startDistance - 1f), 0.05f, 2f);

            // A drag that starts on an on-screen panel (a slider, say) does not turn the camera.
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                dragStartedOnUi = ScreenInput.PointerOverUi;
            if ((Input.GetMouseButton(0) || Input.GetMouseButton(1)) && !dragStartedOnUi)
            {
                goalYaw += Input.GetAxisRaw("Mouse X") * 10f * dragSpeed * speedScale;
                goalPitch -= Input.GetAxisRaw("Mouse Y") * 10f * dragSpeed * speedScale;
            }

            float horizontal = 0f, vertical = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal += 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal -= 1f;
            // Keys move the view like a map (W = north, D = east); dragging pulls the surface with the mouse.
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
            goalYaw += horizontal * keySpeed * speedScale * Time.unscaledDeltaTime;
            goalPitch += vertical * keySpeed * speedScale * Time.unscaledDeltaTime;
            goalPitch = Mathf.Clamp(goalPitch, -89f, 89f);

            float zoom = Mathf.Pow(1f - wheelZoom, Input.mouseScrollDelta.y);
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus))
                zoom *= Mathf.Exp(-keyZoom * Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus))
                zoom *= Mathf.Exp(keyZoom * Time.unscaledDeltaTime);
            goalAltitude = Mathf.Clamp(goalAltitude * zoom, minDistance - 1f, maxDistance - 1f);

            if (Input.GetKeyDown(KeyCode.R))
                ResetView(false);
            if (Input.GetKeyDown(KeyCode.H))
                showHelp = !showHelp;
        }

        void Apply()
        {
            float r = Radius;
            Vector3 center = target != null ? target.position : Vector3.zero;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float distance = r * (1f + altitude);
            transform.SetPositionAndRotation(center + rotation * new Vector3(0f, 0f, -distance), rotation);

            if (cam == null)
                cam = GetComponent<Camera>();
            cam.nearClipPlane = Mathf.Max(0.01f, r * altitude * 0.2f);
            cam.farClipPlane = Mathf.Max(cam.nearClipPlane + 1f, distance + r * 2f);
        }

        void OnGUI()
        {
            if (!showHelp)
                return;
            helpStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                padding = new RectOffset(10, 10, 8, 8),
                normal = { textColor = new Color(0.9f, 0.93f, 1f) },
            };

            string tiles = target != null && target.TryGetComponent(out PlanetView planet) && planet.Sphere != null
                ? $"Tiles: {planet.Sphere.TileCount:N0}\n"
                : "";
            string text = tiles +
                          "Drag (mouse) or WASD/arrows: rotate\n" +
                          "Mouse wheel or Q/E: zoom\n" +
                          "R: reset view    H: hide this help";
            GUI.Box(new Rect(12, Screen.height - 98, 290, 86), text, helpStyle);
        }
    }
}

using KardashevAscent.Presentation.Planet;
using KardashevAscent.Presentation.UI;
using UnityEngine;

namespace KardashevAscent.Presentation.CameraControl
{
    /// <summary>
    /// Orbits a planet: drag (left or right mouse) or WASD/arrows to rotate, mouse wheel or Q/E to zoom, R to reset.
    /// Moves slower near the surface so close-up control stays precise.
    /// <para>Site view: <see cref="FocusSite"/> flies down to one tile and looks at it from an angle. There, right-drag
    /// (the left button selects slots) or A/D turn around the site, W/S or a vertical drag change the viewing angle, the wheel zooms, and zooming out
    /// past the limit raises <see cref="SiteExitRequested"/>. <see cref="LeaveSite"/> flies back up.</para>
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

        [Header("Site view (zoomed in on one tile)")]
        [Tooltip("Seconds for the flight between the planet view and a site.")]
        [Range(0.05f, 3f)] [SerializeField] float siteFlightTime = 0.8f;
        [Tooltip("Viewing angle from straight down, degrees.")]
        [Range(0f, 80f)] [SerializeField] float siteStartTilt = 48f;
        [Tooltip("Start distance from the site centre, in site radii.")]
        [Min(0.2f)] [SerializeField] float siteStartDistance = 2.4f;
        [Min(0.2f)] [SerializeField] float siteMinDistance = 0.5f;
        [Min(0.5f)] [SerializeField] float siteMaxDistance = 5f;

        [Header("Help overlay")]
        [SerializeField] bool showHelp = true;

        float yaw, pitch, altitude;          // altitude: distance above the surface, in radii
        float goalYaw, goalPitch, goalAltitude;
        float yawVel, pitchVel, altitudeVel;
        Camera cam;
        bool dragStartedOnUi;

        // Site view. The site's direction is kept in the target's local space so it follows the planet.
        bool inSite;
        float siteBlend;                                   // 0 = planet view, 1 = site view
        Vector3 siteUpLocal, siteUpGoal;
        float siteRadius;                                  // world units
        float siteHeading, siteTilt, siteDistance;         // degrees, degrees, site radii
        float goalHeading, goalTilt, goalSiteDistance;
        float headingVel, tiltVel, siteDistanceVel;

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

        /// <summary>True while focused on a site (also during the flight down).</summary>
        public bool InSite => inSite;

        /// <summary>0 in the planet view, 1 fully in the site view, in between while flying.</summary>
        public float SiteBlend => siteBlend;

        /// <summary>Raised when the player zooms out past the site view's limit.</summary>
        public event System.Action SiteExitRequested;

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

        /// <summary>
        /// Fly down to a site. <paramref name="directionLocal"/> is the unit direction from the target's centre to the
        /// site, in the target's local space; <paramref name="radiusWorld"/> is the site's size (centre to edge).
        /// Moving between sites keeps the current viewing angle and zoom.
        /// </summary>
        public void FocusSite(Vector3 directionLocal, float radiusWorld)
        {
            if (!inSite)
            {
                siteUpLocal = directionLocal.normalized;
                goalHeading = siteHeading = 0f;
                goalTilt = siteTilt = siteStartTilt;
                goalSiteDistance = siteDistance = siteStartDistance;
            }
            inSite = true;
            siteUpGoal = directionLocal.normalized;
            siteRadius = Mathf.Max(1e-4f, radiusWorld);

            // Turn the planet view to face the site as well, so leaving flies straight back up.
            Vector3 up = target != null ? target.TransformDirection(siteUpGoal) : siteUpGoal;
            goalPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(up.y, -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f);
            goalYaw = Mathf.Atan2(-up.x, -up.z) * Mathf.Rad2Deg;
        }

        /// <summary>Fly back up to the planet view.</summary>
        public void LeaveSite() => inSite = false;

        void LateUpdate()
        {
            if (inSite && siteBlend > 0.5f)
                ReadSiteInput();
            else if (!inSite)
                ReadInput();
            siteBlend = Mathf.MoveTowards(siteBlend, inSite ? 1f : 0f, Time.unscaledDeltaTime / siteFlightTime);
            // Glide to a neighbouring site instead of jumping.
            siteUpLocal = Vector3.Slerp(siteUpLocal, siteUpGoal, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.15f)).normalized;

            if (smoothTime > 0f)
            {
                yaw = Mathf.SmoothDampAngle(yaw, goalYaw, ref yawVel, smoothTime);
                pitch = Mathf.SmoothDamp(pitch, goalPitch, ref pitchVel, smoothTime);
                altitude = Mathf.SmoothDamp(altitude, goalAltitude, ref altitudeVel, smoothTime);
                siteHeading = Mathf.SmoothDampAngle(siteHeading, goalHeading, ref headingVel, smoothTime);
                siteTilt = Mathf.SmoothDamp(siteTilt, goalTilt, ref tiltVel, smoothTime);
                siteDistance = Mathf.SmoothDamp(siteDistance, goalSiteDistance, ref siteDistanceVel, smoothTime);
            }
            else
            {
                yaw = goalYaw;
                pitch = goalPitch;
                altitude = goalAltitude;
                siteHeading = goalHeading;
                siteTilt = goalTilt;
                siteDistance = goalSiteDistance;
            }
            Apply();
        }

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

        void ReadSiteInput()
        {
            // The left button selects slots here; the right (or middle) button turns the view.
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
                dragStartedOnUi = ScreenInput.PointerOverUi;
            if ((Input.GetMouseButton(1) || Input.GetMouseButton(2)) && !dragStartedOnUi)
            {
                goalHeading += Input.GetAxisRaw("Mouse X") * 10f * dragSpeed;
                goalTilt -= Input.GetAxisRaw("Mouse Y") * 10f * dragSpeed;
            }

            float turn = 0f, lean = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) turn += 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) turn -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) lean += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) lean -= 1f;
            goalHeading += turn * keySpeed * Time.unscaledDeltaTime;
            goalTilt = Mathf.Clamp(goalTilt + lean * keySpeed * 0.5f * Time.unscaledDeltaTime, 0f, 80f);

            float wheel = ScreenInput.PointerOverUi ? 0f : Input.mouseScrollDelta.y;
            float zoom = Mathf.Pow(1f - wheelZoom, wheel);
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus))
                zoom *= Mathf.Exp(-keyZoom * Time.unscaledDeltaTime);
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus))
                zoom *= Mathf.Exp(keyZoom * Time.unscaledDeltaTime);
            if (wheel < 0f && goalSiteDistance >= siteMaxDistance - 1e-3f)
                SiteExitRequested?.Invoke();
            goalSiteDistance = Mathf.Clamp(goalSiteDistance * zoom, siteMinDistance, siteMaxDistance);

            if (Input.GetKeyDown(KeyCode.R))
            {
                goalHeading = 0f;
                goalTilt = siteStartTilt;
                goalSiteDistance = siteStartDistance;
            }
            if (Input.GetKeyDown(KeyCode.H))
                showHelp = !showHelp;
        }

        void Apply()
        {
            float r = Radius;
            Vector3 center = target != null ? target.position : Vector3.zero;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float distance = r * (1f + altitude);
            Vector3 position = center + rotation * new Vector3(0f, 0f, -distance);
            float near = Mathf.Max(0.01f, r * altitude * 0.2f);

            if (siteBlend > 0f)
            {
                SitePose(center, r, out Vector3 sitePosition, out Quaternion siteRotation);
                float t = Mathf.SmoothStep(0f, 1f, siteBlend);
                position = Vector3.Lerp(position, sitePosition, t);
                rotation = Quaternion.Slerp(rotation, siteRotation, t);
                near = Mathf.Lerp(near, siteDistance * siteRadius * 0.05f, t);
            }
            transform.SetPositionAndRotation(position, rotation);

            if (cam == null)
                cam = GetComponent<Camera>();
            cam.nearClipPlane = Mathf.Max(1e-4f, near);
            cam.farClipPlane = Mathf.Max(cam.nearClipPlane + 1f, distance + r * 2f);
        }

        void SitePose(Vector3 center, float r, out Vector3 position, out Quaternion rotation)
        {
            Vector3 up = target != null ? target.TransformDirection(siteUpLocal) : siteUpLocal;
            Vector3 axis = target != null ? target.up : Vector3.up;
            Vector3 north = Vector3.ProjectOnPlane(axis, up);
            if (north.sqrMagnitude < 1e-6f)
                north = Vector3.ProjectOnPlane(target != null ? target.forward : Vector3.forward, up);
            Quaternion frame = Quaternion.AngleAxis(siteHeading, up) * Quaternion.LookRotation(north.normalized, up);

            // Heading 0: standing south of the site and looking north, like reading a map.
            Vector3 focus = center + up * r;
            float tilt = siteTilt * Mathf.Deg2Rad;
            Vector3 offset = frame * new Vector3(0f, Mathf.Cos(tilt), -Mathf.Sin(tilt));
            position = focus + offset * (siteDistance * siteRadius);
            rotation = Quaternion.LookRotation(focus - position, frame * Vector3.forward);
        }

        /// <summary>Whether the controls help is shown (H toggles it).</summary>
        public bool ShowHelp
        {
            get => showHelp;
            set => showHelp = value;
        }

        /// <summary>Controls help for the current view.</summary>
        public string HelpText
        {
            get
            {
                if (inSite)
                    return "Site view\n" +
                           "Click or drag: select slots · Shift: add · Ctrl: remove · Ctrl+A: all\n" +
                           "Right-drag or A/D: turn · W/S: view angle\n" +
                           "Wheel or Q/E: zoom (zoom out to leave)\n" +
                           "R: reset · Esc: clear selection, then back to planet · H: hide · C: colony · L: planets · F: flashlight · F5/F9: save/load";
                string tiles = target != null && target.TryGetComponent(out PlanetView planet) && planet.Sphere != null
                    ? $"Tiles: {planet.Sphere.TileCount:N0} · click one to build there\n"
                    : "";
                return tiles +
                       "Drag (mouse) or WASD/arrows: rotate\n" +
                       "Mouse wheel or Q/E: zoom · F: flashlight on/off\n" +
                       "R: reset view · H: hide help · C: colony · L: planets · F5/F9: save/load";
            }
        }
    }
}

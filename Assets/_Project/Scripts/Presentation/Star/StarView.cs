using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Solar;
using UnityEngine;

namespace KardashevAscent.Presentation.Star
{
    /// <summary>
    /// Puts the simulation's star in the scene: turns this directional light to shine from the star,
    /// tints it by the star's temperature, and draws the star as a glowing disc in the sky whose size
    /// comes from the star's real radius and distance (exaggerated so it reads on screen).
    /// The planet hides the disc when the star is behind it.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class StarView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] Material glowMaterial;

        [Header("Disc size")]
        [Tooltip("Multiplier on the star's true angular size (the Sun is only 0.53° from Earth).")]
        [Range(1f, 20f)] [SerializeField] float sizeExaggeration = 4f;
        [Tooltip("Smallest drawn disc, degrees, so dim distant stars stay visible.")]
        [Range(0.1f, 10f)] [SerializeField] float minAngularDiameterDeg = 1.5f;
        [Tooltip("Glow quad size as a multiple of the disc diameter (must match the shader's disc fraction).")]
        [Range(1f, 20f)] [SerializeField] float glowSize = 6f;

        [Header("Light")]
        [Range(0f, 3f)] [SerializeField] float lightIntensity = 1.15f;
        [Tooltip("Ambient light so the night side stays faintly readable.")]
        [SerializeField] Color nightAmbient = new Color(0.035f, 0.04f, 0.055f);

        Light starLight;
        Transform disc;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int DiscFractionId = Shader.PropertyToID("_DiscFraction");

        /// <summary>World-space direction from the planet toward the star.</summary>
        public Vector3 DirectionToStar { get; private set; } = Vector3.forward;

        void Awake()
        {
            starLight = GetComponent<Light>();
            block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = nightAmbient;
        }

        void Start()
        {
            if (glowMaterial != null)
                CreateDisc();
        }

        void OnDestroy()
        {
            if (disc != null)
                Destroy(disc.gameObject);
        }

        void LateUpdate()
        {
            if (runner == null || runner.Simulation == null)
                return;

            var star = runner.Simulation.StarDirection;
            Transform planet = runner.Planet.transform;
            DirectionToStar = planet.TransformDirection(new Vector3(star.X, star.Y, star.Z)).normalized;

            Color color = StarColor.FromTemperature(runner.StarTemperatureK);
            transform.rotation = Quaternion.LookRotation(-DirectionToStar);
            starLight.color = color;
            starLight.intensity = lightIntensity;

            UpdateDisc(color);
        }

        void UpdateDisc(Color color)
        {
            Camera cam = Camera.main;
            if (disc == null || cam == null)
                return;

            PlanetSolarParams p = runner.SolarParams;
            double starRadius = StarGeometry.Radius(p.Luminosity, runner.StarTemperatureK);
            double trueDeg = StarGeometry.AngularDiameter(starRadius, p.SemiMajorAxis) * Mathf.Rad2Deg;
            float angularDeg = Mathf.Max((float)trueDeg * sizeExaggeration, minAngularDiameterDeg);

            // Parallel rays: the disc sits along the star direction from the camera, just inside the far plane,
            // so the planet (which is nearer) covers it when the star is behind the planet.
            float distance = cam.farClipPlane * 0.9f;
            Vector3 position = cam.transform.position + DirectionToStar * distance;
            float diameter = 2f * distance * Mathf.Tan(0.5f * angularDeg * Mathf.Deg2Rad);
            disc.SetPositionAndRotation(position, Quaternion.LookRotation(DirectionToStar, cam.transform.up));
            disc.localScale = Vector3.one * diameter * glowSize;

            var renderer = disc.GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(block);
            block.SetColor(ColorId, color);
            block.SetFloat(DiscFractionId, 1f / glowSize);
            renderer.SetPropertyBlock(block);
        }

        void CreateDisc()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Star Disc";
            go.hideFlags = HideFlags.DontSave;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            disc = go.transform;
        }
    }
}

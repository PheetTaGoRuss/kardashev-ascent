using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>Generates a hex-tiled planet and shows it. Rebuilds in the editor when settings change.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlanetView : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Subdivision frequency n. Tiles = 10·n² + 2.")]
        [Range(2, 64)] [SerializeField] int frequency = 24;
        [Min(0.01f)] [SerializeField] float radius = 5f;

        [Header("Surface")]
        [SerializeField] int seed = 7;
        [Range(0f, 1f)] [SerializeField] float oceanFraction = 0.62f;
        [Range(40f, 90f)] [SerializeField] float iceLatitude = 70f;
        [Range(0.5f, 4f)] [SerializeField] float continentScale = 1.6f;

        [Header("Look")]
        [Range(0f, 0.3f)] [SerializeField] float borderWidth = 0.07f;
        [Range(0f, 1f)] [SerializeField] float borderDarken = 0.3f;
        [SerializeField] Color deepOcean = new Color(0.05f, 0.16f, 0.36f, 0.75f);
        [SerializeField] Color shallowOcean = new Color(0.10f, 0.36f, 0.58f, 0.75f);
        [SerializeField] Color lowland = new Color(0.30f, 0.52f, 0.24f, 0.1f);
        [SerializeField] Color highland = new Color(0.55f, 0.50f, 0.32f, 0.1f);
        [SerializeField] Color mountain = new Color(0.45f, 0.42f, 0.40f, 0.15f);
        [SerializeField] Color ice = new Color(0.90f, 0.94f, 0.97f, 0.35f);

        [Header("Motion")]
        [Tooltip("Planet spin while playing, degrees per second (0 = still).")]
        [SerializeField] float spinDegreesPerSecond;

        Mesh mesh;
        bool dirty = true;

        public HexSphere Sphere { get; private set; }
        public PlanetSurface Surface { get; private set; }
        public float Radius => radius;

        void OnEnable() => dirty = true;

        void OnValidate() => dirty = true;

        void Update()
        {
            if (dirty)
                Rebuild();
            if (Application.isPlaying && spinDegreesPerSecond != 0f)
                transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);
        }

        void OnDisable() => ReleaseMesh();

        [ContextMenu("Rebuild Planet")]
        public void Rebuild()
        {
            dirty = false;
            Sphere = HexSphere.Create(frequency);
            Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(seed, oceanFraction, iceLatitude, continentScale));

            var colors = new Color32[Sphere.TileCount];
            for (int t = 0; t < colors.Length; t++)
                colors[t] = TileColor(Surface.Terrain[t], Surface.Elevation[t], t);

            ReleaseMesh();
            mesh = PlanetMeshBuilder.Build(Sphere, radius, colors, borderWidth, borderDarken);
            mesh.hideFlags = HideFlags.DontSave;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        Color32 TileColor(TerrainType terrain, float elevation, int tile)
        {
            Color c;
            switch (terrain)
            {
                case TerrainType.DeepOcean:
                    c = Color.Lerp(deepOcean, shallowOcean, Mathf.InverseLerp(-1f, -0.35f, elevation) * 0.4f);
                    break;
                case TerrainType.ShallowOcean:
                    c = Color.Lerp(deepOcean, shallowOcean, Mathf.InverseLerp(-0.35f, 0f, elevation) * 0.6f + 0.4f);
                    break;
                case TerrainType.Lowland:
                    c = Color.Lerp(lowland, highland, Mathf.InverseLerp(0f, 0.35f, elevation) * 0.35f);
                    break;
                case TerrainType.Highland:
                    c = Color.Lerp(highland, mountain, Mathf.InverseLerp(0.35f, 0.7f, elevation) * 0.4f);
                    break;
                case TerrainType.Mountain:
                    c = Color.Lerp(mountain, ice, Mathf.InverseLerp(0.7f, 1f, elevation) * 0.5f);
                    break;
                default:
                    c = ice;
                    break;
            }

            // Small per-tile variation so neighbouring tiles read as separate cells.
            float jitter = 1f + (Hash01(tile) - 0.5f) * 0.08f;
            return new Color(c.r * jitter, c.g * jitter, c.b * jitter, c.a);
        }

        static float Hash01(int x)
        {
            unchecked
            {
                uint h = (uint)x * 0x9E3779B1u;
                h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13;
                return (h & 0xFFFF) / 65535f;
            }
        }

        void ReleaseMesh()
        {
            if (mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
            mesh = null;
        }
    }
}

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
        [Tooltip("Where polar ice starts, degrees of latitude (eyeball: radius of the open circle under the star).")]
        [Range(0f, 180f)] [SerializeField] float iceLatitude = 70f;
        [Range(0.5f, 4f)] [SerializeField] float continentScale = 1.6f;
        [Tooltip("Tidally locked look: ice everywhere except a circle under the star (latitude 0, longitude 0).")]
        [SerializeField] bool iceAroundSubStellar;
        [Tooltip("Seas in the basins (frozen where cold). Off: dry basins and deserts.")]
        [SerializeField] bool liquidWater = true;
        [Tooltip("Air temperature, °C: above 40 the lowlands are desert.")]
        [SerializeField] float airTemperatureC = 15f;

        [Header("Relief")]
        [Tooltip("Height of the highest land above sea level, as a share of the radius. About 10x true scale so " +
                 "mountains read from orbit (Earth's highest peak is 0.14 % of its radius).")]
        [Range(0f, 0.05f)] [SerializeField] float reliefScale = 0.015f;
        [Tooltip("Peaks, tree crowns and rivers drawn over the tiles.")]
        [SerializeField] bool showFeatures = true;

        [Header("Look")]
        [Range(0f, 0.3f)] [SerializeField] float borderWidth = 0.07f;
        [Range(0f, 1f)] [SerializeField] float borderDarken = 0.3f;
        [SerializeField] Color deepOcean = new Color(0.05f, 0.16f, 0.36f, 0.75f);
        [SerializeField] Color shallowOcean = new Color(0.10f, 0.36f, 0.58f, 0.75f);
        [SerializeField] Color lowland = new Color(0.30f, 0.52f, 0.24f, 0.1f);
        [SerializeField] Color highland = new Color(0.55f, 0.50f, 0.32f, 0.1f);
        [SerializeField] Color mountain = new Color(0.45f, 0.42f, 0.40f, 0.15f);
        [SerializeField] Color ice = new Color(0.90f, 0.94f, 0.97f, 0.35f);
        [SerializeField] Color frozenSea = new Color(0.66f, 0.80f, 0.90f, 0.45f);
        [SerializeField] Color tundra = new Color(0.47f, 0.47f, 0.42f, 0.1f);
        [SerializeField] Color desert = new Color(0.78f, 0.62f, 0.38f, 0.08f);
        [SerializeField] Color dryBasin = new Color(0.55f, 0.35f, 0.25f, 0.05f);

        [Header("Motion")]
        [Tooltip("Planet spin while playing, degrees per second (0 = still).")]
        [SerializeField] float spinDegreesPerSecond;

        Mesh mesh;
        bool dirty = true;

        Color32[] terrainColors;
        Color32[] overlayColors;
        Material overlayMaterial;
        Material terrainMaterial;

        /// <summary>The planet's own (lit, vertex-colour) material, for terrain detail drawn elsewhere.</summary>
        public Material TerrainMaterial => terrainMaterial != null ? terrainMaterial : GetComponent<MeshRenderer>().sharedMaterial;

        public HexSphere Sphere { get; private set; }
        public PlanetSurface Surface { get; private set; }

        /// <summary>The planet's rivers: the same map the simulation uses (same tiles and seed).</summary>
        public RiverMap Rivers { get; private set; }

        float[] tileRadius;
        GameObject features;
        Mesh featuresMesh;
        int detailTile = -1;

        /// <summary>
        /// Distance from the planet centre to a tile's top (relief): land rises with its elevation, the sea and frozen
        /// sea stay at sea level (<see cref="Radius"/>). Local units, like <see cref="Radius"/>.
        /// </summary>
        public float TileRadius(int tile) =>
            tileRadius != null && tile >= 0 && tile < tileRadius.Length ? tileRadius[tile] : radius;

        /// <summary>
        /// Show close-up detail on this tile (the open building site): its orbit-scale peak, crowns and river leave
        /// so the site's own detail takes over. −1: none.
        /// </summary>
        public void SetDetailTile(int tile)
        {
            if (tile == detailTile)
                return;
            detailTile = tile;
            BuildFeatures();
        }
        public float Radius => radius;

        /// <summary>Raised after the tiles are regenerated; tile ids from before are no longer valid.</summary>
        public event System.Action Rebuilt;

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
            Surface = PlanetSurface.Generate(Sphere, Settings);
            Rivers = RiverMap.Generate(Sphere, Surface, seed);
            tileRadius = new float[Sphere.TileCount];
            for (int t = 0; t < tileRadius.Length; t++)
            {
                bool sea = Terrains.IsWater(Surface.Terrain[t]) || Surface.Terrain[t] == TerrainType.FrozenSea;
                tileRadius[t] = radius * (1f + (sea ? 0f : reliefScale * Mathf.Max(0f, Surface.Elevation[t])));
            }
            detailTile = -1;

            terrainColors = new Color32[Sphere.TileCount];
            for (int t = 0; t < terrainColors.Length; t++)
                terrainColors[t] = TileColor(Surface.Terrain[t], Surface.Elevation[t], t);

            // New tiles: any overlay colours from before no longer line up.
            overlayColors = null;
            overlayMaterial = null;
            ApplyColors();
            Rebuilt?.Invoke();
        }

        /// <summary>
        /// Paints the tiles with other colours (one per tile, e.g. a data map), optionally with another
        /// material such as an unlit one so the night side stays readable. <see cref="ShowTerrain"/> undoes it.
        /// </summary>
        public void ShowTileColors(Color32[] colors, Material material = null)
        {
            EnsureBuilt();
            if (colors == null || colors.Length != Sphere.TileCount)
                throw new System.ArgumentException("Need exactly one colour per tile.", nameof(colors));
            overlayColors = colors;
            overlayMaterial = material;
            ApplyColors();
        }

        /// <summary>Back to the terrain colours and the planet's own material.</summary>
        public void ShowTerrain()
        {
            overlayColors = null;
            overlayMaterial = null;
            ApplyColors();
        }

        void ApplyColors()
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (terrainMaterial == null)
                terrainMaterial = meshRenderer.sharedMaterial;

            ReleaseMesh();
            mesh = PlanetMeshBuilder.Build(Sphere, radius, overlayColors ?? terrainColors, borderWidth, borderDarken, tileRadius);
            mesh.hideFlags = HideFlags.DontSave;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer.sharedMaterial = overlayMaterial != null ? overlayMaterial : terrainMaterial;
            BuildFeatures();
        }

        /// <summary>Peaks, crowns and rivers over the tiles (hidden while a data map is shown).</summary>
        void BuildFeatures()
        {
            if (Sphere == null || tileRadius == null)
                return;
            if (features == null)
            {
                // After a domain reload the field is empty but the child may still be there: reuse it.
                Transform old = transform.Find("Terrain Features");
                if (old != null)
                    features = old.gameObject;
            }
            if (features == null)
            {
                features = new GameObject("Terrain Features") { hideFlags = HideFlags.DontSave };
                features.transform.SetParent(transform, false);
                features.AddComponent<MeshFilter>();
                features.AddComponent<MeshRenderer>();
            }
            if (featuresMesh != null)
            {
                if (Application.isPlaying) Destroy(featuresMesh);
                else DestroyImmediate(featuresMesh);
            }
            featuresMesh = TerrainFeatures.Build(Sphere, Surface, Rivers, tileRadius, radius, detailTile);
            featuresMesh.hideFlags = HideFlags.DontSave;
            features.GetComponent<MeshFilter>().sharedMesh = featuresMesh;
            features.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
            features.SetActive(showFeatures && overlayColors == null);
        }

        /// <summary>
        /// Use another planet's surface (its continents and ice). Returns true if anything changed; the tiles are
        /// then rebuilt on <see cref="EnsureBuilt"/> or the next Update, which raises <see cref="Rebuilt"/>.
        /// </summary>
        public bool SetSurface(in SurfaceSettings settings)
        {
            bool changed = seed != settings.Seed
                || !Mathf.Approximately(oceanFraction, (float)settings.OceanFraction)
                || !Mathf.Approximately(iceLatitude, (float)settings.IceLatitudeDeg)
                || !Mathf.Approximately(continentScale, (float)settings.NoiseScale)
                || iceAroundSubStellar != settings.IceAroundSubStellar
                || liquidWater != settings.LiquidWater
                || !Mathf.Approximately(airTemperatureC, (float)settings.AirTemperatureC);
            if (!changed)
                return false;
            seed = settings.Seed;
            oceanFraction = (float)settings.OceanFraction;
            iceLatitude = (float)settings.IceLatitudeDeg;
            continentScale = (float)settings.NoiseScale;
            iceAroundSubStellar = settings.IceAroundSubStellar;
            liquidWater = settings.LiquidWater;
            airTemperatureC = (float)settings.AirTemperatureC;
            dirty = true;
            return true;
        }

        /// <summary>The surface settings the tiles are generated from.</summary>
        public SurfaceSettings Settings =>
            new SurfaceSettings(seed, oceanFraction, iceLatitude, continentScale, iceAroundSubStellar, liquidWater, airTemperatureC, true);

        /// <summary>Makes sure the tiles exist now (they are otherwise built on the first Update).</summary>
        public void EnsureBuilt()
        {
            if (dirty || Sphere == null)
                Rebuild();
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
                case TerrainType.FrozenSea:
                    // Thicker-looking ice over shallower water.
                    c = Color.Lerp(frozenSea, ice, Mathf.InverseLerp(-1f, 0f, elevation) * 0.35f);
                    break;
                case TerrainType.Tundra:
                    c = Color.Lerp(tundra, mountain, Mathf.InverseLerp(0f, 0.7f, elevation) * 0.4f);
                    break;
                case TerrainType.Desert:
                    c = Color.Lerp(desert, highland, Mathf.InverseLerp(0f, 0.35f, elevation) * 0.35f);
                    break;
                case TerrainType.Forest:
                    c = Color.Lerp(new Color32(44, 98, 52, 255), new Color32(60, 116, 60, 255), Mathf.InverseLerp(0f, 0.5f, elevation));
                    break;
                case TerrainType.Wetland:
                    c = new Color32(64, 118, 98, 255);
                    break;
                case TerrainType.Beach:
                    c = new Color32(222, 205, 150, 255);
                    break;
                case TerrainType.VolcanicRock:
                    c = Color.Lerp(new Color32(70, 54, 54, 255), new Color32(96, 76, 70, 255), Mathf.InverseLerp(0.3f, 1f, elevation));
                    break;
                case TerrainType.DryBasin:
                    // Deeper basins darker.
                    c = Color.Lerp(dryBasin * 0.75f, dryBasin, Mathf.InverseLerp(-1f, 0f, elevation));
                    c.a = dryBasin.a;
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

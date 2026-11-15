using System.Collections.Generic;
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
        /// <summary>How far a canyon floor sits below the rim, share of the radius (twice the largest normal relief).</summary>
        [Range(0f, 0.1f)] [SerializeField] float canyonDepth = 0.03f;
        [Tooltip("Peaks and tree crowns drawn over the tiles.")]
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

        IReadOnlyDictionary<int, float> builtShare;
        IReadOnlyDictionary<int, KardashevAscent.Simulation.Resources.PassageKind> passages;

        /// <summary>Opened mountains and canyons: a tunnel mouth, a blasted notch, a bridge (director 2026-11-15).</summary>
        public void SetPassages(IReadOnlyDictionary<int, KardashevAscent.Simulation.Resources.PassageKind> opened)
        {
            var copy = opened != null ? new Dictionary<int, KardashevAscent.Simulation.Resources.PassageKind>(opened) : null;
            if (SamePassages(copy)) return;
            passages = copy;
            BuildFeatures();
        }

        bool SamePassages(Dictionary<int, KardashevAscent.Simulation.Resources.PassageKind> other)
        {
            int a = passages?.Count ?? 0, b = other?.Count ?? 0;
            if (a != b) return false;
            if (a == 0) return true;
            foreach (var kv in other)
                if (!passages.TryGetValue(kv.Key, out var k) || k != kv.Value) return false;
            return true;
        }

        /// <summary>Share of each tile's slots built on: its forest canopy thins out to match (from orbit).</summary>
        public void SetBuiltShare(IReadOnlyDictionary<int, float> share)
        {
            builtShare = share;
            BuildFeatures();
        }

        /// <summary>
        /// Show close-up detail on this tile (the open building site): its orbit-scale peak and crowns leave
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
            tileRadius = new float[Sphere.TileCount];
            for (int t = 0; t < tileRadius.Length; t++)
            {
                bool sea = Terrains.IsWater(Surface.Terrain[t]) || Surface.Terrain[t] == TerrainType.FrozenSea;
                tileRadius[t] = radius * (1f + (sea ? 0f : reliefScale * Mathf.Max(0f, Surface.Elevation[t])));
            }
            // Canyons go deep (director 2026-11-15): the floor sits far below the ground on either side, deeper than
            // any normal step between tiles, so the walls read as a gorge.
            for (int t = 0; t < tileRadius.Length; t++)
            {
                if (Surface.Terrain[t] != TerrainType.Canyon) continue;
                float rim = 0f;
                foreach (int n in Sphere.Neighbors[t])
                    if (Surface.Terrain[n] != TerrainType.Canyon) rim = Mathf.Max(rim, Mathf.Max(0f, Surface.Elevation[n]));
                tileRadius[t] = radius * (1f + reliefScale * rim - canyonDepth);
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

        /// <summary>Peaks and crowns over the tiles (hidden while a data map is shown).</summary>
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
            featuresMesh = TerrainFeatures.Build(Sphere, Surface, tileRadius, radius, detailTile, builtShare, passages);
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

        /// <summary>
        /// Another planet's tiles in this planet's colours and relief, as a mesh of radius 1 (the planet chooser's
        /// preview). Uses the same surface generator, so the preview is the world the player would land on. A gas
        /// planet has no ground: it is drawn as cloud bands instead.
        /// </summary>
        public Mesh BuildPreview(in SurfaceSettings settings, bool gas = false, int previewFrequency = 20)
        {
            HexSphere sphere = HexSphere.Create(previewFrequency);
            PlanetSurface surface = PlanetSurface.Generate(sphere, settings);
            var colors = new Color32[sphere.TileCount];
            var radii = new float[sphere.TileCount];
            for (int t = 0; t < colors.Length; t++)
            {
                if (gas)
                {
                    float lat = (float)sphere.Latitude(t);
                    float band = Mathf.Sin(lat * 9f + Mathf.Sin(lat * 23f) * 0.6f + (Hash01(t) - 0.5f) * 0.5f) * 0.5f + 0.5f;
                    colors[t] = Color.Lerp(new Color(0.62f, 0.48f, 0.34f, 0.2f), new Color(0.93f, 0.86f, 0.72f, 0.2f), band);
                    radii[t] = 1f;
                    continue;
                }
                colors[t] = TileColor(surface.Terrain[t], surface.Elevation[t], t);
                bool sea = Terrains.IsWater(surface.Terrain[t]) || surface.Terrain[t] == TerrainType.FrozenSea;
                radii[t] = 1f + (sea ? 0f : reliefScale * 1.6f * Mathf.Max(0f, surface.Elevation[t]));
            }
            Mesh preview = PlanetMeshBuilder.Build(sphere, 1f, colors, gas ? 0f : borderWidth, borderDarken, radii);
            preview.hideFlags = HideFlags.DontSave;
            return preview;
        }

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
                case TerrainType.Canyon:
                    // Layered red-brown walls in shadow, like Valles Marineris or the Grand Canyon.
                    c = Color.Lerp(new Color32(92, 50, 36, 255), new Color32(132, 78, 52, 255), Mathf.InverseLerp(0f, 0.5f, elevation));
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

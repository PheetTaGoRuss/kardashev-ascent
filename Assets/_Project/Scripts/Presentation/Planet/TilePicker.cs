using System;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Finds the tile under the mouse, outlines it, and selects it on click (a short press without dragging,
    /// so dragging still turns the camera). Esc or clicking empty space clears the selection.
    /// </summary>
    [RequireComponent(typeof(PlanetView))]
    public sealed class TilePicker : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] Material hoverMaterial;
        [SerializeField] Material selectedMaterial;

        [Tooltip("Mouse movement (pixels) allowed between press and release for it to count as a click.")]
        [Min(0f)] [SerializeField] float clickTolerancePixels = 6f;
        [Range(0.05f, 0.6f)] [SerializeField] float hoverWidth = 0.14f;
        [Range(0.05f, 0.6f)] [SerializeField] float selectedWidth = 0.24f;
        [Tooltip("Outline height above the surface, as a fraction of the radius.")]
        [Range(0f, 0.01f)] [SerializeField] float lift = 0.0015f;

        PlanetView planet;
        Outline hover, selected;
        Vector3 pressPosition;
        bool pressed;

        public int HoveredTile { get; private set; } = -1;
        public int SelectedTile { get; private set; } = -1;

        public event Action<int> HoveredTileChanged;
        public event Action<int> SelectedTileChanged;

        void Awake()
        {
            planet = GetComponent<PlanetView>();
            hover = new Outline("Tile Hover", transform, hoverMaterial);
            selected = new Outline("Tile Selected", transform, selectedMaterial);
        }

        void OnEnable()
        {
            if (planet == null)
                planet = GetComponent<PlanetView>();
            planet.Rebuilt += OnPlanetRebuilt;
        }

        void OnDisable() => planet.Rebuilt -= OnPlanetRebuilt;

        void OnDestroy()
        {
            hover?.Destroy();
            selected?.Destroy();
        }

        void OnPlanetRebuilt()
        {
            SetHovered(-1);
            Select(-1);
        }

        void Update()
        {
            planet.EnsureBuilt();
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null || planet.Sphere == null)
                return;

            // Over an on-screen panel the mouse belongs to the panel, not the planet.
            bool overUi = ScreenInput.PointerOverUi;
            SetHovered(overUi ? -1 : TileUnder(cam, Input.mousePosition));

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                pressed = true;
                pressPosition = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(0) && pressed)
            {
                pressed = false;
                bool click = (Input.mousePosition - pressPosition).sqrMagnitude <= clickTolerancePixels * clickTolerancePixels;
                if (click && !HoldSelection)
                    Select(HoveredTile);
            }
            if (ScreenInput.KeyDown(KeyCode.Escape) && !(EscapeHandler != null && EscapeHandler()))
                Select(-1);
        }

        /// <summary>Tile under a screen position, or −1 if the ray misses the planet.</summary>
        public int TileUnder(Camera cam, Vector3 screenPosition)
        {
            if (planet.Sphere == null || !TryHitLocal(cam, screenPosition, out Vector3 hit))
                return -1;
            return planet.Sphere.FindTile(new System.Numerics.Vector3(hit.x, hit.y, hit.z),
                                          HoveredTile >= 0 ? HoveredTile : 0);
        }

        /// <summary>
        /// Where a screen position's ray meets the planet's sphere, in world space, with the outward surface normal.
        /// False if the ray misses the planet.
        /// </summary>
        public bool TryGetSurfacePoint(Camera cam, Vector3 screenPosition, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (!TryHitLocal(cam, screenPosition, out Vector3 hit))
                return false;
            point = transform.TransformPoint(hit);
            normal = transform.TransformDirection(hit).normalized;
            return true;
        }

        /// <summary>Ray from a screen position against the planet, in its local space (a sphere of radius `Radius` at the origin).</summary>
        bool TryHitLocal(Camera cam, Vector3 screenPosition, out Vector3 hit)
        {
            hit = Vector3.zero;
            Ray ray = cam.ScreenPointToRay(screenPosition);
            Vector3 origin = transform.InverseTransformPoint(ray.origin);
            Vector3 direction = (transform.InverseTransformPoint(ray.origin + ray.direction) - origin).normalized;
            float r = planet.Radius;
            float b = Vector3.Dot(origin, direction);
            float c = origin.sqrMagnitude - r * r;
            float discriminant = b * b - c;
            if (discriminant < 0f)
                return false;
            float t = -b - Mathf.Sqrt(discriminant);
            if (t < 0f)
                t = -b + Mathf.Sqrt(discriminant); // camera inside the sphere
            if (t < 0f)
                return false;
            hit = origin + direction * t;
            return true;
        }

        /// <summary>Selects a tile (−1 clears the selection).</summary>
        /// <summary>
        /// Whether the selected tile gets its thick outline. The site view turns it off: up close the band would
        /// cover the building slots.
        /// </summary>
        public bool ShowSelectedOutline
        {
            get => showSelectedOutline;
            set
            {
                showSelectedOutline = value;
                selected.Show(planet.Sphere, value ? SelectedTile : -1, planet.Radius, selectedWidth, lift * 1.5f);
            }
        }

        bool showSelectedOutline = true;

        /// <summary>
        /// While true, clicks do not change the selected tile (Esc still clears it). The site view sets it while the
        /// mouse is over one of its slots: near a tile corner the nearest tile centre can be the neighbour.
        /// </summary>
        public bool HoldSelection { get; set; }

        /// <summary>Asked first when Esc is pressed; returning true means it was handled (the tile stays selected).</summary>
        public Func<bool> EscapeHandler { get; set; }

        public void Select(int tile)
        {
            if (tile == SelectedTile)
                return;
            SelectedTile = tile;
            selected.Show(planet.Sphere, showSelectedOutline ? tile : -1, planet.Radius, selectedWidth, lift * 1.5f);
            hover.Show(planet.Sphere, HoveredTile == tile ? -1 : HoveredTile, planet.Radius, hoverWidth, lift);
            SelectedTileChanged?.Invoke(tile);
        }

        void SetHovered(int tile)
        {
            if (tile == HoveredTile)
                return;
            HoveredTile = tile;
            hover.Show(planet.Sphere, tile == SelectedTile ? -1 : tile, planet.Radius, hoverWidth, lift);
            HoveredTileChanged?.Invoke(tile);
        }

        /// <summary>Plain description of a tile: id and shape, terrain, latitude/longitude (three lines).</summary>
        public string Describe(int tile)
        {
            HexSphere sphere = planet.Sphere;
            double lat = sphere.Latitude(tile) * Mathf.Rad2Deg;
            double lon = sphere.Longitude(tile) * Mathf.Rad2Deg;
            string terrain = planet.Surface != null ? Pretty(planet.Surface.Terrain[tile]) : "?";
            string shape = sphere.IsPentagon(tile) ? "pentagon" : "hexagon";
            return $"Tile #{tile} ({shape})\n" +
                   $"{terrain}\n" +
                   $"{Math.Abs(lat):F1}° {(lat >= 0 ? "N" : "S")}, {Math.Abs(lon):F1}° {(lon >= 0 ? "E" : "W")}";
        }

        static string Pretty(TerrainType terrain) => Terrains.Name(terrain);

        /// <summary>A child object drawing one tile outline.</summary>
        sealed class Outline
        {
            readonly GameObject gameObject;
            readonly Mesh mesh;

            public Outline(string name, Transform parent, Material material)
            {
                gameObject = new GameObject(name) { hideFlags = HideFlags.DontSave };
                gameObject.transform.SetParent(parent, false);
                mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                gameObject.SetActive(false);
            }

            public void Show(HexSphere sphere, int tile, float radius, float width, float lift)
            {
                if (sphere == null || tile < 0)
                {
                    gameObject.SetActive(false);
                    return;
                }
                PlanetMeshBuilder.BuildOutline(mesh, sphere, tile, radius, width, lift);
                gameObject.SetActive(true);
            }

            public void Destroy()
            {
                UnityEngine.Object.Destroy(gameObject);
                UnityEngine.Object.Destroy(mesh);
            }
        }
    }
}

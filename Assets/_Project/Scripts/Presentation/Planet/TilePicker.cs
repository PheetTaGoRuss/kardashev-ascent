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

        [Header("Info box")]
        [SerializeField] bool showInfo = true;

        PlanetView planet;
        Outline hover, selected;
        Vector3 pressPosition;
        bool pressed;
        GUIStyle infoStyle;

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
            if (Input.GetKeyDown(KeyCode.Escape))
                Select(-1);
        }

        /// <summary>Tile under a screen position, or −1 if the ray misses the planet.</summary>
        public int TileUnder(Camera cam, Vector3 screenPosition)
        {
            if (planet.Sphere == null)
                return -1;
            Ray ray = cam.ScreenPointToRay(screenPosition);

            // Work in the planet's local space, where it is a sphere of radius `Radius` at the origin.
            Vector3 origin = transform.InverseTransformPoint(ray.origin);
            Vector3 direction = (transform.InverseTransformPoint(ray.origin + ray.direction) - origin).normalized;
            float r = planet.Radius;
            float b = Vector3.Dot(origin, direction);
            float c = origin.sqrMagnitude - r * r;
            float discriminant = b * b - c;
            if (discriminant < 0f)
                return -1;
            float t = -b - Mathf.Sqrt(discriminant);
            if (t < 0f)
                t = -b + Mathf.Sqrt(discriminant); // camera inside the sphere
            if (t < 0f)
                return -1;

            Vector3 hit = origin + direction * t;
            return planet.Sphere.FindTile(new System.Numerics.Vector3(hit.x, hit.y, hit.z),
                                          HoveredTile >= 0 ? HoveredTile : 0);
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

        void OnGUI()
        {
            if (!showInfo || planet.Sphere == null)
                return;
            infoStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                richText = true,
                padding = new RectOffset(10, 10, 8, 8),
                normal = { textColor = new Color(0.9f, 0.93f, 1f) },
            };

            string text;
            if (SelectedTile >= 0)
                text = "<b>Selected</b>\n" + Describe(SelectedTile) + "\n<size=11>Esc: clear selection</size>";
            else if (HoveredTile >= 0)
                text = Describe(HoveredTile) + "\n<size=11>Click to select</size>";
            else
                text = "Point at the planet to inspect a tile";
            GUI.Box(new Rect(12, 12, 250, SelectedTile >= 0 || HoveredTile >= 0 ? 104 : 32), text, infoStyle);
        }

        string Describe(int tile)
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

        static string Pretty(TerrainType terrain)
        {
            switch (terrain)
            {
                case TerrainType.DeepOcean: return "Deep ocean";
                case TerrainType.ShallowOcean: return "Shallow ocean";
                case TerrainType.Lowland: return "Lowland";
                case TerrainType.Highland: return "Highland";
                case TerrainType.Mountain: return "Mountain";
                default: return "Ice";
            }
        }

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

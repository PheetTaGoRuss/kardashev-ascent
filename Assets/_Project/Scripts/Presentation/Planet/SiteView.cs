using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.CameraControl;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Zoomed-in building site on one tile. Selecting a tile flies the camera down to it and lays out its
    /// building slots (<see cref="TileSite"/>) as small pads; the slot under the mouse is outlined.
    /// Selecting slots: click one; drag a box over many (Shift adds to the selection, Ctrl subtracts); Shift+click
    /// adds or removes one; Ctrl+A selects all. Esc clears the slot selection first, then flies back to the planet,
    /// as does zooming all the way out.
    /// </summary>
    [RequireComponent(typeof(PlanetView))]
    public sealed class SiteView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] TilePicker picker;
        [SerializeField] OrbitCamera orbitCamera;
        [Tooltip("Lit vertex-colour material for the slot pads (KardashevAscent/PlanetTiles).")]
        [SerializeField] Material padMaterial;
        [SerializeField] Material hoverMaterial;
        [SerializeField] Material selectedMaterial;
        [SerializeField] Color32 padColor = new Color32(150, 156, 140, 40);
        [Tooltip("Pad size as a share of the slot spacing (1 = pads touch).")]
        [Range(0.5f, 1f)] [SerializeField] float padScale = 0.86f;
        [Min(0f)] [SerializeField] float clickTolerancePixels = 6f;

        PlanetView planet;
        readonly Dictionary<int, TileSite> sites = new Dictionary<int, TileSite>();
        GameObject padsObject, hoverObject, selectedObject;
        Mesh padsMesh, hoverMesh, selectedMesh;
        Vector3 pressPosition;
        bool pressed, dragging;
        readonly HashSet<int> selection = new HashSet<int>();

        /// <summary>The site being shown, or null in the planet view.</summary>
        public TileSite Site { get; private set; }
        public int HoveredSlot { get; private set; } = -1;
        /// <summary>The selected slot when exactly one is selected, else −1.</summary>
        public int SelectedSlot { get; private set; } = -1;
        /// <summary>Every selected slot of the open site.</summary>
        public IReadOnlyCollection<int> Selection => selection;
        /// <summary>True while a selection box is being dragged.</summary>
        public bool IsDragging => dragging;
        /// <summary>The selection box in GUI coordinates (origin top-left), while dragging.</summary>
        public Rect DragRect { get; private set; }
        public float PlanetRadius => planet.Radius;

        /// <summary>A site was opened (its tile id) or closed (−1).</summary>
        public event Action<int> SiteChanged;
        public event Action<int> SelectedSlotChanged;
        /// <summary>The set of selected slots changed (one, many or none).</summary>
        public event Action SelectionChanged;

        void Awake()
        {
            planet = GetComponent<PlanetView>();
            if (picker == null)
                picker = GetComponent<TilePicker>();
            if (orbitCamera == null && Camera.main != null)
                orbitCamera = Camera.main.GetComponent<OrbitCamera>();

            padsMesh = NewMesh("Site Pads");
            hoverMesh = NewMesh("Slot Hover");
            selectedMesh = NewMesh("Slot Selected");
            padsObject = NewChild("Site Pads", padsMesh, padMaterial);
            hoverObject = NewChild("Slot Hover", hoverMesh, hoverMaterial);
            selectedObject = NewChild("Slot Selected", selectedMesh, selectedMaterial);
        }

        void OnEnable()
        {
            if (picker != null)
            {
                picker.SelectedTileChanged += OnTileSelected;
                picker.EscapeHandler = OnEscape;
            }
            if (orbitCamera != null)
                orbitCamera.SiteExitRequested += OnExitRequested;
            if (runner != null)
                runner.SimulationCreated += OnSimulationCreated;
        }

        void OnDisable()
        {
            if (picker != null)
            {
                picker.SelectedTileChanged -= OnTileSelected;
                if (picker.EscapeHandler == OnEscape)
                    picker.EscapeHandler = null;
            }
            if (orbitCamera != null)
                orbitCamera.SiteExitRequested -= OnExitRequested;
            if (runner != null)
                runner.SimulationCreated -= OnSimulationCreated;
        }

        void OnDestroy()
        {
            foreach (var go in new[] { padsObject, hoverObject, selectedObject })
                if (go != null) Destroy(go);
            foreach (var m in new[] { padsMesh, hoverMesh, selectedMesh })
                if (m != null) Destroy(m);
        }

        int Rings => runner != null && runner.Simulation != null ? runner.Simulation.SiteRings : TileSite.DefaultRings;

        /// <summary>The slot layout of any tile (cached).</summary>
        public TileSite SiteFor(int tile)
        {
            if (!sites.TryGetValue(tile, out TileSite site))
                sites[tile] = site = TileSite.Create(planet.Sphere, tile, Rings);
            return site;
        }

        /// <summary>Distance from the planet centre to the visible ground in a direction inside the site (local units).</summary>
        public float GroundRadius(Vector3 direction)
        {
            // Tiles are drawn as a fan from a centre vertex at the radius to corners at the radius, so the ground
            // dips a little between them; follow it so buildings stand on the surface.
            if (Site == null)
                return planet.Radius;
            Vector3 c = ToUnity(Site.Centre);
            Vector3[] corners = Array.ConvertAll(planet.Sphere.Corners[Site.Tile], ToUnity);
            Vector3 d = direction.normalized;
            float best = planet.Radius;
            for (int i = 0; i < corners.Length; i++)
            {
                // Plane through centre point and two corner points (all at the radius).
                Vector3 a = corners[i] * planet.Radius, b = corners[(i + 1) % corners.Length] * planet.Radius;
                Vector3 o = c * planet.Radius;
                Vector3 normal = Vector3.Cross(a - o, b - o).normalized;
                float denom = Vector3.Dot(normal, d);
                if (Mathf.Abs(denom) < 1e-6f)
                    continue;
                float t = Vector3.Dot(normal, o) / denom;
                if (t > 0f && t <= best + 1e-6f && InsideTriangle(d * t, o, a, b))
                    return t;
            }
            return best;
        }

        void OnSimulationCreated()
        {
            sites.Clear();
            Close();
        }

        /// <summary>Esc clears the slot selection before it leaves the site.</summary>
        bool OnEscape()
        {
            if (Site == null || selection.Count == 0)
                return false;
            SetSelection(Array.Empty<int>());
            return true;
        }

        void OnExitRequested()
        {
            if (picker != null)
                picker.Select(-1);
            else
                Close();
        }

        void OnTileSelected(int tile)
        {
            if (tile < 0 || planet.Sphere == null)
            {
                Close();
                return;
            }
            Site = SiteFor(tile);
            if (picker != null)
                picker.ShowSelectedOutline = false;
            BuildPads();
            SetHovered(-1);
            dragging = pressed = false;
            SetSelection(Array.Empty<int>());
            float siteRadius = (float)(Site.Spacing * (Site.Rings + 1)) * planet.Radius * transform.lossyScale.x;
            orbitCamera?.FocusSite(ToUnity(Site.Centre), siteRadius);
            SiteChanged?.Invoke(tile);
        }

        void Close()
        {
            bool wasOpen = Site != null;
            Site = null;
            SetHovered(-1);
            dragging = pressed = false;
            SetSelection(Array.Empty<int>());
            padsObject.SetActive(false);
            if (picker != null)
                picker.HoldSelection = false;
            if (picker != null)
                picker.ShowSelectedOutline = true;
            orbitCamera?.LeaveSite();
            if (wasOpen)
                SiteChanged?.Invoke(-1);
        }

        void Update()
        {
            if (Site == null)
                return;
            Camera cam = Camera.main;
            if (cam == null)
                return;

            bool overUi = ScreenInput.PointerOverUi;
            SetHovered(overUi ? -1 : SlotUnder(cam, Input.mousePosition));
            if (picker != null)
                picker.HoldSelection = HoveredSlot >= 0;

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                pressed = true;
                dragging = false;
                pressPosition = Input.mousePosition;
            }
            if (pressed && Input.GetMouseButton(0))
            {
                Vector3 now = Input.mousePosition;
                if (!dragging && (now - pressPosition).sqrMagnitude > clickTolerancePixels * clickTolerancePixels)
                    dragging = true;
                if (dragging)
                {
                    float x0 = Mathf.Min(pressPosition.x, now.x), x1 = Mathf.Max(pressPosition.x, now.x);
                    float y0 = Mathf.Min(pressPosition.y, now.y), y1 = Mathf.Max(pressPosition.y, now.y);
                    DragRect = new Rect(x0, Screen.height - y1, x1 - x0, y1 - y0);
                }
            }
            if (Input.GetMouseButtonUp(0) && pressed)
            {
                pressed = false;
                bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool subtract = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (dragging)
                {
                    dragging = false;
                    BoxSelect(cam, DragRect, add, subtract);
                }
                else if (HoveredSlot >= 0)
                {
                    if (add || subtract)
                        Toggle(HoveredSlot);
                    else
                        SetSelection(new[] { HoveredSlot });
                }
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.A))
            {
                var all = new int[Site.SlotCount];
                for (int i = 0; i < all.Length; i++)
                    all[i] = i;
                SetSelection(all);
            }
        }

        /// <summary>Slots whose centres fall inside a screen box (GUI coordinates), facing the camera.</summary>
        void BoxSelect(Camera cam, Rect guiRect, bool add, bool subtract)
        {
            var next = new HashSet<int>(add || subtract ? selection : (IEnumerable<int>)Array.Empty<int>());
            for (int s = 0; s < Site.SlotCount; s++)
            {
                System.Numerics.Vector3 d = Site.SlotDirections[s];
                Vector3 local = new Vector3(d.X, d.Y, d.Z);
                Vector3 world = transform.TransformPoint(local * GroundRadius(local));
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                    continue;
                var gui = new Vector2(screen.x, Screen.height - screen.y);
                if (!guiRect.Contains(gui))
                    continue;
                if (subtract) next.Remove(s);
                else next.Add(s);
            }
            SetSelection(next);
        }

        void Toggle(int slot)
        {
            var next = new HashSet<int>(selection);
            if (!next.Remove(slot))
                next.Add(slot);
            SetSelection(next);
        }

        int SlotUnder(Camera cam, Vector3 screenPosition)
        {
            Ray ray = cam.ScreenPointToRay(screenPosition);
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
                return -1;
            Vector3 hit = origin + direction * t;
            return Site.FindSlot(new System.Numerics.Vector3(hit.x, hit.y, hit.z));
        }

        /// <summary>Select one slot of the open site (−1 clears).</summary>
        public void SetSelected(int slot) => SetSelection(slot >= 0 ? new[] { slot } : Array.Empty<int>());

        /// <summary>Replace the selected slots.</summary>
        public void SetSelection(IEnumerable<int> slots)
        {
            var next = new HashSet<int>();
            if (Site != null)
                foreach (int s in slots)
                    if (s >= 0 && s < Site.SlotCount)
                        next.Add(s);
            if (next.SetEquals(selection))
                return;
            selection.Clear();
            selection.UnionWith(next);

            int single = selection.Count == 1 ? First(selection) : -1;
            ShowRings(selectedObject, selectedMesh, selection, 0.16f, 1.6f);
            ShowRing(hoverObject, hoverMesh, selection.Contains(HoveredSlot) ? -1 : HoveredSlot, 0.1f, 1.2f);
            if (single != SelectedSlot)
            {
                SelectedSlot = single;
                SelectedSlotChanged?.Invoke(single);
            }
            SelectionChanged?.Invoke();
        }

        static int First(HashSet<int> set)
        {
            foreach (int s in set)
                return s;
            return -1;
        }

        void SetHovered(int slot)
        {
            if (slot == HoveredSlot)
                return;
            HoveredSlot = slot;
            ShowRing(hoverObject, hoverMesh, selection.Contains(slot) ? -1 : slot, 0.1f, 1.2f);
        }

        // Pads: each slot's shape, shrunk a little so the gaps show, just above the ground. Together they cover the tile.
        void BuildPads()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            float lift = (float)Site.Spacing * planet.Radius * 0.02f;
            for (int s = 0; s < Site.SlotCount; s++)
                AddSlotShape(s, padScale, 0f, lift, vertices, normals, colors, triangles, padColor);
            padsMesh.Clear();
            padsMesh.SetVertices(vertices);
            padsMesh.SetNormals(normals);
            padsMesh.SetColors(colors);
            padsMesh.SetTriangles(triangles, 0);
            padsMesh.RecalculateBounds();
            padsObject.SetActive(true);
        }

        void ShowRing(GameObject go, Mesh mesh, int slot, float width, float liftFactor) =>
            ShowRings(go, mesh, slot >= 0 ? new[] { slot } : Array.Empty<int>(), width, liftFactor);

        void ShowRings(GameObject go, Mesh mesh, IEnumerable<int> slots, float width, float liftFactor)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            if (Site != null)
            {
                float lift = (float)Site.Spacing * planet.Radius * 0.02f * liftFactor;
                foreach (int slot in slots)
                    AddSlotShape(slot, 1f, width, lift, vertices, normals, colors, triangles, new Color32(255, 255, 255, 255));
            }
            if (vertices.Count == 0)
            {
                go.SetActive(false);
                return;
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            go.SetActive(true);
        }

        /// <summary>
        /// A slot's own shape (whole, half or corner piece), filled (ringWidth 0) or as a ring of the given width.
        /// <paramref name="scale"/> shrinks it toward its centre, leaving a gap between pads.
        /// </summary>
        void AddSlotShape(int slot, float scale, float ringWidth, float lift, List<Vector3> vertices, List<Vector3> normals,
                          List<Color32> colors, List<int> triangles, Color32 color)
        {
            System.Numerics.Vector2 centre = Site.SlotOffset(slot);
            System.Numerics.Vector2[] outline = Site.SlotOutline(slot);
            int n = outline.Length;
            Vector3 up = ToUnity(Site.SlotDirections[slot]);
            var outer = new Vector3[n];
            var inner = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                outer[k] = Point(centre, outline[k], scale, lift);
                inner[k] = Point(centre, outline[k], scale * (1f - ringWidth), lift);
            }

            int start = vertices.Count;
            if (ringWidth <= 0f)
            {
                vertices.Add(Point(centre, centre, 0f, lift));
                normals.Add(up);
                colors.Add(color);
                for (int k = 0; k < n; k++) { vertices.Add(outer[k]); normals.Add(up); colors.Add(color); }
                // Outlines run counter-clockwise from outside, wound like the planet mesh.
                for (int k = 0; k < n; k++)
                {
                    triangles.Add(start);
                    triangles.Add(start + 1 + k);
                    triangles.Add(start + 1 + (k + 1) % n);
                }
                return;
            }

            for (int k = 0; k < n; k++)
            {
                vertices.Add(outer[k]); normals.Add(up); colors.Add(color);
                vertices.Add(inner[k]); normals.Add(up); colors.Add(color);
            }
            for (int k = 0; k < n; k++)
            {
                int o0 = start + 2 * k, i0 = o0 + 1;
                int o1 = start + 2 * ((k + 1) % n), i1 = o1 + 1;
                triangles.Add(o0); triangles.Add(o1); triangles.Add(i0);
                triangles.Add(i0); triangles.Add(o1); triangles.Add(i1);
            }
        }

        Vector3 Point(System.Numerics.Vector2 centre, System.Numerics.Vector2 point, float scale, float lift)
        {
            System.Numerics.Vector2 p = centre + (point - centre) * scale;
            Vector3 d = ToUnity(Site.DirectionAt(p.X, p.Y));
            return d * (GroundRadius(d) + lift);
        }

        static bool InsideTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            return Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= -1e-9f
                && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= -1e-9f
                && Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= -1e-9f;
        }

        Mesh NewMesh(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            return mesh;
        }

        GameObject NewChild(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

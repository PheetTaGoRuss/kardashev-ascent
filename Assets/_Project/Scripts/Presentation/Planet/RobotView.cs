using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// A small rover for every robot, standing on its tile: next to the building it works on or the charger it uses,
    /// and moving across tiles while it drives. The light on its mast shows its state: green working, white idle,
    /// blue charging, amber in maintenance, red shut down or stranded.
    /// </summary>
    public sealed class RobotView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] PlanetView planet;
        [SerializeField] SiteView siteView;
        [SerializeField] Material bodyMaterial;
        [SerializeField] Material wheelMaterial;
        [Tooltip("Rover length as a share of the distance between building slots.")]
        [Range(0.1f, 1f)] [SerializeField] float sizeOfSlot = 0.45f;

        static readonly Color Working = new Color(0.35f, 0.95f, 0.45f);
        static readonly Color Idle = new Color(0.92f, 0.92f, 0.92f);
        static readonly Color Charging = new Color(0.35f, 0.6f, 1f);
        static readonly Color Repair = new Color(1f, 0.75f, 0.25f);
        static readonly Color Down = new Color(1f, 0.25f, 0.2f);

        readonly Dictionary<int, RoverVisual> rovers = new Dictionary<int, RoverVisual>();
        IPlanetSimulation shownFor;

        void OnDestroy() => Clear();

        void Clear()
        {
            foreach (RoverVisual v in rovers.Values)
                v.Destroy();
            rovers.Clear();
        }

        void LateUpdate()
        {
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim != shownFor)
            {
                Clear();
                shownFor = sim;
            }
            if (sim == null || planet == null || planet.Sphere == null || siteView == null || bodyMaterial == null)
                return;

            var seen = new HashSet<int>();
            var onTile = new Dictionary<int, int>();
            foreach (Robot r in sim.Workforce.Robots)
            {
                seen.Add(r.Id);
                if (!rovers.TryGetValue(r.Id, out RoverVisual rover))
                    rovers[r.Id] = rover = new RoverVisual($"Robot {r.Name}", transform, bodyMaterial, wheelMaterial != null ? wheelMaterial : bodyMaterial);

                TileSite site = siteView.SiteFor(r.Tile);
                if (site == null)
                {
                    rover.Hide();
                    continue;
                }
                int index = onTile.TryGetValue(r.Tile, out int n) ? n : 0;
                onTile[r.Tile] = index + 1;
                Vector3 here = Spot(sim, r, site, index);
                Vector3 direction = here, facing = Vector3.zero;
                if (r.IsTravelling && r.NextTile.HasValue)
                {
                    Vector3 next = ToUnity(planet.Sphere.Centers[r.NextTile.Value]).normalized;
                    direction = Vector3.Slerp(here, next, (float)r.HopFraction).normalized;
                    facing = next - here;
                }
                float size = (float)site.Spacing * planet.Radius * sizeOfSlot;
                // At work on a site: a small hop, like tools going (construction animation).
                bool building = r.Task.HasValue && r.Task.Value.Kind == TaskKind.Build && !r.IsTravelling
                                && r.Tile == r.Task.Value.Tile && r.Activity == RobotActivity.None;
                float hop = building ? size * 0.08f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f + r.Id)) : 0f;
                rover.Show(direction, siteView.GroundRadius(site, direction) + hop, size, facing);
                rover.SetLight(Light(r));
            }

            var gone = new List<int>();
            foreach (int id in rovers.Keys)
                if (!seen.Contains(id)) gone.Add(id);
            foreach (int id in gone)
            {
                rovers[id].Destroy();
                rovers.Remove(id);
            }
        }

        /// <summary>Where a robot stands on its tile: beside its building or charger, else around the tile's centre.</summary>
        Vector3 Spot(IPlanetSimulation sim, Robot r, TileSite site, int index)
        {
            Vector3 centre = ToUnity(site.Centre).normalized;
            int slot = -1;
            if (r.Facility.HasValue && r.Facility.Value.Tile == r.Tile)
                slot = r.Facility.Value.Slot;
            else if (r.Task.HasValue && r.Task.Value.Tile == r.Tile
                     && (r.Task.Value.Kind == TaskKind.Build || r.Task.Value.Kind == TaskKind.Salvage || r.Task.Value.Kind == TaskKind.Manufacture))
                slot = r.Task.Value.Slot;
            if (slot >= 0 && slot < site.SlotDirections.Length)
            {
                Vector3 s = ToUnity(site.SlotDirections[slot]).normalized;
                return Vector3.Lerp(s, centre, 0.3f).normalized;   // just in front of it, towards the centre
            }
            // Around the centre (the lander), each robot in its own place.
            Vector3 east = Vector3.Cross(Vector3.up, centre);
            if (east.sqrMagnitude < 1e-6f) east = Vector3.Cross(Vector3.right, centre);
            east.Normalize();
            Vector3 north = Vector3.Cross(centre, east);
            float angle = index * 2.4f;   // golden-angle steps
            float ring = (float)site.Spacing * (0.55f + 0.25f * (index / 6));
            return (centre + (east * Mathf.Cos(angle) + north * Mathf.Sin(angle)) * ring).normalized;
        }

        static Color Light(Robot r)
        {
            switch (r.State)
            {
                case RobotState.Working: return Working;
                case RobotState.Charging: return Charging;
                case RobotState.Maintenance: return Repair;
                case RobotState.Shutdown: return Down;
                default: return Idle;
            }
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }

    /// <summary>A rover built from primitives: body, four wheels, a mast with a status light. No colliders (clicks go to tiles).</summary>
    public sealed class RoverVisual
    {
        readonly GameObject root;
        readonly Renderer light;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        Color shownLight = Color.clear;
        Vector3 lastForward = Vector3.zero;

        public RoverVisual(string name, Transform planet, Material body, Material wheels)
        {
            root = new GameObject(name) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(planet, false);
            Part(PrimitiveType.Cube, body, new Vector3(0f, 0.22f, 0f), new Vector3(0.55f, 0.18f, 0.85f), Quaternion.identity);
            Part(PrimitiveType.Cube, body, new Vector3(0f, 0.34f, -0.15f), new Vector3(0.45f, 0.06f, 0.4f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.33f : 0.33f, z = i < 2 ? 0.3f : -0.3f;
                Part(PrimitiveType.Cylinder, wheels, new Vector3(x, 0.12f, z), new Vector3(0.24f, 0.05f, 0.24f), Quaternion.Euler(0f, 0f, 90f));
            }
            Part(PrimitiveType.Cylinder, wheels, new Vector3(0f, 0.5f, 0.25f), new Vector3(0.05f, 0.18f, 0.05f), Quaternion.identity);
            light = Part(PrimitiveType.Cube, body, new Vector3(0f, 0.72f, 0.25f), new Vector3(0.18f, 0.1f, 0.12f), Quaternion.identity);
            root.SetActive(false);
        }

        Renderer Part(PrimitiveType type, Material material, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = type.ToString();
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.transform.localRotation = rotation;
            Renderer r = part.GetComponent<Renderer>();
            r.sharedMaterial = material;
            return r;
        }

        /// <summary>Stand on the ground at <paramref name="direction"/>, <paramref name="size"/> long, nose along <paramref name="forward"/> if given.</summary>
        public void Show(Vector3 direction, float groundRadius, float size, Vector3 forward)
        {
            Vector3 up = direction.normalized;
            Vector3 f = Vector3.ProjectOnPlane(forward, up);
            if (f.sqrMagnitude < 1e-10f) f = Vector3.ProjectOnPlane(lastForward, up);
            if (f.sqrMagnitude < 1e-10f) f = Vector3.ProjectOnPlane(Vector3.up, up);
            if (f.sqrMagnitude < 1e-10f) f = Vector3.ProjectOnPlane(Vector3.forward, up);
            lastForward = f.normalized;
            root.transform.localPosition = up * groundRadius;
            root.transform.localRotation = Quaternion.LookRotation(lastForward, up);
            root.transform.localScale = Vector3.one * size;
            if (!root.activeSelf) root.SetActive(true);
        }

        public void SetLight(Color c)
        {
            if (c == shownLight) return;
            shownLight = c;
            block.SetColor("_Color", c);
            block.SetColor("_EmissionColor", c * 0.8f);
            light.SetPropertyBlock(block);
        }

        public void Hide() => root.SetActive(false);

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
        }
    }
}

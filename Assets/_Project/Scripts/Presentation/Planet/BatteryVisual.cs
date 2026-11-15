using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// A block building (battery yard, Workshop, charger, relay, Research Station): one module on each slot it covers,
    /// a row of three containers on a low pad, lined up with the slot's north. Each module fits inside its own hex slot
    /// (director 2026-11-15: buildings fit the tile and don't overlap), so a 2-slot yard is two blocks side by side.
    /// Same interface as <see cref="SolarPanelVisual"/> so the placer can treat both alike.
    /// </summary>
    public sealed class BatteryVisual
    {
        readonly string name;
        readonly Transform planet;
        readonly Material body, top;
        readonly List<GameObject> modules = new List<GameObject>();
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block;
        float shownSize = 1f;
        float growth = 1f;
        readonly FootprintLinks links;
        readonly List<Vector3> linkPositions = new List<Vector3>();
        readonly List<Vector3> linkUps = new List<Vector3>();

        public BatteryVisual(string name, Transform planet, Material body, Material top)
        {
            this.name = name;
            this.planet = planet;
            this.body = body;
            this.top = top;
            links = new FootprintLinks(name, planet, body, top, withDuct: true);
            AddModule();
        }

        GameObject AddModule()
        {
            var root = new GameObject(modules.Count == 0 ? name : $"{name} ({modules.Count + 1})") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(planet, false);
            Part(root, "Pad", body, new Vector3(0f, 0.01f, 0f), new Vector3(0.9f, 0.02f, 0.75f));
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.28f;
                Part(root, "Container", body, new Vector3(x, 0.1f, 0f), new Vector3(0.22f, 0.16f, 0.6f));
                Part(root, "Vents", top, new Vector3(x, 0.185f, 0f), new Vector3(0.18f, 0.012f, 0.52f));
            }
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                renderers.Add(r);
                if (block != null) r.SetPropertyBlock(block);
            }
            root.SetActive(false);
            modules.Add(root);
            return root;
        }

        /// <summary>Stand one module on the ground at <paramref name="direction"/>, <paramref name="size"/> across.</summary>
        public void Show(Vector3 direction, float groundRadius, float size) =>
            Show(new[] { direction }, new[] { groundRadius }, size);

        /// <summary>One module on each slot: directions from the planet's centre and their ground radii; each <paramref name="size"/> across.</summary>
        public void Show(IReadOnlyList<Vector3> directions, IReadOnlyList<float> groundRadii, float size)
        {
            while (modules.Count < directions.Count)
                AddModule();
            shownSize = size;
            growth = 1f;   // shown whole; the construction animation sets its growth again each frame
            for (int i = 0; i < modules.Count; i++)
            {
                GameObject m = modules[i];
                if (i >= directions.Count)
                {
                    m.SetActive(false);
                    continue;
                }
                var d = new System.Numerics.Vector3(directions[i].x, directions[i].y, directions[i].z);
                double lat = PlanetFrame.Latitude(d), lon = PlanetFrame.Longitude(d);
                Vector3 up = ToUnity(PlanetFrame.FromLocal(new Simulation.Solar.Double3(0, 0, 1), lat, lon));
                Vector3 north = ToUnity(PlanetFrame.FromLocal(new Simulation.Solar.Double3(0, 1, 0), lat, lon));
                m.transform.localPosition = up * groundRadii[i];
                m.transform.localRotation = Quaternion.LookRotation(north, up);
                m.transform.localScale = new Vector3(size, size * growth, size);
                m.SetActive(true);
            }
            // One building across its slots: decks and a duct between the modules.
            linkPositions.Clear();
            linkUps.Clear();
            for (int i = 0; i < directions.Count && i < modules.Count; i++)
            {
                linkPositions.Add(modules[i].transform.localPosition);
                linkUps.Add(modules[i].transform.localRotation * Vector3.up);
            }
            links.Show(linkPositions, linkUps, size, growth);
        }

        public void Hide()
        {
            foreach (GameObject m in modules)
                m.SetActive(false);
            links.Hide();
        }

        /// <summary>
        /// Height as a share of the full building (construction animation: a site rises as it is built; above 1 for the
        /// short pop when it is finished). Widths stay as they are.
        /// </summary>
        public void SetGrowth(float g)
        {
            growth = Mathf.Max(0.02f, g);
            foreach (GameObject m in modules)
                m.transform.localScale = new Vector3(shownSize, shownSize * growth, shownSize);
            if (linkPositions.Count > 1)
                links.Show(linkPositions, linkUps, shownSize, growth);
        }

        public void SetTint(Color color)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            foreach (Renderer r in renderers)
                r.SetPropertyBlock(block);
            links.SetTint(color);
        }

        public void Destroy()
        {
            links.Destroy();
            foreach (GameObject m in modules)
                if (m != null)
                    Object.Destroy(m);
            modules.Clear();
            renderers.Clear();
        }

        static void Part(GameObject root, string name, Material material, Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

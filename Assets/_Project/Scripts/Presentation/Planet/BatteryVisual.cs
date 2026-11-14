using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// A battery yard on one slot: a row of three grid-scale battery containers on a low concrete pad, lined up with
    /// the slot's north. Same interface as <see cref="SolarPanelVisual"/> so the placer can treat both alike.
    /// </summary>
    public sealed class BatteryVisual
    {
        readonly GameObject root;
        readonly Renderer[] renderers;
        MaterialPropertyBlock block;

        public BatteryVisual(string name, Transform planet, Material body, Material top)
        {
            root = new GameObject(name) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(planet, false);

            Part("Pad", body, new Vector3(0f, 0.01f, 0f), new Vector3(0.9f, 0.02f, 0.75f));
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.28f;
                Part("Container", body, new Vector3(x, 0.1f, 0f), new Vector3(0.22f, 0.16f, 0.6f));
                Part("Vents", top, new Vector3(x, 0.185f, 0f), new Vector3(0.18f, 0.012f, 0.52f));
            }
            renderers = root.GetComponentsInChildren<Renderer>();
            root.SetActive(false);
        }

        /// <summary>Stand the yard on the ground at <paramref name="direction"/>, <paramref name="size"/> across.</summary>
        public void Show(Vector3 direction, float groundRadius, float size)
        {
            var d = new System.Numerics.Vector3(direction.x, direction.y, direction.z);
            double lat = PlanetFrame.Latitude(d), lon = PlanetFrame.Longitude(d);
            Vector3 up = ToUnity(PlanetFrame.FromLocal(new Simulation.Solar.Double3(0, 0, 1), lat, lon));
            Vector3 north = ToUnity(PlanetFrame.FromLocal(new Simulation.Solar.Double3(0, 1, 0), lat, lon));
            root.transform.localPosition = up * groundRadius;
            root.transform.localRotation = Quaternion.LookRotation(north, up);
            root.transform.localScale = Vector3.one * size;
            shownSize = size;
            root.SetActive(true);
        }

        public void Hide() => root.SetActive(false);

        float shownSize = 1f;

        /// <summary>
        /// Height as a share of the full building (construction animation: a site rises as it is built; above 1 for the
        /// short pop when it is finished). Widths stay as they are.
        /// </summary>
        public void SetGrowth(float growth)
        {
            float g = Mathf.Max(0.02f, growth);
            root.transform.localScale = new Vector3(shownSize, shownSize * g, shownSize);
        }

        public void SetTint(Color color)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            foreach (Renderer r in renderers)
                r.SetPropertyBlock(block);
        }

        public void Destroy()
        {
            if (root != null)
                Object.Destroy(root);
        }

        void Part(string name, Material material, Vector3 position, Vector3 scale)
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

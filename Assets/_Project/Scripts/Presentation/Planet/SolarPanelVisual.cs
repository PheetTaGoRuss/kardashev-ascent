using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// A low-poly solar panel standing on one tile: a post and a framed slab whose face points along the
    /// panel normal the simulation uses (<see cref="PanelModel.Normal"/> in the tile's east/north/up frame).
    /// Built from cube primitives under the planet, so it turns with the planet.
    /// </summary>
    public sealed class SolarPanelVisual
    {
        readonly GameObject root;
        readonly Transform post, pivot;
        readonly Renderer[] renderers;
        MaterialPropertyBlock block;

        public SolarPanelVisual(string name, Transform planet, Material frame, Material cell)
        {
            root = new GameObject(name) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(planet, false);

            post = Part("Post", root.transform, frame).transform;
            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);
            Transform slab = Part("Frame", pivot, frame).transform;
            slab.localPosition = new Vector3(0f, 0.02f, 0f);
            slab.localScale = new Vector3(1f, 0.04f, 0.66f);
            Transform face = Part("Cells", pivot, cell).transform;
            face.localPosition = new Vector3(0f, 0.045f, 0f);
            face.localScale = new Vector3(0.92f, 0.02f, 0.58f);

            renderers = root.GetComponentsInChildren<Renderer>();
            root.SetActive(false);
        }

        public bool Visible => root.activeSelf;

        /// <summary>Stand the panel on a tile with tilt β and azimuth γ (radians, γ clockwise from north).</summary>
        public void Show(HexSphere sphere, int tile, float planetRadius, double tilt, double azimuth)
        {
            if (sphere == null || tile < 0)
            {
                Hide();
                return;
            }
            double lat = sphere.Latitude(tile), lon = sphere.Longitude(tile);
            Vector3 up = ToUnity(PlanetFrame.FromLocal(new Double3(0, 0, 1), lat, lon));
            Vector3 north = ToUnity(PlanetFrame.FromLocal(new Double3(0, 1, 0), lat, lon));
            Vector3 normal = ToUnity(PlanetFrame.FromLocal(PanelModel.Normal(tilt, azimuth), lat, lon));
            // In the panel's plane, pointing toward where it faces (well defined even when flat).
            Vector3 along = ToUnity(PlanetFrame.FromLocal(PanelModel.Normal(tilt + 0.5 * System.Math.PI, azimuth), lat, lon));

            // Size from the tile: a hexagon's circumradius is about 0.6 √area on the unit sphere.
            float size = 0.6f * Mathf.Sqrt(4f * Mathf.PI / sphere.TileCount) * planetRadius;
            float height = 0.45f * size;

            Quaternion tileRotation = Quaternion.LookRotation(north, up);
            root.transform.localPosition = up * planetRadius;
            root.transform.localRotation = tileRotation;
            root.transform.localScale = Vector3.one * size;

            post.localPosition = new Vector3(0f, height / size * 0.5f, 0f);
            post.localScale = new Vector3(0.07f, height / size, 0.07f);
            pivot.localPosition = new Vector3(0f, height / size, 0f);
            pivot.localRotation = Quaternion.Inverse(tileRotation) * Quaternion.LookRotation(along, normal);
            root.SetActive(true);
        }

        public void Hide() => root.SetActive(false);

        /// <summary>Tint every part (used for the see-through placement ghost).</summary>
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

        static GameObject Part(string name, Transform parent, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// A low-poly solar installation: one panel table on each slot it covers, a post and a framed slab whose face points
    /// along the panel normal the simulation uses (<see cref="PanelModel.Normal"/> in each slot's east/north/up frame).
    /// Each table fits inside its own hex slot (director 2026-11-15: buildings fit the tile and don't overlap), so a
    /// bigger field is a row of tables, not one giant slab. Built from cube primitives under the planet.
    /// </summary>
    public sealed class SolarPanelVisual
    {
        sealed class Table
        {
            public GameObject Root;
            public Transform Post, Pivot;
        }

        readonly string name;
        readonly Transform planet;
        readonly Material frame, cell;
        readonly List<Table> tables = new List<Table>();
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block, cellBlock;
        float shownSize = 1f;
        float growth = 1f;
        readonly FootprintLinks links;
        readonly List<Vector3> linkPositions = new List<Vector3>();
        readonly List<Vector3> linkUps = new List<Vector3>();

        public SolarPanelVisual(string name, Transform planet, Material frame, Material cell)
        {
            this.name = name;
            this.planet = planet;
            this.frame = frame;
            this.cell = cell;
            links = new FootprintLinks(name, planet, frame, frame, withDuct: false);
            AddTable();
        }

        Table AddTable()
        {
            var t = new Table();
            t.Root = new GameObject(tables.Count == 0 ? name : $"{name} ({tables.Count + 1})") { hideFlags = HideFlags.DontSave };
            t.Root.transform.SetParent(planet, false);
            t.Post = Part("Post", t.Root.transform, frame).transform;
            t.Pivot = new GameObject("Pivot").transform;
            t.Pivot.SetParent(t.Root.transform, false);
            Transform slab = Part("Frame", t.Pivot, frame).transform;
            slab.localPosition = new Vector3(0f, 0.02f, 0f);
            slab.localScale = new Vector3(1f, 0.04f, 0.66f);
            Transform face = Part("Cells", t.Pivot, cell).transform;
            face.localPosition = new Vector3(0f, 0.045f, 0f);
            face.localScale = new Vector3(0.92f, 0.02f, 0.58f);
            foreach (Renderer r in t.Root.GetComponentsInChildren<Renderer>())
            {
                renderers.Add(r);
                if (block != null) r.SetPropertyBlock(block);
                else if (cellBlock != null && r.gameObject.name == "Cells") r.SetPropertyBlock(cellBlock);
            }
            t.Root.SetActive(false);
            tables.Add(t);
            return t;
        }

        public bool Visible => tables.Count > 0 && tables[0].Root.activeSelf;

        /// <summary>One table on the ground at <paramref name="direction"/>, <paramref name="size"/> wide, with tilt β and azimuth γ (radians).</summary>
        public void Show(Vector3 direction, float groundRadius, float size, double tilt, double azimuth) =>
            Show(new[] { direction }, new[] { groundRadius }, size, tilt, azimuth);

        /// <summary>
        /// A table on each slot (directions from the planet's centre, their ground radii), each <paramref name="size"/>
        /// wide, all at tilt β and azimuth γ (radians, γ clockwise from north) in their own east/north/up frame.
        /// </summary>
        public void Show(IReadOnlyList<Vector3> directions, IReadOnlyList<float> groundRadii, float size, double tilt, double azimuth)
        {
            while (tables.Count < directions.Count)
                AddTable();
            shownSize = size;
            growth = 1f;   // shown whole; the construction animation sets its growth again each frame
            for (int i = 0; i < tables.Count; i++)
            {
                Table t = tables[i];
                if (i >= directions.Count)
                {
                    t.Root.SetActive(false);
                    continue;
                }
                var d = new System.Numerics.Vector3(directions[i].x, directions[i].y, directions[i].z);
                double lat = PlanetFrame.Latitude(d), lon = PlanetFrame.Longitude(d);
                Vector3 up = ToUnity(PlanetFrame.FromLocal(new Double3(0, 0, 1), lat, lon));
                Vector3 north = ToUnity(PlanetFrame.FromLocal(new Double3(0, 1, 0), lat, lon));
                Vector3 normal = ToUnity(PlanetFrame.FromLocal(PanelModel.Normal(tilt, azimuth), lat, lon));
                // In the panel's plane, pointing toward where it faces (well defined even when flat).
                Vector3 along = ToUnity(PlanetFrame.FromLocal(PanelModel.Normal(tilt + 0.5 * System.Math.PI, azimuth), lat, lon));
                float height = 0.45f * size;

                Quaternion tileRotation = Quaternion.LookRotation(north, up);
                t.Root.transform.localPosition = up * groundRadii[i];
                t.Root.transform.localRotation = tileRotation;
                t.Root.transform.localScale = new Vector3(size, size * growth, size);
                t.Post.localPosition = new Vector3(0f, height / size * 0.5f, 0f);
                t.Post.localScale = new Vector3(0.07f, height / size, 0.07f);
                t.Pivot.localPosition = new Vector3(0f, height / size, 0f);
                t.Pivot.localRotation = Quaternion.Inverse(tileRotation) * Quaternion.LookRotation(along, normal);
                t.Root.SetActive(true);
            }
            // One field across its slots: a service deck between the tables.
            linkPositions.Clear();
            linkUps.Clear();
            for (int i = 0; i < directions.Count && i < tables.Count; i++)
            {
                linkPositions.Add(tables[i].Root.transform.localPosition);
                linkUps.Add(tables[i].Root.transform.localRotation * Vector3.up);
            }
            links.Show(linkPositions, linkUps, size, growth);
        }

        public void Hide()
        {
            foreach (Table t in tables)
                t.Root.SetActive(false);
            links.Hide();
        }

        /// <summary>
        /// Height as a share of the full building (construction animation: a site rises as it is built; above 1 for the
        /// short pop when it is finished). Widths stay as they are.
        /// </summary>
        public void SetGrowth(float g)
        {
            growth = Mathf.Max(0.02f, g);
            foreach (Table t in tables)
                t.Root.transform.localScale = new Vector3(shownSize, shownSize * growth, shownSize);
        }

        /// <summary>Tint every part (used for the see-through placement ghost).</summary>
        public void SetTint(Color color)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            foreach (Renderer r in renderers)
                r.SetPropertyBlock(block);
            links.SetTint(color);
        }

        /// <summary>Colour only the cells (bigger installations: farms, parks, arrays get their own colour).</summary>
        public void SetCellTint(Color color)
        {
            cellBlock = new MaterialPropertyBlock();
            cellBlock.SetColor("_Color", color);
            foreach (Renderer r in renderers)
                if (r.gameObject.name == "Cells")
                    r.SetPropertyBlock(cellBlock);
        }

        public void Destroy()
        {
            links.Destroy();
            foreach (Table t in tables)
                if (t.Root != null)
                    Object.Destroy(t.Root);
            tables.Clear();
            renderers.Clear();
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

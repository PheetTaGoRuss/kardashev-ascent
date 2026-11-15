using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Pictures for the guide book (director 2026-11-15), drawn in code: a material as a faceted ore chunk (water as a
    /// drop), a ground as a hex tile in the planet's colours with its features (trees, peaks, lava mounds, dunes, waves,
    /// snow). No image files.
    /// </summary>
    public sealed class GuidePicture : VisualElement
    {
        readonly Material? material;
        readonly TerrainType? ground;

        public GuidePicture(Material m, float size) : this(size) { material = m; }
        public GuidePicture(TerrainType t, float size) : this(size) { ground = t; }

        GuidePicture(float size)
        {
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>Ground colours, the same base colours the planet uses (PlanetView defaults).</summary>
        public static Color GroundColour(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.DeepOcean: return new Color(0.05f, 0.16f, 0.36f);
                case TerrainType.ShallowOcean: return new Color(0.10f, 0.36f, 0.58f);
                case TerrainType.Lowland: return new Color(0.30f, 0.52f, 0.24f);
                case TerrainType.Highland: return new Color(0.55f, 0.50f, 0.32f);
                case TerrainType.Mountain: return new Color(0.45f, 0.42f, 0.40f);
                case TerrainType.Ice: return new Color(0.90f, 0.94f, 0.97f);
                case TerrainType.FrozenSea: return new Color(0.66f, 0.80f, 0.90f);
                case TerrainType.Tundra: return new Color(0.47f, 0.47f, 0.42f);
                case TerrainType.Desert: return new Color(0.78f, 0.62f, 0.38f);
                case TerrainType.DryBasin: return new Color(0.55f, 0.35f, 0.25f);
                case TerrainType.Forest: return new Color32(52, 107, 56, 255);
                case TerrainType.Wetland: return new Color32(64, 118, 98, 255);
                case TerrainType.Beach: return new Color32(222, 205, 150, 255);
                case TerrainType.Canyon: return new Color32(132, 78, 52, 255);
                default: return new Color32(80, 62, 60, 255);   // volcanic rock
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            Painter2D p = ctx.painter2D;
            if (material.HasValue) DrawMaterial(p, r, material.Value);
            else if (ground.HasValue) DrawGround(p, r, ground.Value);
        }

        // ---- materials ----

        static void DrawMaterial(Painter2D p, Rect r, Material m)
        {
            Color c = MaterialChips.ColourOf(m);
            Vector2 o = r.center;
            float s = Mathf.Min(r.width, r.height) * 0.42f;
            if (m == Material.Water)
            {
                // A drop.
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(o + new Vector2(0f, -s));
                p.BezierCurveTo(o + new Vector2(s * 0.9f, -s * 0.1f), o + new Vector2(s * 0.8f, s * 0.9f), o + new Vector2(0f, s * 0.9f));
                p.BezierCurveTo(o + new Vector2(-s * 0.8f, s * 0.9f), o + new Vector2(-s * 0.9f, -s * 0.1f), o + new Vector2(0f, -s));
                p.Fill();
                p.fillColor = new Color(1f, 1f, 1f, 0.45f);
                p.BeginPath();
                p.Arc(o + new Vector2(-s * 0.28f, s * 0.2f), s * 0.16f, 0f, 360f);
                p.Fill();
                return;
            }
            // A faceted chunk: an irregular outline, a light top face and a dark side.
            Vector2[] outline =
            {
                new Vector2(-0.95f, 0.25f), new Vector2(-0.55f, -0.55f), new Vector2(0.10f, -0.85f), new Vector2(0.80f, -0.45f),
                new Vector2(0.95f, 0.30f), new Vector2(0.35f, 0.85f), new Vector2(-0.50f, 0.80f),
            };
            Polygon(p, o, s, outline, c * 0.72f);
            Polygon(p, o, s, new[] { new Vector2(-0.55f, -0.55f), new Vector2(0.10f, -0.85f), new Vector2(0.80f, -0.45f), new Vector2(0.15f, -0.05f), new Vector2(-0.40f, 0.05f) },
                    Color.Lerp(c, Color.white, 0.30f));
            Polygon(p, o, s, new[] { new Vector2(0.15f, -0.05f), new Vector2(0.80f, -0.45f), new Vector2(0.95f, 0.30f), new Vector2(0.35f, 0.85f) },
                    c * 0.55f);
            if (m == Material.Copper || m == Material.Iron || m == Material.Aluminium || m == Material.Lithium || m == Material.RareEarth
                || m == Material.PureAluminium)
            {
                // A metallic glint.
                p.fillColor = new Color(1f, 1f, 1f, 0.7f);
                p.BeginPath();
                p.Arc(o + new Vector2(-0.10f, -0.45f) * s, s * 0.08f, 0f, 360f);
                p.Fill();
            }
        }

        // ---- ground ----

        static void DrawGround(Painter2D p, Rect r, TerrainType t)
        {
            Vector2 o = r.center;
            float s = Mathf.Min(r.width, r.height) * 0.46f;
            Color c = GroundColour(t);
            var hex = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (60f * i - 30f);
                hex[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.62f);   // a little foreshortened, like seen at an angle
            }
            // The tile's edge, then its top.
            Polygon(p, o + new Vector2(0f, s * 0.12f), s, hex, c * 0.6f);
            Polygon(p, o, s, hex, c);

            switch (t)
            {
                case TerrainType.Forest:
                    foreach (Vector2 at in new[] { new Vector2(-0.45f, 0.05f), new Vector2(0.05f, -0.15f), new Vector2(0.45f, 0.1f), new Vector2(-0.1f, 0.3f) })
                        Triangle(p, o + at * s, s * 0.22f, s * 0.42f, new Color32(34, 78, 38, 255));
                    break;
                case TerrainType.Mountain:
                    Triangle(p, o + new Vector2(-0.15f, 0.15f) * s, s * 0.55f, s * 0.85f, new Color32(118, 110, 102, 255));
                    Triangle(p, o + new Vector2(-0.15f, -0.48f) * s, s * 0.18f, s * 0.22f, new Color32(232, 238, 244, 255));
                    break;
                case TerrainType.Highland:
                    Triangle(p, o + new Vector2(-0.3f, 0.15f) * s, s * 0.35f, s * 0.35f, c * 0.8f);
                    Triangle(p, o + new Vector2(0.3f, 0.1f) * s, s * 0.3f, s * 0.28f, c * 0.85f);
                    break;
                case TerrainType.VolcanicRock:
                    foreach (Vector2 at in new[] { new Vector2(-0.4f, 0.1f), new Vector2(0.2f, -0.05f), new Vector2(0.4f, 0.25f) })
                        Triangle(p, o + at * s, s * 0.22f, s * 0.15f, new Color32(62, 50, 50, 255));
                    p.fillColor = new Color(1f, 0.45f, 0.15f, 0.9f);   // a glowing crack
                    p.BeginPath(); p.Arc(o + new Vector2(-0.05f, 0.2f) * s, s * 0.05f, 0f, 360f); p.Fill();
                    break;
                case TerrainType.Canyon:
                    // A gorge: a dark winding cut across the tile, with layered walls.
                    p.strokeColor = new Color32(52, 28, 20, 255); p.lineWidth = s * 0.28f;
                    p.BeginPath();
                    p.MoveTo(o + new Vector2(-0.75f, -0.1f) * s);
                    p.BezierCurveTo(o + new Vector2(-0.3f, 0.25f) * s, o + new Vector2(0.2f, -0.3f) * s, o + new Vector2(0.75f, 0.1f) * s);
                    p.Stroke();
                    p.strokeColor = new Color32(176, 110, 72, 255); p.lineWidth = 1.5f;
                    p.BeginPath();
                    p.MoveTo(o + new Vector2(-0.7f, -0.28f) * s);
                    p.BezierCurveTo(o + new Vector2(-0.3f, 0.05f) * s, o + new Vector2(0.2f, -0.48f) * s, o + new Vector2(0.7f, -0.08f) * s);
                    p.Stroke();
                    break;
                case TerrainType.Desert:
                case TerrainType.DryBasin:
                case TerrainType.Beach:
                    p.strokeColor = c * 0.8f; p.lineWidth = 2f;
                    for (int k = -1; k <= 1; k++)
                    {
                        p.BeginPath();
                        p.MoveTo(o + new Vector2(-0.55f, k * 0.18f) * s);
                        p.BezierCurveTo(o + new Vector2(-0.2f, k * 0.18f - 0.12f) * s, o + new Vector2(0.15f, k * 0.18f + 0.12f) * s, o + new Vector2(0.55f, k * 0.18f) * s);
                        p.Stroke();
                    }
                    break;
                case TerrainType.DeepOcean:
                case TerrainType.ShallowOcean:
                case TerrainType.Wetland:
                    p.strokeColor = new Color(1f, 1f, 1f, 0.45f); p.lineWidth = 2f;
                    for (int k = -1; k <= 1; k++)
                    {
                        p.BeginPath();
                        p.MoveTo(o + new Vector2(-0.45f, k * 0.2f) * s);
                        p.BezierCurveTo(o + new Vector2(-0.2f, k * 0.2f - 0.1f) * s, o + new Vector2(0f, k * 0.2f + 0.1f) * s, o + new Vector2(0.25f, k * 0.2f) * s);
                        p.Stroke();
                    }
                    break;
                case TerrainType.Ice:
                case TerrainType.FrozenSea:
                case TerrainType.Tundra:
                    p.strokeColor = new Color(0.55f, 0.65f, 0.75f, 0.8f); p.lineWidth = 1.5f;
                    p.BeginPath(); p.MoveTo(o + new Vector2(-0.4f, -0.1f) * s); p.LineTo(o + new Vector2(0.05f, 0.1f) * s); p.LineTo(o + new Vector2(0.35f, -0.15f) * s); p.Stroke();
                    if (t == TerrainType.Tundra)
                        foreach (Vector2 at in new[] { new Vector2(-0.3f, 0.2f), new Vector2(0.3f, 0.15f) })
                        { p.fillColor = new Color(0.92f, 0.95f, 0.98f); p.BeginPath(); p.Arc(o + at * s, s * 0.08f, 0f, 360f); p.Fill(); }
                    break;
                default:   // lowland: a few grass tufts
                    p.strokeColor = new Color32(70, 120, 55, 255); p.lineWidth = 2f;
                    foreach (Vector2 at in new[] { new Vector2(-0.35f, 0.1f), new Vector2(0.1f, -0.1f), new Vector2(0.35f, 0.15f) })
                    {
                        p.BeginPath(); p.MoveTo(o + at * s); p.LineTo(o + (at + new Vector2(-0.05f, -0.15f)) * s); p.Stroke();
                        p.BeginPath(); p.MoveTo(o + at * s); p.LineTo(o + (at + new Vector2(0.05f, -0.15f)) * s); p.Stroke();
                    }
                    break;
            }
        }

        static void Polygon(Painter2D p, Vector2 o, float s, Vector2[] points, Color fill)
        {
            fill.a = 1f;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(o + points[0] * s);
            for (int i = 1; i < points.Length; i++)
                p.LineTo(o + points[i] * s);
            p.ClosePath();
            p.Fill();
        }

        static void Triangle(Painter2D p, Vector2 baseCentre, float halfWidth, float height, Color fill)
        {
            fill.a = 1f;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(baseCentre + new Vector2(-halfWidth, 0f));
            p.LineTo(baseCentre + new Vector2(halfWidth, 0f));
            p.LineTo(baseCentre + new Vector2(0f, -height));
            p.ClosePath();
            p.Fill();
        }
    }
}

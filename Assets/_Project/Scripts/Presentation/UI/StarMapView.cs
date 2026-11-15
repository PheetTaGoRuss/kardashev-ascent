using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The star map of the planet chooser (director 2026-11-15: a galactic war-table to pick a star from, with a
    /// preview). The Sun sits in the middle; every star with a playable world is drawn where it really is, seen from
    /// above the Milky Way's disc: its direction is the galactic longitude from the archive's sky position (up is
    /// towards the galactic centre) and its distance from the Sun runs on a log scale (rings at 1, 10, 100 and 1 000
    /// parsecs), because Proxima is 1.3 pc away and Kepler-452 550. A schematic Milky Way in the corner shows where
    /// this patch is in the galaxy, and the Kepler telescope's field shows why four of the stars line up.
    /// Hover a star to light it, click to pick it; the map animates (a sweep, a turning reticle and a route).
    /// </summary>
    public sealed class StarMapView : VisualElement
    {
        public sealed class StarSystem
        {
            public string Name = "";
            /// <summary>Galactic longitude and latitude, degrees, and distance from the Sun, parsecs.</summary>
            public double L, B, DistancePc;
            /// <summary>A real star (false: a made-up world, drawn dashed near the Sun).</summary>
            public bool Real = true;
            public bool IsSun;
            public double TeffK = 5772.0;
            public Color Colour = Color.white;
            public readonly List<int> Planets = new List<int>();
            internal Vector2 At;
            internal Label Tag;
        }

        const double MaxPc = 1000.0;
        /// <summary>Left strip kept clear for the header and the system list.</summary>
        public float LeftInset = 260f;
        /// <summary>Kepler's field of view: centred on RA 19h22m40s, Dec +44°30′ (NASA Kepler mission), about 10° wide.</summary>
        const double KeplerRaDeg = 290.667, KeplerDecDeg = 44.5, KeplerHalfWidthDeg = 6.0;

        readonly List<StarSystem> systems = new List<StarSystem>();
        readonly VisualElement backdrop, live, tags, staticTags;
        StarSystem hovered, selected;
        Vector2 centre;
        float radius;
        float selectedAt;
        Vector2 insetCentre;
        float insetRadius;

        public event Action<StarSystem> Picked;
        public StarSystem Selected => selected;
        public IReadOnlyList<StarSystem> Systems => systems;

        public StarMapView()
        {
            style.position = Position.Absolute;
            style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
            backdrop = Layer();
            backdrop.generateVisualContent += DrawBackdrop;
            staticTags = Layer();
            live = Layer();
            live.generateVisualContent += DrawLive;
            tags = Layer();
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
            RegisterCallback<PointerMoveEvent>(e => Hover(Find(e.localPosition)));
            RegisterCallback<PointerLeaveEvent>(_ => Hover(null));
            RegisterCallback<PointerDownEvent>(e =>
            {
                StarSystem s = Find(e.localPosition);
                if (s == null) return;
                Select(s);
                Picked?.Invoke(s);
            });
            schedule.Execute(() => live.MarkDirtyRepaint()).Every(33);
        }

        VisualElement Layer()
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.right = 0; v.style.top = 0; v.style.bottom = 0;
            Add(v);
            return v;
        }

        public void SetSystems(IEnumerable<StarSystem> list)
        {
            systems.Clear();
            tags.Clear();
            foreach (StarSystem s in list)
            {
                systems.Add(s);
                s.Tag = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
                s.Tag.AddToClassList("map-tag");
                tags.Add(s.Tag);
            }
            Layout();
        }

        public void Select(StarSystem s)
        {
            if (s == selected) return;
            selected = s;
            selectedAt = Time.realtimeSinceStartup;
            foreach (StarSystem o in systems) o.Tag?.EnableInClassList("selected", o == s);
        }

        void Hover(StarSystem s)
        {
            if (s == hovered) return;
            hovered = s;
            foreach (StarSystem o in systems) o.Tag?.EnableInClassList("hover", o == s);
            if (s != null) KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Hover, 0.5f);
        }

        StarSystem Find(Vector2 p)
        {
            StarSystem best = null;
            float bestD = 22f * 22f;
            foreach (StarSystem s in systems)
            {
                float d = (s.At - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>Screen radius for a distance in parsecs (log scale; 1 000 pc at the outer ring).</summary>
        float RadiusOf(double pc) => radius * (float)(Math.Log10(1.0 + Math.Max(0.0, pc)) / Math.Log10(1.0 + MaxPc));

        /// <summary>Screen direction for a galactic longitude: 0° up (galactic centre), 90° to the left (seen from the north).</summary>
        static Vector2 Direction(double lDeg)
        {
            float l = (float)(lDeg * Math.PI / 180.0);
            return new Vector2(-Mathf.Sin(l), -Mathf.Cos(l));
        }

        void Layout()
        {
            Rect r = contentRect;
            if (float.IsNaN(r.width) || r.width < 10f) return;
            float usable = Mathf.Max(200f, r.width - LeftInset);
            centre = new Vector2(LeftInset + usable * 0.5f, r.height * 0.52f);
            radius = Mathf.Max(80f, Mathf.Min(usable * 0.5f - 70f, r.height * 0.5f - 64f));
            insetRadius = Mathf.Clamp(r.height * 0.11f, 60f, 100f);
            insetCentre = new Vector2(insetRadius + 34f, r.height - insetRadius - 46f);

            foreach (StarSystem s in systems)
            {
                if (s.IsSun) s.At = centre;
                else if (!s.Real) s.At = centre + Direction(225.0) * RadiusOf(4.0);
                else s.At = centre + Direction(s.L) * RadiusOf(s.DistancePc * Math.Cos(s.B * Math.PI / 180.0));
            }
            // Name tags beside each star: the first of right, left, below or above that hits no other tag or star,
            // nearest stars to the Sun first (they are the most crowded).
            var placed = new List<Rect>();
            foreach (StarSystem o in systems) placed.Add(new Rect(o.At.x - 7f, o.At.y - 7f, 14f, 14f));
            var order = new List<StarSystem>(systems);
            order.Sort((a, b) => (a.At - centre).sqrMagnitude.CompareTo((b.At - centre).sqrMagnitude));
            foreach (StarSystem s in order)
            {
                if (s.Tag == null) continue;
                string detail = s.IsSun ? "the Sun · 0 pc" : !s.Real ? "made-up world"
                    : $"{s.DistancePc:0.#} pc · {s.DistancePc * GalacticCoordinates.LightYearsPerParsec:N0} ly";
                string worlds = s.Planets.Count > 1 ? $" · {s.Planets.Count} worlds" : "";
                s.Tag.text = $"{s.Name.ToUpperInvariant()}\n<size=10>{detail}{worlds}</size>";
                float w = Mathf.Max(s.Name.Length * 8.5f, (detail.Length + worlds.Length) * 5.6f) + 4f, h = 30f;
                var candidates = new[]
                {
                    new Rect(s.At.x + 13f, s.At.y - h * 0.5f, w, h),
                    new Rect(s.At.x - 13f - w, s.At.y - h * 0.5f, w, h),
                    new Rect(s.At.x - w * 0.5f, s.At.y + 11f, w, h),
                    new Rect(s.At.x - w * 0.5f, s.At.y - 11f - h, w, h),
                    new Rect(s.At.x + 13f, s.At.y + 4f, w, h),
                    new Rect(s.At.x - 13f - w, s.At.y - h - 4f, w, h),
                };
                Rect box = candidates[0];
                bool found = false;
                foreach (Rect c in candidates)
                {
                    bool clear = true;
                    foreach (Rect o in placed)
                        if (o.Overlaps(c) && !(o.width < 15f && o.Contains(s.At))) { clear = false; break; }
                    if (clear) { box = c; found = true; break; }
                }
                if (!found)
                    for (int guard = 0; guard < 12; guard++)
                    {
                        bool moved = false;
                        foreach (Rect o in placed)
                            if (o.width > 15f && o.Overlaps(box)) { box.y = o.yMax + 1f; moved = true; }
                        if (!moved) break;
                    }
                placed.Add(box);
                s.Tag.style.left = box.x;
                s.Tag.style.top = box.y;
                s.Tag.style.width = w;
                s.Tag.style.unityTextAlign = box.x + w < s.At.x ? TextAnchor.UpperRight : box.x > s.At.x ? TextAnchor.UpperLeft : TextAnchor.UpperCenter;
            }
            LayoutStaticTags();
            backdrop.MarkDirtyRepaint();
            live.MarkDirtyRepaint();
        }

        void LayoutStaticTags()
        {
            staticTags.Clear();
            foreach (double pc in new[] { 1.0, 10.0, 100.0, 1000.0 })
            {
                float rr = RadiusOf(pc);
                Tag(staticTags, $"{pc:N0} pc · {pc * GalacticCoordinates.LightYearsPerParsec:N0} ly", centre + new Vector2(rr * 0.71f + 4f, -rr * 0.71f - 14f), "map-ring-tag");
            }
            Tag(staticTags, "0° · TOWARDS THE GALACTIC CENTRE", centre + new Vector2(-110f, -radius - 30f), "map-axis-tag");
            Tag(staticTags, "90°", centre + new Vector2(-radius - 34f, -8f), "map-axis-tag");
            Tag(staticTags, "180°", centre + new Vector2(-14f, radius + 10f), "map-axis-tag");
            Tag(staticTags, "270°", centre + new Vector2(radius + 10f, -8f), "map-axis-tag");
            GalacticCoordinates.FromEquatorial(KeplerRaDeg, KeplerDecDeg, out double kl, out _);
            Vector2 k = centre + Direction(kl) * (radius * 1.02f);
            Tag(staticTags, "KEPLER TELESCOPE'S FIELD", k + new Vector2(-170f, -18f), "map-axis-tag");
            Tag(staticTags, "MILKY WAY (SCHEMATIC)\n<size=9>the Sun 8.2 kpc from the centre</size>",
                insetCentre + new Vector2(-insetRadius, insetRadius + 6f), "map-axis-tag");
        }

        static void Tag(VisualElement to, string text, Vector2 at, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = true };
            l.AddToClassList(cls);
            l.style.position = Position.Absolute;
            l.style.left = at.x;
            l.style.top = at.y;
            to.Add(l);
        }

        // ---------------------------------------------------------------- static layer

        void DrawBackdrop(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 10f) return;
            Painter2D p = ctx.painter2D;
            var rng = new System.Random(4242);

            // Nebulae and a faint Milky Way band across the sky behind the map.
            Glow(p, new Vector2(r.width * 0.22f, r.height * 0.28f), r.height * 0.55f, new Color(0.38f, 0.16f, 0.52f), 0.018f, 9);
            Glow(p, new Vector2(r.width * 0.78f, r.height * 0.75f), r.height * 0.6f, new Color(0.10f, 0.32f, 0.48f), 0.02f, 9);
            Glow(p, new Vector2(r.width * 0.55f, r.height * 0.1f), r.height * 0.35f, new Color(0.55f, 0.32f, 0.18f), 0.014f, 7);
            Vector2 a = new Vector2(-0.05f * r.width, r.height * 0.95f), b = new Vector2(1.05f * r.width, r.height * 0.05f);
            for (int i = 0; i <= 12; i++)
                Glow(p, Vector2.Lerp(a, b, i / 12f), r.height * 0.16f, new Color(0.62f, 0.66f, 0.85f), 0.01f, 5);
            for (int i = 0; i < 900; i++)
            {
                bool band = i < 380;
                Vector2 at;
                if (band)
                {
                    float t = (float)rng.NextDouble();
                    float off = (float)(Gauss(rng) * r.height * 0.07);
                    Vector2 n = new Vector2(b.y - a.y, a.x - b.x).normalized;
                    at = Vector2.Lerp(a, b, t) + n * off;
                }
                else at = new Vector2((float)rng.NextDouble() * r.width, (float)rng.NextDouble() * r.height);
                float size = (float)(0.4 + Math.Pow(rng.NextDouble(), 3) * 1.4);
                float tint = (float)rng.NextDouble();
                Color c = tint < 0.2f ? new Color(1f, 0.85f, 0.7f) : tint > 0.85f ? new Color(0.75f, 0.85f, 1f) : Color.white;
                c.a = (float)(0.25 + rng.NextDouble() * 0.6);
                p.fillColor = c;
                p.BeginPath(); p.Arc(at, size, 0f, 360f); p.Fill();
            }

            // The map: a dark disc, distance rings, longitude spokes and ticks.
            Glow(p, centre, radius * 1.08f, new Color(0.02f, 0.04f, 0.09f), 0.12f, 6);
            Color line = new Color(0.47f, 0.75f, 1f);
            foreach (double pc in new[] { 1.0, 10.0, 100.0, 1000.0 })
            {
                line.a = pc >= 1000.0 ? 0.42f : 0.2f;
                p.strokeColor = line;
                p.lineWidth = pc >= 1000.0 ? 1.6f : 1f;
                Dashed(p, centre, RadiusOf(pc), pc >= 1000.0 ? 0f : 4f);
            }
            for (int deg = 0; deg < 360; deg += 30)
            {
                Vector2 d = Direction(deg);
                bool cardinal = deg % 90 == 0;
                line.a = cardinal ? 0.2f : 0.08f;
                p.strokeColor = line;
                p.lineWidth = 1f;
                p.BeginPath(); p.MoveTo(centre + d * RadiusOf(1.0)); p.LineTo(centre + d * radius * 1.03f); p.Stroke();
            }
            for (int deg = 0; deg < 360; deg += 5)
            {
                Vector2 d = Direction(deg);
                line.a = deg % 30 == 0 ? 0.5f : 0.22f;
                p.strokeColor = line;
                float len = deg % 30 == 0 ? 10f : 5f;
                p.BeginPath(); p.MoveTo(centre + d * radius); p.LineTo(centre + d * (radius + len)); p.Stroke();
            }
            // Arrow to the galactic centre.
            p.fillColor = new Color(1f, 0.78f, 0.36f, 0.75f);
            Vector2 tip = centre + new Vector2(0f, -radius - 16f);
            p.BeginPath(); p.MoveTo(tip); p.LineTo(tip + new Vector2(-6f, 9f)); p.LineTo(tip + new Vector2(6f, 9f)); p.ClosePath(); p.Fill();

            // Kepler's field: a thin cone from the Sun.
            GalacticCoordinates.FromEquatorial(KeplerRaDeg, KeplerDecDeg, out double kl, out _);
            Vector2 k0 = Direction(kl - KeplerHalfWidthDeg), k1 = Direction(kl + KeplerHalfWidthDeg);
            p.fillColor = new Color(0.47f, 0.75f, 1f, 0.05f);
            p.BeginPath(); p.MoveTo(centre); p.LineTo(centre + k0 * radius * 1.02f); p.LineTo(centre + k1 * radius * 1.02f); p.ClosePath(); p.Fill();
            p.strokeColor = new Color(0.47f, 0.75f, 1f, 0.22f);
            p.BeginPath(); p.MoveTo(centre); p.LineTo(centre + k0 * radius * 1.02f); p.Stroke();
            p.BeginPath(); p.MoveTo(centre); p.LineTo(centre + k1 * radius * 1.02f); p.Stroke();

            DrawInset(p, rng);
        }

        /// <summary>A schematic barred spiral (four arms) with the Sun marked, and lines out to the main map.</summary>
        void DrawInset(Painter2D p, System.Random rng)
        {
            Vector2 c = insetCentre;
            float ri = insetRadius;
            p.fillColor = new Color(0.02f, 0.03f, 0.07f, 0.85f);
            p.BeginPath(); p.Arc(c, ri * 1.12f, 0f, 360f); p.Fill();
            p.strokeColor = new Color(0.47f, 0.75f, 1f, 0.35f);
            p.lineWidth = 1f;
            p.BeginPath(); p.Arc(c, ri * 1.12f, 0f, 360f); p.Stroke();
            Glow(p, c, ri * 0.9f, new Color(0.55f, 0.6f, 0.85f), 0.03f, 6);
            Glow(p, c, ri * 0.28f, new Color(1f, 0.85f, 0.6f), 0.12f, 6);
            // Bar.
            p.fillColor = new Color(1f, 0.9f, 0.7f, 0.25f);
            p.BeginPath(); p.Arc(c, ri * 0.06f, 0f, 360f); p.Fill();
            float pitch = Mathf.Tan(12f * Mathf.Deg2Rad);
            for (int arm = 0; arm < 4; arm++)
            {
                float start = arm * Mathf.PI * 0.5f + 0.4f;
                for (int i = 0; i < 170; i++)
                {
                    float th = (float)rng.NextDouble() * 4.2f;
                    float rad = ri * 0.16f * Mathf.Exp(pitch * th * 1.9f);
                    if (rad > ri) continue;
                    float ang = start + th;
                    Vector2 at = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad
                                 + new Vector2((float)Gauss(rng), (float)Gauss(rng)) * ri * 0.035f;
                    Color col = rng.NextDouble() < 0.15 ? new Color(1f, 0.6f, 0.75f) : new Color(0.8f, 0.86f, 1f);
                    col.a = (float)(0.25 + rng.NextDouble() * 0.5);
                    p.fillColor = col;
                    p.BeginPath(); p.Arc(at, (float)(0.5 + rng.NextDouble() * 0.9), 0f, 360f); p.Fill();
                }
            }
            // The Sun: 8.2 kpc from the centre out of ~15 kpc drawn, below the centre (the map's up is towards it).
            Vector2 sun = c + new Vector2(0f, ri * (float)(GalacticCoordinates.SunToCentrePc / 15000.0));
            p.strokeColor = new Color(1f, 0.78f, 0.36f, 0.95f);
            p.lineWidth = 1.2f;
            float h = 5f;
            p.BeginPath(); p.MoveTo(sun + new Vector2(-h, -h)); p.LineTo(sun + new Vector2(h, -h)); p.LineTo(sun + new Vector2(h, h));
            p.LineTo(sun + new Vector2(-h, h)); p.ClosePath(); p.Stroke();
            p.strokeColor = new Color(1f, 0.78f, 0.36f, 0.2f);
            p.BeginPath(); p.MoveTo(sun + new Vector2(h, -h)); p.LineTo(centre + new Vector2(-radius * 0.71f, -radius * 0.71f)); p.Stroke();
            p.BeginPath(); p.MoveTo(sun + new Vector2(h, h)); p.LineTo(centre + new Vector2(-radius * 0.71f, radius * 0.71f)); p.Stroke();
        }

        // ---------------------------------------------------------------- live layer

        void DrawLive(MeshGenerationContext ctx)
        {
            if (radius <= 0f) return;
            Painter2D p = ctx.painter2D;
            float now = Time.realtimeSinceStartup;

            // A slow sweep around the Sun.
            float sweep = (now * 18f) % 360f;
            for (int i = 0; i < 8; i++)
            {
                p.fillColor = new Color(0.47f, 0.75f, 1f, 0.018f);
                float a0 = sweep - i * 3.5f - 3.5f, a1 = sweep - i * 3.5f;
                p.BeginPath(); p.MoveTo(centre); p.Arc(centre, radius, a0 - 90f, a1 - 90f); p.ClosePath(); p.Fill();
            }
            p.strokeColor = new Color(0.47f, 0.75f, 1f, 0.3f);
            p.lineWidth = 1f;
            Vector2 sd = new Vector2(Mathf.Cos((sweep - 90f) * Mathf.Deg2Rad), Mathf.Sin((sweep - 90f) * Mathf.Deg2Rad));
            p.BeginPath(); p.MoveTo(centre); p.LineTo(centre + sd * radius); p.Stroke();

            // The route from the Sun to the picked star: marching dashes.
            if (selected != null && !selected.IsSun)
            {
                Vector2 a = centre, b = selected.At;
                float len = (b - a).magnitude;
                Vector2 d = len > 0f ? (b - a) / len : Vector2.zero;
                p.strokeColor = new Color(1f, 0.78f, 0.36f, 0.85f);
                p.lineWidth = 2f;
                float off = (now * 40f) % 14f;
                for (float s = -14f + off; s < len; s += 14f)
                {
                    float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(len - 10f, s + 8f);
                    if (s1 <= s0) continue;
                    p.BeginPath(); p.MoveTo(a + d * s0); p.LineTo(a + d * s1); p.Stroke();
                }
            }

            foreach (StarSystem s in systems)
            {
                bool sel = s == selected, hov = s == hovered;
                float twinkle = 0.85f + 0.15f * Mathf.Sin(now * 2.2f + s.At.x * 0.07f);
                float core = s.IsSun ? 5.5f : 4f;
                Glow(p, s.At, (hov || sel ? 24f : 16f) * twinkle, s.Colour, 0.06f, 5);
                p.fillColor = s.Real ? s.Colour : new Color(0.75f, 0.8f, 0.9f);
                p.BeginPath(); p.Arc(s.At, core, 0f, 360f); p.Fill();
                p.fillColor = new Color(1f, 1f, 1f, 0.9f);
                p.BeginPath(); p.Arc(s.At, core * 0.45f, 0f, 360f); p.Fill();
                p.lineWidth = 1.2f;
                p.strokeColor = hov ? new Color(1f, 0.78f, 0.36f, 0.9f) : new Color(0.47f, 0.75f, 1f, s.Real ? 0.5f : 0.35f);
                if (s.Real) { p.BeginPath(); p.Arc(s.At, hov ? 12f : 9f, 0f, 360f); p.Stroke(); }
                else Dashed(p, s.At, 10f, 3f);
                if (sel)
                {
                    // Turning reticle and a ping.
                    float spin = now * 60f;
                    p.strokeColor = new Color(1f, 0.78f, 0.36f, 1f);
                    p.lineWidth = 2f;
                    for (int q = 0; q < 4; q++)
                    {
                        float a0 = spin + q * 90f + 12f;
                        p.BeginPath(); p.Arc(s.At, 17f, a0, a0 + 54f); p.Stroke();
                    }
                    float ping = ((now - selectedAt) % 1.6f) / 1.6f;
                    p.strokeColor = new Color(1f, 0.78f, 0.36f, 0.7f * (1f - ping));
                    p.lineWidth = 1.5f;
                    p.BeginPath(); p.Arc(s.At, 12f + ping * 26f, 0f, 360f); p.Stroke();
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        static void Dashed(Painter2D p, Vector2 c, float r, float dashDeg)
        {
            if (dashDeg <= 0f) { p.BeginPath(); p.Arc(c, r, 0f, 360f); p.Stroke(); return; }
            for (float a = 0f; a < 360f; a += dashDeg * 2f)
            {
                p.BeginPath(); p.Arc(c, r, a, a + dashDeg); p.Stroke();
            }
        }

        static void Glow(Painter2D p, Vector2 at, float radius, Color colour, float alpha, int steps)
        {
            for (int i = steps; i >= 1; i--)
            {
                colour.a = alpha;
                p.fillColor = colour;
                p.BeginPath(); p.Arc(at, radius * i / steps, 0f, 360f); p.Fill();
            }
        }

        static double Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}

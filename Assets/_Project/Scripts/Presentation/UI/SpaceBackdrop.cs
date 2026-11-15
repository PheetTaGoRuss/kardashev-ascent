using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The main menu's backdrop (director 2026-11-15: space, hollow, spacious, beautiful), drawn in code: deep space with
    /// soft nebula clouds, a few hundred stars that twinkle and drift, a distant sun, a large planet with a lit limb,
    /// a night side and a glowing atmosphere, a moon on its orbit, and now and then a shooting star. No image files.
    /// </summary>
    public sealed class SpaceBackdrop : VisualElement
    {
        struct Star { public Vector2 P; public float Size, Phase, Speed, Depth; public Color Tint; }

        const int StarCount = 420;
        readonly Star[] stars = new Star[StarCount];
        float time;
        float shootAt = 3f, shootStart = -10f;
        Vector2 shootFrom, shootDir;
        IVisualElementScheduledItem tick;

        static readonly Color Space = new Color(0.012f, 0.016f, 0.035f);

        public SpaceBackdrop()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
            style.backgroundColor = Space;
            var rng = new System.Random(2026);
            for (int i = 0; i < StarCount; i++)
            {
                float warm = (float)rng.NextDouble();
                stars[i] = new Star
                {
                    P = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                    Size = 0.5f + 1.7f * Mathf.Pow((float)rng.NextDouble(), 3f),
                    Phase = (float)rng.NextDouble() * 6.283f,
                    Speed = 0.6f + 2.2f * (float)rng.NextDouble(),
                    Depth = 0.2f + 0.8f * (float)rng.NextDouble(),
                    // Real star colours: most white, some blue-white, some warm.
                    Tint = warm < 0.15f ? new Color(1f, 0.82f, 0.62f) : warm < 0.35f ? new Color(0.75f, 0.85f, 1f) : Color.white,
                };
            }
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(_ => tick = schedule.Execute(Step).Every(33));
            RegisterCallback<DetachFromPanelEvent>(_ => tick?.Pause());
        }

        void Step(TimerState t)
        {
            if (resolvedStyle.display == DisplayStyle.None || (parent != null && parent.resolvedStyle.display == DisplayStyle.None))
                return;
            time += t.deltaTime / 1000f;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 10f || r.height < 10f) return;
            Painter2D p = ctx.painter2D;
            float w = r.width, h = r.height, unit = Mathf.Min(w, h);

            // Nebula: soft clouds made of many faint discs.
            Glow(p, new Vector2(w * 0.20f, h * 0.30f), unit * 0.55f, new Color(0.35f, 0.12f, 0.45f), 0.009f, 44);
            Glow(p, new Vector2(w * 0.42f, h * 0.78f), unit * 0.45f, new Color(0.05f, 0.30f, 0.42f), 0.008f, 40);
            Glow(p, new Vector2(w * 0.62f, h * 0.18f), unit * 0.35f, new Color(0.55f, 0.25f, 0.12f), 0.006f, 36);

            // Stars: twinkling, drifting slowly to the left, nearer ones faster (parallax).
            foreach (Star s in stars)
            {
                float x = Mathf.Repeat(s.P.x - time * 0.004f * s.Depth, 1f) * w;
                float y = s.P.y * h;
                float twinkle = 0.55f + 0.45f * Mathf.Sin(time * s.Speed + s.Phase);
                Color c = s.Tint; c.a = twinkle * (0.35f + 0.65f * s.Depth);
                p.fillColor = c;
                p.BeginPath(); p.Arc(new Vector2(x, y), s.Size * (0.8f + 0.4f * s.Depth), 0f, 360f); p.Fill();
                if (s.Size > 1.7f)
                {
                    // A soft halo and a glint on the brightest stars.
                    Color halo = s.Tint; halo.a = 0.08f * twinkle;
                    p.fillColor = halo;
                    p.BeginPath(); p.Arc(new Vector2(x, y), s.Size * 4f, 0f, 360f); p.Fill();
                    p.strokeColor = new Color(s.Tint.r, s.Tint.g, s.Tint.b, 0.35f * twinkle);
                    p.lineWidth = 1f;
                    float g = s.Size * 6f;
                    p.BeginPath(); p.MoveTo(new Vector2(x - g, y)); p.LineTo(new Vector2(x + g, y)); p.Stroke();
                    p.BeginPath(); p.MoveTo(new Vector2(x, y - g)); p.LineTo(new Vector2(x, y + g)); p.Stroke();
                }
            }

            // A distant sun, top left, with a warm glow.
            Vector2 sun = new Vector2(w * 0.08f, h * 0.12f);
            Glow(p, sun, unit * 0.32f, new Color(1f, 0.75f, 0.42f), 0.011f, 48);
            p.fillColor = new Color(1f, 0.95f, 0.85f, 1f);
            p.BeginPath(); p.Arc(sun, unit * 0.012f, 0f, 360f); p.Fill();

            // The planet: right side, partly below the edge; lit from the sun's side.
            Vector2 c0 = new Vector2(w * 0.80f, h * 0.80f);
            float R = unit * 0.56f;
            // Atmosphere glow around the limb.
            for (int i = 36; i >= 1; i--)
            {
                p.fillColor = new Color(0.30f, 0.65f, 1f, 0.008f);
                p.BeginPath(); p.Arc(c0, R * (1f + 0.005f * i), 0f, 360f); p.Fill();
            }
            // Body: ocean blue fading to deep blue towards the night side.
            p.fillColor = new Color(0.07f, 0.22f, 0.42f);
            p.BeginPath(); p.Arc(c0, R, 0f, 360f); p.Fill();
            Vector2 toSun = (sun - c0).normalized;
            for (int i = 0; i < 40; i++)
            {
                float k = i / 40f;
                p.fillColor = new Color(0.20f + 0.25f * k, 0.48f + 0.25f * k, 0.70f + 0.15f * k, 0.03f);
                p.BeginPath(); p.Arc(c0 + toSun * R * (0.25f + 0.0125f * i), R * (0.95f - 0.0175f * i), 0f, 360f); p.Fill();
            }
            // Continents: a few soft green-brown patches on the day side, slowly turning.
            for (int i = 0; i < 7; i++)
            {
                float a = time * 0.02f + i * 0.9f;
                Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a * 0.7f) * 0.6f) * R * 0.55f;
                Vector2 at = c0 + toSun * R * 0.25f + off;
                if ((at - c0).magnitude > R * 0.92f) continue;
                p.fillColor = new Color(0.26f, 0.42f, 0.24f, 0.35f);
                p.BeginPath(); p.Arc(at, R * (0.09f + 0.03f * (i % 3)), 0f, 360f); p.Fill();
            }
            // Night side: a dark disc shifted away from the sun, leaving a lit crescent.
            for (int i = 0; i < 30; i++)
            {
                p.fillColor = new Color(0.005f, 0.01f, 0.03f, 0.075f);
                p.BeginPath(); p.Arc(c0 - toSun * R * (0.10f + 0.012f * i), R * (0.99f - 0.004f * i), 0f, 360f); p.Fill();
            }
            // City lights on the night side (a growing civilisation).
            var lights = new System.Random(7);
            for (int i = 0; i < 70; i++)
            {
                float a = (float)lights.NextDouble() * 6.283f, d = Mathf.Sqrt((float)lights.NextDouble()) * R * 0.85f;
                Vector2 at = c0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (Vector2.Dot((at - c0).normalized, toSun) > -0.25f) continue;
                p.fillColor = new Color(1f, 0.78f, 0.38f, 0.5f + 0.3f * Mathf.Sin(time * 0.8f + i));
                p.BeginPath(); p.Arc(at, 1.2f, 0f, 360f); p.Fill();
            }
            // A thin bright rim on the sunlit limb.
            p.strokeColor = new Color(0.65f, 0.85f, 1f, 0.55f);
            p.lineWidth = 2f;
            float rim = Mathf.Atan2(toSun.y, toSun.x) * Mathf.Rad2Deg;
            p.BeginPath(); p.Arc(c0, R, rim - 50f, rim + 50f); p.Stroke();

            // An orbit and its moon.
            Vector2 axis = new Vector2(R * 1.55f, R * 0.32f);
            p.strokeColor = new Color(1f, 0.78f, 0.36f, 0.18f);
            p.lineWidth = 1f;
            p.BeginPath();
            for (int i = 0; i <= 96; i++)
            {
                float a = i / 96f * 6.283f;
                Vector2 q = c0 + Rotate(new Vector2(Mathf.Cos(a) * axis.x, Mathf.Sin(a) * axis.y), -18f);
                if (i == 0) p.MoveTo(q); else p.LineTo(q);
            }
            p.Stroke();
            float m = time * 0.12f;
            Vector2 moon = c0 + Rotate(new Vector2(Mathf.Cos(m) * axis.x, Mathf.Sin(m) * axis.y), -18f);
            bool behind = Mathf.Sin(m) < 0f && (moon - c0).magnitude < R;
            if (!behind)
            {
                p.fillColor = new Color(0.82f, 0.80f, 0.76f);
                p.BeginPath(); p.Arc(moon, unit * 0.014f, 0f, 360f); p.Fill();
                Glow(p, moon, unit * 0.04f, new Color(1f, 0.95f, 0.85f), 0.03f, 5);
            }

            // Now and then, a shooting star.
            if (time > shootAt)
            {
                shootStart = time;
                shootAt = time + 5f + Random.value * 7f;
                shootFrom = new Vector2(Random.Range(0.1f, 0.6f) * w, Random.Range(0.05f, 0.35f) * h);
                shootDir = new Vector2(1f, 0.35f).normalized;
            }
            float age = time - shootStart;
            if (age < 1.1f)
            {
                Vector2 head = shootFrom + shootDir * age * unit * 0.6f;
                p.strokeColor = new Color(1f, 1f, 1f, 0.8f * (1f - age / 1.1f));
                p.lineWidth = 1.6f;
                p.BeginPath(); p.MoveTo(head - shootDir * unit * 0.12f); p.LineTo(head); p.Stroke();
            }
        }

        static Vector2 Rotate(Vector2 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>A soft radial glow: many faint discs, smaller ones stacked on bigger ones.</summary>
        static void Glow(Painter2D p, Vector2 at, float radius, Color colour, float alpha, int steps)
        {
            for (int i = steps; i >= 1; i--)
            {
                colour.a = alpha;
                p.fillColor = colour;
                p.BeginPath(); p.Arc(at, radius * i / steps, 0f, 360f); p.Fill();
            }
        }
    }
}

using System;
using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// The sky behind the planet (director 2026-11-15: a galaxy and stars instead of a flat black): a panorama made
    /// once at start, with a Milky Way band (brighter, warmer bulge and a dark dust lane), faint nebulae and about
    /// fourteen thousand stars, more of them along the band, in star colours. Shown as the main camera's skybox through
    /// Resources/SpaceSky.mat (Skybox/Panoramic). Looks only: it is not a real sky for any planet.
    /// </summary>
    public sealed class SpaceSky : MonoBehaviour
    {
        const int Width = 2048, Height = 1024;
        /// <summary>The diffuse light (band, nebulae) is worked out on a coarser grid and blended up.</summary>
        const int CoarseW = 512, CoarseH = 256;
        const int Seed = 1977;

        Texture2D texture;
        Material material;

        /// <summary>Add the sky once (GameFlow calls this at start).</summary>
        public static void Ensure()
        {
            if (FindAnyObjectByType<SpaceSky>() == null)
                new GameObject("Space Sky").AddComponent<SpaceSky>();
        }

        void Start()
        {
            var source = Resources.Load<Material>("SpaceSky");
            Camera cam = Camera.main;
            if (source == null || cam == null)
                return;
            texture = Generate();
            material = new Material(source) { name = "Space Sky (runtime)" };
            material.SetTexture("_MainTex", texture);
            Skybox sky = cam.GetComponent<Skybox>();
            if (sky == null) sky = cam.gameObject.AddComponent<Skybox>();
            sky.material = material;
            cam.clearFlags = CameraClearFlags.Skybox;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
        }

        static Texture2D Generate()
        {
            // The galaxy's plane, tilted against the panorama so the band crosses the sky at a slant.
            Vector3 pole = new Vector3(0.35f, 0.82f, 0.45f).normalized;
            Vector3 e1 = Vector3.Cross(pole, Vector3.right).normalized;
            Vector3 e2 = Vector3.Cross(pole, e1);

            var coarse = new Color[CoarseW * CoarseH];
            for (int y = 0; y < CoarseH; y++)
                for (int x = 0; x < CoarseW; x++)
                {
                    Vector3 v = Direction((x + 0.5f) / CoarseW, (y + 0.5f) / CoarseH);
                    float b = Mathf.Asin(Mathf.Clamp(Vector3.Dot(v, pole), -1f, 1f));
                    float l = Mathf.Atan2(Vector3.Dot(v, e2), Vector3.Dot(v, e1));
                    double n1 = Noise3D.Fractal(v.x * 3.0, v.y * 3.0, v.z * 3.0, Seed, 5) * 0.5 + 0.5;
                    double n2 = Noise3D.Fractal(v.x * 7.0 + 11.0, v.y * 7.0, v.z * 7.0, Seed + 7, 4) * 0.5 + 0.5;
                    double n3 = Noise3D.Fractal(v.x * 1.6 + 40.0, v.y * 1.6, v.z * 1.6, Seed + 21, 3) * 0.5 + 0.5;

                    float band = Mathf.Exp(-Sq(b / 0.17f)) * (float)(0.45 + 0.75 * n1);
                    float bulge = 1.3f * Mathf.Exp(-Sq(l / 0.55f) - Sq(b / 0.24f));
                    float dust = 1f - 0.75f * Mathf.Exp(-Sq((b - 0.01f) / 0.045f)) * (float)Math.Pow(n2, 1.4) * (0.4f + bulge);
                    float glow = (band + bulge) * Mathf.Clamp01(dust);
                    Color warm = new Color(1f, 0.86f, 0.68f), cool = new Color(0.66f, 0.76f, 1f);
                    Color c = Color.Lerp(cool, warm, Mathf.Clamp01(bulge * 1.2f)) * glow * 0.20f;
                    // Nebulae: faint magenta and teal clouds, mostly near the band.
                    float neb = Mathf.Clamp01((float)(n3 - 0.55) * 3f) * (0.35f + 0.65f * Mathf.Exp(-Sq(b / 0.5f)));
                    c += Color.Lerp(new Color(0.55f, 0.18f, 0.5f), new Color(0.12f, 0.42f, 0.5f), (float)n2) * neb * 0.07f;
                    c += new Color(0.004f, 0.006f, 0.014f);
                    coarse[y * CoarseW + x] = c;
                }

            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                float cy = (y + 0.5f) / Height * CoarseH - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(cy), 0, CoarseH - 1), y1 = Mathf.Min(y0 + 1, CoarseH - 1);
                float fy = Mathf.Clamp01(cy - y0);
                for (int x = 0; x < Width; x++)
                {
                    float cx = (x + 0.5f) / Width * CoarseW - 0.5f;
                    int x0 = Mathf.FloorToInt(cx), fxI = x0;
                    float fx = cx - x0;
                    x0 = (x0 % CoarseW + CoarseW) % CoarseW;
                    int x1 = (fxI + 1) % CoarseW;
                    if (x1 < 0) x1 += CoarseW;
                    Color c = Color.Lerp(Color.Lerp(coarse[y0 * CoarseW + x0], coarse[y0 * CoarseW + x1], fx),
                                         Color.Lerp(coarse[y1 * CoarseW + x0], coarse[y1 * CoarseW + x1], fx), fy);
                    pixels[y * Width + x] = c;
                }
            }

            var rng = new System.Random(Seed);
            for (int i = 0; i < 14000; i++)
            {
                Vector3 v;
                if (i < 5000)
                {
                    // Along the band: a gaussian spread in galactic latitude.
                    double bl = Gauss(rng) * 0.14, ll = rng.NextDouble() * Math.PI * 2.0;
                    v = (pole * (float)Math.Sin(bl) + (e1 * (float)Math.Cos(ll) + e2 * (float)Math.Sin(ll)) * (float)Math.Cos(bl)).normalized;
                }
                else
                {
                    double z = rng.NextDouble() * 2.0 - 1.0, a = rng.NextDouble() * Math.PI * 2.0, s = Math.Sqrt(1.0 - z * z);
                    v = new Vector3((float)(s * Math.Cos(a)), (float)z, (float)(s * Math.Sin(a)));
                }
                float bright = (float)Math.Pow(rng.NextDouble(), 7.0);
                float lum = 0.18f + bright * 1.6f;
                double t = rng.NextDouble();
                Color tint = t < 0.12 ? new Color(1f, 0.72f, 0.5f) : t < 0.3 ? new Color(1f, 0.9f, 0.75f) : t > 0.9 ? new Color(0.7f, 0.8f, 1f) : new Color(0.95f, 0.96f, 1f);
                Stamp(pixels, v, tint * lum, bright > 0.35f ? 2 : bright > 0.08f ? 1 : 0);
            }

            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                name = "Space sky",
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Panorama coordinates (u right, v up, 0–1) to a direction, matching Skybox/Panoramic.</summary>
        static Vector3 Direction(float u, float v)
        {
            float lon = (u - 0.5f) * Mathf.PI * 2f;
            float lat = (v - 0.5f) * Mathf.PI;
            return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
        }

        /// <summary>Add a star: a soft dot, widened towards the poles so it stays round on the sphere.</summary>
        static void Stamp(Color32[] pixels, Vector3 v, Color c, int radius)
        {
            float lat = Mathf.Asin(Mathf.Clamp(v.y, -1f, 1f));
            float lon = Mathf.Atan2(v.x, v.z);
            float u = lon / (Mathf.PI * 2f) + 0.5f, w = lat / Mathf.PI + 0.5f;
            int px = Mathf.FloorToInt(u * Width), py = Mathf.Clamp(Mathf.FloorToInt(w * Height), 0, Height - 1);
            int rx = Mathf.Min(40, Mathf.FloorToInt((radius + 0.5f) / Mathf.Max(0.05f, Mathf.Cos(lat))));
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -rx; dx <= rx; dx++)
                {
                    int y = py + dy;
                    if (y < 0 || y >= Height) continue;
                    int x = ((px + dx) % Width + Width) % Width;
                    float d = Mathf.Sqrt(Sq(dx * Mathf.Cos(lat)) + Sq(dy));
                    float k = radius == 0 ? Mathf.Clamp01(1.2f - d) : Mathf.Clamp01(1f - d / (radius + 0.6f));
                    k *= k;
                    if (k <= 0f) continue;
                    Color32 o = pixels[y * Width + x];
                    pixels[y * Width + x] = new Color32(
                        (byte)Mathf.Min(255, o.r + c.r * k * 255f),
                        (byte)Mathf.Min(255, o.g + c.g * k * 255f),
                        (byte)Mathf.Min(255, o.b + c.b * k * 255f), 255);
                }
        }

        static float Sq(float x) => x * x;

        static double Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}

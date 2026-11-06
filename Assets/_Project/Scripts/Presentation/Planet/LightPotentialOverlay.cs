using System;
using System.Threading.Tasks;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Light-potential map (#6): colours every tile by how much a standard solar panel there would make in a year
    /// (<see cref="LightPotential"/>), with a legend. Press M to toggle. Values are computed on a background thread
    /// whenever the simulation (planet or star) is rebuilt.
    /// Colour: one orange hue, dark = little energy, bright = most; it reads on the night side because the map is unlit.
    /// </summary>
    [RequireComponent(typeof(PlanetView))]
    public sealed class LightPotentialOverlay : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [Tooltip("Unlit vertex-colour material (KardashevAscent/PlanetTilesUnlit).")]
        [SerializeField] Material mapMaterial;
        [SerializeField] TilePicker picker;
        [SerializeField] KeyCode toggleKey = KeyCode.M;
        [SerializeField] bool showOnStart;

        // Sequential single-hue orange ramp for a dark surface: low values recede toward the black of space.
        static readonly Color32[] Ramp =
        {
            new Color32(0x2a, 0x13, 0x0a, 255), new Color32(0x5a, 0x24, 0x0f, 255), new Color32(0x8d, 0x37, 0x14, 255),
            new Color32(0xc0, 0x4d, 0x1c, 255), new Color32(0xe3, 0x69, 0x2f, 255), new Color32(0xf2, 0x92, 0x5f, 255),
            new Color32(0xf9, 0xbb, 0x93, 255), new Color32(0xfd, 0xe3, 0xcf, 255),
        };

        PlanetView planet;
        double[] values;
        double min, max;
        Task<double[]> pending;
        HexSphere pendingSphere;
        bool visible;
        Texture2D legendTexture;

        public bool Visible => visible;

        /// <summary>kWh per m² of standard panel per year at a tile, or NaN if not computed yet.</summary>
        public double ValueAt(int tile) => values != null && tile >= 0 && tile < values.Length ? values[tile] : double.NaN;

        void Awake()
        {
            planet = GetComponent<PlanetView>();
            if (picker == null)
                picker = GetComponent<TilePicker>();
            visible = showOnStart;
        }

        void OnEnable()
        {
            if (runner != null)
                runner.SimulationCreated += Recompute;
        }

        void OnDisable()
        {
            if (runner != null)
                runner.SimulationCreated -= Recompute;
        }

        void Start()
        {
            if (runner != null && runner.Simulation != null)
                Recompute();
        }

        void OnDestroy()
        {
            if (legendTexture != null)
                Destroy(legendTexture);
        }

        void Recompute()
        {
            if (runner == null || runner.Simulation == null)
                return;
            HexSphere sphere = runner.Simulation.Sphere;
            PlanetSolarParams solar = runner.SolarParams;
            values = null;
            pendingSphere = sphere;
            // Per orbit → per Earth year, so the numbers compare across planets (and match the panel preview).
            double perEarthYear = solar.YearLengthHours > 0.0 ? SolarConstants.EarthYearHours / solar.YearLengthHours : 1.0;
            pending = Task.Run(() =>
            {
                double[] kwh = LightPotential.ForTiles(sphere, solar);
                for (int i = 0; i < kwh.Length; i++)
                    kwh[i] *= perEarthYear;
                return kwh;
            });
            if (visible)
                planet.ShowTerrain();
        }

        void Update()
        {
            if (pending != null && pending.IsCompleted)
            {
                if (pending.IsFaulted)
                    Debug.LogException(pending.Exception);
                else if (pendingSphere == planet.Sphere)
                    Accept(pending.Result);
                pending = null;
            }

            if (Input.GetKeyDown(toggleKey))
                SetVisible(!visible);
        }

        public void SetVisible(bool show)
        {
            visible = show;
            if (!show)
                planet.ShowTerrain();
            else if (values != null)
                Paint();
        }

        void Accept(double[] result)
        {
            values = result;
            min = double.MaxValue;
            max = double.MinValue;
            foreach (double v in values)
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            if (visible)
                Paint();
        }

        void Paint()
        {
            var colors = new Color32[values.Length];
            double span = Math.Max(max - min, 1e-9);
            for (int t = 0; t < values.Length; t++)
                colors[t] = RampColor((float)((values[t] - min) / span));
            planet.ShowTileColors(colors, mapMaterial);
        }

        static Color32 RampColor(float x)
        {
            x = Mathf.Clamp01(x) * (Ramp.Length - 1);
            int i = Mathf.Min((int)x, Ramp.Length - 2);
            return Color32.Lerp(Ramp[i], Ramp[i + 1], x - i);
        }

        /// <summary>True once values are computed.</summary>
        public bool HasValues => values != null;
        public double Min => min;
        public double Max => max;
        public KeyCode ToggleKey => toggleKey;

        /// <summary>The colour ramp as a 256×1 texture, for a legend.</summary>
        public Texture2D LegendTexture => legendTexture != null ? legendTexture : (legendTexture = BuildLegendTexture());

        static Texture2D BuildLegendTexture()
        {
            const int w = 256;
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < w; i++)
                tex.SetPixel(i, 0, RampColor(i / (float)(w - 1)));
            tex.Apply();
            return tex;
        }
    }
}

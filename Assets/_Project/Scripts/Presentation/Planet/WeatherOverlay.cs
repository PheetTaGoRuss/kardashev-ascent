using System.Diagnostics;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// The weather map (Weather W7; F6): tiles coloured by today's cloud (white over dark blue), dust (brown, where the optical depth is above the planet's usual),
    /// and storms (red). Unlit, like the light-potential map. Read from <see cref="IPlanetSimulation.WeatherAt"/> once a second while shown, so it costs
    /// one pass over the tiles a second; the pass time is shown in the legend.
    /// </summary>
    public sealed class WeatherOverlay : MonoBehaviour
    {
        public const KeyCode ToggleKey = KeyCode.F6;
        SimulationRunner runner;
        PlanetView planet;
        LightPotentialOverlay light;
        float next;
        double lastMs;
        public static bool On { get; private set; }
        public static void Set(bool on) { On = on; }
        bool painted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<WeatherOverlay>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("Weather Overlay").AddComponent<WeatherOverlay>();
        }

        void Start()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            planet = FindFirstObjectByType<PlanetView>();
            light = FindFirstObjectByType<LightPotentialOverlay>();
        }

        void Update()
        {
            if (GameFlow.IsPlaying && ScreenInput.KeyDown(ToggleKey)) On = !On;
            if (!On)
            {
                if (painted && planet != null && (light == null || !light.Visible)) planet.ShowTerrain();
                painted = false;
                return;
            }
            if (planet == null || runner == null || runner.Simulation == null || light == null || light.MapMaterial == null) return;
            IPlanetSimulation sim = runner.Simulation;
            if (!sim.HasWeather) return;
            // one pass over the tiles a second, spread over frames in chunks so no frame pays for all of it
            if (pass == null)
            {
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 1f;
                pass = new Color32[sim.TileCount]; passAt = 0; passMs = 0.0;
            }
            var sw = Stopwatch.StartNew();
            double unused = 0.0;
            for (; passAt < pass.Length && sw.Elapsed.TotalMilliseconds < ChunkMs; passAt++)
                pass[passAt] = ColourOf(sim.WeatherAt(passAt), sim.GetTile(passAt).IsWater, ref unused);
            passMs += sw.Elapsed.TotalMilliseconds;
            if (passAt >= pass.Length)
            {
                planet.ShowTileColors(pass, light.MapMaterial);
                painted = true; lastMs = passMs; pass = null;
            }
        }

        const double ChunkMs = 4.0;
        Color32[] pass;
        int passAt;
        double passMs;

        /// <summary>The tile colour for a day's weather: cloud, then dust, then a storm tint.</summary>
        public static Color32 ColourOf(WeatherSample w, bool water, ref double unused)
        {
            Color sky = water ? new Color(0.04f, 0.10f, 0.22f) : new Color(0.10f, 0.12f, 0.16f);
            float cover = Mathf.Clamp01((float)(1.0 - w.CloudFactor) / 0.5f);
            Color c = Color.Lerp(sky, new Color(0.93f, 0.95f, 0.98f), cover);
            float dust = Mathf.Clamp01((float)(w.DustTau - 0.6) / 3f);
            if (dust > 0f) c = Color.Lerp(c, new Color(0.72f, 0.42f, 0.2f), dust);
            if (w.Storm) c = Color.Lerp(c, new Color(1f, 0.25f, 0.2f), 0.7f);
            return c;
        }

        void OnGUI()
        {
            if (!On) return;
            GUI.Box(new Rect(10f, UiLayout.ClearBottomY - 36f, 560f, 26f), $"Weather map (F6): white cloud · brown dust · red storm · {lastMs:0.0} ms a pass, at most {ChunkMs:0} ms a frame");
        }
    }
}

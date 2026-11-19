using KardashevAscent.Presentation.UI;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Resources;
using UnityEngine;
using Mat = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Testing aid (god mode), now part of the debug panel (F10): the panel picks the material map. While on, every tile's ground
    /// shows as if surveyed (view only: the save is not changed) and the planet is coloured by that material's grade:
    /// dark = none, bright = rich. It sets itself up after the scene loads, so no scene changes are needed.
    /// </summary>
    public sealed class GodMode : MonoBehaviour
    {
        static readonly Mat[] Cycle = { Mat.Copper, Mat.Lithium, Mat.Iron, Mat.Water };

        /// <summary>True while god mode is on: the tile panel shows every tile's ground.</summary>
        public static bool Active { get; private set; }

        PlanetView planet;
        SimulationRunner runner;
        LightPotentialOverlay lightMap;
        int index = -1;
        int tiles, nearSea;
        GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<GodMode>() == null && FindFirstObjectByType<PlanetView>() != null)
                new GameObject("God Mode").AddComponent<GodMode>();
        }

        void Start()
        {
            planet = FindFirstObjectByType<PlanetView>();
            runner = FindFirstObjectByType<SimulationRunner>();
            lightMap = FindFirstObjectByType<LightPotentialOverlay>();
            if (runner != null)
                runner.SimulationCreated += Repaint;
        }

        void OnDestroy()
        {
            Active = false;
            if (runner != null)
                runner.SimulationCreated -= Repaint;
        }

        /// <summary>The material map now shown (-1: off). Set it through <see cref="SetMap"/>.</summary>
        public int MapIndex => index;

        public static string MapName(int i) => i < 0 ? "Off" : Materials.Name(Cycle[i]);

        public static int MapCount => Cycle.Length;

        /// <summary>Show the ground map of one material (0..MapCount-1) on every tile, or -1 to turn it off. Driven by the debug panel (F10).</summary>
        public void SetMap(int i)
        {
            index = i < 0 || i >= Cycle.Length ? -1 : i;
            Active = index >= 0;
            if (Active)
                Repaint();
            else if (lightMap != null && lightMap.Visible)
                lightMap.SetVisible(true);
            else
                planet?.ShowTerrain();
        }

        void Repaint()
        {
            if (!Active || planet == null || runner == null || runner.Simulation == null)
                return;
            var sim = runner.Simulation;
            DepositMap map = sim.Deposits;
            Mat m = Cycle[index];
            int n = sim.Sphere.TileCount;
            var colors = new Color32[n];
            double best = 0.0;
            for (int t = 0; t < n; t++)
                best = System.Math.Max(best, map.GradeOf(t, m));
            tiles = nearSea = 0;
            for (int t = 0; t < n; t++)
            {
                double g = map.GradeOf(t, m);
                if (sim.GetTile(t).IsWater)
                    colors[t] = new Color32(10, 22, 48, 255);
                else if (g <= 0.0)
                    colors[t] = new Color32(38, 38, 42, 255);
                else
                {
                    tiles++;
                    float x = best > 0.0 ? Mathf.Clamp01((float)(g / best)) : 1f;
                    colors[t] = Color32.Lerp(new Color32(120, 70, 20, 255), new Color32(255, 220, 90, 255), x);
                    foreach (int nb in sim.Sphere.Neighbors[t])
                        if (sim.GetTile(nb).IsWater) { nearSea++; break; }
                }
            }
            planet.ShowTileColors(colors, lightMap != null ? lightMap.MapMaterial : null);
        }

        void OnGUI()
        {
            if (!Active)
                return;
            style ??= new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            string what = Materials.Name(Cycle[index]);
            GUI.Box(new Rect(Screen.width / 2f - 260f, Screen.height - 150f, 520f, 28f),
                    $"GOD MODE · {what} map: {tiles} tiles · every tile shows as surveyed · F10 panel: change", style);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Panel placement (#7). Select a land tile and a see-through panel appears on it; set its tilt β and
    /// facing γ with the sliders and the energy per year, capacity factor, EROI and payback update live
    /// (from <see cref="IPlanetSimulation.PreviewPanel"/>). The best angle for the tile is searched in the
    /// background (<see cref="PanelAngleSearch"/>); placing within 98 % of it earns the best-angle bonus.
    /// Keys: P place, B use best angle, Delete remove.
    /// </summary>
    [RequireComponent(typeof(PlanetView))]
    public sealed class PanelPlacer : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] TilePicker picker;
        [SerializeField] Material frameMaterial;
        [SerializeField] Material cellMaterial;
        [Tooltip("See-through material for the placement preview (KardashevAscent/GhostTint).")]
        [SerializeField] Material ghostMaterial;

        [SerializeField] KeyCode placeKey = KeyCode.P;
        [SerializeField] KeyCode bestKey = KeyCode.B;
        [SerializeField] KeyCode removeKey = KeyCode.Delete;
        [SerializeField] Color ghostCanPlace = new Color(0.45f, 0.85f, 1f, 0.5f);
        [SerializeField] Color ghostBlocked = new Color(1f, 0.38f, 0.3f, 0.5f);
        [Tooltip("Seconds between live previews while a slider moves.")]
        [Range(0f, 0.5f)] [SerializeField] float previewInterval = 0.05f;

        const float WindowWidth = 290f;

        PlanetView planet;
        IPlanetSimulation simulation;
        readonly Dictionary<int, Placed> placed = new Dictionary<int, Placed>();
        SolarPanelVisual ghost;

        int tile = -1;
        float tiltDeg, azimuthDeg;
        PanelPreview preview;
        bool previewDirty;
        float lastPreviewTime = float.NegativeInfinity;

        Task<PanelAngles> bestTask;
        int bestTaskTile = -1;
        PanelAngles best;
        bool bestReady;

        GUIStyle boxStyle, labelStyle, smallStyle;

        struct Placed
        {
            public double Tilt, Azimuth;
            public PanelPreview Preview;
            public AngleRating Rating;
            public SolarPanelVisual Visual;
        }

        /// <summary>Panels placed at 98 % or more of their tile's best yield.</summary>
        public int BestAnglePanels { get; private set; }
        public int PanelCount => placed.Count;

        void Awake()
        {
            planet = GetComponent<PlanetView>();
            if (picker == null)
                picker = GetComponent<TilePicker>();
        }

        void OnEnable()
        {
            if (picker != null)
                picker.SelectedTileChanged += OnTileSelected;
            if (runner != null)
                runner.SimulationCreated += OnSimulationCreated;
        }

        void OnDisable()
        {
            if (picker != null)
                picker.SelectedTileChanged -= OnTileSelected;
            if (runner != null)
                runner.SimulationCreated -= OnSimulationCreated;
        }

        void Start()
        {
            if (runner == null || picker == null)
            {
                Debug.LogError("PanelPlacer needs a SimulationRunner and a TilePicker.", this);
                enabled = false;
                return;
            }
            ghost = new SolarPanelVisual("Panel Ghost", transform, ghostMaterial, ghostMaterial);
            if (runner.Simulation != null)
                OnSimulationCreated();
        }

        void OnDestroy()
        {
            ghost?.Destroy();
            foreach (Placed p in placed.Values)
                p.Visual.Destroy();
        }

        void OnSimulationCreated()
        {
            // A new simulation starts with no panels.
            foreach (Placed p in placed.Values)
                p.Visual.Destroy();
            placed.Clear();
            BestAnglePanels = 0;
            simulation = runner.Simulation;
            OnTileSelected(picker.SelectedTile);
        }

        void OnTileSelected(int selected)
        {
            tile = selected;
            bestReady = false;
            bestTask = null;
            if (simulation == null || tile < 0)
            {
                ghost?.Hide();
                return;
            }
            if (placed.TryGetValue(tile, out Placed p))
            {
                tiltDeg = (float)(p.Tilt * Mathf.Rad2Deg);
                azimuthDeg = (float)(p.Azimuth * Mathf.Rad2Deg);
            }
            else
            {
                // Rule of thumb until the search reports: tilt = |latitude|, facing the equator.
                double lat = simulation.Sphere.Latitude(tile);
                tiltDeg = Mathf.Round(Mathf.Clamp(Mathf.Abs((float)(lat * Mathf.Rad2Deg)), 0f, 90f));
                azimuthDeg = lat >= 0.0 ? 180f : 0f;
            }

            IPlanetSimulation sim = simulation;
            int searchTile = tile;
            bestTaskTile = searchTile;
            bestTask = Task.Run(() => PanelAngleSearch.Best(sim, searchTile));
            previewDirty = true;
            lastPreviewTime = float.NegativeInfinity;
        }

        void Update()
        {
            if (simulation == null)
                return;

            if (bestTask != null && bestTask.IsCompleted)
            {
                if (bestTask.Status == TaskStatus.RanToCompletion && bestTaskTile == tile)
                {
                    best = bestTask.Result;
                    bestReady = true;
                }
                else if (bestTask.IsFaulted)
                {
                    Debug.LogException(bestTask.Exception);
                }
                bestTask = null;
            }

            if (tile >= 0)
            {
                if (Input.GetKeyDown(placeKey)) Place();
                if (Input.GetKeyDown(bestKey)) UseBestAngle();
                if (Input.GetKeyDown(removeKey) || Input.GetKeyDown(KeyCode.Backspace)) Remove();
            }

            if (previewDirty && tile >= 0 && Time.unscaledTime - lastPreviewTime >= previewInterval)
            {
                preview = simulation.PreviewPanel(tile, tiltDeg * Mathf.Deg2Rad, azimuthDeg * Mathf.Deg2Rad);
                previewDirty = false;
                lastPreviewTime = Time.unscaledTime;
            }

            UpdateGhost();
        }

        void UpdateGhost()
        {
            if (ghost == null)
                return;
            if (tile < 0 || placed.ContainsKey(tile))
            {
                ghost.Hide();
                return;
            }
            ghost.Show(planet.Sphere, tile, planet.Radius, tiltDeg * Mathf.Deg2Rad, azimuthDeg * Mathf.Deg2Rad);
            ghost.SetTint(preview.CanPlace || previewDirty ? ghostCanPlace : ghostBlocked);
        }

        /// <summary>Build a panel on the selected tile at the current angles.</summary>
        public bool Place()
        {
            if (simulation == null || tile < 0 || placed.ContainsKey(tile))
                return false;
            double tilt = tiltDeg * Mathf.Deg2Rad, azimuth = azimuthDeg * Mathf.Deg2Rad;
            if (!simulation.PlacePanel(tile, tilt, azimuth))
                return false;

            PanelPreview p = simulation.PreviewPanel(tile, tilt, azimuth);
            AngleRating rating = bestReady
                ? PanelAngleSearch.Rate(PanelAngleSearch.ShareOfBest(p.EnergyPerYearWh, best.EnergyPerYearWh))
                : AngleRating.Off;
            if (rating == AngleRating.Optimal)
                BestAnglePanels++;

            var visual = new SolarPanelVisual($"Solar Panel {tile}", transform, frameMaterial, cellMaterial);
            visual.Show(planet.Sphere, tile, planet.Radius, tilt, azimuth);
            placed[tile] = new Placed { Tilt = tilt, Azimuth = azimuth, Preview = p, Rating = rating, Visual = visual };
            preview = p;
            ghost.Hide();
            return true;
        }

        public bool Remove()
        {
            if (simulation == null || tile < 0 || !placed.TryGetValue(tile, out Placed p))
                return false;
            if (!simulation.RemovePanel(tile))
                return false;
            if (p.Rating == AngleRating.Optimal)
                BestAnglePanels--;
            p.Visual.Destroy();
            placed.Remove(tile);
            previewDirty = true;
            return true;
        }

        void UseBestAngle()
        {
            if (!bestReady || !best.Found || placed.ContainsKey(tile))
                return;
            tiltDeg = Mathf.Round((float)(best.Tilt * Mathf.Rad2Deg));
            azimuthDeg = Mathf.Round((float)(best.Azimuth * Mathf.Rad2Deg)) % 360f;
            previewDirty = true;
            lastPreviewTime = float.NegativeInfinity;
        }

        void OnGUI()
        {
            if (simulation == null || tile < 0)
                return;
            EnsureStyles();

            bool hasPanel = placed.TryGetValue(tile, out Placed current);
            float height = hasPanel ? 196f : 292f;
            var rect = new Rect(12f, 124f, WindowWidth, height);
            ScreenInput.Block(rect);
            GUI.Box(rect, GUIContent.none, boxStyle);

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
            if (hasPanel)
                DrawPlaced(current);
            else
                DrawPlanner();
            GUILayout.FlexibleSpace();
            GUILayout.Label(Totals(), smallStyle);
            GUILayout.EndArea();
        }

        void DrawPlanner()
        {
            GUILayout.Label("<b>Plan a solar panel</b>", labelStyle);

            GUILayout.Label($"Tilt β  {tiltDeg:0}°  <size=11>(0° flat, 90° upright)</size>", labelStyle);
            float newTilt = Mathf.Round(GUILayout.HorizontalSlider(tiltDeg, 0f, 90f));
            GUILayout.Space(4f);
            GUILayout.Label($"Facing γ  {azimuthDeg:0}° {Compass(azimuthDeg)}", labelStyle);
            float newAzimuth = Mathf.Round(GUILayout.HorizontalSlider(azimuthDeg, 0f, 359f));
            if (!Mathf.Approximately(newTilt, tiltDeg) || !Mathf.Approximately(newAzimuth, azimuthDeg))
            {
                tiltDeg = newTilt;
                azimuthDeg = newAzimuth;
                previewDirty = true;
            }
            GUILayout.Space(6f);

            if (!preview.CanPlace && !previewDirty)
            {
                GUILayout.Label($"<color=#ff9a8a>Can't build here: {preview.Reason}</color>", labelStyle);
                return;
            }

            DrawNumbers(preview);
            GUILayout.Space(4f);
            DrawBest(preview.EnergyPerYearWh);

            GUILayout.BeginHorizontal();
            GUI.enabled = bestReady && best.Found;
            if (GUILayout.Button("Best angle (B)"))
                UseBestAngle();
            GUI.enabled = preview.CanPlace;
            if (GUILayout.Button("<b>Place panel (P)</b>", ButtonStyle()))
                Place();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        void DrawPlaced(Placed p)
        {
            GUILayout.Label("<b>Solar panel</b>", labelStyle);
            GUILayout.Label($"Tilt {p.Tilt * Mathf.Rad2Deg:0}° · facing {p.Azimuth * Mathf.Rad2Deg:0}° {Compass((float)(p.Azimuth * Mathf.Rad2Deg))}", labelStyle);
            DrawNumbers(p.Preview);
            if (p.Rating == AngleRating.Optimal)
                GUILayout.Label("<color=#ffd27a>★ Best-angle bonus</color>", labelStyle);
            if (GUILayout.Button("Remove panel (Del)"))
                Remove();
        }

        void DrawNumbers(PanelPreview p)
        {
            GUILayout.Label(
                $"Energy  <b>{FormatEnergy(p.EnergyPerYearWh)}</b> / year\n" +
                $"Capacity factor  {p.CapacityFactor * 100.0:0.0} %\n" +
                $"EROI  {p.Eroi:0.0}  ·  payback  {FormatYears(p.PaybackYears)}", labelStyle);
        }

        void DrawBest(double energy)
        {
            if (!bestReady)
            {
                GUILayout.Label("<size=11>Finding the best angle…</size>", smallStyle);
                return;
            }
            if (!best.Found)
                return;
            double share = PanelAngleSearch.ShareOfBest(energy, best.EnergyPerYearWh);
            AngleRating rating = PanelAngleSearch.Rate(share);
            string verdict = rating == AngleRating.Optimal ? "<color=#ffd27a>★ best-angle bonus</color>"
                : rating == AngleRating.Good ? "close" : "<color=#ff9a8a>far off</color>";
            GUILayout.Label(
                $"Best here: β {best.Tilt * Mathf.Rad2Deg:0}°, γ {best.Azimuth * Mathf.Rad2Deg:0}° → {FormatEnergy(best.EnergyPerYearWh)}\n" +
                $"Yours: <b>{share * 100.0:0} %</b> of best · {verdict}", labelStyle);
        }

        string Totals()
        {
            if (placed.Count == 0)
                return "No panels yet";
            return $"Panels {placed.Count} ({BestAnglePanels} at best angle) · now {FormatPower(simulation.GenerationW)}";
        }

        void EnsureStyles()
        {
            if (boxStyle != null)
                return;
            boxStyle = new GUIStyle(GUI.skin.box);
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.93f, 1f) },
            };
            smallStyle = new GUIStyle(labelStyle) { fontSize = 11, normal = { textColor = new Color(0.7f, 0.75f, 0.85f) } };
        }

        GUIStyle buttonStyle;
        GUIStyle ButtonStyle() => buttonStyle ??= new GUIStyle(GUI.skin.button) { richText = true };

        static string Compass(float azimuthDeg)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(Mathf.Repeat(azimuthDeg, 360f) / 45f) % 8];
        }

        static string FormatEnergy(double wh)
        {
            if (wh >= 1e9) return $"{wh / 1e9:0.00} GWh";
            if (wh >= 1e6) return $"{wh / 1e6:0.0} MWh";
            return $"{wh / 1e3:0} kWh";
        }

        static string FormatPower(double w)
        {
            if (w >= 1e9) return $"{w / 1e9:0.00} GW";
            if (w >= 1e6) return $"{w / 1e6:0.00} MW";
            return $"{w / 1e3:0} kW";
        }

        static string FormatYears(double years) =>
            double.IsInfinity(years) || double.IsNaN(years) ? "never" : $"{years:0.0} years";
    }
}

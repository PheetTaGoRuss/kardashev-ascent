using System.Collections.Generic;
using System.Threading.Tasks;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Panel placement (#7) on a zoomed-in site (<see cref="SiteView"/>). Click a slot and a see-through panel
    /// appears on it; set its tilt β and facing γ with the sliders and the energy per year, capacity factor, EROI and
    /// payback update live (from <see cref="IPlanetSimulation.PreviewPanel"/>). The best angle for the tile is searched
    /// in the background (<see cref="PanelAngleSearch"/>); placing within 98 % of it earns the best-angle bonus.
    /// The angles carry over from slot to slot on the same tile, so a row of panels is click, P, click, P
    /// (or Shift+click to build straight away). Keys: P place, B use best angle, Delete remove.
    /// </summary>
    [RequireComponent(typeof(PlanetView))]
    public sealed class PanelPlacer : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] SiteView siteView;
        [SerializeField] Material frameMaterial;
        [SerializeField] Material cellMaterial;
        [Tooltip("See-through material for the placement preview (KardashevAscent/GhostTint).")]
        [SerializeField] Material ghostMaterial;

        [SerializeField] KeyCode placeKey = KeyCode.P;
        [SerializeField] KeyCode bestKey = KeyCode.B;
        [SerializeField] KeyCode removeKey = KeyCode.Delete;
        [SerializeField] Color ghostCanPlace = new Color(0.45f, 0.85f, 1f, 0.5f);
        [SerializeField] Color ghostBlocked = new Color(1f, 0.38f, 0.3f, 0.5f);
        [Tooltip("Panel width as a share of the slot spacing.")]
        [Range(0.3f, 1f)] [SerializeField] float panelScale = 0.78f;
        [Tooltip("Seconds between live previews while a slider moves.")]
        [Range(0f, 0.5f)] [SerializeField] float previewInterval = 0.05f;

        PlanetView planet;
        IPlanetSimulation simulation;
        readonly Dictionary<(int Tile, int Slot), Placed> placed = new Dictionary<(int, int), Placed>();
        SolarPanelVisual ghost;

        int tile = -1, slot = -1;
        float tiltDeg, azimuthDeg;
        PanelPreview preview;
        bool previewDirty;
        float lastPreviewTime = float.NegativeInfinity;

        Task<PanelAngles> bestTask;
        int bestTaskTile = -1;
        PanelAngles best;
        bool bestReady;

        struct Placed
        {
            public double Tilt, Azimuth;
            public PanelPreview Preview;
            public AngleRating Rating;
            public SolarPanelVisual Visual;
        }

        /// <summary>The tile whose site is open, or −1.</summary>
        public int Tile => tile;

        /// <summary>The best angles for <see cref="Tile"/>, once the background search has found them.</summary>
        public bool TryGetBest(out PanelAngles angles)
        {
            angles = best;
            return tile >= 0 && bestReady && best.Found;
        }

        /// <summary>Angles to show for the open site: the selected slot's panel, or the angles being planned (radians).</summary>
        public bool TryGetAngles(out double tilt, out double azimuth)
        {
            if (tile >= 0 && slot >= 0 && placed.TryGetValue((tile, slot), out Placed p))
            {
                tilt = p.Tilt;
                azimuth = p.Azimuth;
                return true;
            }
            tilt = tiltDeg * Mathf.Deg2Rad;
            azimuth = azimuthDeg * Mathf.Deg2Rad;
            return tile >= 0;
        }

        /// <summary>Panels placed at 98 % or more of their tile's best yield.</summary>
        public int BestAnglePanels { get; private set; }
        public int PanelCount => placed.Count;

        void Awake()
        {
            planet = GetComponent<PlanetView>();
            if (siteView == null)
                siteView = GetComponent<SiteView>();
        }

        void OnEnable()
        {
            if (siteView != null)
            {
                siteView.SiteChanged += OnSiteChanged;
                siteView.SelectedSlotChanged += OnSlotSelected;
            }
            if (runner != null)
                runner.SimulationCreated += OnSimulationCreated;
        }

        void OnDisable()
        {
            if (siteView != null)
            {
                siteView.SiteChanged -= OnSiteChanged;
                siteView.SelectedSlotChanged -= OnSlotSelected;
            }
            if (runner != null)
                runner.SimulationCreated -= OnSimulationCreated;
        }

        void Start()
        {
            if (runner == null || siteView == null)
            {
                Debug.LogError("PanelPlacer needs a SimulationRunner and a SiteView.", this);
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
            OnSiteChanged(siteView.Site != null ? siteView.Site.Tile : -1);
        }

        void OnSiteChanged(int newTile)
        {
            tile = newTile;
            slot = -1;
            bestReady = false;
            bestTask = null;
            ghost?.Hide();
            if (simulation == null || tile < 0)
                return;

            // Rule of thumb until the search reports: tilt = |latitude|, facing the equator.
            double lat = simulation.Sphere.Latitude(tile);
            tiltDeg = Mathf.Round(Mathf.Clamp(Mathf.Abs((float)(lat * Mathf.Rad2Deg)), 0f, 90f));
            azimuthDeg = lat >= 0.0 ? 180f : 0f;

            IPlanetSimulation sim = simulation;
            int searchTile = tile;
            bestTaskTile = searchTile;
            bestTask = Task.Run(() => PanelAngleSearch.Best(sim, searchTile));
            previewDirty = true;
            lastPreviewTime = float.NegativeInfinity;
        }

        void OnSlotSelected(int newSlot)
        {
            slot = newSlot;
            if (slot < 0)
                return;
            if (placed.TryGetValue((tile, slot), out Placed p))
            {
                preview = p.Preview;
                return;
            }
            previewDirty = true;
            lastPreviewTime = float.NegativeInfinity;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                Place();
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
                    RateTilePanels();
                }
                else if (bestTask.IsFaulted)
                {
                    Debug.LogException(bestTask.Exception);
                }
                bestTask = null;
            }

            if (slot >= 0)
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

        bool SlotHasPanel => placed.ContainsKey((tile, slot));

        AngleRating RatingOf(PanelPreview p) => bestReady && best.Found
            ? PanelAngleSearch.Rate(PanelAngleSearch.ShareOfBest(p.EnergyPerYearWh, best.EnergyPerYearWh))
            : AngleRating.Off;

        /// <summary>Panels built before this tile's best angle was known get rated once it is.</summary>
        void RateTilePanels()
        {
            var keys = new List<(int Tile, int Slot)>();
            foreach (var key in placed.Keys)
                if (key.Tile == tile)
                    keys.Add(key);
            foreach (var key in keys)
            {
                Placed p = placed[key];
                AngleRating rating = RatingOf(p.Preview);
                if (rating == p.Rating)
                    continue;
                if (p.Rating == AngleRating.Optimal) BestAnglePanels--;
                if (rating == AngleRating.Optimal) BestAnglePanels++;
                p.Rating = rating;
                placed[key] = p;
            }
        }

        void UpdateGhost()
        {
            if (ghost == null)
                return;
            if (tile < 0 || slot < 0 || SlotHasPanel || siteView.Site == null)
            {
                ghost.Hide();
                return;
            }
            ShowOnSlot(ghost, siteView.Site, slot, tiltDeg * Mathf.Deg2Rad, azimuthDeg * Mathf.Deg2Rad);
            ghost.SetTint(preview.CanPlace || previewDirty ? ghostCanPlace : ghostBlocked);
        }

        void ShowOnSlot(SolarPanelVisual visual, TileSite site, int s, double tilt, double azimuth)
        {
            System.Numerics.Vector3 d = site.SlotDirections[s];
            var direction = new Vector3(d.X, d.Y, d.Z);
            // Every slot is a whole one, so every panel is the same size.
            float size = (float)site.Spacing * planet.Radius * panelScale;
            visual.Show(direction, siteView.GroundRadius(direction), size, tilt, azimuth);
        }

        /// <summary>Build a panel in the selected slot at the current angles.</summary>
        public bool Place()
        {
            if (simulation == null || tile < 0 || slot < 0 || SlotHasPanel || siteView.Site == null)
                return false;
            double tilt = tiltDeg * Mathf.Deg2Rad, azimuth = azimuthDeg * Mathf.Deg2Rad;
            if (!simulation.PlacePanel(tile, slot, tilt, azimuth))
                return false;

            PanelPreview p = simulation.PreviewPanel(tile, tilt, azimuth);
            AngleRating rating = RatingOf(p);
            if (rating == AngleRating.Optimal)
                BestAnglePanels++;

            var visual = new SolarPanelVisual($"Solar Panel {tile}.{slot}", transform, frameMaterial, cellMaterial);
            ShowOnSlot(visual, siteView.Site, slot, tilt, azimuth);
            placed[(tile, slot)] = new Placed { Tilt = tilt, Azimuth = azimuth, Preview = p, Rating = rating, Visual = visual };
            preview = p;
            ghost.Hide();
            return true;
        }

        public bool Remove()
        {
            if (simulation == null || slot < 0 || !placed.TryGetValue((tile, slot), out Placed p))
                return false;
            if (!simulation.RemovePanel(tile, slot))
                return false;
            if (p.Rating == AngleRating.Optimal)
                BestAnglePanels--;
            p.Visual.Destroy();
            placed.Remove((tile, slot));
            previewDirty = true;
            return true;
        }

        /// <summary>Set the planned angles to the best ones found for this tile.</summary>
        public void UseBestAngle()
        {
            if (!bestReady || !best.Found || SlotHasPanel)
                return;
            tiltDeg = Mathf.Round((float)(best.Tilt * Mathf.Rad2Deg));
            azimuthDeg = Mathf.Round((float)(best.Azimuth * Mathf.Rad2Deg)) % 360f;
            previewDirty = true;
            lastPreviewTime = float.NegativeInfinity;
        }

        // ---- For the planner panel (PanelPlannerView) ----

        /// <summary>The selected slot of the open site, or −1.</summary>
        public int Slot => slot;

        /// <summary>Planned tilt β, degrees (0–90). Setting it updates the preview and the ghost.</summary>
        public float TiltDeg
        {
            get => tiltDeg;
            set { float v = Mathf.Clamp(Mathf.Round(value), 0f, 90f); if (v != tiltDeg) { tiltDeg = v; previewDirty = true; } }
        }

        /// <summary>Planned facing γ, degrees clockwise from north (0–359).</summary>
        public float AzimuthDeg
        {
            get => azimuthDeg;
            set { float v = Mathf.Repeat(Mathf.Round(value), 360f); if (v != azimuthDeg) { azimuthDeg = v; previewDirty = true; } }
        }

        /// <summary>The live preview for the planned angles; <see cref="PreviewPending"/> while it is being redone.</summary>
        public PanelPreview Preview => preview;
        public bool PreviewPending => previewDirty;

        /// <summary>The panel in the selected slot, if there is one.</summary>
        public bool TryGetSelectedPanel(out double tilt, out double azimuth, out PanelPreview panelPreview, out AngleRating rating)
        {
            if (tile >= 0 && slot >= 0 && placed.TryGetValue((tile, slot), out Placed p))
            {
                tilt = p.Tilt;
                azimuth = p.Azimuth;
                panelPreview = p.Preview;
                rating = p.Rating;
                return true;
            }
            tilt = azimuth = 0.0;
            panelPreview = default;
            rating = AngleRating.Off;
            return false;
        }

        /// <summary>How close the planned angles are to the best (0 when the best is not known yet).</summary>
        public double PlannedShareOfBest => bestReady && best.Found
            ? PanelAngleSearch.ShareOfBest(preview.EnergyPerYearWh, best.EnergyPerYearWh) : 0.0;

        /// <summary>Total power being generated now (panels and RTGs), W.</summary>
        public double GenerationW => simulation != null ? simulation.GenerationW : 0.0;

        /// <summary>Panels on the open site's tile.</summary>
        public int PanelsOnTile => simulation != null && tile >= 0 ? simulation.PanelCount(tile) : 0;

        public KeyCode PlaceKey => placeKey;
        public KeyCode BestKey => bestKey;
    }
}

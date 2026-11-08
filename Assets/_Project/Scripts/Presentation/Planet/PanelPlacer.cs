using System;
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
    /// The angles carry over from slot to slot on the same tile. With several slots selected (drag a box in the site
    /// view), P builds on every empty one, Delete removes every panel in the selection, and "set angle" re-builds the
    /// selected panels at the planned angles (turning a panel is free). Building takes build parts
    /// (<see cref="IPlanetSimulation.BuildCostWh"/>); without enough, a building stands as a see-through construction
    /// site until spare power has made the rest, then it turns into the real thing.
    /// Keys: P place, B use best angle, Delete remove.
    /// </summary>
    /// <summary>What the placer builds.</summary>
    public enum BuildMode
    {
        SolarPanel,
        Battery,
        /// <summary>Robot Workshop (phase 3b).</summary>
        Workshop,
        /// <summary>Robot charging station (spec v2 §7).</summary>
        ChargingStation,
    }

    [RequireComponent(typeof(PlanetView))]
    public sealed class PanelPlacer : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] SiteView siteView;
        [SerializeField] SaveGameController saveGame;
        [SerializeField] Material frameMaterial;
        [SerializeField] Material cellMaterial;
        [Tooltip("See-through material for the placement preview (KardashevAscent/GhostTint).")]
        [SerializeField] Material ghostMaterial;

        [SerializeField] KeyCode placeKey = KeyCode.P;
        [SerializeField] KeyCode bestKey = KeyCode.B;
        [SerializeField] KeyCode removeKey = KeyCode.Delete;
        [SerializeField] Color ghostCanPlace = new Color(0.45f, 0.85f, 1f, 0.5f);
        [SerializeField] Color ghostBlocked = new Color(1f, 0.38f, 0.3f, 0.5f);
        [Tooltip("Tint of a construction site waiting for build parts.")]
        [SerializeField] Color siteTint = new Color(1f, 0.72f, 0.25f, 0.55f);
        [Tooltip("Panel width as a share of the slot spacing.")]
        [Range(0.3f, 1f)] [SerializeField] float panelScale = 0.78f;
        [Tooltip("Seconds between live previews while a slider moves.")]
        [Range(0f, 0.5f)] [SerializeField] float previewInterval = 0.05f;

        PlanetView planet;
        IPlanetSimulation simulation;
        IPlanetSimulation listeningTo;
        readonly Dictionary<(int Tile, int Slot), Placed> placed = new Dictionary<(int, int), Placed>();
        SolarPanelVisual ghost;
        readonly List<SolarPanelVisual> ghosts = new List<SolarPanelVisual>();   // one per empty selected slot
        readonly Dictionary<(int Tile, int Slot), BatteryVisual> batteryVisuals = new Dictionary<(int, int), BatteryVisual>();
        readonly List<BatteryVisual> batteryGhosts = new List<BatteryVisual>();
        BuildMode mode = BuildMode.SolarPanel;

        /// <summary>What P builds: solar panels, battery yards, a Robot Workshop or a charging station. Tab switches.</summary>
        public BuildMode Mode
        {
            get => mode;
            set => mode = value;
        }

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
            return tile >= 0 && bestReady && best.Found && BestAngleUnlocked;
        }

        /// <summary>
        /// Best-angle advice needs this tile's latitude: measured in the sky view (V), or surveyed after the
        /// colony's first latitude fix. Until then the search still runs, but nothing about it is shown.
        /// </summary>
        public bool BestAngleUnlocked => simulation != null && tile >= 0 && simulation.Notebook.CanUseBestAngle(tile);

        /// <summary>This tile's latitude as the colony knows it (degrees), or NaN before it is found.</summary>
        public double KnownLatitudeDeg => simulation != null && tile >= 0
            ? simulation.Notebook.LatitudeDeg(tile, simulation.Sphere.Latitude(tile) * 180.0 / Math.PI)
            : double.NaN;

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
                siteView.SelectionChanged += OnSelectionChanged;
            }
            if (runner != null)
                runner.SimulationCreated += OnSimulationCreated;
            if (saveGame != null)
                saveGame.Loaded += RebuildFromSimulation;
            Listen(simulation);
        }

        void OnDisable()
        {
            if (siteView != null)
            {
                siteView.SiteChanged -= OnSiteChanged;
                siteView.SelectedSlotChanged -= OnSlotSelected;
                siteView.SelectionChanged -= OnSelectionChanged;
            }
            if (runner != null)
                runner.SimulationCreated -= OnSimulationCreated;
            if (saveGame != null)
                saveGame.Loaded -= RebuildFromSimulation;
            Listen(null);
        }

        /// <summary>Follow one simulation's "site finished" event (and stop following the last one).</summary>
        void Listen(IPlanetSimulation sim)
        {
            if (listeningTo == sim)
                return;
            if (listeningTo != null)
            {
                listeningTo.BuildingCompleted -= OnBuildingCompleted;
                listeningTo.BuildingSalvaged -= RemoveVisual;
            }
            listeningTo = sim;
            if (listeningTo != null)
            {
                listeningTo.BuildingCompleted += OnBuildingCompleted;
                listeningTo.BuildingSalvaged += RemoveVisual;
            }
        }

        /// <summary>A construction site got its parts: swap its see-through look for the real building.</summary>
        void OnBuildingCompleted(int doneTile, int doneSlot)
        {
            TileSite site = siteView.SiteFor(doneTile);
            if (placed.TryGetValue((doneTile, doneSlot), out Placed p))
            {
                p.Visual.Destroy();
                p.Visual = MakePanel(doneTile, doneSlot, false);
                ShowOnSlot(p.Visual, site, doneSlot, p.Tilt, p.Azimuth);
                placed[(doneTile, doneSlot)] = p;
            }
            else if (batteryVisuals.TryGetValue((doneTile, doneSlot), out BatteryVisual yard))
            {
                yard.Destroy();
                yard = MakeBattery(doneTile, doneSlot, false);
                ShowBattery(yard, site, doneSlot);
                batteryVisuals[(doneTile, doneSlot)] = yard;
            }
        }

        /// <summary>Cell colours of the bigger tiers (farm, park, array); the panel field keeps its material's colour.</summary>
        static readonly Color[] TierCells =
        {
            Color.white,
            new Color(0.30f, 0.22f, 0.55f),
            new Color(0.08f, 0.40f, 0.42f),
            new Color(0.10f, 0.10f, 0.13f),
        };

        SolarPanelVisual MakePanel(int t, int s, bool constructionSite)
        {
            if (!constructionSite)
            {
                var panel = new SolarPanelVisual($"Solar Panel {t}.{s}", transform, frameMaterial, cellMaterial);
                int tier = TierAt(t, s);
                if (tier > 0)
                    panel.SetCellTint(TierCells[Mathf.Min(tier, TierCells.Length - 1)]);
                return panel;
            }
            var visual = new SolarPanelVisual($"Solar Panel Site {t}.{s}", transform, ghostMaterial, ghostMaterial);
            visual.SetTint(siteTint);
            return visual;
        }

        int TierAt(int t, int s)
        {
            double area = simulation != null ? simulation.PanelAreaAt(t, s) : 0.0;
            double baseArea = simulation != null ? simulation.NewPanelAreaM2(-1) / PanelTiers.All[simulation.PanelTier].Scale : 0.0;
            return area > 0.0 && baseArea > 0.0 ? PanelTiers.OfScale(area / baseArea) : 0;
        }

        /// <summary>
        /// Numbers for the panel in a slot at these angles. The simulation previews a new panel (today's tier), so an
        /// older, smaller one is scaled to its own area (energy only: the ratios don't depend on size).
        /// </summary>
        PanelPreview PreviewBuilt(int t, int s, double tilt, double azimuth)
        {
            PanelPreview p = simulation.PreviewPanel(t, tilt, azimuth);
            double newArea = simulation.NewPanelAreaM2(t), area = simulation.PanelAreaAt(t, s);
            if (area <= 0.0 || newArea <= 0.0 || Math.Abs(area - newArea) < 1e-6 * newArea)
                return p;
            return new PanelPreview(p.CanPlace, p.Reason, p.EnergyPerYearWh * area / newArea, p.CapacityFactor, p.Eroi, p.PaybackYears);
        }

        /// <summary>A battery yard, a Workshop or a charging station: the same blocks in different colours.</summary>
        BatteryVisual MakeBattery(int t, int s, bool constructionSite)
        {
            BuildingType type = simulation != null ? simulation.GetBuilding(t, s) : BuildingType.Battery;
            string what = type == BuildingType.Workshop ? "Workshop" : type == BuildingType.ChargingStation ? "Charger" : "Battery";
            if (!constructionSite)
                return type == BuildingType.Workshop ? new BatteryVisual($"{what} {t}.{s}", transform, cellMaterial, frameMaterial)
                     : type == BuildingType.ChargingStation ? new BatteryVisual($"{what} {t}.{s}", transform, frameMaterial, frameMaterial)
                     : new BatteryVisual($"{what} {t}.{s}", transform, frameMaterial, cellMaterial);
            var visual = new BatteryVisual($"{what} Site {t}.{s}", transform, ghostMaterial, ghostMaterial);
            visual.SetTint(siteTint);
            return visual;
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
            foreach (SolarPanelVisual g in ghosts)
                g.Destroy();
            foreach (BatteryVisual g in batteryGhosts)
                g.Destroy();
            foreach (Placed p in placed.Values)
                p.Visual.Destroy();
            foreach (BatteryVisual b in batteryVisuals.Values)
                b.Destroy();
        }

        void OnSimulationCreated()
        {
            // A new simulation starts with no panels.
            foreach (Placed p in placed.Values)
                p.Visual.Destroy();
            placed.Clear();
            foreach (BatteryVisual b in batteryVisuals.Values)
                b.Destroy();
            batteryVisuals.Clear();
            BestAnglePanels = 0;
            simulation = runner.Simulation;
            Listen(simulation);
            OnSiteChanged(siteView.Site != null ? siteView.Site.Tile : -1);
        }

        /// <summary>Re-draw every building from the simulation (after a load).</summary>
        public void RebuildFromSimulation()
        {
            if (runner == null || runner.Simulation == null)
                return;
            foreach (Placed p in placed.Values)
                p.Visual.Destroy();
            placed.Clear();
            foreach (BatteryVisual b in batteryVisuals.Values)
                b.Destroy();
            batteryVisuals.Clear();
            BestAnglePanels = 0;
            simulation = runner.Simulation;
            Listen(simulation);
            foreach (BuildingRecord r in simulation.CaptureState().Buildings)
            {
                TileSite site = siteView.SiteFor(r.Tile);
                if (r.Type == BuildingType.Battery || r.Type == BuildingType.Workshop || r.Type == BuildingType.ChargingStation)
                {
                    BatteryVisual yard = MakeBattery(r.Tile, r.Slot, r.UnderConstruction);
                    ShowBattery(yard, site, r.Slot);
                    batteryVisuals[(r.Tile, r.Slot)] = yard;
                }
                else if (r.Type == BuildingType.SolarPanel)
                {
                    PanelPreview p = PreviewBuilt(r.Tile, r.Slot, r.Tilt, r.Azimuth);
                    AngleRating rating = r.Tile == tile ? RatingOf(p) : AngleRating.Off;
                    if (rating == AngleRating.Optimal)
                        BestAnglePanels++;
                    SolarPanelVisual visual = MakePanel(r.Tile, r.Slot, r.UnderConstruction);
                    ShowOnSlot(visual, site, r.Slot, r.Tilt, r.Azimuth);
                    placed[(r.Tile, r.Slot)] = new Placed { Tilt = r.Tilt, Azimuth = r.Azimuth, Preview = p, Rating = rating, Visual = visual };
                }
            }
            previewDirty = true;
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
        }

        void OnSelectionChanged()
        {
            if (MultiSelect)
            {
                previewDirty = true;
                lastPreviewTime = float.NegativeInfinity;
            }
        }

        /// <summary>True when more than one slot is selected.</summary>
        public bool MultiSelect => siteView != null && siteView.Selection.Count > 1;

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

            if (tile >= 0 && Input.GetKeyDown(KeyCode.Tab))
                mode = mode == BuildMode.SolarPanel ? BuildMode.Battery : mode == BuildMode.Battery ? BuildMode.Workshop
                     : mode == BuildMode.Workshop ? BuildMode.ChargingStation : BuildMode.SolarPanel;

            if (MultiSelect)
            {
                if (Input.GetKeyDown(placeKey)) PlaceSelection();
                if (Input.GetKeyDown(bestKey)) UseBestAngle();
                if (Input.GetKeyDown(removeKey) || Input.GetKeyDown(KeyCode.Backspace)) RemoveSelection();
            }
            else if (slot >= 0)
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
        bool Occupied(int s) => placed.ContainsKey((tile, s)) || batteryVisuals.ContainsKey((tile, s));

        /// <summary>What stands in the selected slot (single selection).</summary>
        public BuildingType SelectedBuilding =>
            simulation != null && tile >= 0 && slot >= 0 ? simulation.GetBuilding(tile, slot) : BuildingType.None;

        /// <summary>Storage one battery yard adds, Wh.</summary>
        public double BatteryUnitWh => simulation != null ? simulation.BatteryUnitWh : 0.0;

        /// <summary>Build parts one building of the current mode costs, Wh.</summary>
        public double BuildCostWh => simulation != null
            ? simulation.BuildCostWh(mode == BuildMode.Battery ? BuildingType.Battery : mode == BuildMode.Workshop ? BuildingType.Workshop
                                     : mode == BuildMode.ChargingStation ? BuildingType.ChargingStation : BuildingType.SolarPanel) : 0.0;

        /// <summary>What a new solar building here is, e.g. "solar farm (1 km²)".</summary>
        public string NewPanelName => simulation == null ? "solar panel"
            : $"{PanelTiers.All[simulation.PanelTier].Name.ToLowerInvariant()} ({PanelTiers.FormatArea(simulation.NewPanelAreaM2(tile))})";

        /// <summary>What the selected solar building is, e.g. "Panel field (1 ha)".</summary>
        public string SelectedPanelName
        {
            get
            {
                if (simulation == null || tile < 0 || slot < 0) return "Solar panel";
                double area = simulation.PanelAreaAt(tile, slot);
                return $"{PanelTiers.All[TierAt(tile, slot)].Name} ({PanelTiers.FormatArea(area)})";
            }
        }

        /// <summary>Build parts on hand, Wh.</summary>
        public double BuildStockWh => simulation != null ? simulation.BuildStockWh : 0.0;

        /// <summary>True while the selected slot's building is a construction site.</summary>
        public bool SelectedIsSite => simulation != null && tile >= 0 && slot >= 0 && simulation.IsUnderConstruction(tile, slot);

        /// <summary>How much of the selected slot's site is paid for (0–1), and the parts it still needs, Wh.</summary>
        public void SelectedSiteProgress(out double progress, out double remainingWh)
        {
            progress = 1.0;
            remainingWh = 0.0;
            BuildingType type = SelectedBuilding;
            if (!SelectedIsSite || type == BuildingType.None)
                return;
            progress = simulation.ConstructionProgress(tile, slot);
            remainingWh = (1.0 - progress) * simulation.BuildCostWh(type);
        }

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
            Color tint = preview.CanPlace || previewDirty ? ghostCanPlace : ghostBlocked;
            double tilt = tiltDeg * Mathf.Deg2Rad, azimuth = azimuthDeg * Mathf.Deg2Rad;
            bool battery = mode != BuildMode.SolarPanel;

            // One ghost per empty slot that P would build on: the whole selection, or the one selected slot.
            var targets = new List<int>();
            if (siteView.Site != null && tile >= 0)
            {
                if (MultiSelect)
                {
                    foreach (int s in siteView.Selection)
                        if (!Occupied(s))
                            targets.Add(s);
                }
                else if (slot >= 0 && !Occupied(slot))
                {
                    targets.Add(slot);
                }
            }

            int panels = 0, yards = 0;
            foreach (int s in targets)
            {
                if (battery)
                {
                    if (yards == batteryGhosts.Count)
                        batteryGhosts.Add(new BatteryVisual("Battery Ghost", transform, ghostMaterial, ghostMaterial));
                    ShowBattery(batteryGhosts[yards], siteView.Site, s);
                    batteryGhosts[yards].SetTint(tint);
                    yards++;
                }
                else
                {
                    if (panels == ghosts.Count)
                        ghosts.Add(new SolarPanelVisual("Panel Ghost", transform, ghostMaterial, ghostMaterial));
                    ShowOnSlot(ghosts[panels], siteView.Site, s, tilt, azimuth);
                    ghosts[panels].SetTint(tint);
                    panels++;
                }
            }
            for (int i = panels; i < ghosts.Count; i++)
                ghosts[i].Hide();
            for (int i = yards; i < batteryGhosts.Count; i++)
                batteryGhosts[i].Hide();
            ghost.Hide();
        }

        void ShowBattery(BatteryVisual visual, TileSite site, int s)
        {
            System.Numerics.Vector3 d = site.SlotDirections[s];
            var direction = new Vector3(d.X, d.Y, d.Z);
            float size = (float)site.Spacing * planet.Radius * panelScale;
            visual.Show(direction, siteView.GroundRadius(site, direction), size);
        }

        void ShowOnSlot(SolarPanelVisual visual, TileSite site, int s, double tilt, double azimuth)
        {
            System.Numerics.Vector3 d = site.SlotDirections[s];
            var direction = new Vector3(d.X, d.Y, d.Z);
            // Every slot is a whole one, so every panel is the same size.
            float size = (float)site.Spacing * planet.Radius * panelScale;
            visual.Show(direction, siteView.GroundRadius(site, direction), size, tilt, azimuth);
        }

        /// <summary>Build in the selected slot: a panel at the current angles, or a battery yard.</summary>
        public bool Place() => PlaceAt(slot);

        /// <summary>Remove whatever stands in the selected slot.</summary>
        public bool Remove() => RemoveAt(slot);

        /// <summary>Build on every empty selected slot (panels at the planned angles, or batteries). Returns how many.</summary>
        public int PlaceSelection()
        {
            int built = 0;
            foreach (int s in new List<int>(siteView.Selection))
                if (!Occupied(s) && PlaceAt(s))
                    built++;
            return built;
        }

        /// <summary>Remove every building in the selection. Returns how many were removed.</summary>
        public int RemoveSelection()
        {
            int removed = 0;
            foreach (int s in new List<int>(siteView.Selection))
                if (RemoveAt(s))
                    removed++;
            return removed;
        }

        /// <summary>Turn the selected panels to the planned angles (free: nothing is rebuilt). Returns how many were changed.</summary>
        public int ReangleSelection()
        {
            if (simulation == null || siteView.Site == null)
                return 0;
            double tilt = tiltDeg * Mathf.Deg2Rad, azimuth = azimuthDeg * Mathf.Deg2Rad;
            int changed = 0;
            foreach (int s in new List<int>(siteView.Selection))
            {
                if (!placed.TryGetValue((tile, s), out Placed p) || !simulation.SetPanelAngles(tile, s, tilt, azimuth))
                    continue;
                if (p.Rating == AngleRating.Optimal) BestAnglePanels--;
                p.Tilt = tilt;
                p.Azimuth = azimuth;
                p.Preview = PreviewBuilt(tile, s, tilt, azimuth);
                p.Rating = RatingOf(p.Preview);
                if (p.Rating == AngleRating.Optimal) BestAnglePanels++;
                ShowOnSlot(p.Visual, siteView.Site, s, tilt, azimuth);
                placed[(tile, s)] = p;
                changed++;
            }
            return changed;
        }

        bool PlaceAt(int s)
        {
            if (simulation == null || tile < 0 || s < 0 || Occupied(s) || siteView.Site == null)
                return false;
            if (mode == BuildMode.Battery || mode == BuildMode.Workshop || mode == BuildMode.ChargingStation)
            {
                if (!(mode == BuildMode.Battery ? simulation.PlaceBattery(tile, s)
                      : mode == BuildMode.Workshop ? simulation.PlaceWorkshop(tile, s) : simulation.PlaceChargingStation(tile, s)))
                    return false;
                BatteryVisual yard = MakeBattery(tile, s, simulation.IsUnderConstruction(tile, s));
                ShowBattery(yard, siteView.Site, s);
                batteryVisuals[(tile, s)] = yard;
                return true;
            }
            double tilt = tiltDeg * Mathf.Deg2Rad, azimuth = azimuthDeg * Mathf.Deg2Rad;
            if (!simulation.PlacePanel(tile, s, tilt, azimuth))
                return false;

            PanelPreview p = simulation.PreviewPanel(tile, tilt, azimuth);
            AngleRating rating = RatingOf(p);
            if (rating == AngleRating.Optimal)
                BestAnglePanels++;

            SolarPanelVisual visual = MakePanel(tile, s, simulation.IsUnderConstruction(tile, s));
            ShowOnSlot(visual, siteView.Site, s, tilt, azimuth);
            placed[(tile, s)] = new Placed { Tilt = tilt, Azimuth = azimuth, Preview = p, Rating = rating, Visual = visual };
            preview = p;
            return true;
        }

        /// <summary>
        /// Ask a robot to take down what stands in a slot (a site is cancelled at once). Asking again for a building
        /// still waiting for its robot withdraws the order.
        /// </summary>
        bool RemoveAt(int s)
        {
            if (simulation == null || tile < 0 || s < 0 || simulation.GetBuilding(tile, s) == BuildingType.None)
                return false;
            if (simulation.IsSalvageOrdered(tile, s))
                return simulation.CancelSalvage(tile, s);
            if (!simulation.OrderSalvage(tile, s))
                return false;
            if (simulation.GetBuilding(tile, s) == BuildingType.None)
                RemoveVisual(tile, s);
            return true;
        }

        /// <summary>A building is gone (cancelled, or salvaged by a robot): drop its visual.</summary>
        void RemoveVisual(int atTile, int s)
        {
            if (batteryVisuals.TryGetValue((atTile, s), out BatteryVisual yard))
            {
                yard.Destroy();
                batteryVisuals.Remove((atTile, s));
            }
            if (placed.TryGetValue((atTile, s), out Placed p))
            {
                if (p.Rating == AngleRating.Optimal)
                    BestAnglePanels--;
                p.Visual.Destroy();
                placed.Remove((atTile, s));
            }
            previewDirty = true;
        }

        /// <summary>True while the selected slot's building waits for a robot to take it down.</summary>
        public bool SelectedSalvageOrdered => simulation != null && tile >= 0 && slot >= 0 && simulation.IsSalvageOrdered(tile, slot);

        /// <summary>Set the planned angles to the best ones found for this tile.</summary>
        public void UseBestAngle()
        {
            if (!bestReady || !best.Found || !BestAngleUnlocked || (!MultiSelect && SlotHasPanel))
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

        /// <summary>Selected slots: how many, how many are empty, how many hold panels.</summary>
        public void CountSelection(out int selected, out int empty, out int withPanels, out int withBatteries)
        {
            selected = empty = withPanels = withBatteries = 0;
            if (siteView == null)
                return;
            foreach (int s in siteView.Selection)
            {
                selected++;
                if (placed.ContainsKey((tile, s))) withPanels++;
                else if (batteryVisuals.ContainsKey((tile, s))) withBatteries++;
                else empty++;
            }
        }

        /// <summary>Batteries on the open site's tile.</summary>
        public int BatteriesOnTile => simulation != null && tile >= 0 ? simulation.BatteryCount(tile) : 0;

        public KeyCode PlaceKey => placeKey;
        public KeyCode BestKey => bestKey;
    }
}

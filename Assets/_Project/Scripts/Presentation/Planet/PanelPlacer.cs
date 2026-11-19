using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KardashevAscent.Presentation.Game;
using MaterialBill = KardashevAscent.Simulation.Resources.MaterialBill;
using MaterialStock = KardashevAscent.Simulation.Resources.MaterialStock;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Presentation.UI;
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
    /// selected panels at the planned angles (turning a panel is free). Building takes materials
    /// (<see cref="IConstructionReadout.BuildBill"/>): a building stands as a see-through construction site until a robot
    /// has brought them from the colony's stock, then it turns into the real thing.
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
        /// <summary>Network relay (workforce v2 stage E).</summary>
        Relay,
        /// <summary>Solar panels on pontoons over shallow sea or wetland.</summary>
        FloatingSolar,
        /// <summary>Research Station: measurements and experiments (director 2026-11-14).</summary>
        ResearchStation,
        /// <summary>Refinery module: alumina (director 2026-11-15). Next to a Workshop.</summary>
        AluminaPlant,
        /// <summary>Refinery module: electrolysis. Next to a Workshop.</summary>
        Electrolyser,
        /// <summary>Free-flow tidal turbine on shallow sea (director 2026-11-19).</summary>
        TidalTurbine,
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
        [Tooltip("Tint of a construction site waiting for materials.")]
        [SerializeField] Color siteTint = new Color(1f, 0.72f, 0.25f, 0.55f);
        [Tooltip("Tint of a construction site the stock can't supply yet (director 2026-11-14: red until the materials are there).")]
        [SerializeField] Color shortTint = new Color(1f, 0.22f, 0.18f, 0.6f);
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
            set
            {
                if (value == mode) return;
                mode = value;
                previewDirty = true;   // land and floating panels are checked differently
                lastPreviewTime = float.NegativeInfinity;
            }
        }

        bool dragging;
        bool padsDirty = true;

        /// <summary>Pad colour of a built-on slot: one colour per kind of building, fainter while it is a site.</summary>
        Color32? PadTintOf(int s)
        {
            if (simulation == null || tile < 0)
                return null;
            BuildingType type = simulation.GetBuilding(tile, s);
            if (type == BuildingType.None)
                return null;
            byte a = simulation.IsUnderConstruction(tile, s) ? (byte)90 : (byte)190;
            switch (type)
            {
                case BuildingType.SolarPanel: return new Color32(70, 110, 200, a);
                case BuildingType.FloatingSolar: return new Color32(60, 160, 190, a);
                case BuildingType.Battery: return new Color32(70, 160, 100, a);
                case BuildingType.Workshop: return new Color32(210, 140, 60, a);
                case BuildingType.ChargingStation: return new Color32(200, 190, 70, a);
                case BuildingType.Relay: return new Color32(120, 200, 220, a);
                case BuildingType.ResearchStation: return new Color32(170, 120, 230, a);
                case BuildingType.AluminaPlant: return new Color32(225, 225, 215, a);
                case BuildingType.Electrolyser: return new Color32(230, 110, 70, a);
                case BuildingType.TidalTurbine: return new Color32(40, 190, 170, a);
                default: return new Color32(160, 160, 160, a);
            }
        }

        /// <summary>
        /// Start dragging a building of this kind from the build bar: it follows the mouse over the site and is built
        /// where the button is let go. Letting go over the UI, Esc or the right button cancels.
        /// </summary>
        public void BeginDrag(BuildMode what)
        {
            mode = what;
            dragging = tile >= 0;
        }

        /// <summary>True while a building is being dragged from the build bar.</summary>
        public bool Dragging => dragging;

        /// <summary>True for the two panel modes (they share the tilt and facing controls).</summary>
        public static bool IsPanel(BuildMode m) => m == BuildMode.SolarPanel || m == BuildMode.FloatingSolar;

        static BuildingType TypeOf(BuildMode m) =>
            m == BuildMode.FloatingSolar ? BuildingType.FloatingSolar :
            m == BuildMode.Battery ? BuildingType.Battery : m == BuildMode.Workshop ? BuildingType.Workshop
            : m == BuildMode.ChargingStation ? BuildingType.ChargingStation
            : m == BuildMode.Relay ? BuildingType.Relay
            : m == BuildMode.ResearchStation ? BuildingType.ResearchStation
            : m == BuildMode.AluminaPlant ? BuildingType.AluminaPlant
            : m == BuildMode.Electrolyser ? BuildingType.Electrolyser
            : m == BuildMode.TidalTurbine ? BuildingType.TidalTurbine : BuildingType.SolarPanel;

        /// <summary>Panels aside, every building is a block on its slots (battery yard, Workshop, stations, refinery modules).</summary>
        static bool IsBlock(BuildingType t) =>
            t == BuildingType.Battery || t == BuildingType.Workshop || t == BuildingType.ChargingStation || t == BuildingType.Relay
            || t == BuildingType.ResearchStation || t == BuildingType.AluminaPlant || t == BuildingType.Electrolyser
            || t == BuildingType.TidalTurbine;

        /// <summary>The slot a building covering this one is keyed by (its anchor), or the slot itself when empty.</summary>
        int Anchor(int t, int s)
        {
            if (simulation == null || t < 0 || s < 0) return s;
            int a = simulation.AnchorOf(t, s);
            return a >= 0 ? a : s;
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

        // ---- Re-angling a built panel (a robot does it) ----

        /// <summary>A panel's visual and numbers follow its angles in the simulation (after a robot turned it).</summary>
        void RefreshPanel(int atTile, int s)
        {
            if (!placed.TryGetValue((atTile, s), out Placed p) || !simulation.TryGetPanelAngles(atTile, s, out double t, out double a))
                return;
            if (p.Rating == AngleRating.Optimal) BestAnglePanels--;
            p.Tilt = t;
            p.Azimuth = a;
            p.Preview = PreviewBuilt(atTile, s, t, a);
            p.Rating = atTile == tile ? RatingOf(p.Preview) : AngleRating.Off;
            if (p.Rating == AngleRating.Optimal) BestAnglePanels++;
            ShowOnSlot(p.Visual, siteView.SiteFor(atTile), simulation.FootprintAt(atTile, s), t, a);
            placed[(atTile, s)] = p;
        }

        /// <summary>Where the selected slot (or the building covering it) is in the world, for things drawn over it.</summary>
        public bool TryGetSelectedWorldPosition(out Vector3 world)
        {
            world = default;
            if (simulation == null || siteView.Site == null || tile < 0 || (slot < 0 && siteView.Selection.Count == 0))
                return false;
            TileSite site = siteView.SiteFor(tile);
            // One slot: the building on it. Several selected slots: the middle of the selection.
            IReadOnlyList<int> slots = slot < 0 ? new List<int>(siteView.Selection)
                : simulation.GetBuilding(tile, slot) != BuildingType.None ? simulation.FootprintAt(tile, slot) : new List<int> { slot };
            FootprintPlace(site, slots, out Vector3 direction, out _);
            world = transform.TransformPoint(direction * siteView.GroundRadius(site, direction));
            return true;
        }

        /// <summary>What the selected panel would make at these angles (radians).</summary>
        public PanelPreview PreviewSelectedAt(double tilt, double azimuth) =>
            simulation != null && tile >= 0 && slot >= 0 ? PreviewBuilt(tile, Anchor(tile, slot), tilt, azimuth) : default;

        /// <summary>Ask a robot to turn the selected panel to these angles (a site just takes them).</summary>
        public bool OrderSelectedAngles(double tilt, double azimuth)
        {
            if (simulation == null || tile < 0 || slot < 0)
                return false;
            int a = Anchor(tile, slot);
            if (!simulation.OrderPanelAngles(tile, a, tilt, azimuth))
                return false;
            RefreshPanel(tile, a);   // a site changes at once; a built panel waits for its robot
            return true;
        }

        /// <summary>The angles the selected panel is waiting to be turned to, if any.</summary>
        public bool SelectedAdjustOrder(out double tilt, out double azimuth)
        {
            tilt = azimuth = 0.0;
            return simulation != null && tile >= 0 && slot >= 0 && simulation.TryGetAdjustOrder(tile, slot, out tilt, out azimuth);
        }

        public bool CancelSelectedAdjust() => simulation != null && tile >= 0 && slot >= 0 && simulation.CancelAdjust(tile, slot);

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
        /// <summary>The colony knows this tile's latitude (measured, or from the map once one was measured).</summary>
        public bool KnowsLatitude(int t) => simulation != null && simulation.Notebook != null && simulation.Notebook.KnowsLatitude(t);

        public bool BestAngleUnlocked => simulation != null && tile >= 0 && simulation.Notebook.CanUseBestAngle(tile);

        /// <summary>This tile's latitude as the colony knows it (degrees), or NaN before it is found.</summary>
        public double KnownLatitudeDeg => simulation != null && tile >= 0
            ? simulation.Notebook.LatitudeDeg(tile, simulation.Sphere.Latitude(tile) * 180.0 / Math.PI)
            : double.NaN;

        /// <summary>Angles to show for the open site: the selected slot's panel, or the angles being planned (radians).</summary>
        public bool TryGetAngles(out double tilt, out double azimuth)
        {
            if (tile >= 0 && slot >= 0 && placed.TryGetValue((tile, Anchor(tile, slot)), out Placed p))
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
        void OnPassageOpened(int tile, KardashevAscent.Simulation.Resources.PassageKind kind) => padsDirty = true;

        void Listen(IPlanetSimulation sim)
        {
            if (listeningTo == sim)
                return;
            if (listeningTo != null)
            {
                listeningTo.BuildingCompleted -= OnBuildingCompleted;
                listeningTo.PassageOpened -= OnPassageOpened;
                listeningTo.BuildingSalvaged -= RemoveVisual;
                listeningTo.PanelAdjusted -= RefreshPanel;
            }
            listeningTo = sim;
            if (listeningTo != null)
            {
                listeningTo.PassageOpened += OnPassageOpened;
                listeningTo.BuildingCompleted += OnBuildingCompleted;
                listeningTo.BuildingSalvaged += RemoveVisual;
                listeningTo.PanelAdjusted += RefreshPanel;
            }
        }

        /// <summary>A construction site got its parts: swap its see-through look for the real building.</summary>
        void OnBuildingCompleted(int doneTile, int doneSlot)
        {
            padsDirty = true;
            finishedAt[(doneTile, doneSlot)] = Time.unscaledTime;
            TileSite site = siteView.SiteFor(doneTile);
            if (placed.TryGetValue((doneTile, doneSlot), out Placed p))
            {
                p.Visual.Destroy();
                p.Visual = MakePanel(doneTile, doneSlot, false);
                ShowOnSlot(p.Visual, site, simulation.FootprintAt(doneTile, doneSlot), p.Tilt, p.Azimuth);
                placed[(doneTile, doneSlot)] = p;
            }
            else if (batteryVisuals.TryGetValue((doneTile, doneSlot), out BatteryVisual yard))
            {
                yard.Destroy();
                yard = MakeBattery(doneTile, doneSlot, false);
                ShowBattery(yard, site, simulation.FootprintAt(doneTile, doneSlot));
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
            PanelPreview p = simulation.GetBuilding(t, s) == BuildingType.FloatingSolar
                ? simulation.PreviewFloatingPanel(t, tilt, azimuth) : simulation.PreviewPanel(t, tilt, azimuth);
            double newArea = simulation.NewPanelAreaM2(t), area = simulation.PanelAreaAt(t, s);
            if (area <= 0.0 || newArea <= 0.0 || Math.Abs(area - newArea) < 1e-6 * newArea)
                return p;
            return new PanelPreview(p.CanPlace, p.Reason, p.EnergyPerYearWh * area / newArea, p.CapacityFactor, p.Eroi, p.PaybackYears);
        }

        /// <summary>A battery yard, a Workshop or a charging station: the same blocks in different colours.</summary>
        BatteryVisual MakeBattery(int t, int s, bool constructionSite)
        {
            BuildingType type = simulation != null ? simulation.GetBuilding(t, s) : BuildingType.Battery;
            string what = type == BuildingType.Workshop ? "Workshop" : type == BuildingType.ChargingStation ? "Charger"
                        : type == BuildingType.Relay ? "Relay" : type == BuildingType.ResearchStation ? "Research"
                        : type == BuildingType.AluminaPlant ? "Alumina plant" : type == BuildingType.Electrolyser ? "Electrolysis cell"
                        : type == BuildingType.TidalTurbine ? "Tidal turbine" : "Battery";
            if (!constructionSite && (type == BuildingType.AluminaPlant || type == BuildingType.Electrolyser))
            {
                var module = new BatteryVisual($"{what} {t}.{s}", transform, cellMaterial, frameMaterial);
                // Pale for the alumina kiln and tanks, hot orange for the electrolysis pots.
                module.SetTint(type == BuildingType.AluminaPlant ? new Color(0.88f, 0.88f, 0.84f) : new Color(0.92f, 0.45f, 0.26f));
                return module;
            }
            if (!constructionSite && type == BuildingType.TidalTurbine)
            {
                var turbine = new BatteryVisual($"{what} {t}.{s}", transform, cellMaterial, frameMaterial);
                turbine.SetTint(new Color(0.16f, 0.75f, 0.68f));   // a teal housing standing in the water
                return turbine;
            }
            if (!constructionSite && type == BuildingType.ResearchStation)
            {
                var lab = new BatteryVisual($"{what} {t}.{s}", transform, cellMaterial, frameMaterial);
                lab.SetTint(new Color(0.66f, 0.47f, 0.90f));   // a violet lab block
                return lab;
            }
            if (!constructionSite)
                return type == BuildingType.Workshop ? new BatteryVisual($"{what} {t}.{s}", transform, cellMaterial, frameMaterial)
                     : type == BuildingType.ChargingStation ? new BatteryVisual($"{what} {t}.{s}", transform, frameMaterial, frameMaterial)
                     : type == BuildingType.Relay ? new BatteryVisual($"{what} {t}.{s}", transform, cellMaterial, cellMaterial)
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
            siteView.PadTint = PadTintOf;
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
                if (IsBlock(r.Type))
                {
                    BatteryVisual yard = MakeBattery(r.Tile, r.Slot, r.UnderConstruction);
                    ShowBattery(yard, site, simulation.FootprintAt(r.Tile, r.Slot));
                    batteryVisuals[(r.Tile, r.Slot)] = yard;
                }
                else if (r.Type == BuildingType.SolarPanel || r.Type == BuildingType.FloatingSolar)
                {
                    PanelPreview p = PreviewBuilt(r.Tile, r.Slot, r.Tilt, r.Azimuth);
                    AngleRating rating = r.Tile == tile ? RatingOf(p) : AngleRating.Off;
                    if (rating == AngleRating.Optimal)
                        BestAnglePanels++;
                    SolarPanelVisual visual = MakePanel(r.Tile, r.Slot, r.UnderConstruction);
                    ShowOnSlot(visual, site, simulation.FootprintAt(r.Tile, r.Slot), r.Tilt, r.Azimuth);
                    placed[(r.Tile, r.Slot)] = new Placed { Tilt = r.Tilt, Azimuth = r.Azimuth, Preview = p, Rating = rating, Visual = visual };
                }
            }
            previewDirty = true;
            padsDirty = true;
        }

        /// <summary>The angles of a solar panel already on this tile (the lowest slot), else flat: β 0°, facing south.</summary>
        void CopyTileLayoutOrFlat()
        {
            for (int s = 0; s < simulation.SlotCount(tile); s++)
            {
                BuildingType b = simulation.GetBuilding(tile, s);
                if ((b == BuildingType.SolarPanel || b == BuildingType.FloatingSolar) && simulation.TryGetPanelAngles(tile, s, out double t, out double a))
                {
                    tiltDeg = Mathf.Round((float)(t * Mathf.Rad2Deg));
                    azimuthDeg = Mathf.Repeat(Mathf.Round((float)(a * Mathf.Rad2Deg)), 360f);
                    return;
                }
            }
            tiltDeg = 0f;
            azimuthDeg = 180f;
        }

        void OnSiteChanged(int newTile)
        {
            tile = newTile;
            padsDirty = true;
            slot = -1;
            bestReady = false;
            bestTask = null;
            ghost?.Hide();
            if (simulation == null || tile < 0)
                return;

            // New panels start flat (β = 0), or copy the panels already on this tile (director 2026-11-14): the best
            // angle is never set for you, and nothing here gives the latitude away before it is measured.
            CopyTileLayoutOrFlat();

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
            if (placed.TryGetValue((tile, Anchor(tile, slot)), out Placed p))
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

        // ---- Angle previews: a see-through copy of a panel at the angles being set, until a robot turns the real one ----

        static readonly Color PreviewTint = new Color(0.4f, 0.9f, 1f, 0.55f);
        readonly Dictionary<int, (SolarPanelVisual Visual, double Tilt, double Azimuth)> angleGhosts =
            new Dictionary<int, (SolarPanelVisual, double, double)>();
        (HashSet<int> Slots, double Tilt, double Azimuth)? editPreview;

        /// <summary>Show see-through panels at these angles (radians) over the built panels (by anchor slot) while the player is editing them.</summary>
        public void SetEditPreview(IEnumerable<int> slotAnchors, double tilt, double azimuth) => editPreview = (new HashSet<int>(slotAnchors), tilt, azimuth);

        public void ClearEditPreview() => editPreview = null;

        /// <summary>The preview angles for each built panel of the open site: the edit in progress, or an order a robot has yet to carry out.</summary>
        void UpdateAngleGhosts()
        {
            var wanted = new Dictionary<int, (double Tilt, double Azimuth)>();
            if (tile >= 0 && siteView != null && siteView.Site != null && siteView.Site.Tile == tile)
            {
                foreach (var key in placed.Keys)
                    if (key.Tile == tile && simulation.TryGetAdjustOrder(tile, key.Slot, out double ot, out double oa))
                        wanted[key.Slot] = (ot, oa);
                if (editPreview.HasValue)
                    foreach (int s in editPreview.Value.Slots)
                        if (placed.ContainsKey((tile, s)))
                            wanted[s] = (editPreview.Value.Tilt, editPreview.Value.Azimuth);
            }

            List<int> gone = null;
            foreach (int s in angleGhosts.Keys)
                if (!wanted.ContainsKey(s))
                    (gone ?? (gone = new List<int>())).Add(s);
            if (gone != null)
                foreach (int s in gone)
                {
                    angleGhosts[s].Visual.Destroy();
                    angleGhosts.Remove(s);
                }

            foreach (var pair in wanted)
            {
                if (angleGhosts.TryGetValue(pair.Key, out var shown) && shown.Tilt == pair.Value.Tilt && shown.Azimuth == pair.Value.Azimuth)
                    continue;
                SolarPanelVisual visual = shown.Visual;
                if (visual == null)
                {
                    visual = new SolarPanelVisual($"Angle Preview {tile}.{pair.Key}", transform, ghostMaterial, ghostMaterial);
                    visual.SetTint(PreviewTint);
                }
                ShowOnSlot(visual, siteView.SiteFor(tile), simulation.FootprintAt(tile, pair.Key), pair.Value.Tilt, pair.Value.Azimuth);
                angleGhosts[pair.Key] = (visual, pair.Value.Tilt, pair.Value.Azimuth);
            }
        }


        /// <summary>When each building was finished (real time), for its short pop.</summary>
        readonly Dictionary<(int Tile, int Slot), float> finishedAt = new Dictionary<(int, int), float>();
        /// <summary>The height each site is drawn at (eases toward its progress).</summary>
        readonly Dictionary<(int Tile, int Slot), float> shownGrowth = new Dictionary<(int, int), float>();

        const float PopSeconds = 0.45f;

        /// <summary>
        /// Construction animation (director 2026-11-14): a site rises from its footing as robots build it, its outline
        /// pulses while a robot is at work, and a finished building pops up to full height.
        /// </summary>
        void AnimateConstruction()
        {
            float now = Time.unscaledTime;
            foreach (var kv in placed)
                Animate(kv.Key, g => kv.Value.Visual.SetGrowth(g), c => kv.Value.Visual.SetTint(c), now);
            foreach (var kv in batteryVisuals)
                Animate(kv.Key, kv.Value.SetGrowth, kv.Value.SetTint, now);
            var over = new List<(int, int)>();
            foreach (var kv in finishedAt)
                if (now - kv.Value > PopSeconds) over.Add(kv.Key);
            foreach (var k in over) finishedAt.Remove(k);
        }

        void Animate((int Tile, int Slot) key, Action<float> grow, Action<Color> tint, float now)
        {
            if (simulation.IsUnderConstruction(key.Tile, key.Slot))
            {
                float target = 0.12f + 0.88f * (float)simulation.ConstructionProgress(key.Tile, key.Slot);
                float g = shownGrowth.TryGetValue(key, out float was) ? Mathf.MoveTowards(was, target, Time.unscaledDeltaTime * 0.6f) : target;
                shownGrowth[key] = g;
                grow(g);
                // Red while the stock can't cover what it still needs; yellow (waiting to be built) once it can.
                bool shortOf = !simulation.ConstructionRemainingAt(key.Tile, key.Slot).Minus(simulation.Materials).IsEmpty;
                Color baseTint = shortOf ? shortTint : siteTint;
                if (simulation.IsBeingBuilt(key.Tile, key.Slot))
                {
                    Color c = baseTint;
                    c.a = baseTint.a * (0.65f + 0.35f * Mathf.Sin(now * 5f));
                    tint(c);
                }
                else
                    tint(baseTint);
                return;
            }
            shownGrowth.Remove(key);
            if (finishedAt.TryGetValue(key, out float at))
            {
                float u = Mathf.Clamp01((now - at) / PopSeconds);
                grow(1f + 0.18f * Mathf.Sin(u * Mathf.PI));   // overshoot, then settle
            }
        }

        void Update()
        {
            if (simulation == null)
                return;

            UpdateAngleGhosts();
            AnimateConstruction();

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

            if (tile >= 0 && ScreenInput.KeyDown(KeyCode.Tab))
                Mode = mode == BuildMode.SolarPanel ? BuildMode.Battery : mode == BuildMode.Battery ? BuildMode.Workshop
                     : mode == BuildMode.Workshop ? BuildMode.ChargingStation
                     : mode == BuildMode.ChargingStation ? BuildMode.Relay
                     : mode == BuildMode.Relay ? BuildMode.ResearchStation
                     : mode == BuildMode.ResearchStation ? BuildMode.AluminaPlant
                     : mode == BuildMode.AluminaPlant ? BuildMode.Electrolyser
                     : mode == BuildMode.Electrolyser ? BuildMode.FloatingSolar
                     : mode == BuildMode.FloatingSolar ? BuildMode.TidalTurbine : BuildMode.SolarPanel;

            if (dragging)
            {
                if (tile < 0 || ScreenInput.KeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                {
                    dragging = false;
                }
                else if (Input.GetMouseButtonUp(0))
                {
                    dragging = false;
                    int target = siteView.HoveredSlot;
                    if (!ScreenInput.PointerOverUi && target >= 0 && PlaceAt(target))
                        siteView.SetSelection(new[] { target });
                }
            }

            if (MultiSelect)
            {
                if (ScreenInput.KeyDown(placeKey)) PlaceSelection();
                if (ScreenInput.KeyDown(bestKey)) UseBestAngle();
                if (ScreenInput.KeyDown(removeKey) || ScreenInput.KeyDown(KeyCode.Backspace)) RemoveSelection();
            }
            else if (slot >= 0)
            {
                if (ScreenInput.KeyDown(placeKey)) Place();
                if (ScreenInput.KeyDown(bestKey)) UseBestAngle();
                if (ScreenInput.KeyDown(removeKey) || ScreenInput.KeyDown(KeyCode.Backspace)) Remove();
            }

            if (previewDirty && tile >= 0 && Time.unscaledTime - lastPreviewTime >= previewInterval)
            {
                preview = mode == BuildMode.FloatingSolar
                    ? simulation.PreviewFloatingPanel(tile, tiltDeg * Mathf.Deg2Rad, azimuthDeg * Mathf.Deg2Rad)
                    : simulation.PreviewPanel(tile, tiltDeg * Mathf.Deg2Rad, azimuthDeg * Mathf.Deg2Rad);
                if (preview.CanPlace && slot >= 0 && (mode == BuildMode.AluminaPlant || mode == BuildMode.Electrolyser))
                {
                    string why = simulation.RefineryBlocker(TypeOf(mode), tile, slot);
                    if (why != null) preview = new PanelPreview(false, why, 0, 0, 0, 0);
                }
                previewDirty = false;
                lastPreviewTime = Time.unscaledTime;
            }

            UpdateGhost();
            if (padsDirty)
            {
                padsDirty = false;
                siteView.RefreshPads();
                UpdateClearedForest();
            }
        }

        /// <summary>From orbit, each tile's forest thins out with the share of its slots that are built on.</summary>
        void UpdateClearedForest()
        {
            if (simulation == null || planet == null)
                return;
            var slots = new Dictionary<int, int>();
            foreach (var key in placed.Keys)
                slots[key.Tile] = (slots.TryGetValue(key.Tile, out int a) ? a : 0) + simulation.FootprintAt(key.Tile, key.Slot).Count;
            foreach (var key in batteryVisuals.Keys)
                slots[key.Tile] = (slots.TryGetValue(key.Tile, out int a) ? a : 0) + simulation.FootprintAt(key.Tile, key.Slot).Count;
            var share = new Dictionary<int, float>();
            foreach (var kv in slots)
            {
                int total = simulation.SlotCount(kv.Key);
                if (total > 0)
                    share[kv.Key] = Mathf.Clamp01(kv.Value / (float)total);
            }
            planet.SetBuiltShare(share);
            planet.SetPassages(simulation.OpenedPassages);
        }

        bool SlotHasPanel => placed.ContainsKey((tile, Anchor(tile, slot)));
        bool Occupied(int s) => simulation != null && tile >= 0 && simulation.GetBuilding(tile, s) != BuildingType.None;

        /// <summary>What stands in the selected slot (single selection).</summary>
        public BuildingType SelectedBuilding =>
            simulation != null && tile >= 0 && slot >= 0 ? simulation.GetBuilding(tile, slot) : BuildingType.None;

        /// <summary>Storage one battery yard adds, Wh.</summary>
        public double BatteryUnitWh => simulation != null ? simulation.BatteryUnitWh : 0.0;

        /// <summary>Materials one building of the current mode takes.</summary>
        public MaterialBill BuildBill => simulation != null ? simulation.BillToBuild(TypeOf(mode)) : new MaterialBill();

        /// <summary>With several slots selected: how many buildings of the current mode P would make there.</summary>
        public int PlannedBuildings { get; private set; }

        /// <summary>How many slots a building of the current mode covers.</summary>
        public int ModeFootprint => FootprintOf(mode);

        /// <summary>How many slots a building of this kind covers (solar: at today's tier).</summary>
        public int FootprintOf(BuildMode what) => simulation != null ? simulation.FootprintSize(TypeOf(what)) : 1;

        /// <summary>Robot hours a building of this kind takes for one robot (a panel at today's size here).</summary>
        public double BuildHoursOf(BuildMode what)
        {
            if (simulation == null) return 0.0;
            double area = IsPanel(what) ? simulation.NewPanelAreaM2(tile >= 0 ? tile : simulation.BaseTileId) : 0.0;
            return BuildTimes.Hours(TypeOf(what), area);
        }

        /// <summary>Materials a building of this kind takes.</summary>
        public MaterialBill CostOf(BuildMode what) => simulation != null ? simulation.BillToBuild(TypeOf(what)) : new MaterialBill();

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
                string kind = simulation.GetBuilding(tile, slot) == BuildingType.FloatingSolar ? "Floating " + PanelTiers.All[TierAt(tile, slot)].Name.ToLowerInvariant()
                                                                                               : PanelTiers.All[TierAt(tile, slot)].Name;
                return $"{kind} ({PanelTiers.FormatArea(area)})";
            }
        }

        /// <summary>The colony's materials on hand (null before a game).</summary>
        public MaterialStock Stock => simulation?.Materials;

        /// <summary>True while the selected slot's building is a construction site.</summary>
        public bool SelectedIsSite => simulation != null && tile >= 0 && slot >= 0 && simulation.IsUnderConstruction(tile, slot);

        /// <summary>Robot work done and needed on the selected site, hours.</summary>
        public (double Done, double Total) SelectedSiteWork =>
            SelectedIsSite ? simulation.ConstructionWork(tile, slot) : (0.0, 0.0);

        /// <summary>How much of the selected slot's site is built (0–1), and the materials it still needs.</summary>
        public void SelectedSiteProgress(out double progress, out MaterialBill remaining)
        {
            progress = 1.0;
            remaining = new MaterialBill();
            BuildingType type = SelectedBuilding;
            if (!SelectedIsSite || type == BuildingType.None)
                return;
            progress = simulation.ConstructionProgress(tile, slot);
            remaining = simulation.ConstructionRemainingAt(tile, slot);
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
            bool battery = !IsPanel(mode);

            // One ghost per building P (or letting go of a drag) would make, over its whole footprint: the dragged
            // building under the mouse, each building the selection has room for, or the one selected slot.
            BuildingType type = TypeOf(mode);
            var targets = new List<(List<int> Footprint, bool Fits)>();
            if (siteView.Site != null && tile >= 0)
            {
                if (dragging)
                {
                    int s = ScreenInput.PointerOverUi ? -1 : siteView.HoveredSlot;
                    if (s >= 0)
                        targets.Add((simulation.FootprintFor(type, tile, s), simulation.CanPlace(type, tile, s)));
                }
                else if (MultiSelect)
                {
                    // The same order PlaceSelection builds in; a footprint can't take slots an earlier one took.
                    var taken = new HashSet<int>();
                    foreach (int s in siteView.Selection)
                    {
                        if (Occupied(s) || taken.Contains(s) || !simulation.CanPlace(type, tile, s))
                            continue;
                        List<int> f = simulation.FootprintFor(type, tile, s);
                        if (f.Exists(taken.Contains))
                            continue;
                        taken.UnionWith(f);
                        targets.Add((f, true));
                    }
                }
                else if (slot >= 0 && !Occupied(slot))
                {
                    targets.Add((simulation.FootprintFor(type, tile, slot), simulation.CanPlace(type, tile, slot)));
                }
            }

            PlannedBuildings = MultiSelect && !dragging ? targets.Count : 0;
            int panels = 0, yards = 0;
            foreach (var (footprint, fits) in targets)
            {
                Color t = fits ? tint : ghostBlocked;
                if (battery)
                {
                    if (yards == batteryGhosts.Count)
                        batteryGhosts.Add(new BatteryVisual("Battery Ghost", transform, ghostMaterial, ghostMaterial));
                    ShowBattery(batteryGhosts[yards], siteView.Site, footprint);
                    batteryGhosts[yards].SetTint(t);
                    yards++;
                }
                else
                {
                    if (panels == ghosts.Count)
                        ghosts.Add(new SolarPanelVisual("Panel Ghost", transform, ghostMaterial, ghostMaterial));
                    ShowOnSlot(ghosts[panels], siteView.Site, footprint, tilt, azimuth);
                    ghosts[panels].SetTint(t);
                    panels++;
                }
            }
            for (int i = panels; i < ghosts.Count; i++)
                ghosts[i].Hide();
            for (int i = yards; i < batteryGhosts.Count; i++)
                batteryGhosts[i].Hide();
            ghost.Hide();
        }

        /// <summary>
        /// Where a building over these slots stands: the middle of the footprint, as wide as the footprint (from the
        /// middle to the farthest slot's centre, plus half a slot each side), so the model fills its patch of the tile.
        /// </summary>
        void FootprintPlace(TileSite site, IReadOnlyList<int> slots, out Vector3 direction, out float size)
        {
            var sum = Vector3.zero;
            foreach (int s in slots)
            {
                System.Numerics.Vector3 d = site.SlotDirections[s];
                sum += new Vector3(d.X, d.Y, d.Z);
            }
            direction = sum.normalized;
            float reach = 0f;   // angle from the middle to the farthest slot centre, radians
            foreach (int s in slots)
            {
                System.Numerics.Vector3 d = site.SlotDirections[s];
                reach = Mathf.Max(reach, Vector3.Angle(direction, new Vector3(d.X, d.Y, d.Z)) * Mathf.Deg2Rad);
            }
            size = (2f * reach + (float)site.Spacing) * planet.Radius * 0.95f;
        }

        // A building's module on every slot it covers, each sized to sit inside its hex slot (director 2026-11-15: no
        // overlap). 0.78 of the slot spacing keeps a module's corners inside the slot's inscribed circle.
        const float ModuleShareOfSlot = 0.78f;

        void SlotSpots(TileSite site, IReadOnlyList<int> slots, List<Vector3> directions, List<float> radii, out float size)
        {
            directions.Clear();
            radii.Clear();
            foreach (int s in slots)
            {
                System.Numerics.Vector3 d = site.SlotDirections[s];
                var dir = new Vector3(d.X, d.Y, d.Z).normalized;
                directions.Add(dir);
                radii.Add(siteView.GroundRadius(site, dir));
            }
            size = (float)site.Spacing * planet.Radius * ModuleShareOfSlot;
        }

        readonly List<Vector3> spotDirections = new List<Vector3>();
        readonly List<float> spotRadii = new List<float>();

        void ShowBattery(BatteryVisual visual, TileSite site, IReadOnlyList<int> slots)
        {
            SlotSpots(site, slots, spotDirections, spotRadii, out float size);
            visual.Show(spotDirections, spotRadii, size);
        }

        void ShowOnSlot(SolarPanelVisual visual, TileSite site, IReadOnlyList<int> slots, double tilt, double azimuth)
        {
            SlotSpots(site, slots, spotDirections, spotRadii, out float size);
            visual.Show(spotDirections, spotRadii, size, tilt, azimuth);
        }

        /// <summary>Build in the selected slot: a panel at the current angles, or a battery yard.</summary>
        public bool Place() => PlaceAt(slot);

        /// <summary>
        /// Build one <paramref name="what"/> on the first free slot of the open site that takes it, from the centre
        /// out (panels at the planned angles). Used when the player skips the setup tutorial. Returns false if no slot fits.
        /// </summary>
        public bool AutoPlace(BuildMode what)
        {
            if (simulation == null || tile < 0 || siteView.Site == null)
                return false;
            BuildMode was = mode;
            mode = what;
            BuildingType type = TypeOf(what);
            bool done = false;
            for (int s = 0; s < simulation.SlotCount(tile) && !done; s++)
                if (!Occupied(s) && simulation.CanPlace(type, tile, s))
                    done = PlaceAt(s);
            mode = was;
            return done;
        }

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
            // Once per building: asking twice (two of its slots selected) would withdraw the order again.
            var anchors = new HashSet<int>();
            foreach (int s in siteView.Selection)
                if (Occupied(s))
                    anchors.Add(Anchor(tile, s));
            int removed = 0;
            foreach (int a in anchors)
                if (RemoveAt(a))
                    removed++;
            return removed;
        }

        /// <summary>Ask robots to turn the selected panels to the planned angles (sites take them at once). Returns how many.</summary>
        public int ReangleSelection()
        {
            if (simulation == null || siteView.Site == null)
                return 0;
            double tilt = tiltDeg * Mathf.Deg2Rad, azimuth = azimuthDeg * Mathf.Deg2Rad;
            int changed = 0;
            var anchors = new HashSet<int>();
            foreach (int selected in siteView.Selection)
                anchors.Add(Anchor(tile, selected));
            foreach (int s in anchors)
            {
                if (!placed.ContainsKey((tile, s)) || !simulation.OrderPanelAngles(tile, s, tilt, azimuth))
                    continue;
                RefreshPanel(tile, s);
                changed++;
            }
            return changed;
        }

        bool PlaceAt(int s)
        {
            if (simulation == null || tile < 0 || s < 0 || Occupied(s) || siteView.Site == null)
                return false;
            // During the setup tutorial the essentials go on the landing tile only (director 2026-11-15).
            if (GameFlow.InSetup && GameFlow.SetupTile >= 0 && tile != GameFlow.SetupTile)
            {
                GameFlow.WarnNotLandingTile();
                return false;
            }
            padsDirty = true;
            if (IsBlock(TypeOf(mode)))
            {
                if (!(mode == BuildMode.Battery ? simulation.PlaceBattery(tile, s)
                      : mode == BuildMode.AluminaPlant || mode == BuildMode.Electrolyser ? simulation.PlaceRefinery(TypeOf(mode), tile, s)
                      : mode == BuildMode.Workshop ? simulation.PlaceWorkshop(tile, s)
                      : mode == BuildMode.ResearchStation ? simulation.PlaceResearchStation(tile, s)
                      : mode == BuildMode.TidalTurbine ? simulation.PlaceTidalTurbine(tile, s)
                      : mode == BuildMode.Relay ? simulation.PlaceRelay(tile, s) : simulation.PlaceChargingStation(tile, s)))
                    return false;
                BatteryVisual yard = MakeBattery(tile, s, simulation.IsUnderConstruction(tile, s));
                ShowBattery(yard, siteView.Site, simulation.FootprintAt(tile, s));
                batteryVisuals[(tile, s)] = yard;
                KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Place);
                return true;
            }
            double tilt = tiltDeg * Mathf.Deg2Rad, azimuth = azimuthDeg * Mathf.Deg2Rad;
            bool floating = mode == BuildMode.FloatingSolar;
            if (!(floating ? simulation.PlaceFloatingPanel(tile, s, tilt, azimuth) : simulation.PlacePanel(tile, s, tilt, azimuth)))
                return false;

            PanelPreview p = floating ? simulation.PreviewFloatingPanel(tile, tilt, azimuth) : simulation.PreviewPanel(tile, tilt, azimuth);
            AngleRating rating = RatingOf(p);
            if (rating == AngleRating.Optimal)
                BestAnglePanels++;

            SolarPanelVisual visual = MakePanel(tile, s, simulation.IsUnderConstruction(tile, s));
            ShowOnSlot(visual, siteView.Site, simulation.FootprintAt(tile, s), tilt, azimuth);
            placed[(tile, s)] = new Placed { Tilt = tilt, Azimuth = azimuth, Preview = p, Rating = rating, Visual = visual };
            preview = p;
            KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Place);
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
            s = Anchor(tile, s);
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
            padsDirty = true;
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
            if (tile >= 0 && slot >= 0 && placed.TryGetValue((tile, Anchor(tile, slot)), out Placed p))
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
            var counted = new HashSet<int>();
            foreach (int s in siteView.Selection)
            {
                selected++;
                int a = Anchor(tile, s);
                if (!Occupied(s)) empty++;
                else if (!counted.Add(a)) continue;   // one building, several of its slots selected
                else if (placed.ContainsKey((tile, a))) withPanels++;
                else withBatteries++;
            }
        }

        /// <summary>Batteries on the open site's tile.</summary>
        public int BatteriesOnTile => simulation != null && tile >= 0 ? simulation.BatteryCount(tile) : 0;

        public KeyCode PlaceKey => placeKey;
        public KeyCode BestKey => bestKey;
    }
}

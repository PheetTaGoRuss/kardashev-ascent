using System;
using KardashevAscent.Presentation.CameraControl;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The game's side panels in UI Toolkit (UI/Panels): the tile under the mouse, the building site and the panel
    /// planner (tilt and facing sliders, live numbers, best angle, Place / Remove), the light-map legend, and the
    /// controls help. Replaces the IMGUI boxes those components used to draw themselves.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PanelsView : MonoBehaviour
    {
        [SerializeField] TilePicker picker;
        [SerializeField] SiteView siteView;
        [SerializeField] PanelPlacer placer;
        [SerializeField] LightPotentialOverlay lightMap;
        [SerializeField] OrbitCamera orbitCamera;
        [Range(0.02f, 0.5f)] [SerializeField] float textInterval = 0.1f;

        VisualElement hoverBox, siteBox, plannerBox, placedBox, legend, legendBar, dragRect, multiButtons, buildBar;
        // Re-angling a built panel: sliders, the gain, and a robot to do it.
        VisualElement adjustBox;
        Slider adjustTilt, adjustFacing;
        Label adjustTiltLabel, adjustFacingLabel, adjustGain;
        Button adjustBest, adjustApply, adjustCancel;
        (int Tile, int Slot) adjustFor = (-1, -1);
        bool adjustDirty;
        float nextAdjustPreview;
        PanelPreview adjustPreview;
        Label buildHint;
        Label hoverText, siteTitle, siteText, plannerTitle, tiltLabel, facingLabel, plannerNumbers, plannerBest,
              plannerReason, placedTitle, placedAngles, placedNumbers, placedBonus, totals, mapHint, help,
              legendMin, legendMax, legendTile, multiText, batteryText, plannerCost;
        Slider tilt, facing;
        Button bestButton, placeButton, removeAllButton, reangleButton, modePanel, modeBattery, modeWorkshop, modeStation, modeRelay, modeFloating;
        // Build bar categories and the (i) descriptions.
        readonly System.Collections.Generic.Dictionary<BuildMode, (VisualElement Item, Button Info)> buildItems =
            new System.Collections.Generic.Dictionary<BuildMode, (VisualElement, Button)>();
        Button catPower, catStations;
        BuildInfo.Category buildCategory = BuildInfo.Category.Power;
        VisualElement buildInfo;
        Label buildInfoTitle, buildInfoText;
        BuildMode? infoShown;
        BuildMode lastModeSeen;
        float nextText;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);

            hoverBox = root.Q("hover-box");
            siteBox = root.Q("site-box");
            plannerBox = root.Q("planner-box");
            placedBox = root.Q("placed-box");
            legend = root.Q("legend");
            buildBar = root.Q("build-bar");
            buildHint = root.Q<Label>("build-hint");
            legendBar = root.Q("legend-bar");
            dragRect = root.Q("drag-rect");
            multiButtons = root.Q("multi-buttons");
            multiText = root.Q<Label>("multi-text");
            removeAllButton = root.Q<Button>("remove-all-button");
            reangleButton = root.Q<Button>("reangle-button");
            batteryText = root.Q<Label>("battery-text");
            modePanel = root.Q<Button>("mode-panel");
            modeBattery = root.Q<Button>("mode-battery");
            modePanel.clicked += () => { if (placer != null) placer.Mode = BuildMode.SolarPanel; };
            modeBattery.clicked += () => { if (placer != null) placer.Mode = BuildMode.Battery; };
            modeWorkshop = root.Q<Button>("mode-workshop");
            if (modeWorkshop != null)
                modeWorkshop.clicked += () => { if (placer != null) placer.Mode = BuildMode.Workshop; };
            modeStation = root.Q<Button>("mode-station");
            if (modeStation != null)
                modeStation.clicked += () => { if (placer != null) placer.Mode = BuildMode.ChargingStation; };
            // Press a build button and drag onto the site: the building is built where the mouse is let go.
            DragFrom(modePanel, BuildMode.SolarPanel);
            DragFrom(modeBattery, BuildMode.Battery);
            DragFrom(modeWorkshop, BuildMode.Workshop);
            DragFrom(modeStation, BuildMode.ChargingStation);
            modeRelay = root.Q<Button>("mode-relay");
            if (modeRelay != null)
                modeRelay.clicked += () => { if (placer != null) placer.Mode = BuildMode.Relay; };
            DragFrom(modeRelay, BuildMode.Relay);
            catPower = root.Q<Button>("cat-power");
            catStations = root.Q<Button>("cat-stations");
            if (catPower != null) catPower.clicked += () => buildCategory = BuildInfo.Category.Power;
            if (catStations != null) catStations.clicked += () => buildCategory = BuildInfo.Category.Stations;
            buildInfo = root.Q("build-info");
            buildInfoTitle = root.Q<Label>("build-info-title");
            buildInfoText = root.Q<Label>("build-info-text");
            Button infoClose = root.Q<Button>("build-info-close");
            if (infoClose != null) infoClose.clicked += () => infoShown = null;
            foreach (var (mode, key) in new[] { (BuildMode.SolarPanel, "panel"), (BuildMode.FloatingSolar, "floating"), (BuildMode.Battery, "battery"),
                                                (BuildMode.Workshop, "workshop"), (BuildMode.ChargingStation, "station"), (BuildMode.Relay, "relay") })
            {
                VisualElement item = root.Q("item-" + key);
                Button info = root.Q<Button>("info-" + key);
                if (item == null || info == null)
                    continue;
                BuildMode m = mode;
                info.clicked += () => infoShown = infoShown == m ? (BuildMode?)null : m;
                info.tooltip = "What it does";
                buildItems[m] = (item, info);
            }
            modeFloating = root.Q<Button>("mode-floating");
            if (modeFloating != null)
                modeFloating.clicked += () => { if (placer != null) placer.Mode = BuildMode.FloatingSolar; };
            DragFrom(modeFloating, BuildMode.FloatingSolar);
            hoverText = root.Q<Label>("hover-text");
            siteTitle = root.Q<Label>("site-title");
            siteText = root.Q<Label>("site-text");
            plannerTitle = root.Q<Label>("planner-title");
            tiltLabel = root.Q<Label>("tilt-label");
            facingLabel = root.Q<Label>("facing-label");
            plannerNumbers = root.Q<Label>("planner-numbers");
            plannerBest = root.Q<Label>("planner-best");
            plannerReason = root.Q<Label>("planner-reason");
            plannerCost = root.Q<Label>("planner-cost");
            placedTitle = root.Q<Label>("placed-title");
            placedAngles = root.Q<Label>("placed-angles");
            placedNumbers = root.Q<Label>("placed-numbers");
            placedBonus = root.Q<Label>("placed-bonus");
            adjustBox = root.Q("adjust-box");
            adjustTilt = root.Q<Slider>("adjust-tilt");
            adjustFacing = root.Q<Slider>("adjust-facing");
            adjustTiltLabel = root.Q<Label>("adjust-tilt-label");
            adjustFacingLabel = root.Q<Label>("adjust-facing-label");
            adjustGain = root.Q<Label>("adjust-gain");
            adjustBest = root.Q<Button>("adjust-best");
            adjustApply = root.Q<Button>("adjust-apply");
            adjustCancel = root.Q<Button>("adjust-cancel");
            adjustTilt?.RegisterValueChangedCallback(e => adjustDirty = true);
            adjustFacing?.RegisterValueChangedCallback(e => adjustDirty = true);
            if (adjustBest != null)
                adjustBest.clicked += ToggleEditAngles;
            if (adjustApply != null)
                adjustApply.clicked += () => placer?.OrderSelectedAngles(adjustTilt.value * Mathf.Deg2Rad, adjustFacing.value * Mathf.Deg2Rad);
            if (adjustCancel != null)
                adjustCancel.clicked += () => { if (placer != null && placer.CancelSelectedAdjust()) adjustFor = (-1, -1); };
            totals = root.Q<Label>("totals");
            mapHint = root.Q<Label>("map-hint");
            help = root.Q<Label>("help");
            legendMin = root.Q<Label>("legend-min");
            legendMax = root.Q<Label>("legend-max");
            legendTile = root.Q<Label>("legend-tile");
            tilt = root.Q<Slider>("tilt");
            facing = root.Q<Slider>("facing");
            bestButton = root.Q<Button>("best-button");
            placeButton = root.Q<Button>("place-button");

            tilt.RegisterValueChangedCallback(e => { if (placer != null) placer.TiltDeg = e.newValue; });
            facing.RegisterValueChangedCallback(e => { if (placer != null) placer.AzimuthDeg = e.newValue; });
            bestButton.clicked += ToggleEditAngles;
            placeButton.clicked += () =>
            {
                if (placer == null) return;
                if (placer.MultiSelect) placer.PlaceSelection();
                else placer.Place();
            };
            removeAllButton.clicked += () => placer?.RemoveSelection();
            reangleButton.clicked += () => placer?.ReangleSelection();
            root.Q<Button>("remove-button").clicked += () => placer?.Remove();
            nextText = 0f;
        }

        void OnDisable()
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        void Update()
        {
            if (hoverBox == null || picker == null)
                return;

            bool siteOpen = siteView != null && siteView.Site != null && placer != null;
            bool multi = siteOpen && placer.MultiSelect;
            int slot = siteOpen && !multi ? placer.Slot : -1;
            BuildingType building = siteOpen && slot >= 0 ? placer.SelectedBuilding : BuildingType.None;
            bool hasPanel = building != BuildingType.None;

            Show(hoverBox, !siteOpen && picker.HoveredTile >= 0);
            Show(siteBox, siteOpen && slot < 0 && !multi);
            Show(plannerBox, multi || (siteOpen && slot >= 0 && !hasPanel));
            Show(placedBox, hasPanel);
            Show(multiButtons, multi);
            Show(multiText, multi);

            // The selection box while dragging.
            bool dragging = siteOpen && siteView.IsDragging;
            Show(dragRect, dragging);
            if (dragging)
            {
                Rect r = siteView.DragRect;
                IPanel panel = dragRect.panel;
                Vector2 min = panel != null ? RuntimePanelUtils.ScreenToPanel(panel, r.min) : r.min;
                Vector2 max = panel != null ? RuntimePanelUtils.ScreenToPanel(panel, r.max) : r.max;
                dragRect.style.left = min.x;
                dragRect.style.top = min.y;
                dragRect.style.width = max.x - min.x;
                dragRect.style.height = max.y - min.y;
            }
            Show(totals, siteOpen);
            Show(help, orbitCamera != null);
            bool mapOn = lightMap != null && lightMap.Visible;
            Show(legend, mapOn);
            legend?.EnableInClassList("above-build-bar", siteOpen);

            // The build bar: always there while a site is open, so a building can be dragged onto any slot.
            Show(buildBar, siteOpen);
            if (siteOpen)
            {
                // The selected building's category follows Tab too.
                if (placer.Dragging || buildCategory != BuildInfo.CategoryOf(placer.Mode))
                {
                    bool userPickedTab = buildCategory != BuildInfo.CategoryOf(placer.Mode) && !placer.Dragging && lastModeSeen == placer.Mode;
                    if (!userPickedTab) buildCategory = BuildInfo.CategoryOf(placer.Mode);
                }
                lastModeSeen = placer.Mode;
                catPower?.EnableInClassList("selected", buildCategory == BuildInfo.Category.Power);
                catStations?.EnableInClassList("selected", buildCategory == BuildInfo.Category.Stations);
                foreach (var kv in buildItems)
                {
                    kv.Value.Item.style.display = BuildInfo.CategoryOf(kv.Key) == buildCategory ? DisplayStyle.Flex : DisplayStyle.None;
                    kv.Value.Info.EnableInClassList("selected", infoShown == kv.Key);
                }
                if (infoShown.HasValue && buildInfoText != null)
                {
                    buildInfoTitle.text = BuildInfo.Title(infoShown.Value);
                    buildInfoText.text = BuildInfo.Describe(infoShown.Value, placer);
                }
                NameButton(modePanel, "Solar panel", BuildMode.SolarPanel);
                NameButton(modeBattery, "Battery yard", BuildMode.Battery);
                NameButton(modeWorkshop, "Workshop", BuildMode.Workshop);
                NameButton(modeStation, "Charger", BuildMode.ChargingStation);
                NameButton(modeRelay, "Relay", BuildMode.Relay);
                NameButton(modeFloating, "Floating solar", BuildMode.FloatingSolar);
                if (buildHint != null)
                    buildHint.text = placer.Dragging
                        ? "Let go over the site to build · Esc or right-click: cancel"
                        : "Drag a building onto the site · Tab: switch";
            }
            Show(buildInfo, siteOpen && infoShown.HasValue);
            Show(mapHint, lightMap != null && !mapOn);

            // Panel-only controls hide while building batteries.
            bool panelMode = !siteOpen || PanelPlacer.IsPanel(placer.Mode);
            foreach (VisualElement e in new VisualElement[] { tiltLabel, tilt, facingLabel, facing, plannerBest, bestButton })
                Show(e, panelMode);
            Show(batteryText, !panelMode);
            Show(reangleButton, panelMode);
            modePanel.EnableInClassList("selected", panelMode && (!siteOpen || placer.Mode == BuildMode.SolarPanel));
            modeFloating?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.FloatingSolar);
            modeBattery.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.Battery);
            modeWorkshop?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.Workshop);
            modeStation?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.ChargingStation);
            modeRelay?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.Relay);

            // Keep the sliders on the planned angles (keys and "best angle" move them too).
            if (siteOpen)
            {
                tilt.SetValueWithoutNotify(placer.TiltDeg);
                facing.SetValueWithoutNotify(placer.AzimuthDeg);
            }

            if (Time.unscaledTime < nextText)
                return;
            nextText = Time.unscaledTime + textInterval;

            if (orbitCamera != null)
                help.text = orbitCamera.ShowHelp ? orbitCamera.HelpText : "H: all controls";
            if (lightMap != null)
                mapHint.text = $"{lightMap.ToggleKey}: light-potential map";

            if (!siteOpen && picker.HoveredTile >= 0)
            {
                int tile = picker.HoveredTile;
                double kwh = lightMap != null ? lightMap.ValueAt(tile) : double.NaN;
                hoverText.text = picker.Describe(tile) + (double.IsNaN(kwh) ? "" : $"\nSunlight {kwh:N0} kWh/m²/yr");
            }

            if (siteOpen)
                UpdateSite(slot, hasPanel, multi);
            if (mapOn)
                UpdateLegend();
        }

        void UpdateSite(int slot, bool hasPanel, bool multi)
        {
            Show(adjustBox, hasPanel && (placer.SelectedBuilding == BuildingType.SolarPanel || placer.SelectedBuilding == BuildingType.FloatingSolar));
            int tile = siteView.Site.Tile;
            siteTitle.text = $"Building site · tile #{tile}";
            siteText.text = $"{placer.PanelsOnTile} panels · {placer.BatteriesOnTile} battery yards · " +
                            $"{placer.PanelsOnTile + placer.BatteriesOnTile} of {siteView.Site.SlotCount} slots used";
            totals.text = placer.PanelCount == 0
                ? ""
                : placer.BestAngleUnlocked
                    ? $"Panels {placer.PanelCount} ({placer.BestAnglePanels} at best angle) · now {Format.Power(placer.GenerationW)}"
                    : $"Panels {placer.PanelCount} · now {Format.Power(placer.GenerationW)}";

            bool isSite = hasPanel && placer.SelectedIsSite;
            string siteLine = "";
            if (isSite)
            {
                placer.SelectedSiteProgress(out double progress, out KardashevAscent.Simulation.Resources.MaterialBill remaining);
                KardashevAscent.Simulation.Resources.MaterialBill short_ = remaining.Minus(placer.Stock);
                string boat = placer.SelectedBuilding == BuildingType.FloatingSolar ? " Needs a robot with a boat hull (Workshop upgrade)." : "";
                siteLine = $"Under construction: {progress * 100.0:0} % · needs {remaining.Describe()} " +
                           "(a robot brings it from stock while it works here)" +
                           (short_.IsEmpty ? "" : $"; short of {short_.Describe()}: mine or refine it") + $". Works once finished.{boat}\n";
            }
            if (hasPanel && placer.SelectedSalvageOrdered)
                siteLine = "Salvage ordered: it works until a robot takes it down (Del again to cancel).\n" + siteLine;
            if (hasPanel && placer.SelectedBuilding == BuildingType.Relay)
            {
                placedTitle.text = isSite ? $"Network relay site · slot {slot}" : $"Network relay · slot {slot}";
                placedAngles.text = $"Extends the colony network {KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles} tiles around it";
                placedNumbers.text = siteLine + "Robots within range of the lander or a powered relay stay connected: they hear new " +
                                     "orders and the colony's needs. Relays need grid power; in a brownout the whole network is down.";
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && placer.SelectedBuilding == BuildingType.ChargingStation)
            {
                placedTitle.text = isSite ? $"Charging station site · slot {slot}" : $"Charging station · slot {slot}";
                placedAngles.text = "Charges robots from the grid";
                placedNumbers.text = siteLine + "Robots below 20 % come here to charge. Faster than a battery yard, " +
                                     "and more robots at once (placeholder values).";
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && placer.SelectedBuilding == BuildingType.Workshop)
            {
                placedTitle.text = isSite ? $"Robot Workshop site · slot {slot}" : $"Robot Workshop · slot {slot}";
                placedAngles.text = "Makes robots, upgrades and refined materials";
                placedNumbers.text = siteLine + "Order jobs in the Robots panel (G). Each Workshop works one job at a time, " +
                                     "and a robot has to work there.";
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && placer.SelectedBuilding == BuildingType.Battery)
            {
                placedTitle.text = isSite ? $"Battery yard site · slot {slot}" : $"Battery yard · slot {slot}";
                placedAngles.text = $"Stores {Format.Energy(placer.BatteryUnitWh)}";
                placedNumbers.text = siteLine + "Charges from surplus by day and carries the colony through the night.";
                Show(placedBonus, false);
                return;
            }
            if (hasPanel)
            {
                placer.TryGetSelectedPanel(out double t, out double a, out PanelPreview p, out AngleRating rating);
                placedTitle.text = isSite ? $"{placer.SelectedPanelName} site · slot {slot}" : $"{placer.SelectedPanelName} · slot {slot}";
                placedAngles.text = $"Tilt {t * Mathf.Rad2Deg:0}° · facing {a * Mathf.Rad2Deg:0}° {Compass((float)(a * Mathf.Rad2Deg))}";
                placedNumbers.text = siteLine + Numbers(p);
                Show(placedBonus, rating == AngleRating.Optimal && placer.BestAngleUnlocked);
                UpdateAdjust(tile, slot, t, a, p, isSite);
                return;
            }
            if (slot < 0 && !multi)
                return;

            bool panelMode = PanelPlacer.IsPanel(placer.Mode);
            bool workshopMode = placer.Mode == BuildMode.Workshop;
            bool stationMode = placer.Mode == BuildMode.ChargingStation;
            bool relayMode = placer.Mode == BuildMode.Relay;
            string thing = panelMode ? "panel" : workshopMode ? "Workshop" : stationMode ? "charging station" : relayMode ? "relay" : "battery yard";
            int selected = 0, empty = 0, withPanels = 0, withBatteries = 0;
            if (multi)
            {
                placer.CountSelection(out selected, out empty, out withPanels, out withBatteries);
                plannerTitle.text = $"{selected} slots selected";
                int planned = placer.PlannedBuildings;
                string adds = !placer.Preview.CanPlace || planned == 0 ? ""
                    : panelMode ? $"\nBuilding on the empty ones adds {Format.Energy(placer.Preview.EnergyPerYearWh * planned)} a year"
                    : workshopMode || stationMode || relayMode ? ""
                    : $"\nBuilding on the empty ones adds {Format.Energy(placer.BatteryUnitWh * planned)} of storage";
                multiText.text = $"{empty} empty · {withPanels} panels · {withBatteries} battery yards" + adds;
                placeButton.text = $"Place {planned} {thing}s (P)";
                removeAllButton.text = $"Remove {withPanels + withBatteries} (Del)";
                reangleButton.text = $"Set angle on {withPanels}";
                removeAllButton.SetEnabled(withPanels + withBatteries > 0);
                reangleButton.SetEnabled(withPanels > 0);
            }
            else
            {
                int n = placer.ModeFootprint;
                string covers = n > 1 ? $" · covers {n} slots" : "";
                plannerTitle.text = (panelMode ? $"Plan a {placer.NewPanelName} · slot {slot}" : $"Plan a {thing} · slot {slot}") + covers;
                placeButton.text = $"Place {thing} (P)";
            }
            batteryText.text = relayMode
                ? $"Takes {placer.BuildBill.Describe()}. Extends the colony network " +
                  $"{KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles} tiles around it, so robots working out there stay connected. " +
                  "Draws a little grid power; the Network window (N) shows the coverage."
                : stationMode
                ? $"Takes {placer.BuildBill.Describe()}. Charges robots from the grid, faster than a " +
                  "battery yard and more at once. Build one near where robots work: they drive to the nearest charger."
                : workshopMode
                ? $"Takes {placer.BuildBill.Describe()}. Makes new robots, upgrades, and aluminium " +
                  "or sodium from rock. Order jobs in the Robots panel (G)."
                : $"Stores {Format.Energy(placer.BatteryUnitWh)}: charges from surplus by day, " +
                  "carries the colony through the night so it can keep growing.";
            tiltLabel.text = $"Tilt β  {placer.TiltDeg:0}°  (0° flat, 90° upright)";
            facingLabel.text = $"Facing γ  {placer.AzimuthDeg:0}° {Compass(placer.AzimuthDeg)}";

            PanelPreview preview = placer.Preview;
            bool blocked = !preview.CanPlace && !placer.PreviewPending;
            Show(plannerReason, blocked);
            Show(plannerNumbers, !blocked && panelMode);
            Show(plannerBest, !blocked && panelMode);
            plannerReason.text = blocked ? $"Can't build here: {preview.Reason}" : "";
            plannerNumbers.text = Numbers(preview);

            if (!placer.BestAngleUnlocked)
            {
                // Measuring matters: no angle advice until the colony knows where on the planet it is.
                plannerBest.text = "Best angle: unknown until you know this place's latitude φ.\n" +
                                   "Press V for the sky view and measure the noon sun (or the celestial pole at night).";
                bestButton.SetEnabled(true);
            }
            else if (placer.TryGetBest(out PanelAngles best))
            {
                double share = placer.PlannedShareOfBest;
                AngleRating rating = PanelAngleSearch.Rate(share);
                string verdict = rating == AngleRating.Optimal ? "★ best-angle bonus"
                    : rating == AngleRating.Good ? "close" : "far off";
                double phi = placer.KnownLatitudeDeg;
                plannerBest.text = $"φ = {phi:+0.0;−0.0}° → rule of thumb β ≈ |φ| = {Mathf.Abs((float)phi):0}°, facing the equator\n" +
                                   $"Best here: β {best.Tilt * Mathf.Rad2Deg:0}°, γ {best.Azimuth * Mathf.Rad2Deg:0}° → {Format.Energy(best.EnergyPerYearWh)}\n" +
                                   $"Yours: {share * 100.0:0} % of best · {verdict}";
                bestButton.SetEnabled(true);
            }
            else
            {
                plannerBest.text = "Finding the best angle…";
                bestButton.SetEnabled(true);
            }
            placeButton.SetEnabled(preview.CanPlace && (!multi || empty > 0));

            int count = multi ? empty : 1;
            KardashevAscent.Simulation.Resources.MaterialBill cost = placer.BuildBill.Times(count);
            KardashevAscent.Simulation.Resources.MaterialBill missing = cost.Minus(placer.Stock);
            Show(plannerCost, count > 0 && !blocked);
            plannerCost.text = missing.IsEmpty
                ? $"Takes {cost.Describe()} · in stock; a robot brings it"
                : $"Takes {cost.Describe()} · short of {missing.Describe()}: the site waits until it is mined or refined";
        }

        void UpdateLegend()
        {
            if (legendBar.style.backgroundImage.value.texture == null)
                legendBar.style.backgroundImage = new StyleBackground(lightMap.LegendTexture);
            if (!lightMap.HasValues)
            {
                legendMin.text = "Calculating…";
                legendMax.text = "";
                legendTile.text = "";
                return;
            }
            legendMin.text = $"{lightMap.Min:N0}";
            legendMax.text = $"{lightMap.Max:N0}";
            int tile = picker.SelectedTile >= 0 ? picker.SelectedTile : picker.HoveredTile;
            legendTile.text = tile >= 0 ? $"Tile #{tile}: {lightMap.ValueAt(tile):N0} kWh/m²/yr" : "Point at a tile for its value";
        }

        static string Numbers(PanelPreview p) =>
            $"Energy  {Format.Energy(p.EnergyPerYearWh)} / year\n" +
            $"Capacity factor  {p.CapacityFactor * 100.0:0.0} %\n" +
            $"EROI  {p.Eroi:0.0}  ·  payback  {(double.IsInfinity(p.PaybackYears) || double.IsNaN(p.PaybackYears) ? "never" : $"{p.PaybackYears:0.0} years")}";

        static string Compass(float azimuthDeg)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(Mathf.Repeat(azimuthDeg, 360f) / 45f) % 8];
        }

        // ---- Angle editing: the rings (AngleEditor) turn whichever tilt and facing sliders are showing ----

        /// <summary>True while the player is dragging the rings to set the angles.</summary>
        public bool EditAngles { get; private set; }

        public void ToggleEditAngles()
        {
            EditAngles = !EditAngles;
            string text = EditAngles ? "Done editing" : "Edit angle";
            if (bestButton != null) bestButton.text = text;
            if (adjustBest != null) adjustBest.text = text;
        }

        /// <summary>The tilt and facing sliders of whichever panel box is open, or null.</summary>
        (Slider Tilt, Slider Facing)? ActiveAngleSliders()
        {
            if (placedBox != null && !placedBox.ClassListContains("hidden") && adjustBox != null && !adjustBox.ClassListContains("hidden"))
                return (adjustTilt, adjustFacing);
            if (plannerBox != null && !plannerBox.ClassListContains("hidden") && tilt != null && !tilt.ClassListContains("hidden"))
                return (tilt, facing);
            return null;
        }

        /// <summary>The angles being set (degrees), if a panel is selected or being planned.</summary>
        public bool TryGetEditedAngles(out float tiltDeg, out float facingDeg)
        {
            var sliders = ActiveAngleSliders();
            tiltDeg = sliders?.Tilt.value ?? 0f;
            facingDeg = sliders?.Facing.value ?? 0f;
            return sliders.HasValue;
        }

        /// <summary>Turn the open angle sliders by these amounts (degrees); tilt stays in 0–90, facing wraps.</summary>
        public bool TryNudgeAngles(float dTiltDeg, float dFacingDeg)
        {
            var sliders = ActiveAngleSliders();
            if (!sliders.HasValue) return false;
            SetEditedAngles(sliders.Value.Tilt.value + dTiltDeg, sliders.Value.Facing.value + dFacingDeg);
            return true;
        }

        public void SetEditedAngles(float tiltDeg, float facingDeg)
        {
            var sliders = ActiveAngleSliders();
            if (!sliders.HasValue) return;
            sliders.Value.Tilt.value = Mathf.Clamp(Mathf.Round(tiltDeg), 0f, 90f);
            sliders.Value.Facing.value = Mathf.Repeat(Mathf.Round(facingDeg), 360f) % 360f;
        }

        static void Show(VisualElement element, bool visible) => element?.EnableInClassList("hidden", !visible);

        /// <summary>A build button's name and how many slots that building covers.</summary>
        void NameButton(Button button, string name, BuildMode what)
        {
            if (button == null)
                return;
            int n = placer.FootprintOf(what);
            string text = n > 1 ? $"{name} · {n} slots" : name;
            if (button.text != text)
                button.text = text;
        }

        /// <summary>Pressing this build button starts dragging that building (trickle-down: before the button's own click).</summary>
        void DragFrom(Button button, BuildMode what)
        {
            if (button == null)
                return;
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (placer != null && e.button == 0)
                    placer.BeginDrag(what);
            }, TrickleDown.TrickleDown);
            button.tooltip = "Drag onto the site to build";
        }

        /// <summary>
        /// The ADJUST ANGLES part of a selected solar panel: sliders start at its angles (or the ones it waits for),
        /// the gain against today's angles updates as they move, and a robot is sent to turn it.
        /// </summary>
        void UpdateAdjust(int tile, int slot, double tilt, double azimuth, PanelPreview now, bool isSite)
        {
            if (adjustBox == null)
                return;
            bool waiting = placer.SelectedAdjustOrder(out double wantTilt, out double wantAzimuth);
            if (adjustFor != (tile, slot))
            {
                adjustFor = (tile, slot);
                adjustTilt.SetValueWithoutNotify(Mathf.Round((float)((waiting ? wantTilt : tilt) * Mathf.Rad2Deg)));
                adjustFacing.SetValueWithoutNotify(Mathf.Round((float)((waiting ? wantAzimuth : azimuth) * Mathf.Rad2Deg)));
                adjustDirty = true;
            }
            adjustTiltLabel.text = $"Tilt β  {adjustTilt.value:0}°  (now {tilt * Mathf.Rad2Deg:0}°)";
            adjustFacingLabel.text = $"Facing γ  {adjustFacing.value:0}° {Compass(adjustFacing.value)}  (now {azimuth * Mathf.Rad2Deg:0}°)";
            if (adjustDirty && Time.unscaledTime >= nextAdjustPreview)
            {
                adjustPreview = placer.PreviewSelectedAt(adjustTilt.value * Mathf.Deg2Rad, adjustFacing.value * Mathf.Deg2Rad);
                adjustDirty = false;
                nextAdjustPreview = Time.unscaledTime + 0.15f;
            }
            double before = now.EnergyPerYearWh, after = adjustPreview.EnergyPerYearWh;
            bool same = Mathf.Abs(adjustTilt.value - (float)(tilt * Mathf.Rad2Deg)) < 0.5f
                        && Mathf.Abs(Mathf.DeltaAngle(adjustFacing.value, (float)(azimuth * Mathf.Rad2Deg))) < 0.5f;
            string gain = before > 0.0 && after > 0.0
                ? $"{Format.Energy(before)} → {Format.Energy(after)} a year ({(after / before - 1.0) * 100.0:+0.0;-0.0;0.0} %)"
                : "working out…";
            adjustGain.text = same ? "Move the sliders to try other angles." : gain;
            adjustGain.EnableInClassList("warn", !same && after < before);
            adjustBest.SetEnabled(true);
            adjustBest.tooltip = "Drag the rings around the panel to turn it";
            adjustApply.text = isSite ? "Build it at these angles" : waiting ? "Change the order" : "Send a robot to turn it";
            adjustApply.SetEnabled(!same || waiting);
            Show(adjustCancel, waiting);
            if (waiting)
                adjustGain.text = $"Waiting for a robot to turn it to {wantTilt * Mathf.Rad2Deg:0}°, {wantAzimuth * Mathf.Rad2Deg:0}° " +
                                  "(it keeps its old angles until then).\n" + adjustGain.text;
        }
    }
}

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
        // Icons instead of sentences (director 2026-11-15): the cost and what a site still needs as material chips.
        VisualElement plannerCostRow, siteRow, siteBar, siteFill, siteNeeds, buildInfoCost;
        Label siteProgress;
        string plannerCostShape, siteNeedsShape, buildInfoShape;
        Slider tilt, facing;
        Button bestButton, placeButton, removeAllButton, reangleButton, modePanel, modeBattery, modeWorkshop, modeStation, modeRelay, modeFloating, modeTidal, modeWave, modeResearch,
               modeAlumina, modeElectrolyser;
        // Build bar categories and the (i) descriptions.
        readonly System.Collections.Generic.Dictionary<BuildMode, (VisualElement Item, Button Info)> buildItems =
            new System.Collections.Generic.Dictionary<BuildMode, (VisualElement, Button)>();
        Button catPower, catStations, catIndustry;
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
            modeResearch = root.Q<Button>("mode-research");
            if (modeResearch != null)
                modeResearch.clicked += () => { if (placer != null) placer.Mode = BuildMode.ResearchStation; };
            DragFrom(modeResearch, BuildMode.ResearchStation);
            modeAlumina = root.Q<Button>("mode-alumina");
            if (modeAlumina != null)
                modeAlumina.clicked += () => { if (placer != null) placer.Mode = BuildMode.AluminaPlant; };
            DragFrom(modeAlumina, BuildMode.AluminaPlant);
            modeElectrolyser = root.Q<Button>("mode-electrolyser");
            if (modeElectrolyser != null)
                modeElectrolyser.clicked += () => { if (placer != null) placer.Mode = BuildMode.Electrolyser; };
            DragFrom(modeElectrolyser, BuildMode.Electrolyser);
            catPower = root.Q<Button>("cat-power");
            catStations = root.Q<Button>("cat-stations");
            if (catPower != null) catPower.clicked += () => buildCategory = BuildInfo.Category.Power;
            if (catStations != null) catStations.clicked += () => buildCategory = BuildInfo.Category.Stations;
            catIndustry = root.Q<Button>("cat-industry");
            if (catIndustry != null) catIndustry.clicked += () => buildCategory = BuildInfo.Category.Industry;
            buildInfo = root.Q("build-info");
            // The inspect boxes and the building info card drag like the calculator (director 2026-11-14); the
            // inspect boxes move their whole column together.
            VisualElement leftColumn = root.Q("left");
            foreach (VisualElement box in new[] { siteBox, plannerBox, placedBox })
                PanelDrag.AddGrip(box, leftColumn);
            PanelDrag.AddGrip(buildInfo);
            buildInfoTitle = root.Q<Label>("build-info-title");
            buildInfoText = root.Q<Label>("build-info-text");
            buildInfoCost = new VisualElement();
            buildInfoCost.style.marginTop = 6;
            if (buildInfoText != null)
                buildInfoText.parent.Add(buildInfoCost);
            Button infoClose = root.Q<Button>("build-info-close");
            if (infoClose != null) infoClose.clicked += () => infoShown = null;
            foreach (var (mode, key) in new[] { (BuildMode.SolarPanel, "panel"), (BuildMode.FloatingSolar, "floating"), (BuildMode.Battery, "battery"),
                                                (BuildMode.Workshop, "workshop"), (BuildMode.ChargingStation, "station"), (BuildMode.Relay, "relay"),
                                                (BuildMode.ResearchStation, "research"), (BuildMode.AluminaPlant, "alumina"),
                                                (BuildMode.Electrolyser, "electrolyser"), (BuildMode.TidalTurbine, "tidal"), (BuildMode.WaveConverter, "wave") })
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
            modeTidal = root.Q<Button>("mode-tidal");
            if (modeTidal != null)
                modeTidal.clicked += () => { if (placer != null) placer.Mode = BuildMode.TidalTurbine; };
            DragFrom(modeTidal, BuildMode.TidalTurbine);
            modeWave = root.Q<Button>("mode-wave");
            if (modeWave != null)
                modeWave.clicked += () => { if (placer != null) placer.Mode = BuildMode.WaveConverter; };
            DragFrom(modeWave, BuildMode.WaveConverter);
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
            // "i" boxes for the planner's numbers and angles (director 2026-11-15).
            Label plannerNumbersLabel = root.Q<Label>("planner-numbers"), placedNumbersLabel = root.Q<Label>("placed-numbers");
            Label tiltL = root.Q<Label>("tilt-label");
            if (plannerNumbersLabel != null) InfoTip.After(plannerNumbersLabel, NumbersInfo);
            if (tiltL != null) InfoTip.After(tiltL, "Tilt β: 0° lies flat, 90° stands upright. Facing γ: the compass direction the panel faces " +
                                                    "(180° = south). A panel facing the equator, tilted about your latitude, gets the most over a year.");
            plannerCostRow = new VisualElement();
            plannerCostRow.style.marginTop = 4;
            if (plannerCost != null)
            {
                plannerCost.parent.Insert(plannerCost.parent.IndexOf(plannerCost) + 1, plannerCostRow);
                plannerCost.style.display = DisplayStyle.None;
            }
            placedTitle = root.Q<Label>("placed-title");
            placedAngles = root.Q<Label>("placed-angles");
            placedNumbers = root.Q<Label>("placed-numbers");
            siteRow = new VisualElement();
            siteRow.style.marginTop = 4;
            siteProgress = new Label("");
            siteProgress.AddToClassList("body");
            siteRow.Add(siteProgress);
            siteBar = new VisualElement();
            siteBar.AddToClassList("meter");
            siteFill = new VisualElement();
            siteFill.AddToClassList("meter-fill");
            siteBar.Add(siteFill);
            siteRow.Add(siteBar);
            siteNeeds = new VisualElement();
            siteRow.Add(siteNeeds);
            if (placedNumbers != null)
                placedNumbers.parent.Insert(placedNumbers.parent.IndexOf(placedNumbers), siteRow);
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
                catIndustry?.EnableInClassList("selected", buildCategory == BuildInfo.Category.Industry);
                foreach (var kv in buildItems)
                {
                    kv.Value.Item.style.display = BuildInfo.CategoryOf(kv.Key) == buildCategory ? DisplayStyle.Flex : DisplayStyle.None;
                    kv.Value.Info.EnableInClassList("selected", infoShown == kv.Key);
                }
                if (infoShown.HasValue && buildInfoText != null)
                {
                    buildInfoTitle.text = BuildInfo.Title(infoShown.Value);
                    buildInfoText.text = BuildInfo.Describe(infoShown.Value, placer);
                    KardashevAscent.Simulation.Resources.MaterialBill infoBill = placer.CostOf(infoShown.Value);
                    string shape = infoShown.Value + "|" + infoBill.Describe() + "|" + infoBill.Minus(placer.Stock).Describe();
                    if (shape != buildInfoShape)
                    {
                        buildInfoShape = shape;
                        MaterialChips.Fill(buildInfoCost, infoBill, placer.Stock, 20f);
                    }
                }
                NameButton(modePanel, "Solar panel", BuildMode.SolarPanel);
                NameButton(modeBattery, "Battery yard", BuildMode.Battery);
                NameButton(modeWorkshop, "Workshop", BuildMode.Workshop);
                NameButton(modeStation, "Charger", BuildMode.ChargingStation);
                NameButton(modeRelay, "Relay", BuildMode.Relay);
                NameButton(modeResearch, "Research Station", BuildMode.ResearchStation);
                NameButton(modeFloating, "Floating solar", BuildMode.FloatingSolar);
                NameButton(modeTidal, "Tidal turbine", BuildMode.TidalTurbine);
                NameButton(modeWave, "Wave converter", BuildMode.WaveConverter);
                NameButton(modeAlumina, "Alumina plant", BuildMode.AluminaPlant);
                NameButton(modeElectrolyser, "Electrolysis cell", BuildMode.Electrolyser);
                if (buildHint != null)
                    buildHint.text = placer.Dragging
                        ? "Let go over the site to build · Esc or right-click: cancel"
                        : "Drag a building onto the site · Tab: switch";
            }
            Show(buildInfo, siteOpen && infoShown.HasValue);
            Show(mapHint, lightMap != null && !mapOn);

            // Panel-only controls hide while building batteries.
            bool panelMode = !siteOpen || PanelPlacer.IsPanel(placer.Mode);
            foreach (VisualElement e in new VisualElement[] { tiltLabel, tilt, facingLabel, facing, bestButton })
                Show(e, panelMode);
            // One place decides the best-angle line (it blinked when two did: director 2026-11-15).
            Show(plannerBest, panelMode && !plannerBlocked);
            Show(batteryText, !panelMode);
            Show(reangleButton, panelMode);
            modePanel.EnableInClassList("selected", panelMode && (!siteOpen || placer.Mode == BuildMode.SolarPanel));
            modeFloating?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.FloatingSolar);
            modeTidal?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.TidalTurbine);
            modeWave?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.WaveConverter);
            modeBattery.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.Battery);
            modeWorkshop?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.Workshop);
            modeStation?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.ChargingStation);
            modeRelay?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.Relay);
            modeResearch?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.ResearchStation);
            modeAlumina?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.AluminaPlant);
            modeElectrolyser?.EnableInClassList("selected", siteOpen && placer.Mode == BuildMode.Electrolyser);

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
                hoverText.text = picker.Describe(tile, placer != null && placer.KnowsLatitude(tile)) + (double.IsNaN(kwh) ? "" : $"\nSunlight {kwh:N0} kWh/m²/yr");
            }

            if (siteOpen)
                UpdateSite(slot, hasPanel, multi);
            if (mapOn)
                UpdateLegend();
        }

        /// <summary>The planned building can't go where it is planned (the last finished preview said so).</summary>
        bool plannerBlocked;

        void UpdateSite(int slot, bool hasPanel, bool multi)
        {
            Show(adjustBox, hasPanel && (placer.SelectedBuilding == BuildingType.SolarPanel || placer.SelectedBuilding == BuildingType.FloatingSolar));
            int tile = siteView.Site.Tile;
            siteTitle.text = $"Building site · tile #{tile}";
            siteText.text = $"{placer.PanelsOnTile + placer.BatteriesOnTile}/{siteView.Site.SlotCount} slots used";
            totals.text = placer.PanelCount == 0
                ? ""
                : placer.BestAngleUnlocked
                    ? $"Panels {placer.PanelCount} ({placer.BestAnglePanels} at best angle) · now {Format.Power(placer.GenerationW)}"
                    : $"Panels {placer.PanelCount} · now {Format.Power(placer.GenerationW)}";

            bool isSite = hasPanel && placer.SelectedIsSite;
            string siteLine = "";
            Show(siteRow, isSite);
            if (isSite)
            {
                placer.SelectedSiteProgress(out double progress, out KardashevAscent.Simulation.Resources.MaterialBill remaining);
                KardashevAscent.Simulation.Resources.MaterialBill short_ = remaining.Minus(placer.Stock);
                var (workDone, workTotal) = placer.SelectedSiteWork;
                siteProgress.text = $"Building {progress * 100.0:0} % · {workDone:0.#}/{workTotal:0.#} h" +
                                    (remaining.IsEmpty ? "" : short_.IsEmpty ? " · needs:" : " · needs (red = short):");
                siteFill.style.width = Length.Percent((float)(progress * 100.0));
                string shape = remaining.Describe() + "|" + short_.Describe();
                if (shape != siteNeedsShape)
                {
                    siteNeedsShape = shape;
                    MaterialChips.Fill(siteNeeds, remaining, placer.Stock);
                }
                if (placer.SelectedBuilding == BuildingType.FloatingSolar)
                    siteLine = "Needs a boat robot.\n";
            }
            if (hasPanel && placer.SelectedSalvageOrdered)
                siteLine = "Salvage ordered (Del: cancel).\n" + siteLine;
            if (hasPanel && placer.SelectedBuilding == BuildingType.ResearchStation)
            {
                placedTitle.text = isSite ? $"Research Station site · slot {slot}" : $"Research Station · slot {slot}";
                placedAngles.text = "Measures & experiments · Research (T)";
                placedNumbers.text = siteLine.TrimEnd('\n');
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && placer.SelectedBuilding == BuildingType.Relay)
            {
                placedTitle.text = isSite ? $"Network relay site · slot {slot}" : $"Network relay · slot {slot}";
                placedAngles.text = $"Network +{KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles} tiles · needs grid power";
                placedNumbers.text = siteLine.TrimEnd('\n');
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && placer.SelectedBuilding == BuildingType.ChargingStation)
            {
                placedTitle.text = isSite ? $"Charging station site · slot {slot}" : $"Charging station · slot {slot}";
                placedAngles.text = "Charges robots below 20 %";
                placedNumbers.text = siteLine.TrimEnd('\n');
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && (placer.SelectedBuilding == BuildingType.AluminaPlant || placer.SelectedBuilding == BuildingType.Electrolyser))
            {
                string name = placer.SelectedBuilding == BuildingType.AluminaPlant ? "Alumina plant" : "Electrolysis cell";
                placedTitle.text = isSite ? $"{name} site · slot {slot}" : $"{name} · slot {slot}";
                placedAngles.text = "Refining · Manufacturing (U)";
                placedNumbers.text = siteLine.TrimEnd('\n');
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && placer.SelectedBuilding == BuildingType.Workshop)
            {
                placedTitle.text = isSite ? $"Robot Workshop site · slot {slot}" : $"Robot Workshop · slot {slot}";
                placedAngles.text = "Robots, upgrades, refining · Manufacturing (U)";
                placedNumbers.text = siteLine.TrimEnd('\n');
                Show(placedBonus, false);
                return;
            }
            if (hasPanel && placer.SelectedBuilding == BuildingType.Battery)
            {
                placedTitle.text = isSite ? $"Battery yard site · slot {slot}" : $"Battery yard · slot {slot}";
                placedAngles.text = $"Stores {Format.Energy(placer.BatteryUnitWh)}";
                placedNumbers.text = siteLine.TrimEnd('\n');
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
            bool researchMode = placer.Mode == BuildMode.ResearchStation;
            bool moduleMode = placer.Mode == BuildMode.AluminaPlant || placer.Mode == BuildMode.Electrolyser;
            string thing = panelMode ? "panel" : workshopMode ? "Workshop" : stationMode ? "charging station" : relayMode ? "relay"
                         : researchMode ? "Research Station" : placer.Mode == BuildMode.AluminaPlant ? "Alumina plant"
                         : placer.Mode == BuildMode.Electrolyser ? "Electrolysis cell" : placer.Mode == BuildMode.TidalTurbine ? "tidal turbine" : placer.Mode == BuildMode.WaveConverter ? "wave converter" : "battery yard";
            int selected = 0, empty = 0, withPanels = 0, withBatteries = 0;
            if (multi)
            {
                placer.CountSelection(out selected, out empty, out withPanels, out withBatteries);
                plannerTitle.text = $"{selected} slots selected";
                int planned = placer.PlannedBuildings;
                string adds = !placer.Preview.CanPlace || planned == 0 ? ""
                    : panelMode ? $"\nBuilding on the empty ones adds {Format.Energy(placer.Preview.EnergyPerYearWh * planned)} a year"
                    : workshopMode || stationMode || relayMode || researchMode || moduleMode ? ""
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
            batteryText.text = moduleMode ? "Refinery module · must touch a Robot Workshop"
                : placer.Mode == BuildMode.WaveConverter ? "Power from the waves · none in a calm, shut down in a storm · needs the wave buoy"
                : placer.Mode == BuildMode.TidalTurbine ? "Power from the tide · none at slack water · needs the tide gauge"
                : researchMode ? "Measures & experiments · Research (T)"
                : relayMode ? $"Network +{KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles} tiles · needs grid power"
                : stationMode ? "Charges robots · build it near their work"
                : workshopMode ? "Robots, upgrades, refining · Manufacturing (U)"
                : $"Stores {Format.Energy(placer.BatteryUnitWh)} for the night";
            tiltLabel.text = $"Tilt β  {placer.TiltDeg:0}°";
            facingLabel.text = $"Facing γ  {placer.AzimuthDeg:0}° {Compass(placer.AzimuthDeg)}";

            PanelPreview preview = placer.Preview;
            // While a new preview is being worked out, keep what was shown (no blink between "can't" and the numbers).
            if (!placer.PreviewPending)
                plannerBlocked = !preview.CanPlace;
            bool blocked = plannerBlocked;
            Show(plannerReason, blocked);
            Show(plannerNumbers, !blocked && panelMode);
            plannerReason.text = blocked ? $"Can't build here: {preview.Reason}" : "";
            plannerNumbers.text = Numbers(preview);

            if (!placer.BestAngleUnlocked)
            {
                // Measuring matters: no angle advice until the colony knows where on the planet it is.
                plannerBest.text = "Best angle: ? · measure your latitude (V)";
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
            Show(plannerCostRow, count > 0 && !blocked);
            string costShape = cost.Describe() + "|" + missing.Describe();
            if (costShape != plannerCostShape)
            {
                plannerCostShape = costShape;
                MaterialChips.Fill(plannerCostRow, cost, placer.Stock);
            }
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

        // One short line; what the numbers mean is behind "i" (director 2026-11-15).
        static string Numbers(PanelPreview p) =>
            $"{Format.Energy(p.EnergyPerYearWh)}/yr · CF {p.CapacityFactor * 100.0:0} % · EROI {p.Eroi:0}" +
            $" · pays back {(double.IsInfinity(p.PaybackYears) || double.IsNaN(p.PaybackYears) ? "never" : $"{p.PaybackYears:0.0} y")}";

        const string NumbersInfo = "Energy: what the panel makes in an Earth year here. Capacity factor: that energy as a share of running at full power all year (nights, angle, air and clouds take the rest). EROI: energy it returns for the energy it took to make. Payback: years until it has returned that energy.";

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

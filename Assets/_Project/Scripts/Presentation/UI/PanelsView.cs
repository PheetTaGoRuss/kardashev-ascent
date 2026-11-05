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

        VisualElement hoverBox, siteBox, plannerBox, placedBox, legend, legendBar;
        Label hoverText, siteTitle, siteText, plannerTitle, tiltLabel, facingLabel, plannerNumbers, plannerBest,
              plannerReason, placedTitle, placedAngles, placedNumbers, placedBonus, totals, mapHint, help,
              legendMin, legendMax, legendTile;
        Slider tilt, facing;
        Button bestButton, placeButton;
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
            legendBar = root.Q("legend-bar");
            hoverText = root.Q<Label>("hover-text");
            siteTitle = root.Q<Label>("site-title");
            siteText = root.Q<Label>("site-text");
            plannerTitle = root.Q<Label>("planner-title");
            tiltLabel = root.Q<Label>("tilt-label");
            facingLabel = root.Q<Label>("facing-label");
            plannerNumbers = root.Q<Label>("planner-numbers");
            plannerBest = root.Q<Label>("planner-best");
            plannerReason = root.Q<Label>("planner-reason");
            placedTitle = root.Q<Label>("placed-title");
            placedAngles = root.Q<Label>("placed-angles");
            placedNumbers = root.Q<Label>("placed-numbers");
            placedBonus = root.Q<Label>("placed-bonus");
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
            bestButton.clicked += () => placer?.UseBestAngle();
            placeButton.clicked += () => placer?.Place();
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
            int slot = siteOpen ? placer.Slot : -1;
            bool hasPanel = siteOpen && slot >= 0 &&
                            placer.TryGetSelectedPanel(out _, out _, out _, out _);

            Show(hoverBox, !siteOpen && picker.HoveredTile >= 0);
            Show(siteBox, siteOpen && slot < 0);
            Show(plannerBox, siteOpen && slot >= 0 && !hasPanel);
            Show(placedBox, hasPanel);
            Show(totals, siteOpen);
            Show(help, orbitCamera != null && orbitCamera.ShowHelp);
            bool mapOn = lightMap != null && lightMap.Visible;
            Show(legend, mapOn);
            Show(mapHint, lightMap != null && !mapOn);

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
                help.text = orbitCamera.HelpText;
            if (lightMap != null)
                mapHint.text = $"{lightMap.ToggleKey}: light-potential map";

            if (!siteOpen && picker.HoveredTile >= 0)
            {
                int tile = picker.HoveredTile;
                double kwh = lightMap != null ? lightMap.ValueAt(tile) : double.NaN;
                hoverText.text = picker.Describe(tile) + (double.IsNaN(kwh) ? "" : $"\nSunlight {kwh:N0} kWh/m²/yr");
            }

            if (siteOpen)
                UpdateSite(slot, hasPanel);
            if (mapOn)
                UpdateLegend();
        }

        void UpdateSite(int slot, bool hasPanel)
        {
            int tile = siteView.Site.Tile;
            siteTitle.text = $"Building site · tile #{tile}";
            siteText.text = $"{placer.PanelsOnTile} of {siteView.Site.SlotCount} slots used";
            totals.text = placer.PanelCount == 0
                ? "No panels yet"
                : $"Panels {placer.PanelCount} ({placer.BestAnglePanels} at best angle) · now {Format.Power(placer.GenerationW)}";

            if (hasPanel)
            {
                placer.TryGetSelectedPanel(out double t, out double a, out PanelPreview p, out AngleRating rating);
                placedTitle.text = $"Solar panel · slot {slot}";
                placedAngles.text = $"Tilt {t * Mathf.Rad2Deg:0}° · facing {a * Mathf.Rad2Deg:0}° {Compass((float)(a * Mathf.Rad2Deg))}";
                placedNumbers.text = Numbers(p);
                Show(placedBonus, rating == AngleRating.Optimal);
                return;
            }
            if (slot < 0)
                return;

            plannerTitle.text = $"Plan a solar panel · slot {slot}";
            tiltLabel.text = $"Tilt β  {placer.TiltDeg:0}°  (0° flat, 90° upright)";
            facingLabel.text = $"Facing γ  {placer.AzimuthDeg:0}° {Compass(placer.AzimuthDeg)}";

            PanelPreview preview = placer.Preview;
            bool blocked = !preview.CanPlace && !placer.PreviewPending;
            Show(plannerReason, blocked);
            Show(plannerNumbers, !blocked);
            Show(plannerBest, !blocked);
            plannerReason.text = blocked ? $"Can't build here: {preview.Reason}" : "";
            plannerNumbers.text = Numbers(preview);

            if (placer.TryGetBest(out PanelAngles best))
            {
                double share = placer.PlannedShareOfBest;
                AngleRating rating = PanelAngleSearch.Rate(share);
                string verdict = rating == AngleRating.Optimal ? "★ best-angle bonus"
                    : rating == AngleRating.Good ? "close" : "far off";
                plannerBest.text = $"Best here: β {best.Tilt * Mathf.Rad2Deg:0}°, γ {best.Azimuth * Mathf.Rad2Deg:0}° → {Format.Energy(best.EnergyPerYearWh)}\n" +
                                   $"Yours: {share * 100.0:0} % of best · {verdict}";
                bestButton.SetEnabled(true);
            }
            else
            {
                plannerBest.text = "Finding the best angle…";
                bestButton.SetEnabled(false);
            }
            placeButton.SetEnabled(preview.CanPlace);
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

        static void Show(VisualElement element, bool visible) => element?.EnableInClassList("hidden", !visible);
    }
}

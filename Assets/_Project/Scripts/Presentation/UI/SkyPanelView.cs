using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The sky view's panel: the chain from the date to the panel's power (δ → h, A_s → φ → θ → W), each number shown
    /// only once the player has worked it out or measured it, with the formula and the numbers that went in.
    /// Also the orbit with its equinoxes and solstices, and the labels on the 3D dome.
    /// UI Toolkit: UI/Sky/Sky.uxml + .uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkyPanelView : MonoBehaviour
    {
        [SerializeField] SkyView sky;
        [SerializeField] SimulationRunner runner;
        [SerializeField] Camera viewCamera;

        VisualElement panelRoot, labelLayer;
        Label title, season, date, decl, hour, sunLabel, lat, panelLabel, theta, power, log;
        Button workDecl, measureSun, measurePole;
        OrbitInset orbit;
        readonly List<Label> domeLabels = new List<Label>();
        float nextText;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);
            panelRoot = root.Q("sky");
            labelLayer = root.Q("labels");
            title = root.Q<Label>("title");
            season = root.Q<Label>("season");
            date = root.Q<Label>("date");
            decl = root.Q<Label>("decl");
            hour = root.Q<Label>("hour");
            sunLabel = root.Q<Label>("sun");
            lat = root.Q<Label>("lat");
            panelLabel = root.Q<Label>("panel");
            theta = root.Q<Label>("theta");
            power = root.Q<Label>("power");
            log = root.Q<Label>("log");
            workDecl = root.Q<Button>("work-decl");
            measureSun = root.Q<Button>("measure-sun");
            measurePole = root.Q<Button>("measure-pole");
            if (workDecl != null) workDecl.clicked += () => { sky?.WorkOutDeclination(); nextText = 0f; };
            if (measureSun != null) measureSun.clicked += () => { sky?.MeasureSun(); nextText = 0f; };
            if (measurePole != null) measurePole.clicked += () => { sky?.MeasurePole(); nextText = 0f; };
            orbit = new OrbitInset();
            root.Q("orbit")?.Add(orbit);
        }

        void OnDisable()
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        void LateUpdate()
        {
            bool show = sky != null && sky.Active && sky.Report.Valid;
            panelRoot?.EnableInClassList("hidden", !show);
            PlaceLabels(show);
            if (!show || Time.unscaledTime < nextText)
                return;
            nextText = Time.unscaledTime + 0.1f;
            Refresh();
        }

        // ---- Labels on the dome ----

        void PlaceLabels(bool show)
        {
            if (labelLayer == null)
                return;
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            int used = 0;
            if (show && cam != null)
            {
                Transform planet = sky.transform;
                foreach (var (text, local, color) in sky.Labels)
                {
                    Vector3 world = planet.TransformPoint(local);
                    if (cam.WorldToViewportPoint(world).z <= 0f)
                        continue;
                    Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(labelLayer.panel, world, cam);
                    if (used == domeLabels.Count)
                    {
                        var l = new Label { pickingMode = PickingMode.Ignore };
                        l.AddToClassList("dome-label");
                        labelLayer.Add(l);
                        domeLabels.Add(l);
                    }
                    Label label = domeLabels[used++];
                    label.text = text;
                    label.style.color = color;
                    label.style.left = p.x - 4f;
                    label.style.top = p.y - 8f;
                    label.style.display = DisplayStyle.Flex;
                }
            }
            for (int i = used; i < domeLabels.Count; i++)
                domeLabels[i].style.display = DisplayStyle.None;
        }

        // ---- The chain of numbers ----

        void Refresh()
        {
            SkyReport r = sky.Report;
            SkyNotebook book = sky.Notebook;
            int tile = sky.Tile;
            bool earthCalendar = runner != null && Math.Abs(runner.SolarParams.YearLengthHours / 24.0 - 365.256) < 1.0
                                 && Math.Abs(r.ObliquityDeg - 23.44) < 0.5;
            string where = runner?.Record?.displayName ?? "";
            title.text = $"Sky over slot {sky.Slot} · tile #{tile}";

            orbit.Set(runner != null ? runner.SolarParams : default, r.EclipticLongitudeDeg, earthCalendar, r.ObliquityDeg > 0.5);
            season.text = r.ObliquityDeg > 0.5
                ? $"{where}: λ = {r.EclipticLongitudeDeg:0}° along the orbit (0° = northern spring equinox), axial tilt ε = {r.ObliquityDeg:0.##}°."
                : $"{where}: no axial tilt, so no seasons: the star's declination stays 0°.";

            // 1. Date → δ
            if (earthCalendar)
            {
                int n = SkyMath.EarthDayOfYear(r.EclipticLongitudeDeg);
                var day = new DateTime(2026, 1, 1).AddDays(n - 1);
                date.text = $"Day n = {n} of the year ({day:d MMM}).";
                decl.text = book.KnowsDeclination
                    ? $"δ = 23.44° · sin(360° · (284 + {n}) / 365) = {Signed(SkyMath.CooperDeclinationDeg(n), 2)}°\n" +
                      $"Exact: sin δ = sin ε · sin λ → δ = {Signed(r.DeclinationDeg, 2)}°"
                    : "δ = ?   Today's date and the planet's tilt give it.";
            }
            else
            {
                date.text = r.TidallyLocked
                    ? "Tidally locked: the same face always looks at the star."
                    : $"λ = {r.EclipticLongitudeDeg:0}° along the orbit.";
                decl.text = book.KnowsDeclination
                    ? $"sin δ = sin ε · sin λ = sin {r.ObliquityDeg:0.##}° · sin {r.EclipticLongitudeDeg:0}° → δ = {Signed(r.DeclinationDeg, 2)}°"
                    : "δ = ?   The orbit position and the planet's tilt give it.";
            }
            decl.EnableInClassList("unknown", !book.KnowsDeclination);
            workDecl.SetEnabled(!book.KnowsDeclination);

            // 2. The sun
            hour.text = r.TidallyLocked
                ? $"The star never moves here: hour angle H = {Signed(r.HourAngleDeg, 1)}° for good."
                : $"Local solar time {Clock(r.LocalSolarHours)} → H = 15° × (t − 12 h) = {Signed(r.HourAngleDeg, 1)}°";
            bool knowLat = book.KnowsLatitude(tile);
            if (!book.TracksSun)
                sunLabel.text = "h = ?   A_s = ?   Measure the sun to find where it is.";
            else if (knowLat && book.KnowsDeclination)
                sunLabel.text = $"sin h = sin φ · sin δ + cos φ · cos δ · cos H\n" +
                                $"= sin {Signed(book.LatitudeDeg(tile, r.LatitudeDeg), 1)}° · sin {Signed(r.DeclinationDeg, 1)}° + cos {Signed(book.LatitudeDeg(tile, r.LatitudeDeg), 1)}° · cos {Signed(r.DeclinationDeg, 1)}° · cos {Signed(r.HourAngleDeg, 1)}°\n" +
                                $"→ h = {r.SunAltitudeDeg:0.0}°, azimuth A_s = {r.SunAzimuthDeg:0}°";
            else
                sunLabel.text = $"Measured: altitude h = {r.SunAltitudeDeg:0.0}°, azimuth A_s = {r.SunAzimuthDeg:0}° (clockwise from north).";
            sunLabel.EnableInClassList("unknown", !book.TracksSun);

            // 3. Latitude
            lat.text = knowLat
                ? $"φ = {Signed(book.LatitudeDeg(tile, r.LatitudeDeg), 1)}°  ({book.LatitudeMethod(tile)})\n" +
                  $"Rule of thumb: a fixed panel works best tilted β ≈ |φ| = {SkyMath.RuleOfThumbTiltDeg(book.LatitudeDeg(tile, r.LatitudeDeg)):0}°, facing the equator."
                : "φ = ?   Measure the sun within 15 minutes of noon (H ≈ 0°) once you know δ: φ = 90° − h + δ.\nOr sight the celestial pole at night: it stands φ above the horizon.";
            lat.EnableInClassList("unknown", !knowLat);

            // 4. The panel
            double beta = sky.Tilt * 180.0 / Math.PI, ap = sky.Azimuth * 180.0 / Math.PI;
            panelLabel.text = $"Tilt β = {beta:0}°, facing A_p = {ap:0}° ({(sky.PanelBuilt ? "this panel" : "planned")}; you set these).";
            if (book.TracksSun)
            {
                double th = Math.Acos(Math.Max(-1.0, Math.Min(1.0, r.CosIncidence))) * 180.0 / Math.PI;
                theta.text = $"cos θ = cos h · sin β · cos(A_s − A_p) + sin h · cos β\n" +
                             $"= cos {r.SunAltitudeDeg:0.0}° · sin {beta:0}° · cos({r.SunAzimuthDeg:0}° − {ap:0}°) + sin {r.SunAltitudeDeg:0.0}° · cos {beta:0}°\n" +
                             $"= {r.CosIncidence:0.000} → θ = {th:0}°" +
                             (r.CosIncidence < 0.0 ? "  (the sun is behind the panel)" : th < 10.0 ? "  (almost straight on)" : "");
            }
            else
                theta.text = "cos θ = ?   Needs the sun's h and A_s.";
            theta.EnableInClassList("unknown", !book.TracksSun);

            // 5. Light and power
            if (!book.TracksSun)
                power.text = "? Measure the sun first.";
            else if (!r.SunUp)
                power.text = "Night: no light on the panel. The battery carries the colony.";
            else
                power.text = $"Outside the air {r.TopOfAtmosphereWm2:0} W/m² → direct beam {r.DirectNormalWm2:0} W/m², diffuse sky {r.DiffuseHorizontalWm2:0} W/m²\n" +
                             $"On the panel {r.PlaneOfArrayWm2:0} W/m² · cells at {r.CellTemperatureC:0} °C\n" +
                             $"Star colour × {r.SpectralFactor:0.00} · clouds × {r.CloudFactor:0.00}\n" +
                             $"→ a new panel here makes {Format.Power(r.PanelPowerW)} right now";
            power.EnableInClassList("unknown", !book.TracksSun);

            int from = Math.Max(0, book.Log.Count - 4);
            var lines = new List<string>();
            for (int i = book.Log.Count - 1; i >= from; i--)
                lines.Add("• " + book.Log[i]);
            log.text = lines.Count > 0 ? string.Join("\n", lines) : "Nothing measured yet.";
        }

        static string Signed(double v, int decimals) =>
            v.ToString((decimals == 1 ? "+0.0;−0.0;0.0" : "+0.00;−0.00;0.00"), System.Globalization.CultureInfo.InvariantCulture);

        static string Clock(double hours)
        {
            if (double.IsNaN(hours)) return "—";
            int h = (int)hours, m = (int)((hours - h) * 60.0);
            return $"{h:00}:{m:00}";
        }
    }

    /// <summary>The planet's orbit from above: star at the focus, equinoxes and solstices, where the planet is now.</summary>
    public sealed class OrbitInset : VisualElement
    {
        PlanetSolarParams planet;
        double lambdaDeg;
        bool seasons;
        readonly Label[] marks = new Label[4];
        readonly Label now;

        static readonly string[] EarthDates = { "21 Mar · spring", "21 Jun · summer", "23 Sep · autumn", "21 Dec · winter" };
        static readonly string[] Generic = { "spring equinox", "summer solstice", "autumn equinox", "winter solstice" };
        bool earthCalendar;

        public OrbitInset()
        {
            style.flexGrow = 1;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            for (int i = 0; i < 4; i++)
            {
                marks[i] = new Label();
                marks[i].AddToClassList("orbit-label");
                Add(marks[i]);
            }
            now = new Label("now");
            now.AddToClassList("orbit-label");
            Add(now);
            RegisterCallback<GeometryChangedEvent>(_ => Place());
        }

        public void Set(PlanetSolarParams p, double eclipticLongitudeDeg, bool earthDates, bool hasSeasons)
        {
            planet = p;
            lambdaDeg = eclipticLongitudeDeg;
            earthCalendar = earthDates;
            seasons = hasSeasons;
            MarkDirtyRepaint();
            Place();
        }

        Vector2 Point(Rect r, double lambdaDeg)
        {
            double e = Math.Min(0.9, Math.Max(0.0, planet.Eccentricity));
            double nu = lambdaDeg * Math.PI / 180.0 - planet.PerihelionLongitude;
            double dist = (1.0 - e * e) / (1.0 + e * Math.Cos(nu));
            // The star sits at the focus (centre of the box); periapsis to the right.
            float scale = Mathf.Min(r.width * 0.36f, r.height * 0.40f);
            var c = new Vector2(r.width * 0.5f, r.height * 0.5f);
            return c + new Vector2((float)(Math.Cos(nu) * dist), -(float)(Math.Sin(nu) * dist)) * scale;
        }

        void Place()
        {
            Rect r = contentRect;
            if (r.width <= 0f)
                return;
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = Point(r, i * 90.0);
                marks[i].text = earthCalendar ? EarthDates[i] : Generic[i];
                marks[i].style.display = seasons ? DisplayStyle.Flex : DisplayStyle.None;
                marks[i].style.left = Mathf.Clamp(p.x + 4f, 0f, r.width - 70f);
                marks[i].style.top = Mathf.Clamp(p.y - 6f, 0f, r.height - 12f);
            }
            Vector2 q = Point(r, lambdaDeg);
            now.style.left = Mathf.Clamp(q.x + 6f, 0f, r.width - 24f);
            now.style.top = Mathf.Clamp(q.y + 2f, 0f, r.height - 12f);
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            Painter2D p = ctx.painter2D;
            p.strokeColor = new Color(1f, 1f, 1f, 0.35f);
            p.lineWidth = 1.2f;
            p.BeginPath();
            for (int i = 0; i <= 72; i++)
            {
                Vector2 v = Point(r, i * 5.0);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.Stroke();

            var c = new Vector2(r.width * 0.5f, r.height * 0.5f);
            p.fillColor = new Color(1f, 0.85f, 0.3f);
            p.BeginPath();
            p.Arc(c, 5f, 0f, 360f);
            p.Fill();

            if (seasons)
            {
                p.fillColor = new Color(1f, 1f, 1f, 0.6f);
                for (int i = 0; i < 4; i++)
                {
                    p.BeginPath();
                    p.Arc(Point(r, i * 90.0), 2.5f, 0f, 360f);
                    p.Fill();
                }
            }
            p.fillColor = new Color(0.4f, 0.9f, 1f);
            p.BeginPath();
            p.Arc(Point(r, lambdaDeg), 4f, 0f, 360f);
            p.Fill();
            // No label moves here: UI Toolkit forbids changing elements while it draws (Set and layout changes place them).
        }
    }
}

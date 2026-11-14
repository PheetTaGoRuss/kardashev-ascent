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
        Button workDecl, measureSun, measurePole, noonLat;
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
            // Each button opens a puzzle from real-world readings (director 2026-11-14: no more click-to-know).
            if (workDecl != null) { workDecl.text = "Work out δ  ►"; workDecl.clicked += () => Open(Puzzle.Declination); }
            if (measureSun != null) { measureSun.text = "Measure the sun's shadow  ►"; measureSun.clicked += () => Open(Puzzle.Sun); }
            if (measurePole != null)
            {
                measurePole.text = "Sight the celestial pole (night)  ►";
                measurePole.clicked += () => Open(Puzzle.Pole);
                noonLat = new Button(() => Open(Puzzle.Noon)) { text = "Latitude from the noon sun  ►" };
                noonLat.AddToClassList("action");
                measurePole.parent.Insert(measurePole.parent.IndexOf(measurePole), noonLat);
            }
            BuildTutorial(root.Q<ScrollView>("scroll"));
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
            noonLat?.SetEnabled(book.NoonSighting != null && book.KnowsDeclination && !KnowsLatitudeHere(book, tile));

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

        static bool KnowsLatitudeHere(SkyNotebook book, int tile) => book.MeasuredLatitude(tile);

        // ---- Puzzles (director 2026-11-14): readings from real instruments; the player works the value out ----

        enum Puzzle { Declination, Sun, Noon, Pole }

        VisualElement card;
        Puzzle openPuzzle;
        SkyReport snapshot;
        TextField answerA, answerB;
        Label cardMessage;

        void Open(Puzzle which)
        {
            if (sky == null) return;
            bool same = card != null && openPuzzle == which;
            CloseCard();
            if (same) return;   // the button toggles its card
            openPuzzle = which;
            snapshot = sky.Report;
            Button anchor = which == Puzzle.Declination ? workDecl : which == Puzzle.Sun ? measureSun : which == Puzzle.Noon ? noonLat : measurePole;
            card = BuildCard(which, snapshot);
            anchor.parent.Insert(anchor.parent.IndexOf(anchor) + 1, card);
        }

        void CloseCard()
        {
            card?.RemoveFromHierarchy();
            card = null;
        }

        VisualElement BuildCard(Puzzle which, SkyReport r)
        {
            var c = new VisualElement();
            c.AddToClassList("sky-puzzle");
            c.style.marginTop = 4; c.style.marginBottom = 6;
            c.style.paddingLeft = c.style.paddingRight = 8; c.style.paddingTop = c.style.paddingBottom = 6;
            c.style.backgroundColor = new Color(0.56f, 0.70f, 1f, 0.10f);
            c.style.borderLeftWidth = 2; c.style.borderLeftColor = new Color(0.56f, 0.70f, 1f);
            SkyNotebook book = sky.Notebook;
            string hint;
            answerA = answerB = null;
            switch (which)
            {
                case Puzzle.Declination:
                    c.Add(Line("The tilted axis and where the planet is on its orbit give the sun's declination δ."));
                    c.Add(Line($"Tilt ε = {r.ObliquityDeg:0.##}°   ·   orbit position λ = {r.EclipticLongitudeDeg:0.#}°", true));
                    answerA = Field(c, "δ (°)");
                    hint = "sin δ = sin ε · sin λ, so δ = arcsin(sin ε · sin λ).\nExample: ε = 23.44°, λ = 90° (northern summer solstice) → δ = +23.44°.";
                    break;
                case Puzzle.Sun:
                {
                    var (shadow, bearing) = SkyNotebook.GnomonReading(r);
                    if (double.IsNaN(shadow))
                    {
                        c.Add(Line("The sun is down: no shadow. Wait for day, or sight the celestial pole tonight."));
                        hint = "";
                        break;
                    }
                    c.Add(new GnomonPlot(SkyNotebook.GnomonLengthM, shadow));
                    c.Add(Line($"A {SkyNotebook.GnomonLengthM:0.000} m stick stands straight up. Its shadow is {shadow:0.000} m long and points to {bearing:0}° on the compass.", true));
                    answerA = Field(c, "Sun's height h (°)");
                    answerB = Field(c, "Sun's direction A_s (°)");
                    hint = "tan h = stick / shadow, so h = arctan(stick / shadow).\nThe sun is on the other side of the stick from its shadow: A_s = bearing − 180° (add 360° if it is negative).";
                    break;
                }
                case Puzzle.Noon:
                {
                    var noon = book.NoonSighting;
                    if (noon == null || !book.KnowsDeclination)
                    {
                        c.Add(Line("Needs δ (step 1) and a shadow sighting within a quarter hour of noon (step 2)."));
                        hint = "";
                        break;
                    }
                    c.Add(Line($"At noon the sun stood h = {noon.Value.AltitudeDeg:0.0}° high, in the {(noon.Value.South ? "south" : "north")}. Today δ = {r.DeclinationDeg:+0.00;−0.00}°.", true));
                    answerA = Field(c, "Latitude φ (°, south is −)");
                    hint = "At noon the sun is on your meridian. Sun in the south: φ = 90° − h + δ. Sun in the north: φ = h − 90° + δ.";
                    break;
                }
                default:
                {
                    var (reading, north) = SkyNotebook.QuadrantReading(r);
                    if (double.IsNaN(reading))
                    {
                        c.Add(Line("Too bright to see the stars: sight the pole after sunset."));
                        hint = "";
                        break;
                    }
                    c.Add(new QuadrantPlot(reading));
                    c.Add(Line($"Sighting the {(north ? "north" : "south")} celestial pole along the quadrant's edge, the plumb line crosses the scale at {reading:0.0}° (counted from straight up).", true));
                    answerA = Field(c, "Latitude φ (°, south is −)");
                    hint = "The pole's height above the horizon is 90° − the reading. That height is your latitude: north pole → φ positive, south pole → φ negative.";
                    break;
                }
            }
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4;
            if (answerA != null)
            {
                var check = new Button(() => Check(which)) { text = "Check" };
                check.AddToClassList("action");
                row.Add(check);
                var hintLabel = Line(hint);
                hintLabel.style.display = DisplayStyle.None;
                var help = new Button(() => hintLabel.style.display = hintLabel.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None) { text = "? Formula" };
                help.AddToClassList("action");
                row.Add(help);
                if (which == Puzzle.Sun)
                {
                    var again = new Button(() => { CloseCard(); Open(Puzzle.Sun); }) { text = "New reading" };
                    again.AddToClassList("action");
                    row.Add(again);
                }
                c.Add(row);
                c.Add(hintLabel);
            }
            cardMessage = Line("");
            c.Add(cardMessage);
            return c;
        }

        static Label Line(string text, bool strong = false)
        {
            var l = new Label(text);
            l.AddToClassList(strong ? "body" : "caption");
            l.AddToClassList("wrap");
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        static TextField Field(VisualElement parent, string label)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            var l = Line(label);
            l.style.flexGrow = 1;
            row.Add(l);
            var f = new TextField { maxLength = 10 };
            f.AddToClassList("puzzle-field");
            f.style.width = 80;
            row.Add(f);
            parent.Add(row);
            return f;
        }

        static bool Number(TextField f, out double v) =>
            double.TryParse((f?.value ?? "").Trim().Replace('−', '-').Replace(',', '.'),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        void Check(Puzzle which)
        {
            SkyNotebook book = sky.Notebook;
            if (!Number(answerA, out double a) || (answerB != null && !Number(answerB, out _)))
            {
                cardMessage.text = "Enter a number in degrees.";
                return;
            }
            Number(answerB, out double b);
            bool right;
            string result = which == Puzzle.Declination ? book.AnswerDeclination(snapshot, a, out right)
                : which == Puzzle.Sun ? book.AnswerSun(sky.Tile, snapshot, a, b, out right)
                : which == Puzzle.Noon ? book.AnswerNoonLatitude(sky.Tile, snapshot, a, out right)
                : book.AnswerPole(sky.Tile, snapshot, a, out right);
            cardMessage.text = (right ? "Right! " : "") + result;
            cardMessage.style.color = right ? new Color(0.45f, 0.85f, 0.55f) : new Color(1f, 0.70f, 0.45f);
            nextText = 0f;
            if (right)
            {
                // Keep the result on screen a moment, then fold the card away.
                var done = card;
                done.schedule.Execute(() => { if (card == done) CloseCard(); }).StartingIn(1800);
            }
        }

        // ---- First-time tutorial ----

        const string TutorialSeenKey = "kardashev.tutorial.sky";
        VisualElement tutorial;
        Label tutorialTitle, tutorialText, tutorialCount;
        VisualElement tutorialPicture;
        Button tutorialNext;
        int tutorialAt;

        static readonly (string Title, string Text, int Picture)[] Steps =
        {
            ("Find your place from the sky", "Before GPS, people found where they were with a stick, a shadow and the date. You'll do the same here.", 0),
            ("1 · The date gives δ", "The planet's tilt and its place on the orbit give the sun's declination δ: how far north or south of the equator it shines.", 0),
            ("2 · A stick's shadow gives the sun", "A stick 1 m tall: the shorter its shadow, the higher the sun. tan h = stick / shadow, and the sun is opposite the shadow.", 1),
            ("3 · Noon gives your latitude", "At noon the sun is highest: φ = 90° − h + δ. At night, sight the celestial pole with a quadrant instead.", 2),
            ("Your latitude unlocks the best angle", "Once φ is known, B sets a panel to the best angle for this place, and every tile's latitude is known.", 1),
        };

        void BuildTutorial(ScrollView scroll)
        {
            if (scroll == null) return;
            tutorial = new VisualElement();
            tutorial.style.marginBottom = 8;
            tutorial.style.paddingLeft = tutorial.style.paddingRight = 8;
            tutorial.style.paddingTop = tutorial.style.paddingBottom = 6;
            tutorial.style.backgroundColor = new Color(1f, 0.85f, 0.35f, 0.10f);
            tutorial.style.borderLeftWidth = 2; tutorial.style.borderLeftColor = new Color(1f, 0.85f, 0.35f);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            tutorialPicture = new VisualElement();
            tutorialPicture.style.width = 90;
            tutorialPicture.style.marginRight = 8;
            row.Add(tutorialPicture);
            var words = new VisualElement();
            words.style.flexGrow = 1; words.style.flexShrink = 1;
            tutorialTitle = Line("", true);
            tutorialTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            words.Add(tutorialTitle);
            tutorialText = Line("");
            words.Add(tutorialText);
            row.Add(words);
            tutorial.Add(row);
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.alignItems = Align.Center;
            buttons.style.marginTop = 4;
            tutorialCount = Line("");
            tutorialCount.style.flexGrow = 1;
            buttons.Add(tutorialCount);
            var skip = new Button(CloseTutorial) { text = "Skip" };
            skip.AddToClassList("action");
            buttons.Add(skip);
            tutorialNext = new Button(() => ShowTutorial(tutorialAt + 1)) { text = "Next ►" };
            tutorialNext.AddToClassList("action");
            buttons.Add(tutorialNext);
            tutorial.Add(buttons);
            scroll.contentContainer.Insert(0, tutorial);
            var help = new Button(() => ShowTutorial(0)) { text = "? How to measure" };
            help.AddToClassList("action");
            scroll.contentContainer.Insert(1, help);
            bool seen = false;
            try { seen = PlayerPrefs.GetInt(TutorialSeenKey, 0) == 1; } catch { }
            if (seen) tutorial.style.display = DisplayStyle.None;
            else ShowTutorial(0);
        }

        void ShowTutorial(int step)
        {
            if (step >= Steps.Length) { CloseTutorial(); return; }
            tutorialAt = step;
            tutorial.style.display = DisplayStyle.Flex;
            var s = Steps[step];
            tutorialTitle.text = s.Title;
            tutorialText.text = s.Text;
            tutorialCount.text = $"{step + 1} / {Steps.Length}";
            tutorialNext.text = step == Steps.Length - 1 ? "Got it" : "Next ►";
            tutorialPicture.Clear();
            tutorialPicture.Add(s.Picture == 1 ? (VisualElement)new GnomonPlot(1.0, 0.6) { style = { height = 70 } }
                                : s.Picture == 2 ? new QuadrantPlot(55.0) { style = { height = 70 } }
                                : new Label("δ") { style = { fontSize = 34, unityTextAlign = TextAnchor.MiddleCenter, color = new Color(1f, 0.85f, 0.35f) } });
        }

        void CloseTutorial()
        {
            tutorial.style.display = DisplayStyle.None;
            try { PlayerPrefs.SetInt(TutorialSeenKey, 1); PlayerPrefs.Save(); } catch { }
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
    /// <summary>A stick standing on the ground and its shadow, with the sun's height angle h at the shadow's tip.</summary>
    public sealed class GnomonPlot : VisualElement
    {
        readonly double stick, shadow;

        public GnomonPlot(double stickM, double shadowM)
        {
            stick = stickM; shadow = shadowM;
            style.height = 90;
            style.marginTop = 2;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width <= 0f) return;
            float ground = r.height - 10f, left = 14f;
            float scale = Mathf.Min((r.height - 20f) / (float)stick, (r.width - 28f) / (float)Math.Max(stick, shadow));
            Vector2 foot = new Vector2(left, ground), top = new Vector2(left, ground - (float)stick * scale);
            Vector2 tip = new Vector2(left + (float)shadow * scale, ground);
            var g = ctx.painter2D;
            g.lineWidth = 1f; g.strokeColor = new Color(1f, 1f, 1f, 0.35f);
            g.BeginPath(); g.MoveTo(new Vector2(4f, ground)); g.LineTo(new Vector2(r.width - 4f, ground)); g.Stroke();
            g.lineWidth = 4f; g.strokeColor = new Color(0.25f, 0.25f, 0.30f);
            g.BeginPath(); g.MoveTo(foot); g.LineTo(tip); g.Stroke();                                   // shadow
            g.lineWidth = 3f; g.strokeColor = new Color(0.85f, 0.72f, 0.50f);
            g.BeginPath(); g.MoveTo(foot); g.LineTo(top); g.Stroke();                                    // stick
            g.lineWidth = 1f; g.strokeColor = new Color(1f, 0.85f, 0.35f, 0.8f);
            g.BeginPath(); g.MoveTo(top); g.LineTo(tip); g.Stroke();                                     // sunbeam over the stick
            float h = Mathf.Atan2((float)stick, (float)shadow) * Mathf.Rad2Deg;
            g.BeginPath(); g.Arc(tip, 16f, Angle.Degrees(180f), Angle.Degrees(180f + h)); g.Stroke();   // h at the tip
        }
    }

    /// <summary>A quarter-circle quadrant with its plumb line hanging at the reading (degrees from straight up).</summary>
    public sealed class QuadrantPlot : VisualElement
    {
        readonly double reading;

        public QuadrantPlot(double readingDeg)
        {
            reading = readingDeg;
            style.height = 90;
            style.marginTop = 2;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width <= 0f) return;
            float radius = Mathf.Min(r.height - 12f, r.width - 12f);
            Vector2 apex = new Vector2(8f, 6f);
            var g = ctx.painter2D;
            g.lineWidth = 1.5f; g.strokeColor = new Color(0.85f, 0.72f, 0.50f);
            g.BeginPath(); g.MoveTo(apex); g.LineTo(apex + new Vector2(radius, 0f)); g.Stroke();            // sighting edge
            g.BeginPath(); g.MoveTo(apex); g.LineTo(apex + new Vector2(0f, radius)); g.Stroke();
            g.BeginPath(); g.Arc(apex, radius, Angle.Degrees(0f), Angle.Degrees(90f)); g.Stroke();          // scale
            g.strokeColor = new Color(1f, 1f, 1f, 0.3f); g.lineWidth = 1f;
            for (int d = 0; d <= 90; d += 10)
            {
                float a = d * Mathf.Deg2Rad;
                Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                g.BeginPath(); g.MoveTo(apex + u * (radius - 5f)); g.LineTo(apex + u * radius); g.Stroke();
            }
            // Plumb line: hangs from the apex; with the edge on the pole it crosses the scale at the reading.
            float p = (float)(90.0 - reading) * Mathf.Deg2Rad;
            Vector2 down = new Vector2(Mathf.Cos(p), Mathf.Sin(p));
            g.strokeColor = new Color(0.56f, 0.70f, 1f); g.lineWidth = 1.5f;
            g.BeginPath(); g.MoveTo(apex); g.LineTo(apex + down * radius); g.Stroke();
            g.fillColor = new Color(0.56f, 0.70f, 1f);
            g.BeginPath(); g.Arc(apex + down * radius, 3f, Angle.Degrees(0f), Angle.Degrees(360f)); g.Fill();
        }
    }

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

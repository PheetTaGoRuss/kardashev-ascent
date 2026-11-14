using System;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Choose where to land (#11): the planets in planets.json, real ones from the NASA Exoplanet Archive and
    /// hand-made ones. Every value carries a NASA / calc / assumed tag, because which numbers are measured and which
    /// nobody knows yet is part of what the game teaches. Opens at start (clock paused) and with L (land).
    /// UI Toolkit: UI/Planets/Planets.uxml + .uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlanetChooserView : MonoBehaviour
    {
        const double SunLuminosityW = 3.828e26;
        const double AuM = 1.495978707e11;

        [SerializeField] SimulationRunner runner;
        [SerializeField] KeyCode toggleKey = KeyCode.L;
        [Tooltip("Open the chooser when the game starts.")]
        [SerializeField] bool showAtStart = true;

        VisualElement panelRoot, rows;
        ScrollView list;
        Label subtitle, nameLabel, source, verdict, note;
        Button land;
        int selected = -1;
        bool shownAtStart;
        ClockSpeed speedWhenOpened = ClockSpeed.HourPerSecond;

        public bool Visible { get; private set; }
        public KeyCode ToggleKey => toggleKey;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);

            panelRoot = root.Q("planets");
            list = root.Q<ScrollView>("list");
            rows = root.Q("rows");
            subtitle = root.Q<Label>("subtitle");
            nameLabel = root.Q<Label>("name");
            source = root.Q<Label>("source");
            verdict = root.Q<Label>("verdict");
            note = root.Q<Label>("note");
            land = root.Q<Button>("land");
            if (land != null)
                land.clicked += Land;
            Button close = root.Q<Button>("close");
            if (close != null)
                close.clicked += () => SetVisible(false);
            if (runner != null)
                runner.SimulationCreated += OnSimulationCreated;
            SetVisible(Visible);
        }

        void OnDisable()
        {
            if (runner != null)
                runner.SimulationCreated -= OnSimulationCreated;
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        void Update()
        {
            if (showAtStart && !shownAtStart && !GameFlow.Active && runner != null && runner.Simulation != null && runner.Planets.Count > 0)
            {
                shownAtStart = true;
                SetVisible(true);
            }
            if (ScreenInput.KeyDown(toggleKey) && GameFlow.Phase != GamePhase.MainMenu)
                SetVisible(!Visible);
            else if (Visible && ScreenInput.KeyDown(KeyCode.Escape))
                SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            bool opening = visible && !Visible, closing = !visible && Visible;
            Visible = visible;
            panelRoot?.EnableInClassList("hidden", !visible);
            // The colony waits while the player reads.
            if (runner != null && runner.Clock != null)
            {
                if (opening)
                {
                    speedWhenOpened = runner.Clock.Speed;
                    runner.SetSpeed(ClockSpeed.Paused);
                }
                else if (closing && GameFlow.IsPlaying && runner.Clock.Speed == ClockSpeed.Paused && speedWhenOpened != ClockSpeed.Paused)
                    runner.SetSpeed(speedWhenOpened);
            }
            if (opening)
            {
                selected = runner != null && runner.CurrentIndex >= 0 ? runner.CurrentIndex : 0;
                Rebuild();
            }
        }

        void OnSimulationCreated()
        {
            if (Visible)
                Rebuild();
        }

        void Land()
        {
            if (runner == null || selected < 0)
                return;
            bool newPlanet = selected != runner.CurrentIndex;
            if (newPlanet && !runner.ChoosePlanet(selected))
                return;
            SetVisible(false);
            // A new planet (or the first one) means choosing where to land next.
            if (newPlanet || GameFlow.Phase == GamePhase.ChoosingPlanet)
                GameFlow.PlanetChosen();
        }

        void Rebuild()
        {
            if (runner == null || list == null)
                return;
            subtitle.text = $"{toggleKey}: open or close · landing somewhere new starts a new colony";
            list.Clear();
            for (int i = 0; i < runner.Planets.Count; i++)
            {
                PlanetDNA dna = runner.Planets[i];
                if (dna == null)
                    continue;
                int index = i;
                PlanetRecord r = dna.Record;
                string here = i == runner.CurrentIndex ? "  ● here" : "";
                var item = new Button(() => { selected = index; Rebuild(); })
                {
                    text = $"{dna.DisplayName}{here}\n<size=10>{Short(r)}</size>"
                };
                item.AddToClassList("planet-item");
                item.EnableInClassList("selected", i == selected);
                item.EnableInClassList("unplayable", !r.IsPlayable);
                list.Add(item);
            }
            ShowDetails();
        }

        static string Short(PlanetRecord r)
        {
            string star = string.IsNullOrEmpty(r.star.spectralType) ? $"{r.star.teffK:0} K star" : r.star.spectralType;
            return $"{star} · {r.InsolationEarth:0.##}× Earth's light";
        }

        void ShowDetails()
        {
            rows.Clear();
            if (selected < 0 || selected >= runner.Planets.Count || runner.Planets[selected] == null)
            {
                nameLabel.text = "No planets";
                source.text = "Run Kardashev Ascent ▸ Set Up Scene to import planets.json.";
                verdict.text = "";
                land.SetEnabled(false);
                return;
            }
            PlanetDNA dna = runner.Planets[selected];
            PlanetRecord r = dna.Record;
            nameLabel.text = dna.DisplayName;
            source.text = string.IsNullOrEmpty(r.archiveName)
                ? r.source
                : $"{r.source} (“{r.archiveName}”), retrieved {r.retrieved}";

            Section("STAR");
            if (!string.IsNullOrEmpty(r.star.spectralType))
                Row(r, "Type", r.star.spectralType, "star.spectralType");
            Row(r, "Temperature", $"{r.star.teffK:N0} K (the Sun: 5,772 K)", "star.teffK");
            Row(r, "Brightness", $"{r.solar.luminosityW / SunLuminosityW:0.####} × the Sun", "solar.luminosityW");
            if (r.star.count > 1)
                Row(r, "Stars in system", r.flags.circumbinary ? $"{r.star.count}, the planet orbits two" : $"{r.star.count}; the others are far away", "star.count");

            Section("ORBIT");
            Row(r, "Distance", $"{r.solar.semiMajorAxisM / AuM:0.####} AU", "solar.semiMajorAxisM");
            Row(r, "Year", Year(r.solar.yearLengthHours), "solar.yearLengthHours");
            Row(r, "Eccentricity", $"{r.solar.eccentricity:0.###}", "solar.eccentricity");
            double insolation = r.planet.insolationWm2 > 0.0 ? r.planet.insolationWm2 / 1361.0 : r.InsolationEarth;
            Row(r, "Starlight", $"{insolation:0.###} × Earth's ({insolation * 1361.0:N0} W/m²)", "planet.insolationWm2");

            Section("SOLAR PANELS");
            double fSi = SpectralMatch.FSpec(CellMaterial.Silicon, r.star.teffK);
            CellMaterial best = SpectralMatch.BestMaterial(r.star.teffK);
            Row(ValueOrigin.Derived, "Silicon cells", $"{fSi * 100.0:0} % of their output under the Sun (star colour)",
                "Shockley–Queisser detailed balance under a blackbody star (Solar/SpectralMatch)");
            // Only worth a row when another cell clearly beats silicon here (under Sun-like stars they are within a few %).
            if (best != CellMaterial.Silicon && SpectralMatch.DetailedBalanceEfficiency(best, r.star.teffK)
                > 1.1 * SpectralMatch.DetailedBalanceEfficiency(CellMaterial.Silicon, r.star.teffK))
                Row(ValueOrigin.Derived, "Best cell here", $"{best} ({SpectralMatch.FSpec(best, r.star.teffK) * 100.0:0} % of its Sun output)",
                    "the material with the highest efficiency under this star's spectrum");
            Row(r, "Clouds", r.Clouds == CloudModel.EarthClimatology ? "Earth's cloud records (NASA POWER)" : "clear sky (unknown)",
                "flags.clouds");

            Section("PLANET");
            Row(r, "Size", $"{r.RadiusEarth:0.##} × Earth", "planet.radiusM");
            if (r.planet.surfaceGravityMs2 > 0.0)
                Row(r, "Gravity", $"{r.planet.surfaceGravityMs2 / 9.80665:0.##} g", "planet.surfaceGravityMs2");
            Row(r, "Ground", Surface(r.Surface), "flags.surface");
            Row(r, "Day", r.flags.tidallyLocked ? "tidally locked: the star never moves in the sky" : $"{r.solar.dayLengthHours:0.#} h",
                r.flags.tidallyLocked ? "flags.tidallyLocked" : "solar.dayLengthHours");
            if (!r.flags.tidallyLocked)
                Row(r, "Axial tilt", $"{r.solar.obliquityDeg:0.#}°", "solar.obliquityDeg");
            if (r.planet.equilibriumTempK > 0.0)
                Row(r, "Equilibrium temp.", $"{r.planet.equilibriumTempK:0} K ({r.planet.equilibriumTempK - 273.15:0} °C)", "planet.equilibriumTempK");
            Row(r, "Air at the panels", $"{r.solar.airTemperatureC:0} °C", "solar.airTemperatureC");
            SurfaceSettings look = r.ToSurfaceSettings();
            Row(r, "Surface water", r.HasLiquidWater ? $"seas over {look.OceanFraction * 100.0:0} % of it" : $"none: dry basins over {look.OceanFraction * 100.0:0} %",
                "look.water");
            string cold = look.IceAroundSubStellar
                ? look.IceLatitudeDeg >= 180.0 ? "none" : $"beyond {look.IceLatitudeDeg:0}° from the point under the star (tundra, frozen seas, then glacier)"
                : look.IceLatitudeDeg >= 90.0 ? "none" : $"from {look.IceLatitudeDeg:0}° latitude (tundra, frozen seas, then glacier)";
            Row(ValueOrigin.Assumed, "Cold zone", cold,
                "game rule from the air temperature: Earth-like at 0–35 °C, wider when colder; locked planets are eyeballs");

            string why = r.WhyNotPlayable;
            verdict.EnableInClassList("bad", why != null);
            verdict.EnableInClassList("warn", why == null && (r.Surface == SurfaceKind.Uncertain || fSi < 0.7));
            if (why != null)
                verdict.text = "Can't land: " + why;
            else if (r.Surface == SurfaceKind.Uncertain)
                verdict.text = "Its size sits in the radius valley: it may be rocky or have a deep gas layer. The game lets you land.";
            else if (fSi < 0.7)
                verdict.text = $"A cool red star: most of its light is infrared, which silicon cannot use. Panels make {fSi * 100.0:0} % of what they would under the Sun.";
            else
                verdict.text = "";

            bool here = selected == runner.CurrentIndex;
            land.text = here ? "You are here (close)" : why != null ? "Can't land here" : "Land here (starts a new colony)";
            land.SetEnabled(here || why == null);
        }

        static string Surface(SurfaceKind kind) =>
            kind == SurfaceKind.Rocky ? "rocky" : kind == SurfaceKind.Uncertain ? "maybe rocky (uncertain)" : "gas: no surface";

        static string Year(double hours)
        {
            double days = hours / 24.0;
            return days >= 600.0 ? $"{days / 365.256:0.##} Earth years" : $"{days:0.#} Earth days";
        }

        void Section(string title)
        {
            var label = new Label(title);
            label.AddToClassList("heading");
            label.AddToClassList("row-section");
            rows.Add(label);
        }

        void Row(PlanetRecord r, string label, string value, string field)
        {
            string text = r.NoteOf(field);
            Row(r.OriginOf(field), label, value, string.IsNullOrEmpty(text) ? field : $"{field}: {text}");
        }

        void Row(ValueOrigin origin, string label, string value, string hint)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            var name = new Label(label);
            name.AddToClassList("row-label");
            var text = new Label(value);
            text.AddToClassList("row-value");
            var chip = new Label(Chip(origin));
            chip.AddToClassList("chip");
            chip.AddToClassList("origin-" + origin.ToString().ToLowerInvariant());
            row.Add(name);
            row.Add(text);
            row.Add(chip);
            string explain = $"{label}: {Explain(origin)}. {hint}";
            row.RegisterCallback<PointerEnterEvent>(_ => note.text = explain);
            rows.Add(row);
        }

        static string Chip(ValueOrigin origin)
        {
            switch (origin)
            {
                case ValueOrigin.Nasa: return "NASA";
                case ValueOrigin.Derived: return "calc";
                case ValueOrigin.Assumed: return "assumed";
                default: return "?";
            }
        }

        static string Explain(ValueOrigin origin)
        {
            switch (origin)
            {
                case ValueOrigin.Nasa: return "measured, from the NASA Exoplanet Archive";
                case ValueOrigin.Derived: return "worked out from measured values with physics";
                case ValueOrigin.Assumed: return "not measurable for this planet yet, so the game assumes it";
                default: return "source not recorded";
            }
        }
    }
}

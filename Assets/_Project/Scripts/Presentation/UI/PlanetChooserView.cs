using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
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
    /// Director 2026-11-15: a full-screen star map to pick a star from (<see cref="StarMapView"/>: real directions and
    /// distances), with a target panel on the right: the system, its worlds as tabs, a turning 3D preview of the
    /// picked world (<see cref="PlanetPreview"/>), key numbers as tiles, the full mission data and Land.
    /// UI Toolkit: UI/Planets/Planets.uxml + .uss; the layout is built here.
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

        VisualElement panelRoot, rows, systemList, worldTabs, stats, previewBox, previewGlow;
        Label subtitle, nameLabel, source, verdict, note, systemName, starLine;
        Button land;
        StarMapView map;
        PlanetPreview preview;
        Color previewColour = Color.white;
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
            if (panelRoot != null)
                Build(panelRoot);
            if (runner != null)
                runner.SimulationCreated += OnSimulationCreated;
            SetVisible(Visible);
        }

        void OnDisable()
        {
            preview?.Dispose();
            preview = null;
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
            preview?.Update(Time.unscaledDeltaTime);
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
            if (opening && panelRoot != null)
            {
                selected = runner != null && runner.CurrentIndex >= 0 ? runner.CurrentIndex : 0;
                if (preview == null)
                {
                    PlanetView view = FindAnyObjectByType<PlanetView>();
                    if (view != null)
                    {
                        preview = new PlanetPreview(view);
                        previewBox?.Add(preview.Element);
                        PlacePreview();
                    }
                }
                Rebuild();
                // Sweep in: the map settles from a little larger and fades up.
                panelRoot.style.opacity = 0f;
                panelRoot.style.scale = new Scale(new Vector3(1.04f, 1.04f, 1f));
                panelRoot.schedule.Execute(() =>
                {
                    panelRoot.style.opacity = 1f;
                    panelRoot.style.scale = new Scale(Vector3.one);
                }).StartingIn(20);
                KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Whoosh, 0.5f);
            }
            preview?.SetActive(visible);
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
            KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Milestone, 0.8f);
            SetVisible(false);
            // A new planet (or the first one) means choosing where to land next.
            if (newPlanet || GameFlow.Phase == GamePhase.ChoosingPlanet)
                GameFlow.PlanetChosen();
        }

        void Rebuild()
        {
            if (runner == null || map == null)
                return;
            subtitle.text = $"Click a star · {toggleKey} or Esc: close · landing somewhere new starts a new colony";
            var systems = new List<StarMapView.StarSystem>();
            var byName = new Dictionary<string, StarMapView.StarSystem>();
            for (int i = 0; i < runner.Planets.Count; i++)
            {
                PlanetDNA dna = runner.Planets[i];
                if (dna == null) continue;
                PlanetRecord r = dna.Record;
                string host = HostOf(dna);
                if (!byName.TryGetValue(host, out StarMapView.StarSystem sys))
                {
                    sys = new StarMapView.StarSystem { Name = host, TeffK = r.star.teffK };
                    sys.IsSun = host == "Sol";
                    sys.DistancePc = r.star.distancePc;
                    sys.Real = sys.IsSun || r.star.distancePc > 0.0;
                    if (sys.Real && !sys.IsSun)
                    {
                        GalacticCoordinates.FromEquatorial(r.star.raDeg, r.star.decDeg, out double l, out double b);
                        sys.L = l;
                        sys.B = b;
                    }
                    sys.Colour = PlanetPreview.StarColour(r.star.teffK);
                    byName[host] = sys;
                    systems.Add(sys);
                }
                sys.Planets.Add(i);
            }
            map.SetSystems(systems);

            systemList.Clear();
            var sorted = new List<StarMapView.StarSystem>(systems);
            sorted.Sort((a, b) => Order(a).CompareTo(Order(b)));
            foreach (StarMapView.StarSystem sys in sorted)
            {
                StarMapView.StarSystem s = sys;
                string dist = s.IsSun ? "home system" : s.Real
                    ? $"{s.DistancePc:0.#} pc · {s.DistancePc * GalacticCoordinates.LightYearsPerParsec:N0} ly"
                    : "made-up world";
                var item = new Button(() => PickSystem(s)) { text = $"{s.Name.ToUpperInvariant()}\n<size=10>{dist}</size>" };
                item.AddToClassList("sys-item");
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("sys-dot");
                dot.style.backgroundColor = s.Colour;
                item.Add(dot);
                item.userData = s;
                item.RegisterCallback<PointerEnterEvent>(_ => KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Hover, 0.4f));
                systemList.Add(item);
            }
            ShowDetails();
        }

        static double Order(StarMapView.StarSystem s) => s.IsSun ? -1.0 : s.Real ? s.DistancePc : 1e9;

        /// <summary>The star a world circles: the archive name without its planet letter; Mars is the Sun's.</summary>
        static string HostOf(PlanetDNA dna)
        {
            PlanetRecord r = dna.Record;
            if (!string.IsNullOrEmpty(r.archiveName))
            {
                int cut = r.archiveName.LastIndexOf(' ');
                return cut > 0 ? r.archiveName.Substring(0, cut) : r.archiveName;
            }
            return r.id == "mars" ? "Sol" : dna.DisplayName;
        }

        StarMapView.StarSystem SystemOf(int index)
        {
            if (map == null) return null;
            foreach (StarMapView.StarSystem s in map.Systems)
                if (s.Planets.Contains(index)) return s;
            return null;
        }

        void PickSystem(StarMapView.StarSystem s)
        {
            if (s == null || s.Planets.Count == 0) return;
            KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Beep, 0.6f);
            int pick = s.Planets[0];
            if (s.Planets.Contains(runner.CurrentIndex)) pick = runner.CurrentIndex;
            else
                foreach (int i in s.Planets)
                    if (runner.Planets[i] != null && runner.Planets[i].Record.IsPlayable) { pick = i; break; }
            selected = pick;
            ShowDetails();
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
            nameLabel.text = dna.DisplayName.ToUpperInvariant();
            ShowSystem(dna);
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
            if (r.star.distancePc > 0.0)
                Row(r, "Distance", $"{r.star.distancePc:0.###} pc ({r.star.distancePc * GalacticCoordinates.LightYearsPerParsec:0.#} light years)", "star.distancePc");
            if (r.star.distancePc > 0.0)
                Row(r, "Light from Earth", $"{KardashevAscent.Simulation.Logistics.HaulPhysics.LightYearsFromParsecs(r.star.distancePc):0.##} years one way: a question and its answer take {2.0 * KardashevAscent.Simulation.Logistics.HaulPhysics.LightYearsFromParsecs(r.star.distancePc):0.#} years, so the colony runs on its rulebook", "star.distancePc");

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

            // What the colony knows before it lands (director 2026-11-14): rough values from orbit and the records, with
            // wide error bars. A Research Station measures them exactly after landing.
            Section("FROM ORBIT · BEFORE LANDING (ROUGH)");
            double meanC = r.solar.airTemperatureC;
            Row(ValueOrigin.Derived, "Mean temperature", $"about {meanC:0} °C ± {OrbitalBrief.TemperatureSpreadC:0} °C",
                "from orbit; the Research Station's surface temperature experiment measures it at the base");
            OrbitalBrief.RobotVerdict robots = OrbitalBrief.Robots(meanC);
            Row(ValueOrigin.Derived, "Robots", OrbitalBrief.Say(robots),
                $"robots charge their LFP cells at {OrbitalBrief.ChargeMinC:0}–{OrbitalBrief.ChargeMaxC:0} °C and survive {OrbitalBrief.SurviveMinC:0} to {OrbitalBrief.SurviveMaxC:0} °C (datasheet ranges; game rule)");
            double g = r.planet.surfaceGravityMs2 > 0.0 ? r.planet.surfaceGravityMs2
                : r.planet.massKg > 0.0 && r.planet.radiusM > 0.0 ? PlanetSimulation.GravitationalConstant * r.planet.massKg / (r.planet.radiusM * r.planet.radiusM) : 0.0;
            if (g > 0.0)
                Row(ValueOrigin.Derived, "Gravity", $"about {g / PlanetSimulation.StandardGravityMs2:0.##} g ± {OrbitalBrief.GravitySpreadShare * 100.0:0} %",
                    "from mass and radius; the pendulum experiment measures it");
            double atm = r.solar.surfacePressureEarth;
            Row(ValueOrigin.Derived, "Air pressure", atm > 0.0 ? $"about {atm:0.###} × Earth's ± {OrbitalBrief.PressureSpreadShare * 100.0:0} %" : "no air",
                "from orbit; the barometer experiment measures it");
            double boil = Experiments.WaterBoilingPointC(Experiments.PressurePa(atm));
            Row(ValueOrigin.Derived, "Water", double.IsNaN(boil) ? "can't be liquid: the air is too thin" : $"would boil near {boil:0} °C",
                "rough, from the air pressure; the boiling point experiment says for sure");

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
            verdict.style.display = verdict.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            bool here = selected == runner.CurrentIndex;
            land.text = here ? "YOU ARE HERE  ·  CLOSE" : why != null ? "CAN'T LAND HERE" : "LAND HERE  ►";
            land.SetEnabled(here || why == null);
            ShowStats(r, meanC, g, atm, insolation);
        }

        /// <summary>The target panel's top: the star system, its worlds as tabs, and the turning preview.</summary>
        void ShowSystem(PlanetDNA dna)
        {
            PlanetRecord r = dna.Record;
            StarMapView.StarSystem sys = SystemOf(selected);
            map.Select(sys);
            foreach (VisualElement item in systemList.Children())
                item.EnableInClassList("selected", item.userData == sys);
            systemName.text = sys != null ? sys.Name.ToUpperInvariant() : "";
            string type = string.IsNullOrEmpty(r.star.spectralType) ? "" : r.star.spectralType + " · ";
            string where = sys == null ? "" : sys.IsSun ? " · the Sun" : sys.Real
                ? $" · {r.star.distancePc:0.##} pc ({r.star.distancePc * GalacticCoordinates.LightYearsPerParsec:0.#} light years) · galactic longitude {sys.L:0}°"
                : " · a made-up system for practice";
            starLine.text = $"{type}{r.star.teffK:N0} K star{where}";

            worldTabs.Clear();
            if (sys != null && sys.Planets.Count > 1)
                foreach (int i in sys.Planets)
                {
                    int index = i;
                    var tab = new Button(() =>
                    {
                        selected = index;
                        KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Click, 0.6f);
                        ShowDetails();
                    }) { text = runner.Planets[i].DisplayName.ToUpperInvariant() };
                    tab.AddToClassList("tab");
                    tab.EnableInClassList("selected", i == selected);
                    worldTabs.Add(tab);
                }
            worldTabs.style.display = worldTabs.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            previewColour = sys != null ? sys.Colour : Color.white;
            previewGlow?.MarkDirtyRepaint();
            if (preview != null)
            {
                SurfaceSettings look = r.ToSurfaceSettings();
                var rich = new SurfaceSettings(look.Seed, look.OceanFraction, look.IceLatitudeDeg, look.NoiseScale,
                                               look.IceAroundSubStellar, look.LiquidWater, look.AirTemperatureC, true);
                bool gas = r.Surface == SurfaceKind.Gaseous;
                preview.Show(selected, view => view.BuildPreview(rich, gas), previewColour,
                             r.flags.tidallyLocked ? 0f : (float)r.solar.obliquityDeg, r.flags.tidallyLocked);
            }
        }

        /// <summary>Six numbers that decide a landing, big enough to read at a glance.</summary>
        void ShowStats(PlanetRecord r, double meanC, double g, double atm, double insolation)
        {
            stats.Clear();
            Stat("STARLIGHT", $"{insolation:0.##}×", "Earth's");
            Stat("GRAVITY", g > 0.0 ? $"{g / PlanetSimulation.StandardGravityMs2:0.##} g" : "?", "rough, from orbit");
            Stat("MEAN TEMP.", $"{meanC:0} °C", $"± {OrbitalBrief.TemperatureSpreadC:0} °C");
            Stat("AIR", atm > 0.0 ? $"{atm:0.##}×" : "none", atm > 0.0 ? "Earth's pressure" : "vacuum");
            Stat("DAY", r.flags.tidallyLocked ? "locked" : $"{r.solar.dayLengthHours:0.#} h", r.flags.tidallyLocked ? "one face to the star" : "sunrise to sunrise");
            Stat("YEAR", Year(r.solar.yearLengthHours).Replace(" Earth", ""), "one orbit");
        }

        void Stat(string label, string value, string caption)
        {
            var tile = new VisualElement();
            tile.AddToClassList("stat");
            var l = new Label(label);
            l.AddToClassList("stat-label");
            var v = new Label(value);
            v.AddToClassList("stat-value");
            var c = new Label(caption);
            c.AddToClassList("stat-caption");
            tile.Add(l);
            tile.Add(v);
            tile.Add(c);
            stats.Add(tile);
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

        // ---------------------------------------------------------------- layout

        void Build(VisualElement root)
        {
            root.Clear();
            map = new StarMapView();
            map.style.right = 456;
            map.Picked += PickSystem;
            root.Add(map);

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("map-header");
            var kicker = new Label("STELLAR CARTOGRAPHY  ·  NASA EXOPLANET ARCHIVE");
            kicker.AddToClassList("heading");
            kicker.AddToClassList("map-kicker");
            header.Add(kicker);
            var title = new Label("CHOOSE A WORLD");
            title.AddToClassList("map-title");
            header.Add(title);
            var line = new VisualElement();
            line.AddToClassList("map-line");
            header.Add(line);
            subtitle = new Label("");
            subtitle.AddToClassList("caption");
            header.Add(subtitle);
            root.Add(header);

            var listHead = new Label("STAR SYSTEMS · NEAREST FIRST");
            listHead.AddToClassList("heading");
            listHead.AddToClassList("sys-head");
            root.Add(listHead);
            var listScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            listScroll.AddToClassList("sys-list");
            systemList = listScroll.contentContainer;
            root.Add(listScroll);

            var footnote = new Label("Up is towards the centre of the Milky Way. Each star's direction comes from its sky position; its distance from the Sun is on a log scale.");
            footnote.AddToClassList("caption");
            footnote.AddToClassList("map-footnote");
            root.Add(footnote);

            // The target panel.
            var side = new VisualElement();
            side.AddToClassList("panel");
            side.AddToClassList("side-panel");
            var sk = new Label("TARGET SYSTEM");
            sk.AddToClassList("heading");
            side.Add(sk);
            systemName = new Label("");
            systemName.AddToClassList("side-system");
            side.Add(systemName);
            starLine = new Label("");
            starLine.AddToClassList("caption");
            starLine.AddToClassList("wrap");
            side.Add(starLine);
            worldTabs = new VisualElement();
            worldTabs.AddToClassList("world-tabs");
            side.Add(worldTabs);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("side-scroll");
            side.Add(scroll);

            previewBox = new VisualElement();
            previewBox.AddToClassList("preview-box");
            previewGlow = new VisualElement { pickingMode = PickingMode.Ignore };
            previewGlow.style.position = Position.Absolute;
            previewGlow.style.left = 0; previewGlow.style.right = 0; previewGlow.style.top = 0; previewGlow.style.bottom = 0;
            previewGlow.generateVisualContent += DrawPreviewGlow;
            previewBox.Add(previewGlow);
            previewBox.RegisterCallback<GeometryChangedEvent>(_ => PlacePreview());
            scroll.Add(previewBox);

            nameLabel = new Label("");
            nameLabel.AddToClassList("side-planet");
            scroll.Add(nameLabel);
            source = new Label("");
            source.AddToClassList("caption");
            source.AddToClassList("wrap");
            scroll.Add(source);
            stats = new VisualElement();
            stats.AddToClassList("stats");
            scroll.Add(stats);
            verdict = new Label("");
            verdict.AddToClassList("body");
            verdict.AddToClassList("wrap");
            verdict.AddToClassList("verdict");
            scroll.Add(verdict);

            var data = new Foldout { text = "MISSION DATA · WHERE EVERY NUMBER COMES FROM", value = false };
            data.AddToClassList("mission-data");
            rows = new VisualElement();
            rows.AddToClassList("rows");
            data.Add(rows);
            var legend = new VisualElement();
            legend.AddToClassList("legend");
            AddChip(legend, "NASA", "origin-nasa", "measured");
            AddChip(legend, "calc", "origin-derived", "worked out with physics");
            AddChip(legend, "assumed", "origin-assumed", "a game value");
            data.Add(legend);
            note = new Label("Point at a value to see where it comes from.");
            note.AddToClassList("caption");
            note.AddToClassList("wrap");
            note.AddToClassList("note");
            data.Add(note);
            scroll.Add(data);

            land = new Button(Land) { text = "LAND HERE" };
            land.AddToClassList("land-button");
            side.Add(land);
            root.Add(side);

            var close = new Button(() => SetVisible(false)) { text = "×", tooltip = "Close (Esc)" };
            close.AddToClassList("close-button");
            close.AddToClassList("map-close");
            root.Add(close);

            root.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("scale") };
            root.style.transitionDuration = new List<TimeValue> { new TimeValue(0.45f, TimeUnit.Second) };
            root.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };
        }

        static void AddChip(VisualElement to, string chip, string cls, string text)
        {
            var c = new Label(chip);
            c.AddToClassList("chip");
            c.AddToClassList(cls);
            to.Add(c);
            var t = new Label(text);
            t.AddToClassList("caption");
            t.AddToClassList("legend-text");
            to.Add(t);
        }

        void PlacePreview()
        {
            if (preview == null || previewBox == null) return;
            float h = previewBox.contentRect.height, w = previewBox.contentRect.width;
            if (float.IsNaN(h) || h <= 0f) return;
            VisualElement img = preview.Element;
            img.style.position = Position.Absolute;
            img.style.width = h;
            img.style.height = h;
            img.style.left = (w - h) * 0.5f;
            img.style.top = 0;
        }

        /// <summary>A soft halo in the star's colour behind the planet, and two thin orbit arcs.</summary>
        void DrawPreviewGlow(MeshGenerationContext ctx)
        {
            Rect rr = previewGlow.contentRect;
            if (rr.width < 4f) return;
            Painter2D p = ctx.painter2D;
            Vector2 c = rr.center;
            float r = rr.height * 0.5f * 0.80f;
            Color col = previewColour;
            for (int i = 8; i >= 1; i--)
            {
                col.a = 0.035f;
                p.fillColor = col;
                p.BeginPath(); p.Arc(c, r * (1f + 0.045f * i), 0f, 360f); p.Fill();
            }
            p.lineWidth = 1f;
            p.strokeColor = new Color(1f, 0.78f, 0.36f, 0.35f);
            p.BeginPath(); p.Arc(c, r * 1.22f, 200f, 340f); p.Stroke();
            p.strokeColor = new Color(0.47f, 0.75f, 1f, 0.3f);
            p.BeginPath(); p.Arc(c, r * 1.22f, 20f, 160f); p.Stroke();
        }
    }
}

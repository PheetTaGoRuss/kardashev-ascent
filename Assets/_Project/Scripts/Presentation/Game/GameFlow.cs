using KardashevAscent.Presentation.Planet;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using BuildingType = KardashevAscent.Simulation.Game.BuildingType;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.Game
{
    /// <summary>Where the player is in a game (decided 2026-11-14).</summary>
    public enum GamePhase
    {
        /// <summary>The main menu: New game, Continue, Quit.</summary>
        MainMenu,
        /// <summary>The planet chooser is open for a new game.</summary>
        ChoosingPlanet,
        /// <summary>A planet is chosen; time is stopped until the player lands on a tile.</summary>
        Landing,
        /// <summary>The colony is down and running.</summary>
        Playing,
        /// <summary>
        /// Just landed: time stays stopped while the setup tutorial asks for the essential buildings (or places them
        /// when skipped). The site is open and the build bar works.
        /// </summary>
        Setup,
    }

    /// <summary>
    /// The game's flow and its menus. Launch opens the main menu; New game opens the planet chooser; then time stays
    /// stopped while the player picks a land tile and presses Land here (nothing is placed and no building site opens
    /// until then); after landing the colony can't move. In play, Esc with nothing else open opens the pause menu.
    /// Sets itself up after the scene loads (no scene changes).
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        /// <summary>The current phase. Playing when there is no flow (editor tests, other scenes).</summary>
        public static GamePhase Phase { get; private set; } = GamePhase.Playing;

        /// <summary>True when the player can build (in play, or in the setup tutorial right after landing).</summary>
        public static bool IsPlaying => Phase == GamePhase.Playing || Phase == GamePhase.Setup;

        /// <summary>True while the setup tutorial runs (time stopped).</summary>
        public static bool InSetup => Phase == GamePhase.Setup;

        /// <summary>True when a flow is running (it opens the chooser itself, so the chooser doesn't open on its own).</summary>
        public static bool Active => instance != null;

        static GameFlow instance;

        SimulationRunner runner;
        SaveGameController saveGame;
        PlanetChooserView chooser;
        TilePicker picker;
        SiteView siteView;
        PanelPlacer placer;
        VisualElement root, mainMenu, pauseMenu, landingBanner, setupPanel;
        Label setupSteps, setupNow;
        int setupTile = -1;
        float nextSetupRefresh;
        Button continueButton, loadButton;
        Label menuMessage, pauseMessage;
        bool pauseOpen;
        bool idleLastFrame;
        ClockSpeed speedBeforePause = ClockSpeed.HourPerSecond;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var r = FindAnyObjectByType<SimulationRunner>();
            if (r == null || FindAnyObjectByType<GameFlow>() != null)
                return;
            UIDocument source = FindAnyObjectByType<PanelsView>()?.GetComponent<UIDocument>();
            if (source == null)
                return;
            var go = new GameObject("Game Flow");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = source.panelSettings;
            doc.sortingOrder = source.sortingOrder + 50;   // over every other panel
            go.AddComponent<GameFlow>();
            go.SetActive(true);
        }

        void OnEnable()
        {
            instance = this;
            runner = FindAnyObjectByType<SimulationRunner>();
            saveGame = FindAnyObjectByType<SaveGameController>();
            chooser = FindAnyObjectByType<PlanetChooserView>();
            picker = FindAnyObjectByType<TilePicker>();
            siteView = FindAnyObjectByType<SiteView>();
            placer = FindAnyObjectByType<PanelPlacer>();
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            CopyStyles(root);
            ScreenInput.Register(root.panel);
            BuildMenus();
            if (saveGame != null)
                saveGame.Loaded += OnLoaded;
            SetPhase(GamePhase.MainMenu);
        }

        void OnDisable()
        {
            if (saveGame != null)
                saveGame.Loaded -= OnLoaded;
            if (instance == this)
            {
                instance = null;
                Phase = GamePhase.Playing;
            }
            UIDocument d = GetComponent<UIDocument>();
            if (d != null && d.rootVisualElement != null)
                ScreenInput.Unregister(d.rootVisualElement.panel);
        }

        // ---- Phases ----

        void SetPhase(GamePhase phase)
        {
            Phase = phase;
            pauseOpen = false;
            Show(mainMenu, phase == GamePhase.MainMenu);
            Show(pauseMenu, false);
            Show(landingBanner, phase == GamePhase.Landing);
            Show(setupPanel, phase == GamePhase.Setup);
            if (phase != GamePhase.Playing)
            {
                if (picker != null && phase != GamePhase.Landing && phase != GamePhase.Setup) picker.Select(-1);
                Pause();
            }
            if (phase == GamePhase.MainMenu)
            {
                if (chooser != null && chooser.Visible) chooser.SetVisible(false);
                bool hasSave = saveGame != null && saveGame.HasSave;
                continueButton.SetEnabled(hasSave);
                menuMessage.text = hasSave ? "" : "No saved game yet.";
            }
            if (phase == GamePhase.ChoosingPlanet && chooser != null)
                chooser.SetVisible(true);
            if (phase == GamePhase.Landing)
            {
                NewMap();
                if (picker != null) picker.Select(-1);
            }
        }

        /// <summary>The chooser picked a planet (or was closed): pick a tile to land on next.</summary>
        public static void PlanetChosen()
        {
            if (instance != null && (Phase == GamePhase.ChoosingPlanet || Phase == GamePhase.Playing))
                instance.SetPhase(GamePhase.Landing);
        }

        /// <summary>Every new colony lands on a new random map of its planet (director 2026-11-14).</summary>
        void NewMap() => runner?.NewRandomMap();

        /// <summary>
        /// The colony landed on this tile: its building site opens and the setup tutorial asks for the essential
        /// buildings, with time still stopped (director, 2026-11-14).
        /// </summary>
        public static void Landed(int tile)
        {
            if (instance == null || Phase != GamePhase.Landing)
                return;
            instance.setupTile = tile;
            instance.SetPhase(GamePhase.Setup);
            if (instance.picker != null)
            {
                instance.picker.Select(-1);
                instance.picker.Select(tile);
            }
            instance.RefreshSetup();
        }

        // ---- Setup tutorial ----

        /// <summary>The essential buildings, in the order the tutorial asks for them.</summary>
        static readonly (BuildMode Mode, BuildingType Type, string Name, string Category, string Why)[] Essentials =
        {
            (BuildMode.SolarPanel, BuildingType.SolarPanel, "Solar panel", "Power", "makes the colony's power by day"),
            (BuildMode.Battery, BuildingType.Battery, "Battery yard", "Power", "carries the colony through the night"),
            (BuildMode.ChargingStation, BuildingType.ChargingStation, "Charging station", "Stations", "robots charge here between jobs"),
            (BuildMode.Workshop, BuildingType.Workshop, "Robot Workshop", "Stations", "makes robots, upgrades (a boat hull for work on water) and refines metals"),
            (BuildMode.ResearchStation, BuildingType.ResearchStation, "Research Station", "Stations", "robots measure the planet and run experiments here"),
        };

        bool Has(BuildingType type)
        {
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim == null || setupTile < 0) return false;
            for (int s = 0; s < sim.SlotCount(setupTile); s++)
                if (sim.GetBuilding(setupTile, s) == type) return true;
            return false;
        }

        void RefreshSetup()
        {
            if (setupSteps == null) return;
            string list = "";
            int next = -1;
            for (int i = 0; i < Essentials.Length; i++)
            {
                bool done = Has(Essentials[i].Type);
                if (!done && next < 0) next = i;
                list += (done ? "✓ " : next == i ? "▶ " : "· ") + Essentials[i].Name + " — " + Essentials[i].Why + "\n";
            }
            setupSteps.text = list.TrimEnd('\n');
            if (next < 0)
            {
                FinishSetup();
                return;
            }
            var e = Essentials[next];
            setupNow.text = $"Step {next + 1} of {Essentials.Length}: open {e.Category} in the build bar at the bottom and drag a {e.Name} onto a free slot of the site. " +
                            "Time starts when all four are placed; your three robots then build them from the lander's cargo.";
        }

        void SkipSetup()
        {
            if (placer != null)
                foreach (var e in Essentials)
                    if (!Has(e.Type))
                        placer.AutoPlace(e.Mode);
            FinishSetup();
        }

        void FinishSetup()
        {
            if (Phase != GamePhase.Setup) return;
            SetPhase(GamePhase.Playing);
            runner?.SetSpeed(ClockSpeed.HourPerSecond);
        }

        void OnLoaded()
        {
            SetPhase(GamePhase.Playing);
            runner?.SetSpeed(speedBeforePause != ClockSpeed.Paused ? speedBeforePause : ClockSpeed.HourPerSecond);
        }

        void Pause()
        {
            if (runner == null || runner.Clock == null || runner.Clock.Speed == ClockSpeed.Paused)
                return;
            speedBeforePause = runner.Clock.Speed;
            runner.SetSpeed(ClockSpeed.Paused);
        }

        void Update()
        {
            if (Phase == GamePhase.Setup && Time.unscaledTime >= nextSetupRefresh)
            {
                nextSetupRefresh = Time.unscaledTime + 0.25f;
                RefreshSetup();
            }
            // Outside play (and in the setup tutorial), time stays stopped whatever else asks for it.
            if (!IsPlaying || InSetup || pauseOpen)
            {
                if (runner != null && runner.Clock != null && runner.Clock.Speed != ClockSpeed.Paused)
                    runner.SetSpeed(ClockSpeed.Paused);
            }
            // The chooser was closed without picking: land on the planet already loaded.
            if (Phase == GamePhase.ChoosingPlanet && chooser != null && !chooser.Visible)
                SetPhase(GamePhase.Landing);

            if (ScreenInput.KeyDown(KeyCode.Escape))
            {
                if (pauseOpen) ClosePause();
                else if (IsPlaying && idleLastFrame) OpenPause();
            }
        }

        /// <summary>Esc opens the pause menu only when it had nothing else to close (judged last frame, before the others act).</summary>
        void LateUpdate()
        {
            idleLastFrame = IsPlaying && !pauseOpen
                && (picker == null || picker.SelectedTile < 0)
                && (siteView == null || siteView.Site == null)
                && (chooser == null || !chooser.Visible)
                && !SystemTabs.AnyOpen;
        }

        void OpenPause()
        {
            pauseOpen = true;
            Pause();
            bool hasSave = saveGame != null && saveGame.HasSave;
            loadButton.SetEnabled(hasSave);
            pauseMessage.text = "";
            Show(pauseMenu, true);
        }

        void ClosePause()
        {
            pauseOpen = false;
            Show(pauseMenu, false);
            if (!InSetup)
                runner?.SetSpeed(speedBeforePause != ClockSpeed.Paused ? speedBeforePause : ClockSpeed.HourPerSecond);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- UI ----

        void BuildMenus()
        {
            mainMenu = Screen(dim: 0.88f);
            VisualElement box = Box(mainMenu, 360);
            var title = new Label("Kardashev Ascent");
            title.style.fontSize = 30; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter; title.style.marginBottom = 4;
            box.Add(title);
            var tag = new Label("Land a colony on another world and grow it toward a Kardashev civilisation.");
            tag.AddToClassList("caption"); tag.style.whiteSpace = WhiteSpace.Normal;
            tag.style.unityTextAlign = TextAnchor.MiddleCenter; tag.style.marginBottom = 14;
            box.Add(tag);
            box.Add(MenuButton("New game", () => SetPhase(GamePhase.ChoosingPlanet), primary: true));
            continueButton = MenuButton("Continue saved game", () =>
            {
                saveGame?.Load();
                if (Phase == GamePhase.MainMenu) menuMessage.text = saveGame != null ? saveGame.Message : "";
            });
            box.Add(continueButton);
            box.Add(MenuButton("Quit", Quit));
            menuMessage = new Label(""); menuMessage.AddToClassList("caption");
            menuMessage.style.unityTextAlign = TextAnchor.MiddleCenter; menuMessage.style.marginTop = 6;
            box.Add(menuMessage);

            pauseMenu = Screen(dim: 0.55f);
            VisualElement pbox = Box(pauseMenu, 300);
            var ptitle = new Label("Paused");
            ptitle.style.fontSize = 22; ptitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            ptitle.style.unityTextAlign = TextAnchor.MiddleCenter; ptitle.style.marginBottom = 10;
            pbox.Add(ptitle);
            pbox.Add(MenuButton("Resume (Esc)", ClosePause, primary: true));
            pbox.Add(MenuButton("Save", () => { saveGame?.Save(); pauseMessage.text = saveGame != null ? saveGame.Message : ""; loadButton.SetEnabled(saveGame != null && saveGame.HasSave); }));
            loadButton = MenuButton("Load", () => saveGame?.Load());
            pbox.Add(loadButton);
            pbox.Add(MenuButton("Main menu", () => SetPhase(GamePhase.MainMenu)));
            pbox.Add(MenuButton("Quit", Quit));
            pauseMessage = new Label(""); pauseMessage.AddToClassList("caption");
            pauseMessage.style.unityTextAlign = TextAnchor.MiddleCenter; pauseMessage.style.marginTop = 6;
            pbox.Add(pauseMessage);

            landingBanner = new VisualElement();
            landingBanner.AddToClassList("panel");
            landingBanner.pickingMode = PickingMode.Ignore;
            landingBanner.style.position = Position.Absolute;
            landingBanner.style.top = 96;
            landingBanner.style.left = new Length(50, LengthUnit.Percent);
            landingBanner.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            landingBanner.style.paddingLeft = landingBanner.style.paddingRight = 14;
            landingBanner.style.paddingTop = landingBanner.style.paddingBottom = 8;
            var lt = new Label("Choose where to land");
            lt.style.fontSize = 16; lt.style.unityFontStyleAndWeight = FontStyle.Bold;
            landingBanner.Add(lt);
            var lb = new Label("Click a land tile and press Land here in its panel. Time starts when the lander is down, " +
                               "and the colony can't move afterwards. M shows where sunlight is best; L picks another planet.");
            lb.AddToClassList("body"); lb.style.whiteSpace = WhiteSpace.Normal; lb.style.maxWidth = 520;
            landingBanner.Add(lb);
            root.Add(landingBanner);

            setupPanel = new VisualElement();
            setupPanel.AddToClassList("panel");
            setupPanel.style.position = Position.Absolute;
            setupPanel.style.top = 116;   // under the system tab bar
            setupPanel.style.left = new Length(50, LengthUnit.Percent);
            setupPanel.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            setupPanel.style.width = 560;
            setupPanel.style.paddingLeft = setupPanel.style.paddingRight = 14;
            setupPanel.style.paddingTop = setupPanel.style.paddingBottom = 10;
            var st = new Label("Set up your colony");
            st.style.fontSize = 16; st.style.unityFontStyleAndWeight = FontStyle.Bold;
            setupPanel.Add(st);
            setupNow = new Label("");
            setupNow.AddToClassList("body"); setupNow.style.whiteSpace = WhiteSpace.Normal; setupNow.style.marginTop = 4;
            setupPanel.Add(setupNow);
            setupSteps = new Label("");
            setupSteps.AddToClassList("body"); setupSteps.style.whiteSpace = WhiteSpace.Normal; setupSteps.style.marginTop = 6;
            setupPanel.Add(setupSteps);
            Button skip = MenuButton("Skip: place the rest for me", SkipSetup);
            skip.style.height = 30; skip.style.fontSize = 13; skip.style.marginTop = 8;
            setupPanel.Add(skip);
            root.Add(setupPanel);
        }

        VisualElement Screen(float dim)
        {
            var s = new VisualElement();
            s.style.position = Position.Absolute;
            s.style.left = 0; s.style.right = 0; s.style.top = 0; s.style.bottom = 0;
            s.style.backgroundColor = new Color(0.02f, 0.03f, 0.06f, dim);
            s.style.alignItems = Align.Center;
            s.style.justifyContent = Justify.Center;
            root.Add(s);
            return s;
        }

        static VisualElement Box(VisualElement screen, float width)
        {
            var b = new VisualElement();
            b.AddToClassList("panel");
            b.style.width = width;
            b.style.paddingLeft = b.style.paddingRight = 22;
            b.style.paddingTop = b.style.paddingBottom = 18;
            screen.Add(b);
            return b;
        }

        static Button MenuButton(string text, System.Action onClick, bool primary = false)
        {
            var b = new Button(onClick) { text = text };
            b.style.height = 36;
            b.style.marginTop = 4; b.style.marginBottom = 4;
            b.style.fontSize = 15;
            // Square grey blocks from the theme; the main choice is the solid amber one.
            if (primary) b.AddToClassList("primary-button");
            return b;
        }

        static void Show(VisualElement e, bool visible)
        {
            if (e != null) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void CopyStyles(VisualElement target)
        {
            foreach (MonoBehaviour view in new MonoBehaviour[] { FindAnyObjectByType<PanelsView>(), FindAnyObjectByType<WorkforceView>() })
            {
                UIDocument d = view != null ? view.GetComponent<UIDocument>() : null;
                if (d == null || d.rootVisualElement == null)
                    continue;
                var sources = new System.Collections.Generic.List<VisualElement> { d.rootVisualElement };
                foreach (VisualElement c in d.rootVisualElement.Children()) sources.Add(c);
                foreach (VisualElement e in sources)
                    for (int i = 0; i < e.styleSheets.count; i++)
                        if (!target.styleSheets.Contains(e.styleSheets[i]))
                            target.styleSheets.Add(e.styleSheets[i]);
            }
        }
    }
}

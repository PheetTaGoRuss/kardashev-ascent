using System.Collections.Generic;
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

        /// <summary>The landing tile while the setup tutorial runs (−1 otherwise): the essentials go there.</summary>
        public static int SetupTile => instance != null && InSetup ? instance.setupTile : -1;

        /// <summary>A building was refused because it wasn't on the landing tile: say so in the setup panel.</summary>
        public static void WarnNotLandingTile()
        {
            if (instance == null || !InSetup || instance.setupNow == null) return;
            instance.setupWarning = $"Build the essentials on your landing tile #{instance.setupTile}. Press R to fly back there.";
            instance.RefreshSetup();
        }

        string setupWarning;
        int lastSetupStep = -1;

        /// <summary>True when a flow is running (it opens the chooser itself, so the chooser doesn't open on its own).</summary>
        public static bool Active => instance != null;

        static GameFlow instance;

        SimulationRunner runner;
        SaveGameController saveGame;
        PlanetChooserView chooser;
        TilePicker picker;
        SiteView siteView;
        PanelPlacer placer;
        VisualElement root, mainMenu, pauseMenu, landingBanner, setupPanel, settingsScreen, creditsScreen, menuButtons, menuHome;
        GuideWindow menuGuide;
        VisualElement menuGuidePanel;
        Label setupSteps, setupNow;
        int setupTile = -1;
        float nextSetupRefresh;
        Button continueButton, loadButton;
        Label menuMessage, pauseMessage;
        bool pauseOpen;
        Button saveButton;
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
            KardashevAscent.Presentation.Planet.SpaceSky.Ensure();
            ThemePopups(root);
            ScreenInput.Register(root.panel);
            BuildMenus();
            if (saveGame != null)
                saveGame.Loaded += OnLoaded;
            SetPhase(GamePhase.MainMenu);
        }

        void OnDisable()
        {
            menuPlanet?.Dispose();
            menuPlanet = null;
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
            Show(settingsScreen, false);
            Show(creditsScreen, false);
            ShowMenuGuide(false);
            if (phase == GamePhase.MainMenu) PlayIntro();
            Show(pauseMenu, false);
            Show(landingBanner, phase == GamePhase.Landing && !KardashevAscent.Presentation.UI.SystemTabs.AnyOpen);   // out of the way while a system window is open
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
            // The charger first (director 2026-11-15: robots can't recharge without one).
            (BuildMode.ChargingStation, BuildingType.ChargingStation, "Charging station", "Stations", "robots can only recharge at a charger: build it first"),
            (BuildMode.SolarPanel, BuildingType.SolarPanel, "Solar panel", "Power", "makes the colony's power by day"),
            (BuildMode.Battery, BuildingType.Battery, "Battery yard", "Power", "carries the colony through the night (robots can charge here too)"),
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
            // The last step: nothing is wired for you, so wire what you built (director 2026-11-20).
            int unwired = 0;
            if (next < 0 && runner != null && runner.Simulation != null && runner.Simulation.WiredGrid)
                foreach (var w in runner.Simulation.UnwiredBuildings())
                    if (w.Tile == setupTile) unwired++;
            if (next < 0 && unwired > 0)
            {
                setupSteps.text = list + "▶ Wire them — nothing makes or uses power until a wire joins it to the grid";
                setupNow.text = $"Step {Essentials.Length + 1}/{Essentials.Length + 1}: Grid ► drag a Power line onto each building marked IN or OUT ({unwired} left).";
                setupNow.style.color = StyleKeyword.Null;
                return;
            }
            setupSteps.text = list.TrimEnd('\n');
            if (next < 0)
            {
                FinishSetup();
                return;
            }
            var e = Essentials[next];
            if (next != lastSetupStep)
                setupWarning = null;   // a building went down: the warning has done its job
            lastSetupStep = next;
            // Short: what to do now; the warning (wrong tile) in red underneath.
            setupNow.text = $"Step {next + 1}/{Essentials.Length}: {e.Category} ► drag a {e.Name} onto your site." +
                            (setupWarning != null ? "\n" + setupWarning : "");
            setupNow.style.color = setupWarning != null ? new Color(1f, 0.47f, 0.41f) : StyleKeyword.Null;
        }

        void SkipSetup()
        {
            // The rest go on the landing tile, whichever site is open now (director 2026-11-15: building elsewhere first
            // left Skip with nothing to place).
            if (placer != null && setupTile >= 0 && placer.Tile != setupTile && picker != null)
            {
                picker.Select(-1);
                picker.Select(setupTile);
            }
            if (placer != null && placer.Tile == setupTile)
                foreach (var e in Essentials)
                    if (!Has(e.Type))
                        placer.AutoPlace(e.Mode);
            // One panel is a small slot on rich ground: add panels until the colony covers its base demand (KI-34).
            if (placer != null && placer.Tile == setupTile && runner != null && runner.Simulation != null)
                placer.AutoPlacePanelsFor(System.Math.Max(runner.Simulation.DemandW, KardashevAscent.Simulation.Energy.PowerGrid.Lander().DemandW));   // demand reads 0 until setup ends
            runner?.Simulation?.WireAllBuildings();   // "place the rest for me" includes the wires
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
            AnimateMenu();
            if (setupPanel != null) setupPanel.style.top = KardashevAscent.Presentation.UI.UiLayout.WindowTop;   // under the tabs, wherever the top bar ends
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

            if (!IsPlaying && ScreenInput.KeyDown(KeyCode.J))
                ShowMenuGuide(menuGuide == null || !menuGuide.Visible);

            if (ScreenInput.KeyDown(KeyCode.Escape))
            {
                if (menuGuide != null && menuGuide.Visible) ShowMenuGuide(false);
                else if (settingsScreen != null && settingsScreen.style.display == DisplayStyle.Flex) ShowSettings(false);
                else if (creditsScreen != null && creditsScreen.style.display == DisplayStyle.Flex) ShowCredits(false);
                else if (pauseOpen) ClosePause();
                else if ((IsPlaying || Phase == GamePhase.Landing) && idleLastFrame) OpenPause();
            }
        }

        /// <summary>Esc opens the pause menu only when it had nothing else to close (judged last frame, before the others act).</summary>
        void LateUpdate()
        {
            idleLastFrame = (IsPlaying || Phase == GamePhase.Landing) && !pauseOpen
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
            saveButton?.SetEnabled(Phase != GamePhase.Landing);   // nothing to save before the colony lands
            pauseMessage.text = "";
            Show(pauseMenu, true);
        }

        void ClosePause()
        {
            pauseOpen = false;
            Show(pauseMenu, false);
            if (!InSetup && Phase != GamePhase.Landing)
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
            BuildMainMenu();

            pauseMenu = Screen(dim: 0.55f);
            VisualElement pbox = Box(pauseMenu, 300);
            var ptitle = new Label("Paused");
            ptitle.style.fontSize = 22; ptitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            ptitle.style.unityTextAlign = TextAnchor.MiddleCenter; ptitle.style.marginBottom = 10;
            pbox.Add(ptitle);
            pbox.Add(MenuButton("Resume (Esc)", ClosePause, primary: true));
            saveButton = MenuButton("Save", () => { saveGame?.Save(); pauseMessage.text = saveGame != null ? saveGame.Message : ""; loadButton.SetEnabled(saveGame != null && saveGame.HasSave); });
            pbox.Add(saveButton);
            loadButton = MenuButton("Load", () => saveGame?.Load());
            pbox.Add(loadButton);
            pbox.Add(MenuButton("Guide book (J)", () => { ClosePause(); SystemTabs.Toggle("guide"); }));
            pbox.Add(MenuButton("Settings", () => ShowSettings(true)));
            pbox.Add(MenuButton("Main menu", () => SetPhase(GamePhase.MainMenu)));
            pbox.Add(MenuButton("Quit", Quit));
            // Sound (director 2026-11-14): effects and ambience volumes, and mute; kept between games.
            var soundHead = new Label("SOUND");
            soundHead.AddToClassList("heading");
            soundHead.style.marginTop = 10;
            pbox.Add(soundHead);
            pbox.Add(VolumeSlider("Effects", KardashevAscent.Presentation.Audio.GameAudio.SfxVolume,
                                  v => { KardashevAscent.Presentation.Audio.GameAudio.SetSfxVolume(v); KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Click); }));
            pbox.Add(VolumeSlider("Ambience", KardashevAscent.Presentation.Audio.GameAudio.MusicVolume, KardashevAscent.Presentation.Audio.GameAudio.SetMusicVolume));
            var mute = new Toggle("Mute all") { value = KardashevAscent.Presentation.Audio.GameAudio.Muted };
            mute.RegisterValueChangedCallback(e => KardashevAscent.Presentation.Audio.GameAudio.SetMuted(e.newValue));
            mute.style.marginTop = 4;
            pbox.Add(mute);
            pauseMessage = new Label(""); pauseMessage.AddToClassList("caption");
            pauseMessage.style.unityTextAlign = TextAnchor.MiddleCenter; pauseMessage.style.marginTop = 6;
            pbox.Add(pauseMessage);

            landingBanner = new VisualElement();
            landingBanner.AddToClassList("panel");
            landingBanner.pickingMode = PickingMode.Ignore;
            landingBanner.style.position = Position.Absolute;
            landingBanner.style.bottom = 36;   // at the bottom edge, clear of the tab bar and the system windows
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
            // Settings and the menu are open before landing too (director 2026-11-20).
            var lbMenu = new Button(OpenPause) { text = "⚙  Settings and menu (Esc)" };
            lbMenu.AddToClassList("small-button");
            lbMenu.style.marginTop = 6;
            lbMenu.style.alignSelf = Align.FlexStart;
            landingBanner.Add(lbMenu);
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

            settingsScreen = BuildSettings();
            creditsScreen = BuildCredits();
            // The guide book on the menus and before landing (director 2026-11-15: open it any time).
            menuGuide = new GuideWindow { CloseOverride = () => ShowMenuGuide(false) };
            menuGuidePanel = menuGuide.Build();
            root.Add(menuGuidePanel);
            menuGuide.SetVisible(false);
        }

        // ---- The space menu (director 2026-11-15: make it beautiful) ----

        // Pieces of the menu the intro animates.
        SpaceBackdrop menuBack, menuFront;
        MenuPlanet menuPlanet;
        VisualElement blackout, flash, shockRing, menuLine;
        Label menuKicker, menuTag, menuFooter;
        readonly System.Collections.Generic.List<Label> titleWords = new System.Collections.Generic.List<Label>();
        readonly System.Collections.Generic.List<Button> menuItems = new System.Collections.Generic.List<Button>();
        static bool introPlayed;
        bool introRunning;
        float shakeUntil, ringStart = -10f;

        void BuildMainMenu()
        {
            mainMenu = Screen(dim: 0f);
            mainMenu.style.alignItems = Align.Stretch;
            mainMenu.style.justifyContent = Justify.FlexStart;
            menuBack = new SpaceBackdrop(SpaceBackdrop.Layer.Back);
            mainMenu.Add(menuBack);

            // The real planet in 3D, behind the title (director 2026-11-15), with the moon's near half drawn over it.
            PlanetView view = FindAnyObjectByType<PlanetView>();
            if (view != null)
            {
                menuPlanet = new MenuPlanet(view);
                menuBack.PlanetIs3D = true;
                mainMenu.Add(menuPlanet.Element);
                menuBack.RegisterCallback<GeometryChangedEvent>(_ => PlacePlanet());
            }
            menuFront = new SpaceBackdrop(SpaceBackdrop.Layer.Front);
            mainMenu.Add(menuFront);

            menuHome = new VisualElement();
            menuHome.style.position = Position.Absolute;
            menuHome.style.left = new Length(7, LengthUnit.Percent);
            menuHome.style.top = new Length(16, LengthUnit.Percent);
            menuHome.style.width = 560;
            mainMenu.Add(menuHome);

            menuKicker = new Label("A PLANETARY ENGINEERING GAME");
            menuKicker.style.fontSize = 14; menuKicker.style.letterSpacing = 8;
            menuKicker.style.color = UiPalette.Accent; menuKicker.style.marginBottom = 6;
            menuHome.Add(menuKicker);
            titleWords.Clear();
            foreach (string word in new[] { "KARDASHEV", "ASCENT" })
            {
                var t = new Label(word);
                t.style.fontSize = 80; t.style.unityFontStyleAndWeight = FontStyle.Bold;
                t.style.letterSpacing = 16; t.style.color = Color.white;
                t.style.marginTop = -10; t.style.marginBottom = -10;
                t.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 22, color = new Color(0.4f, 0.7f, 1f, 0.6f) };
                t.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(50));
                menuHome.Add(t);
                titleWords.Add(t);
            }
            menuLine = new VisualElement();
            menuLine.style.height = 2; menuLine.style.width = 160;
            menuLine.style.backgroundColor = UiPalette.Accent;
            menuLine.style.marginTop = 20; menuLine.style.marginBottom = 14;
            menuHome.Add(menuLine);
            menuTag = new Label("From one lander to a civilisation that harnesses its star.\nReal exoplanets · NASA data · real physics.");
            menuTag.style.fontSize = 17; menuTag.style.whiteSpace = WhiteSpace.Normal;
            menuTag.style.color = new Color(0.82f, 0.86f, 0.92f);
            menuTag.style.marginBottom = 30;
            menuHome.Add(menuTag);

            menuButtons = new VisualElement();
            menuButtons.style.width = 340;
            menuHome.Add(menuButtons);
            menuItems.Clear();
            menuItems.Add(SpaceButton("NEW MISSION", NewMission, primary: true));
            continueButton = SpaceButton("CONTINUE", () =>
            {
                saveGame?.Load();
                if (Phase == GamePhase.MainMenu) menuMessage.text = saveGame != null ? saveGame.Message : "";
            });
            menuItems.Add(continueButton);
            menuItems.Add(SpaceButton("GUIDE BOOK", () => ShowMenuGuide(true)));
            menuItems.Add(SpaceButton("SETTINGS", () => ShowSettings(true)));
            menuItems.Add(SpaceButton("CREDITS", () => ShowCredits(true)));
            menuItems.Add(SpaceButton("QUIT", Quit));
            foreach (Button b in menuItems) menuButtons.Add(b);
            menuMessage = new Label("");
            menuMessage.style.fontSize = 13; menuMessage.style.color = new Color(0.65f, 0.7f, 0.78f);
            menuMessage.style.marginTop = 8; menuMessage.style.paddingLeft = 18;
            menuButtons.Add(menuMessage);

            menuFooter = new Label("TEAM GIE  ·  NASA SPACE APPS CHALLENGE 2026  ·  SPACE MISSION DESIGN GAME");
            menuFooter.style.position = Position.Absolute;
            menuFooter.style.left = new Length(7, LengthUnit.Percent);
            menuFooter.style.bottom = 28;
            menuFooter.style.fontSize = 12; menuFooter.style.letterSpacing = 3;
            menuFooter.style.color = new Color(0.6f, 0.65f, 0.75f, 0.9f);
            mainMenu.Add(menuFooter);

            // The intro's effects: a shock ring, a white flash and the black it fades in from.
            shockRing = new VisualElement { pickingMode = PickingMode.Ignore };
            shockRing.style.position = Position.Absolute;
            shockRing.style.borderTopWidth = shockRing.style.borderBottomWidth = shockRing.style.borderLeftWidth = shockRing.style.borderRightWidth = 3;
            shockRing.style.display = DisplayStyle.None;
            mainMenu.Add(shockRing);
            flash = new VisualElement { pickingMode = PickingMode.Ignore };
            flash.style.position = Position.Absolute;
            flash.style.left = 0; flash.style.right = 0; flash.style.top = 0; flash.style.bottom = 0;
            flash.style.backgroundColor = new Color(1f, 0.93f, 0.8f);
            flash.style.opacity = 0f;
            mainMenu.Add(flash);
            blackout = new VisualElement { pickingMode = PickingMode.Ignore };
            blackout.style.position = Position.Absolute;
            blackout.style.left = 0; blackout.style.right = 0; blackout.style.top = 0; blackout.style.bottom = 0;
            blackout.style.backgroundColor = Color.black;
            blackout.style.opacity = 0f;
            mainMenu.Add(blackout);
            // Any click skips the intro.
            mainMenu.RegisterCallback<PointerDownEvent>(_ => { if (introRunning) FinishIntro(); }, TrickleDown.TrickleDown);
        }

        /// <summary>Put the 3D planet's picture exactly where the backdrop draws its glow and orbit.</summary>
        void PlacePlanet()
        {
            if (menuPlanet == null) return;
            SpaceBackdrop.PlanetGeometry(menuBack.contentRect, out Vector2 c, out float r);
            float size = 2f * r / MenuPlanet.Fill;
            VisualElement e = menuPlanet.Element;
            e.style.left = c.x - size * 0.5f;
            e.style.top = c.y - size * 0.5f;
            e.style.width = size;
            e.style.height = size;
        }

        // ---- The intro (director 2026-11-15: "boom and epic like a AAA game") ----

        static System.Collections.Generic.List<TimeValue> Seconds(float s) =>
            new System.Collections.Generic.List<TimeValue> { new TimeValue(s, TimeUnit.Second) };

        static System.Collections.Generic.List<EasingFunction> Ease(EasingMode m) =>
            new System.Collections.Generic.List<EasingFunction> { new EasingFunction(m) };

        /// <summary>
        /// Black; a whoosh as the stars fade in; BOOM: the title slams in with a flash, a shock ring and a shake; then the
        /// tagline and the buttons slide in one by one. A click skips it. Only the first time the menu opens.
        /// </summary>
        void PlayIntro()
        {
            if (introPlayed) { FinishIntro(); return; }
            introPlayed = true;
            introRunning = true;
            foreach (VisualElement e in IntroParts()) { e.style.transitionDuration = Seconds(0f); e.style.opacity = 0f; }
            foreach (Label t in titleWords) t.style.scale = new Scale(new Vector3(1.6f, 1.6f, 1f));
            foreach (Button b in menuItems) b.style.translate = new Translate(-40, 0);
            blackout.style.transitionDuration = Seconds(0f);
            blackout.style.opacity = 1f;

            At(250, () =>
            {
                KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Whoosh);
                blackout.style.transitionDuration = Seconds(1.3f);
                blackout.style.transitionTimingFunction = Ease(EasingMode.EaseInCubic);
                blackout.style.opacity = 0f;
            });
            At(1300, Boom);
            At(1900, () =>
            {
                Reveal(menuKicker, 0.5f); Reveal(menuLine, 0.5f); Reveal(menuTag, 0.7f);
            });
            for (int i = 0; i < menuItems.Count; i++)
            {
                Button b = menuItems[i];
                At(2200 + 90 * i, () =>
                {
                    b.style.transitionDuration = Seconds(0.45f);
                    b.style.transitionTimingFunction = Ease(EasingMode.EaseOutCubic);
                    b.style.opacity = 1f;
                    b.style.translate = new Translate(0, 0);
                    KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Hover, 0.5f);
                });
            }
            At(2200 + 90 * menuItems.Count + 300, () => { Reveal(menuFooter, 0.8f); introRunning = false; });
        }

        void Boom()
        {
            if (!introRunning) return;
            KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Boom);
            foreach (Label t in titleWords)
            {
                t.style.transitionDuration = Seconds(0.28f);
                t.style.transitionTimingFunction = Ease(EasingMode.EaseOutBack);
                t.style.opacity = 1f;
                t.style.scale = new Scale(Vector3.one);
            }
            flash.style.transitionDuration = Seconds(0f);
            flash.style.opacity = 0.65f;
            flash.schedule.Execute(() =>
            {
                flash.style.transitionDuration = Seconds(0.9f);
                flash.style.transitionTimingFunction = Ease(EasingMode.EaseOutCubic);
                flash.style.opacity = 0f;
            }).StartingIn(40);
            shakeUntil = Time.unscaledTime + 0.45f;
            ringStart = Time.unscaledTime;
        }

        IEnumerable<VisualElement> IntroParts()
        {
            yield return menuKicker; yield return menuLine; yield return menuTag; yield return menuFooter;
            foreach (Label t in titleWords) yield return t;
            foreach (Button b in menuItems) yield return b;
        }

        static void Reveal(VisualElement e, float seconds)
        {
            e.style.transitionDuration = Seconds(seconds);
            e.style.transitionTimingFunction = Ease(EasingMode.EaseOutCubic);
            e.style.opacity = 1f;
        }

        /// <summary>Run a step of the intro after <paramref name="ms"/>, unless the intro was skipped.</summary>
        void At(long ms, System.Action a) => mainMenu.schedule.Execute(() => { if (introRunning) a(); }).StartingIn(ms);

        /// <summary>Jump to the end of the intro (a click, or every later visit to the menu).</summary>
        void FinishIntro()
        {
            introRunning = false;
            foreach (VisualElement e in IntroParts()) { e.style.transitionDuration = Seconds(0.25f); e.style.opacity = 1f; }
            foreach (Label t in titleWords) t.style.scale = new Scale(Vector3.one);
            foreach (Button b in menuItems) b.style.translate = new Translate(0, 0);
            blackout.style.transitionDuration = Seconds(0.3f);
            blackout.style.opacity = 0f;
            flash.style.opacity = 0f;
        }

        /// <summary>Per frame: the 3D planet's camera, the title shake and the shock ring.</summary>
        void AnimateMenu()
        {
            bool menuOn = Phase == GamePhase.MainMenu;
            menuPlanet?.SetActive(menuOn);
            if (!menuOn) return;
            menuPlanet?.Update(Time.unscaledDeltaTime);
            float now = Time.unscaledTime;
            menuHome.style.translate = now < shakeUntil
                ? new Translate(Random.Range(-7f, 7f) * (shakeUntil - now) / 0.45f, Random.Range(-5f, 5f) * (shakeUntil - now) / 0.45f)
                : new Translate(0, 0);
            float age = now - ringStart;
            if (age >= 0f && age < 1.2f && titleWords.Count > 0)
            {
                Rect t = titleWords[0].worldBound;
                Vector2 at = mainMenu.WorldToLocal(new Vector2(t.xMin + t.width * 0.45f, t.yMax + 4f));
                float d = Mathf.Lerp(40f, 1900f, 1f - Mathf.Pow(1f - age / 1.2f, 3f));
                shockRing.style.display = DisplayStyle.Flex;
                shockRing.style.left = at.x - d * 0.5f; shockRing.style.top = at.y - d * 0.5f;
                shockRing.style.width = d; shockRing.style.height = d;
                float r = d * 0.5f;
                shockRing.style.borderTopLeftRadius = shockRing.style.borderTopRightRadius = r;
                shockRing.style.borderBottomLeftRadius = shockRing.style.borderBottomRightRadius = r;
                Color c = new Color(1f, 0.82f, 0.5f, 0.75f * (1f - age / 1.2f));
                shockRing.style.borderTopColor = shockRing.style.borderBottomColor = shockRing.style.borderLeftColor = shockRing.style.borderRightColor = c;
            }
            else
                shockRing.style.display = DisplayStyle.None;
        }

        /// <summary>New mission: a whoosh and a quick fade to black, then the planet chooser.</summary>
        void NewMission()
        {
            KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Whoosh, 0.8f);
            blackout.style.transitionDuration = Seconds(0.45f);
            blackout.style.transitionTimingFunction = Ease(EasingMode.EaseInCubic);
            blackout.style.opacity = 1f;
            mainMenu.schedule.Execute(() =>
            {
                SetPhase(GamePhase.ChoosingPlanet);
                blackout.style.transitionDuration = Seconds(0f);
                blackout.style.opacity = 0f;
            }).StartingIn(480);
        }

        /// <summary>A menu button: just text on space, no box (director 2026-11-15). Hover: it glows amber, slides right,
        /// grows a little, an amber bar sweeps out under it, and it ticks.</summary>
        static Button SpaceButton(string text, System.Action onClick, bool primary = false)
        {
            var b = new Button(onClick) { text = text };
            b.style.height = 44;
            b.style.marginTop = 2; b.style.marginBottom = 2;
            b.style.fontSize = primary ? 24 : 20;
            b.style.letterSpacing = 6;
            b.style.unityFontStyleAndWeight = primary ? FontStyle.Bold : FontStyle.Normal;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingLeft = 18;
            b.style.backgroundColor = Color.clear;
            b.style.borderTopWidth = b.style.borderRightWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = 0;
            b.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(50));
            Color idle = primary ? UiPalette.Accent : new Color(0.86f, 0.9f, 0.96f, 0.85f);
            b.style.color = idle;
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute;
            bar.style.left = 18; bar.style.bottom = 4;
            bar.style.height = 2; bar.style.width = 0;
            bar.style.backgroundColor = UiPalette.Accent;
            bar.style.transitionDuration = Seconds(0.25f);
            bar.style.transitionTimingFunction = Ease(EasingMode.EaseOutCubic);
            b.Add(bar);
            var chevron = new Label("›") { pickingMode = PickingMode.Ignore };
            chevron.style.position = Position.Absolute;
            chevron.style.left = 0; chevron.style.top = 4;
            chevron.style.fontSize = 26; chevron.style.color = UiPalette.Accent;
            chevron.style.opacity = 0f;
            chevron.style.transitionDuration = Seconds(0.2f);
            b.Add(chevron);
            b.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (!b.enabledSelf) return;
                KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Hover);
                b.style.transitionDuration = Seconds(0.18f);
                b.style.transitionTimingFunction = Ease(EasingMode.EaseOutCubic);
                b.style.color = primary ? new Color(1f, 0.9f, 0.62f) : Color.white;
                b.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 14, color = new Color(1f, 0.75f, 0.3f, 0.9f) };
                b.style.translate = new Translate(14, 0);
                b.style.scale = new Scale(new Vector3(1.06f, 1.06f, 1f));
                bar.style.width = 180;
                chevron.style.opacity = 1f;
            });
            b.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                b.style.color = idle;
                b.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 0, color = Color.clear };
                b.style.translate = new Translate(0, 0);
                b.style.scale = new Scale(Vector3.one);
                bar.style.width = 0;
                chevron.style.opacity = 0f;
            });
            return b;
        }

        /// <summary>
        /// A glass panel that slides in from the right over the live menu (director 2026-11-15: the menu must not change
        /// when Settings opens): the 3D planet, starfield and title stay as they are behind a light scrim, the menu
        /// buttons dim, and the panel has an amber kicker, a spaced title, a scrolling body and Back. A click on the
        /// scrim closes it too.
        /// </summary>
        VisualElement Overlay(string title, out VisualElement body, float width)
        {
            VisualElement s = Screen(dim: 0.32f);
            s.style.alignItems = Align.FlexEnd;
            s.style.paddingRight = new Length(3, LengthUnit.Percent);
            s.RegisterCallback<PointerDownEvent>(e => { if (e.target == s) ShowOverlay(s, false); });
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.AddToClassList("overlay-panel");
            panel.style.width = width;
            panel.style.maxWidth = new Length(40, LengthUnit.Percent);   // clear of the title on small screens
            panel.style.maxHeight = new Length(84, LengthUnit.Percent);
            panel.style.paddingLeft = panel.style.paddingRight = 30;
            panel.style.paddingTop = 22; panel.style.paddingBottom = 20;
            var kicker = new Label("KARDASHEV ASCENT  ·  MISSION CONTROL");
            kicker.AddToClassList("heading");
            kicker.style.color = UiPalette.Cool;
            panel.Add(kicker);
            var t = new Label(title.ToUpperInvariant());
            t.style.fontSize = 30; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.letterSpacing = 10;
            t.style.marginTop = 4;
            t.style.color = UiPalette.Text;
            panel.Add(t);
            var rule = new VisualElement();
            rule.style.height = 2; rule.style.width = 90;
            rule.style.marginTop = 6; rule.style.marginBottom = 14;
            rule.style.backgroundColor = UiPalette.Accent;
            panel.Add(rule);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.flexShrink = 1;
            body = scroll.contentContainer;
            panel.Add(scroll);
            var back = MenuButton("◄  Back (Esc)", () => ShowOverlay(s, false));
            back.style.marginTop = 14;
            panel.Add(back);
            s.Add(panel);
            Show(s, false);
            return s;
        }

        /// <summary>Slide an overlay's panel in (or hide it) and dim the menu buttons behind it meanwhile.</summary>
        void ShowOverlay(VisualElement s, bool show)
        {
            if (s == null) return;
            Show(s, show);
            if (menuButtons != null)
            {
                menuButtons.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(0.3f, TimeUnit.Second) };
                menuButtons.style.opacity = show ? 0.25f : 1f;
            }
            if (!show) return;
            KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Whoosh, 0.6f);
            VisualElement panel = s.Q(className: "overlay-panel");
            s.style.opacity = 0f;
            s.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(0.35f, TimeUnit.Second) };
            if (panel != null)
            {
                panel.style.translate = new Translate(70, 0);
                panel.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("translate") };
                panel.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(0.45f, TimeUnit.Second) };
                panel.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };
            }
            s.schedule.Execute(() =>
            {
                s.style.opacity = 1f;
                if (panel != null) panel.style.translate = new Translate(0, 0);
            }).StartingIn(30);
        }

        static Label Heading(VisualElement to, string text)
        {
            var h = new Label(text.ToUpperInvariant());
            h.AddToClassList("heading");
            h.style.marginTop = 14; h.style.marginBottom = 4; h.style.letterSpacing = 3;
            h.style.color = UiPalette.Accent;
            to.Add(h);
            return h;
        }

        static Label Line(VisualElement to, string text, int size = 15)
        {
            var l = new Label(text);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.fontSize = size;
            l.style.marginTop = 2;
            to.Add(l);
            return l;
        }

        const string QualityKey = "kardashev.settings.quality", VSyncKey = "kardashev.settings.vsync";

        VisualElement BuildSettings()
        {
            // Saved choices come back at start.
            if (PlayerPrefs.HasKey(QualityKey)) QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt(QualityKey), 0, QualitySettings.names.Length - 1), true);
            if (PlayerPrefs.HasKey(VSyncKey)) QualitySettings.vSyncCount = PlayerPrefs.GetInt(VSyncKey);

            VisualElement s = Overlay("Settings", out VisualElement body, 520);
            Heading(body, "Sound");
            body.Add(VolumeSlider("Effects", KardashevAscent.Presentation.Audio.GameAudio.SfxVolume,
                                  v => { KardashevAscent.Presentation.Audio.GameAudio.SetSfxVolume(v); KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Click); }));
            body.Add(VolumeSlider("Ambience", KardashevAscent.Presentation.Audio.GameAudio.MusicVolume, KardashevAscent.Presentation.Audio.GameAudio.SetMusicVolume));
            var mute = new Toggle("Mute all") { value = KardashevAscent.Presentation.Audio.GameAudio.Muted };
            mute.RegisterValueChangedCallback(e => KardashevAscent.Presentation.Audio.GameAudio.SetMuted(e.newValue));
            body.Add(mute);

            Heading(body, "Display");
            var full = new Toggle("Fullscreen") { value = UnityEngine.Screen.fullScreen };
            full.RegisterValueChangedCallback(e => UnityEngine.Screen.fullScreen = e.newValue);
            body.Add(full);
            var vsync = new Toggle("V-Sync (smooth, no tearing)") { value = QualitySettings.vSyncCount > 0 };
            vsync.RegisterValueChangedCallback(e => { QualitySettings.vSyncCount = e.newValue ? 1 : 0; PlayerPrefs.SetInt(VSyncKey, QualitySettings.vSyncCount); });
            body.Add(vsync);
            if (QualitySettings.names.Length > 1)
            {
                var quality = new DropdownField("Graphics quality", new System.Collections.Generic.List<string>(QualitySettings.names), QualitySettings.GetQualityLevel());
                quality.RegisterValueChangedCallback(e =>
                {
                    int i = System.Array.IndexOf(QualitySettings.names, e.newValue);
                    if (i >= 0) { QualitySettings.SetQualityLevel(i, true); PlayerPrefs.SetInt(QualityKey, i); }
                });
                body.Add(quality);
            }

            Heading(body, "Controls");
            Line(body, "Drag or WASD / arrows: turn the camera · wheel or Q / E: zoom · R: back to the colony", 14);
            Line(body, "J: guide book (any time) · K: calculator · H: all controls · Esc: menu · F5 / F9: save / load", 14);
            root.Add(s);
            return s;
        }

        VisualElement BuildCredits()
        {
            VisualElement s = Overlay("Credits", out VisualElement body, 620);
            Line(body, "Kardashev Ascent: a planetary engineering game about running a robotic mission on a real world, with real physics.", 16);
            Line(body, "Made for the NASA Space Apps Challenge 2026 (challenge: Space Mission Design Game), Bangkok.", 14);
            if (TeamMembers.Length > 0)
            {
                Heading(body, "Team");
                Line(body, string.Join("\n", TeamMembers));
            }
            Heading(body, "NASA data");
            Line(body, "NASA Exoplanet Archive: the real planets and their stars (orbit, size, mass, star light and metals)");
            Line(body, "NASA POWER: 25 years of monthly climate for clouds, air temperature, ground reflectance and haze");
            Line(body, "NASA NSSDC Mars Fact Sheet: Mars");
            Line(body, "NASA MMRTG fact sheet: the lander's radioisotope power · JPL Mars 2020 press kit: the robots' batteries");
            Heading(body, "Science");
            Line(body, "Solar model checked against pvlib · Antoine equation (Dortmund Data Bank) · IAPWS steam tables · ISO 2533 atmosphere");
            Line(body, "IACS conductivity · IUPAC molar masses · International Aluminium Institute (smelting energy) · Rudnick & Gao 2003 (crust)");
            Heading(body, "Sound");
            Line(body, "Interface Sounds and Sci-Fi Sounds by Kenney (www.kenney.nl), CC0. Ambience, alarms and chords made in code.");
            Heading(body, "Made with");
            Line(body, "Unity 6 · C# · Python · NUnit");
            Line(body, "This project uses NASA open data. It is not affiliated with or endorsed by NASA.", 13).style.color = new Color(0.65f, 0.7f, 0.78f);
            root.Add(s);
            return s;
        }

        /// <summary>The team, shown in the credits (one line each; empty hides the section). Fill in your names.</summary>
        static readonly string[] TeamMembers = { "Team GIE", "Kiattiyot Sinprasom", "Puwanantorn Lertpoonsawat", "Methapat Khamkanya" };

        void ShowSettings(bool show) => ShowOverlay(settingsScreen, show);

        void ShowCredits(bool show) => ShowOverlay(creditsScreen, show);

        /// <summary>The guide book on the menus, the planet chooser and before landing (in play it is the J tab).</summary>
        void ShowMenuGuide(bool show)
        {
            if (menuGuide == null) return;
            menuGuide.SetVisible(show);
            if (show) menuGuidePanel.BringToFront();
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

        static VisualElement VolumeSlider(string name, float value, System.Action<float> changed)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            var label = new Label(name);
            label.AddToClassList("body");
            label.style.width = 80;
            row.Add(label);
            var slider = new Slider(0f, 1f) { value = value };
            slider.AddToClassList("slider");
            slider.style.flexGrow = 1;
            var pct = new Label($"{value * 100f:0} %");
            pct.AddToClassList("caption");
            pct.style.width = 44;
            pct.style.unityTextAlign = TextAnchor.MiddleRight;
            slider.RegisterValueChangedCallback(e => { pct.text = $"{e.newValue * 100f:0} %"; changed(e.newValue); });
            row.Add(slider);
            row.Add(pct);
            return row;
        }

        static void Show(VisualElement e, bool visible)
        {
            UiMotion.Show(e, visible);
        }

        /// <summary>
        /// Drop-down lists open on the panel's top level, outside every document, so the shared theme never reached them
        /// (they were Unity's white with a blue highlight and hard to read). Give the top level the shared sheet too.
        /// </summary>
        static void ThemePopups(VisualElement root)
        {
            VisualElement top = root?.panel?.visualTree;
            if (top == null) return;
            for (int i = 0; i < root.styleSheets.count; i++)
            {
                StyleSheet sheet = root.styleSheets[i];
                if (sheet != null && sheet.name == "Common" && !top.styleSheets.Contains(sheet))
                    top.styleSheets.Add(sheet);
            }
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

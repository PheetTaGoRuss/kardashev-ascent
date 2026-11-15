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
            Show(settingsScreen, false);
            Show(creditsScreen, false);
            ShowMenuGuide(false);
            if (phase == GamePhase.MainMenu) FadeIn(menuHome);
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

            if (!IsPlaying && ScreenInput.KeyDown(KeyCode.J))
                ShowMenuGuide(menuGuide == null || !menuGuide.Visible);

            if (ScreenInput.KeyDown(KeyCode.Escape))
            {
                if (menuGuide != null && menuGuide.Visible) ShowMenuGuide(false);
                else if (settingsScreen != null && settingsScreen.style.display == DisplayStyle.Flex) ShowSettings(false);
                else if (creditsScreen != null && creditsScreen.style.display == DisplayStyle.Flex) ShowCredits(false);
                else if (pauseOpen) ClosePause();
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
            BuildMainMenu();

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

            settingsScreen = BuildSettings();
            creditsScreen = BuildCredits();
            // The guide book on the menus and before landing (director 2026-11-15: open it any time).
            menuGuide = new GuideWindow { CloseOverride = () => ShowMenuGuide(false) };
            menuGuidePanel = menuGuide.Build();
            root.Add(menuGuidePanel);
            menuGuide.SetVisible(false);
        }

        // ---- The space menu (director 2026-11-15: make it beautiful) ----

        void BuildMainMenu()
        {
            mainMenu = Screen(dim: 0f);
            mainMenu.style.alignItems = Align.Stretch;
            mainMenu.style.justifyContent = Justify.FlexStart;
            mainMenu.Add(new SpaceBackdrop());

            menuHome = new VisualElement();
            menuHome.style.position = Position.Absolute;
            menuHome.style.left = new Length(7, LengthUnit.Percent);
            menuHome.style.top = new Length(18, LengthUnit.Percent);
            menuHome.style.width = 520;
            mainMenu.Add(menuHome);

            var kicker = new Label("A PLANETARY ENGINEERING GAME");
            kicker.style.fontSize = 14; kicker.style.letterSpacing = 6;
            kicker.style.color = UiPalette.Accent; kicker.style.marginBottom = 6;
            menuHome.Add(kicker);
            foreach (string word in new[] { "KARDASHEV", "ASCENT" })
            {
                var t = new Label(word);
                t.style.fontSize = 76; t.style.unityFontStyleAndWeight = FontStyle.Bold;
                t.style.letterSpacing = 14; t.style.color = Color.white;
                t.style.marginTop = -10; t.style.marginBottom = -10;
                t.style.textShadow = new TextShadow { offset = new Vector2(0, 0), blurRadius = 18, color = new Color(0.4f, 0.7f, 1f, 0.55f) };
                menuHome.Add(t);
            }
            var line = new VisualElement();
            line.style.height = 2; line.style.width = 140;
            line.style.backgroundColor = UiPalette.Accent;
            line.style.marginTop = 18; line.style.marginBottom = 14;
            menuHome.Add(line);
            var tag = new Label("From one lander to a civilisation that harnesses its star.\nReal exoplanets · NASA data · real physics.");
            tag.style.fontSize = 17; tag.style.whiteSpace = WhiteSpace.Normal;
            tag.style.color = new Color(0.82f, 0.86f, 0.92f);
            tag.style.marginBottom = 34;
            menuHome.Add(tag);

            menuButtons = new VisualElement();
            menuButtons.style.width = 320;
            menuHome.Add(menuButtons);
            menuButtons.Add(SpaceButton("New mission", () => SetPhase(GamePhase.ChoosingPlanet), primary: true));
            continueButton = SpaceButton("Continue", () =>
            {
                saveGame?.Load();
                if (Phase == GamePhase.MainMenu) menuMessage.text = saveGame != null ? saveGame.Message : "";
            });
            menuButtons.Add(continueButton);
            menuButtons.Add(SpaceButton("Guide book", () => ShowMenuGuide(true)));
            menuButtons.Add(SpaceButton("Settings", () => ShowSettings(true)));
            menuButtons.Add(SpaceButton("Credits", () => ShowCredits(true)));
            menuButtons.Add(SpaceButton("Quit", Quit));
            menuMessage = new Label("");
            menuMessage.style.fontSize = 13; menuMessage.style.color = new Color(0.65f, 0.7f, 0.78f);
            menuMessage.style.marginTop = 8;
            menuButtons.Add(menuMessage);

            var footer = new Label("NASA Space Apps Challenge 2026  ·  Space Mission Design Game");
            footer.style.position = Position.Absolute;
            footer.style.left = new Length(7, LengthUnit.Percent);
            footer.style.bottom = 28;
            footer.style.fontSize = 13; footer.style.letterSpacing = 2;
            footer.style.color = new Color(0.6f, 0.65f, 0.75f, 0.9f);
            mainMenu.Add(footer);
        }

        /// <summary>A menu button: text on space, an amber bar and glow on hover; the main choice is solid amber.</summary>
        static Button SpaceButton(string text, System.Action onClick, bool primary = false)
        {
            var b = new Button(() => { KardashevAscent.Presentation.Audio.GameAudio.Play(KardashevAscent.Presentation.Audio.Sfx.Click); onClick(); }) { text = text };
            b.style.height = 46;
            b.style.marginTop = 5; b.style.marginBottom = 5;
            b.style.fontSize = 19;
            b.style.letterSpacing = 3;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingLeft = 22;
            b.style.borderTopWidth = b.style.borderRightWidth = b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 3;
            Color idleBg = primary ? UiPalette.Accent : new Color(1f, 1f, 1f, 0.04f);
            Color idleText = primary ? UiPalette.OnAccent : new Color(0.9f, 0.93f, 0.97f);
            Color idleEdge = primary ? UiPalette.Accent : new Color(1f, 1f, 1f, 0.15f);
            void Idle()
            {
                b.style.backgroundColor = idleBg; b.style.color = idleText; b.style.borderLeftColor = idleEdge;
                b.style.translate = new Translate(0, 0);
            }
            Idle();
            b.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(0.15f, TimeUnit.Second) };
            b.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (!b.enabledSelf) return;
                b.style.backgroundColor = primary ? new Color(1f, 0.85f, 0.5f) : new Color(1f, 0.78f, 0.36f, 0.16f);
                b.style.borderLeftColor = UiPalette.Accent;
                b.style.color = primary ? UiPalette.OnAccent : Color.white;
                b.style.translate = new Translate(6, 0);
            });
            b.RegisterCallback<PointerLeaveEvent>(_ => Idle());
            return b;
        }

        static void FadeIn(VisualElement e)
        {
            if (e == null) return;
            e.style.opacity = 0f;
            e.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(1.2f, TimeUnit.Second) };
            e.schedule.Execute(() => e.style.opacity = 1f).StartingIn(60);
        }

        /// <summary>A screen over the menus: the space backdrop, a title and a scrolling panel, and Back.</summary>
        VisualElement Overlay(string title, out VisualElement body, float width)
        {
            VisualElement s = Screen(dim: 0f);
            s.Add(new SpaceBackdrop());
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.style.width = width;
            panel.style.maxHeight = new Length(82, LengthUnit.Percent);
            panel.style.paddingLeft = panel.style.paddingRight = 26;
            panel.style.paddingTop = panel.style.paddingBottom = 20;
            panel.style.backgroundColor = new Color(0.06f, 0.07f, 0.1f, 0.92f);
            var t = new Label(title.ToUpperInvariant());
            t.style.fontSize = 28; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.letterSpacing = 8;
            t.style.marginBottom = 10;
            panel.Add(t);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.flexShrink = 1;
            body = scroll.contentContainer;
            panel.Add(scroll);
            var back = MenuButton("Back (Esc)", () => { Show(s, false); });
            back.style.marginTop = 12;
            panel.Add(back);
            s.Add(panel);
            Show(s, false);
            return s;
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
        static readonly string[] TeamMembers = { };

        void ShowSettings(bool show) { Show(settingsScreen, show); if (show) FadeIn(settingsScreen); }

        void ShowCredits(bool show) { Show(creditsScreen, show); if (show) FadeIn(creditsScreen); }

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

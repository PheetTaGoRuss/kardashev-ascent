using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.Audio
{
    /// <summary>
    /// The game's sound (director 2026-11-14): effects for clicks, building, puzzles and alerts, and a quiet space
    /// ambience. Effects come from Kenney's CC0 packs where one fits (Resources/Audio), the rest (ambience, alarm,
    /// milestone) are made by <see cref="SoundSynth"/>. Starts itself after the scene loads (no scene edit), listens to
    /// the simulation's events and to every button press, and keeps the volumes in PlayerPrefs.
    /// <para>Call <see cref="Play"/> from anywhere: <c>GameAudio.Play(Sfx.Correct)</c>.</para>
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        const string SfxKey = "kardashev.audio.sfx", MusicKey = "kardashev.audio.music", MuteKey = "kardashev.audio.mute";
        public const float DefaultSfx = 0.7f, DefaultMusic = 0.35f;

        static GameAudio instance;
        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        AudioSource sfxSource, musicSource;
        SimulationRunner runner;
        IPlanetSimulation listening;
        readonly HashSet<IPanel> hookedPanels = new HashSet<IPanel>();
        float nextPanelScan;
        readonly Dictionary<Sfx, float> lastPlayed = new Dictionary<Sfx, float>();

        public static float SfxVolume { get; private set; } = DefaultSfx;
        public static float MusicVolume { get; private set; } = DefaultMusic;
        public static bool Muted { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            // Only in the game's scene: an empty scene playing sound looks like the game with no planet.
            if (FindAnyObjectByType<SimulationRunner>() == null) return;
            var go = new GameObject("Game Audio");
            DontDestroyOnLoad(go);
            go.AddComponent<GameAudio>();
        }

        /// <summary>Play a sound effect (quietly ignored before the audio starts, and not twice within a few frames).</summary>
        public static void Play(Sfx s, float volume = 1f)
        {
            if (instance == null || Muted) return;
            instance.PlayOne(s, volume);
        }

        public static void SetSfxVolume(float v) { SfxVolume = Mathf.Clamp01(v); Save(); }
        public static void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); Save(); instance?.ApplyMusic(); }
        public static void SetMuted(bool m) { Muted = m; Save(); instance?.ApplyMusic(); }

        static void Save()
        {
            try
            {
                PlayerPrefs.SetFloat(SfxKey, SfxVolume);
                PlayerPrefs.SetFloat(MusicKey, MusicVolume);
                PlayerPrefs.SetInt(MuteKey, Muted ? 1 : 0);
            }
            catch { }
        }

        void Awake()
        {
            instance = this;
            try
            {
                SfxVolume = PlayerPrefs.GetFloat(SfxKey, DefaultSfx);
                MusicVolume = PlayerPrefs.GetFloat(MusicKey, DefaultMusic);
                Muted = PlayerPrefs.GetInt(MuteKey, 0) == 1;
            }
            catch { }
            if (FindAnyObjectByType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            // Kenney's CC0 sounds where we have one (Resources/Audio/<name>.ogg; see docs/credits.md), else made in code.
            foreach (Sfx s in (Sfx[])System.Enum.GetValues(typeof(Sfx)))
            {
                AudioClip recorded = Resources.Load<AudioClip>("Audio/" + s);
                clips[s] = recorded != null ? recorded : Clip(s.ToString(), SoundSynth.Make(s));
            }
            musicSource.clip = Clip("Ambience", SoundSynth.Ambience());
            ApplyMusic();
        }

        static AudioClip Clip(string name, float[] samples)
        {
            var c = AudioClip.Create(name, Mathf.Max(1, samples.Length), 1, SoundSynth.Rate, false);
            c.SetData(samples, 0);
            return c;
        }

        void ApplyMusic()
        {
            if (musicSource != null)
                musicSource.volume = Muted ? 0f : MusicVolume;
        }

        void PlayOne(Sfx s, float volume)
        {
            if (!clips.TryGetValue(s, out AudioClip c)) return;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(s, out float t) && now - t < 0.05f) return;   // ten panels finishing at once: one sound
            lastPlayed[s] = now;
            sfxSource.PlayOneShot(c, SfxVolume * volume);
        }

        void OnEnable()
        {
            runner = FindAnyObjectByType<SimulationRunner>();
            if (runner != null)
                runner.SimulationCreated += Listen;
            Listen();
        }

        void OnDisable()
        {
            if (runner != null)
                runner.SimulationCreated -= Listen;
            Unlisten();
        }

        // ---- the simulation's events ----

        void Listen()
        {
            Unlisten();
            listening = runner != null ? runner.Simulation : null;
            if (listening == null) return;
            listening.BuildingCompleted += OnBuilt;
            listening.BrownoutChanged += OnBrownout;
            listening.MilestoneReached += OnMilestone;
            listening.ParameterMeasured += OnMeasured;
            listening.PuzzleReady += OnPuzzle;
            if (listening is PlanetSimulation p)
                p.ExperimentDone += OnExperiment;
        }

        void Unlisten()
        {
            if (listening == null) return;
            listening.BuildingCompleted -= OnBuilt;
            listening.BrownoutChanged -= OnBrownout;
            listening.MilestoneReached -= OnMilestone;
            listening.ParameterMeasured -= OnMeasured;
            listening.PuzzleReady -= OnPuzzle;
            if (listening is PlanetSimulation p)
                p.ExperimentDone -= OnExperiment;
            listening = null;
        }

        void OnBuilt(int tile, int slot) => Play(Sfx.BuildComplete, 0.8f);
        void OnBrownout(bool on) { if (on) Play(Sfx.Alert); }
        void OnMilestone(int n) => Play(Sfx.Milestone);
        void OnMeasured(KardashevAscent.Simulation.Solar.SolarParameter p) => Play(Sfx.Discovery);
        void OnPuzzle(KardashevAscent.Simulation.Solar.SolarParameter p) => Play(Sfx.Beep);
        void OnExperiment(KardashevAscent.Simulation.Planet.ExperimentKind k) => Play(Sfx.Discovery);

        // ---- every button press clicks ----

        void Update()
        {
            // The ambience starts once the audio system runs, and comes back if anything stopped it.
            if (musicSource != null && musicSource.clip != null && !musicSource.isPlaying)
                musicSource.Play();
            if (Time.unscaledTime < nextPanelScan) return;
            nextPanelScan = Time.unscaledTime + 1f;
            foreach (UIDocument d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                IPanel panel = d.rootVisualElement?.panel;
                if (panel == null || !hookedPanels.Add(panel)) continue;
                panel.visualTree.RegisterCallback<ClickEvent>(OnUiClick, TrickleDown.TrickleDown);
            }
        }

        static void OnUiClick(ClickEvent e)
        {
            if (e.target is VisualElement v && (v is Button || v.GetFirstAncestorOfType<Button>() != null))
                Play(Sfx.Click);
        }
    }
}

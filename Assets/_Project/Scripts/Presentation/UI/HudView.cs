using System;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The top bar (#12, with the K meter of #10): date, local time and season; power made and needed;
    /// battery level; Kardashev K against this planet's Type I target; time-speed buttons; and a brownout alert.
    /// Built with UI Toolkit (UI/Hud/Hud.uxml + Hud.uss on the shared KardashevTheme); the game's other panels
    /// move to UI Toolkit too.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] SaveGameController saveGame;
        [Tooltip("Seconds between text updates (the bars move every frame).")]
        [Range(0f, 1f)] [SerializeField] float textInterval = 0.1f;

        UIDocument document;
        Label date, time, season, power, demand, battery, kLabel, kPlanetLabel, brownout, toast, buildStock, buildQueue;
        VisualElement batteryFill, kFill;
        readonly Button[] speedButtons = new Button[5];
        static readonly ClockSpeed[] Speeds =
            { ClockSpeed.Paused, ClockSpeed.RealTime, ClockSpeed.HourPerSecond, ClockSpeed.DayPerSecond, ClockSpeed.YearPerMinute };
        float nextText;

        void OnEnable()
        {
            document = GetComponent<UIDocument>();
            VisualElement root = document.rootVisualElement;
            if (root == null)
                return;
            // Only the bar itself catches the mouse; the rest of the screen belongs to the planet.
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);

            date = root.Q<Label>("date");
            time = root.Q<Label>("time");
            season = root.Q<Label>("season");
            power = root.Q<Label>("power");
            demand = root.Q<Label>("demand");
            battery = root.Q<Label>("battery");
            kLabel = root.Q<Label>("k");
            kPlanetLabel = root.Q<Label>("k-planet");
            brownout = root.Q<Label>("brownout");
            toast = root.Q<Label>("toast");
            buildStock = root.Q<Label>("build-stock");
            buildQueue = root.Q<Label>("build-queue");
            batteryFill = root.Q("battery-fill");
            kFill = root.Q("k-fill");

            string[] names = { "speed-paused", "speed-real", "speed-hour", "speed-day", "speed-year" };
            for (int i = 0; i < names.Length; i++)
            {
                ClockSpeed speed = Speeds[i];
                speedButtons[i] = root.Q<Button>(names[i]);
                if (speedButtons[i] != null)
                    speedButtons[i].clicked += () => runner?.SetSpeed(speed);
            }
            nextText = 0f;
            if (saveGame != null)
                saveGame.Loaded += OnLoaded;
        }

        void OnDisable()
        {
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
            if (saveGame != null)
                saveGame.Loaded -= OnLoaded;
        }

        /// <summary>A loaded save brings its own milestones: not news.</summary>
        void OnLoaded()
        {
            watched = runner != null ? runner.Simulation : null;
            milestonesSeen = watched != null ? watched.MilestonesReached : 0;
            milestoneMessage = null;
        }

        // Milestone news: shown longer than save messages, and the newest wins. The count is compared every frame
        // (not an event), so nothing is missed when a new simulation starts; a new or loaded colony sets the baseline.
        IPlanetSimulation watched;
        int milestonesSeen;
        string milestoneMessage;
        float milestoneTime = float.NegativeInfinity;
        const float MilestoneSeconds = 6f;

        void CheckMilestones(IPlanetSimulation sim)
        {
            if (sim != watched)
            {
                watched = sim;
                milestonesSeen = sim != null ? sim.MilestonesReached : 0;
                milestoneMessage = null;
                return;
            }
            if (sim == null || sim.MilestonesReached <= milestonesSeen)
            {
                if (sim != null) milestonesSeen = sim.MilestonesReached;   // a loaded save may lower it
                return;
            }
            int milestonesBefore = milestonesSeen;
            int n = milestonesSeen = sim.MilestonesReached;
            double p = Kardashev.PowerForMilestone(n, sim.TypeITargetW);
            milestoneMessage = n >= 10
                ? $"Type I for {runner.Record?.displayName ?? "this planet"}: {Format.Power(sim.TypeITargetW)} through a whole day (planet K 1)."
                : $"Milestone {n} of 10: planet K {n * Kardashev.MilestoneStep:0.0}, {Format.Power(p)} through a whole day. Next: ten times more.";
            // Any tier unlocked by the milestones since the last message (several can come in one day).
            for (int m = milestonesBefore + 1; m <= n; m++)
            {
                int unlocked = PanelTiers.UnlockedAt(m);
                if (unlocked > 0)
                    milestoneMessage += $"\nUnlocked: {PanelTiers.All[unlocked].Name.ToLowerInvariant()}s, " +
                                        $"{PanelTiers.All[unlocked].Scale:#,0}× the ground per slot. Rebuild old panels to grow them.";
            }
            milestoneTime = Time.unscaledTime;
        }

        void Update()
        {
            CheckMilestones(runner != null ? runner.Simulation : null);
            if (toast != null)
            {
                bool milestone = !string.IsNullOrEmpty(milestoneMessage) && Time.unscaledTime - milestoneTime < MilestoneSeconds;
                bool save = saveGame != null && !string.IsNullOrEmpty(saveGame.Message)
                    && Time.unscaledTime - saveGame.MessageTime < 2.5f
                    && (!milestone || saveGame.MessageTime > milestoneTime);
                toast.EnableInClassList("hidden", !milestone && !save);
                if (save)
                    toast.text = saveGame.Message;
                else if (milestone)
                    toast.text = milestoneMessage;
            }
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim == null || date == null)
                return;

            double soc = sim.BatteryCapacityWh > 0.0 ? sim.BatteryStoredWh / sim.BatteryCapacityWh : 0.0;
            batteryFill.style.width = Length.Percent((float)(soc * 100.0));
            batteryFill.EnableInClassList("warning", soc < 0.4 && soc >= 0.15);
            batteryFill.EnableInClassList("critical", soc < 0.15);

            // The bar is planet K on 0..1 (log scale: each tenth is ten times the power), not P / P_I, which stays
            // near zero for most of the game.
            double planetK = Math.Max(0.0, Math.Min(1.0, sim.PlanetK));
            kFill.style.width = Length.Percent((float)(planetK * 100.0));

            for (int i = 0; i < speedButtons.Length; i++)
                speedButtons[i]?.EnableInClassList("selected", runner.Clock != null && runner.Clock.Speed == Speeds[i]);

            if (Time.unscaledTime < nextText)
                return;
            nextText = Time.unscaledTime + textInterval;

            double t = sim.TimeHours;
            date.text = $"Year {runner.Clock.YearIndex(t) + 1} · Day {runner.Clock.DayOfYear(t)}";
            double local = runner.LocalTimeAt(sim.BaseTileId);
            time.text = double.IsNaN(local)
                ? "Star fixed in the sky"
                : $"{(int)local:00}:{(int)(local % 1.0 * 60.0):00} at base · {(sim.IsDayAtBase ? "day" : "night")}";
            string planetName = runner.Record != null && !string.IsNullOrEmpty(runner.Record.displayName) ? runner.Record.displayName + " · " : "";
            if (runner.SolarParams.Obliquity < 0.5 * Math.PI / 180.0)
                season.text = planetName + "no seasons";
            else
            {
                Season s = SimClock.SeasonFromEclipticLongitude(SimClock.EclipticLongitude(runner.SolarParams, t));
                season.text = $"{planetName}northern {s.ToString().ToLowerInvariant()}";
            }

            power.text = Format.Power(sim.GenerationW);
            demand.text = sim.IsColonyGrowing
                ? $"needed {Format.Power(sim.DemandW)} ▲"
                : $"needed {Format.Power(sim.DemandW)}";
            string flow = sim.BatteryPowerW > 1.0 ? $"+{Format.Power(sim.BatteryPowerW)}"
                : sim.BatteryPowerW < -1.0 ? $"−{Format.Power(-sim.BatteryPowerW)}"
                : sim.CurtailedW > 1.0 || sim.BuildPowerW > 1.0 ? "full" : "idle";
            battery.text = $"{soc * 100.0:0} % of {Format.Energy(sim.BatteryCapacityWh)} · {flow}";

            if (buildStock != null)
            {
                string making = sim.BuildPowerW > 1.0 ? $"+{Format.Power(sim.BuildPowerW)}" : "no spare power";
                buildStock.text = Format.Energy(sim.BuildStockWh);
                buildQueue.text = sim.ConstructionQueueCount == 0
                    ? making
                    : $"{making} · {sim.ConstructionQueueCount} site{(sim.ConstructionQueueCount == 1 ? "" : "s")}, " +
                      $"{Format.Energy(sim.ConstructionRemainingWh)} to go";
            }
            kLabel.text = $"K {sim.KardashevK:0.00} · {Format.Power(sim.KardashevPowerW)}";
            if (kPlanetLabel != null)
                kPlanetLabel.text = $"Planet K {sim.PlanetK:0.00} ({Format.Percent(Kardashev.PlanetCompletion(sim.KardashevPowerW, sim.TypeITargetW))})";

            // The grid updates its brownout state on the next tick; while paused, don't warn once panels cover demand.
            bool brownoutNow = sim.IsBrownout && sim.GenerationW < sim.DemandW;
            brownout.EnableInClassList("hidden", !brownoutNow);
            if (brownoutNow)
            {
                double shortBy = Math.Max(0.0, sim.DemandW - sim.GenerationW);
                brownout.text = $"Brownout: the base is {Format.Power(shortBy)} short and the battery is empty. Build more panels.";
            }
        }
    }

    /// <summary>Number formats shared by the UI.</summary>
    public static class Format
    {
        public static string Power(double w)
        {
            double a = Math.Abs(w);
            if (a >= 1e15) return $"{w / 1e15:0.00} PW";
            if (a >= 1e12) return $"{w / 1e12:0.00} TW";
            if (a >= 1e9) return $"{w / 1e9:0.00} GW";
            if (a >= 1e6) return $"{w / 1e6:0.00} MW";
            if (a >= 1e3) return $"{w / 1e3:0} kW";
            return $"{w:0} W";
        }

        /// <summary>
        /// A share as a percentage. Below 0.01 % it switches to powers of ten (2.1×10⁻⁷ %), because K progress
        /// spans many orders of magnitude and "0.00 %" would hide that the colony is growing.
        /// </summary>
        public static string Percent(double share)
        {
            double p = share * 100.0;
            if (p <= 0.0) return "0 %";
            if (p >= 10.0) return $"{p:0} %";
            if (p >= 0.01) return $"{p:0.##} %";
            int exponent = (int)Math.Floor(Math.Log10(p));
            double mantissa = p / Math.Pow(10.0, exponent);
            if (mantissa >= 9.95) { mantissa = 1.0; exponent++; }
            return $"{mantissa:0.0}×10{Superscript(exponent)} %";
        }

        static string Superscript(int n)
        {
            const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
            var s = new System.Text.StringBuilder();
            if (n < 0) s.Append('⁻');
            foreach (char c in Math.Abs(n).ToString())
                s.Append(digits[c - '0']);
            return s.ToString();
        }

        public static string Energy(double wh)
        {
            double a = Math.Abs(wh);
            if (a >= 1e9) return $"{wh / 1e9:0.00} GWh";
            if (a >= 1e7) return $"{wh / 1e6:0} MWh";
            if (a >= 1e6) return $"{wh / 1e6:0.0} MWh";
            return $"{wh / 1e3:0} kWh";
        }
    }
}

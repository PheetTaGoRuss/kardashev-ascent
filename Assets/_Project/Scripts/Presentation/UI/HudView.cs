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
        Label date, time, season, power, demand, battery, kLabel, kPlanetLabel, kDetailLabel, kHeading, brownout, toast, buildStock, buildQueue;
        VisualElement stockChips;
        string stockShape;
        static readonly KardashevAscent.Simulation.Resources.Material[] StockShown =
        {
            KardashevAscent.Simulation.Resources.Material.Iron, KardashevAscent.Simulation.Resources.Material.Copper,
            KardashevAscent.Simulation.Resources.Material.Lithium, KardashevAscent.Simulation.Resources.Material.Rock,
        };
        VisualElement batteryFill, kFill;
        // The fastest speed is a day per second (director, 2026-11-14): no year per minute.
        readonly Button[] speedButtons = new Button[4];
        static readonly ClockSpeed[] Speeds =
            { ClockSpeed.Paused, ClockSpeed.RealTime, ClockSpeed.HourPerSecond, ClockSpeed.DayPerSecond };
        float nextText;

        static string Mass(double kg) => kg >= 1000.0 ? $"{kg / 1000.0:0.#}t" : $"{kg:0}kg";

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
            kDetailLabel = root.Q<Label>("k-detail");
            kHeading = root.Q<Label>("k-heading");
            brownout = root.Q<Label>("brownout");
            toast = root.Q<Label>("toast");
            buildStock = root.Q<Label>("build-stock");
            buildQueue = root.Q<Label>("build-queue");
            // Materials as icons (director 2026-11-15): Fe Cu Li Rk chips, red when there is none.
            stockChips = new VisualElement();
            if (buildStock != null)
            {
                buildStock.parent.Insert(buildStock.parent.IndexOf(buildStock), stockChips);
                buildStock.style.display = DisplayStyle.None;
            }
            batteryFill = root.Q("battery-fill");
            kFill = root.Q("k-fill");

            string[] names = { "speed-paused", "speed-real", "speed-hour", "speed-day" };
            for (int i = 0; i < names.Length; i++)
            {
                ClockSpeed speed = Speeds[i];
                speedButtons[i] = root.Q<Button>(names[i]);
                if (speedButtons[i] != null)
                    speedButtons[i].clicked += () => runner?.SetSpeed(speed);
            }
            // The calculator opens any time (director 2026-11-14): K or this button by the speeds.
            Button dayButton = speedButtons[speedButtons.Length - 1];
            if (dayButton != null && dayButton.parent != null && dayButton.parent.Q<Button>("calculator") == null)
            {
                var calc = new Button(() => CalculatorPad.Toggle(root)) { name = "calculator", text = "Calc (K)" };
                calc.AddToClassList("speed-button");
                calc.style.marginLeft = 10;
                dayButton.parent.Add(calc);
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
        VisualElement toastStack;
        Label toastMilestone, toastSave, toastWeather;
        float lastLoggedSave = -1f;
        int weatherSerial;
        string weatherMessage;
        float weatherTime;
        const float WeatherToastSeconds = 6f;
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
            sim.NoteEvent("Colony", "Milestone reached", milestoneMessage.Replace("\n", " "));
        }

        /// <summary>A toast line in the shared stack: the HUD toast style, one line each.</summary>
        static Label NewToast()
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("toast");
            l.AddToClassList("hidden");
            l.style.marginTop = 4f;
            return l;
        }

        void Update()
        {
            if (ScreenInput.KeyDown(KeyCode.K) && document != null && document.rootVisualElement != null)
                CalculatorPad.Toggle(document.rootVisualElement);
            CheckMilestones(runner != null ? runner.Simulation : null);
            if (runner != null && runner.Simulation != null && runner.Simulation.RobotWeatherNewsSerial != weatherSerial)
            {
                weatherSerial = runner.Simulation.RobotWeatherNewsSerial;
                weatherMessage = runner.Simulation.RobotWeatherNews;
                weatherTime = Time.unscaledTime;
            }
            // Saves and milestones go to the field log (Operations, I), not to a toast: a toast is for "act now" only (UI information design, stage 7).
            if (runner != null && runner.Simulation != null && saveGame != null && !string.IsNullOrEmpty(saveGame.Message) && saveGame.MessageTime != lastLoggedSave)
            {
                lastLoggedSave = saveGame.MessageTime;
                runner.Simulation.NoteEvent("Colony", "Game saved", saveGame.Message);
            }
            if (toast != null)
            {
                // One stack of toasts below the system tab bar: the milestone, the save and the weather each have a line and never cover each other.
                if (toastStack == null)
                {
                    toast.style.display = DisplayStyle.None;   // the old single toast sat under the tab bar
                    toastStack = new VisualElement { pickingMode = PickingMode.Ignore };
                    toastStack.style.position = Position.Absolute;
                    toastStack.style.left = 0; toastStack.style.right = 0;
                    toastStack.style.alignItems = Align.Center;
                    foreach (Label l in new[] { toastMilestone = NewToast(), toastSave = NewToast(), toastWeather = NewToast() }) toastStack.Add(l);
                    document.rootVisualElement.Add(toastStack);
                }
                toastStack.style.top = UiLayout.WindowTop + 2f;
                bool milestone = !string.IsNullOrEmpty(milestoneMessage) && Time.unscaledTime - milestoneTime < MilestoneSeconds;
                bool save = saveGame != null && !string.IsNullOrEmpty(saveGame.Message) && Time.unscaledTime - saveGame.MessageTime < 2.5f;
                bool weather = !string.IsNullOrEmpty(weatherMessage) && Time.unscaledTime - weatherTime < WeatherToastSeconds;
                toastMilestone.EnableInClassList("hidden", true);   // the milestone is in the field log; only warnings toast
                toastSave.EnableInClassList("hidden", true);
                toastWeather.EnableInClassList("hidden", !weather);
                if (weather) toastWeather.text = weatherMessage;
            }
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim == null || date == null)
                return;

            double soc = sim.BatteryCapacityWh > 0.0 ? sim.BatteryStoredWh / sim.BatteryCapacityWh : 0.0;
            batteryFill.style.width = Length.Percent((float)(soc * 100.0));
            batteryFill.EnableInClassList("warning", soc < 0.4 && soc >= 0.15);
            batteryFill.EnableInClassList("critical", soc < 0.15);

            // The bar fills toward the next milestone (ten times the power of the last), on a log scale.
            int reached = sim.MilestonesReached;
            double dayPowerW = sim.DayAveragePowerW > 0.0 ? sim.DayAveragePowerW : sim.KardashevPowerW;
            double lastW = Kardashev.PowerForMilestone(reached, sim.TypeITargetW);
            double progress = reached >= 10 ? 1.0 : Math.Max(0.0, Math.Min(1.0, Math.Log10(Math.Max(dayPowerW, 1.0) / lastW)));
            kFill.style.width = Length.Percent((float)(progress * 100.0));

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
                : sim.CurtailedW > 1.0 ? "full" : "idle";
            battery.text = $"{soc * 100.0:0} % of {Format.Energy(sim.BatteryCapacityWh)} · {flow}";

            if (buildStock != null)
            {
                var m = sim.Materials;
                // Every material the colony holds (director 2026-11-20: "all resources I have"); the four basics always show, red when short.
                // the four basics, then the other materials held, at most eight chips so the top bar stays two rows (the rest on hover)
                var shown = new System.Collections.Generic.List<KardashevAscent.Simulation.Resources.Material>(StockShown);
                var more = new System.Collections.Generic.List<KardashevAscent.Simulation.Resources.Material>();
                string shape = "";
                foreach (var material in KardashevAscent.Simulation.Resources.Materials.All)
                {
                    if (System.Array.IndexOf(StockShown, material) < 0 && m[material] > 0.0)
                    {
                        if (shown.Count < 8) shown.Add(material); else more.Add(material);
                    }
                    shape += Mass(m[material]) + "|";
                }
                if (shape != stockShape)
                {
                    stockShape = shape;
                    MaterialChips.FillStock(stockChips, m, shown.ToArray(), 14f);
                    if (more.Count > 0)
                    {
                        string rest = "";
                        foreach (var material in more) rest += (rest.Length > 0 ? " · " : "") + $"{KardashevAscent.Simulation.Resources.Materials.Name(material)} {MaterialChips.Kg(m[material])}";
                        var overflow = new Label($"+{more.Count}");
                        overflow.style.color = UiPalette.Muted;
                        overflow.AddToClassList(UiType.Secondary);
                        overflow.style.marginLeft = 4;
                        overflow.tooltip = rest;
                        stockChips.Add(overflow);
                    }
                    stockChips.style.flexWrap = Wrap.Wrap;   // eight chips make two rows of four
                    stockChips.style.maxWidth = 340;
                }
                int sites = sim.ConstructionQueueCount;
                bool shortOf = !sim.ConstructionShortfall.IsEmpty;
                buildQueue.text = sites == 0 ? "no sites" : shortOf ? $"{sites} site{(sites == 1 ? "" : "s")} short" : $"{sites} site{(sites == 1 ? "" : "s")}";
                buildQueue.style.color = sites > 0 && shortOf ? UiPalette.Critical : StyleKeyword.Null;
            }
            kLabel.text = Format.Power(sim.KardashevPowerW);
            if (kHeading != null)
                kHeading.text = $"KARDASHEV · MILESTONE {sim.MilestonesReached} OF 10";
            if (kPlanetLabel != null)
                kPlanetLabel.text = sim.MilestonesReached >= 10
                    ? "Type I reached"
                    : $"Next: {Format.Power(Kardashev.PowerForMilestone(sim.MilestonesReached + 1, sim.TypeITargetW))} through a whole day";
            kLabel.tooltip = $"K {sim.KardashevK:0.00} · planet K {sim.PlanetK:0.00} ({Format.Percent(Kardashev.PlanetCompletion(sim.KardashevPowerW, sim.TypeITargetW))})";

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

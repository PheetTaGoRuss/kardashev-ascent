using System;
using KardashevAscent.Presentation.Game;
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
        [Tooltip("Seconds between text updates (the bars move every frame).")]
        [Range(0f, 1f)] [SerializeField] float textInterval = 0.1f;

        UIDocument document;
        Label date, time, season, power, demand, battery, kLabel, brownout;
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
            brownout = root.Q<Label>("brownout");
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
        }

        void OnDisable()
        {
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        void Update()
        {
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim == null || date == null)
                return;

            double soc = sim.BatteryCapacityWh > 0.0 ? sim.BatteryStoredWh / sim.BatteryCapacityWh : 0.0;
            batteryFill.style.width = Length.Percent((float)(soc * 100.0));
            batteryFill.EnableInClassList("warning", soc < 0.4 && soc >= 0.15);
            batteryFill.EnableInClassList("critical", soc < 0.15);

            double targetK = Kardashev(sim.TypeITargetW);
            double progress = targetK > 0.0 ? Math.Max(0.0, Math.Min(1.0, sim.KardashevK / targetK)) : 0.0;
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
            Season s = SimClock.SeasonFromEclipticLongitude(SimClock.EclipticLongitude(runner.SolarParams, t));
            season.text = $"Northern {s.ToString().ToLowerInvariant()}";

            power.text = Format.Power(sim.GenerationW);
            demand.text = $"needed {Format.Power(sim.DemandW)}";
            string flow = sim.BatteryPowerW > 1.0 ? $"charging {Format.Power(sim.BatteryPowerW)}"
                : sim.BatteryPowerW < -1.0 ? $"draining {Format.Power(-sim.BatteryPowerW)}"
                : sim.CurtailedW > 1.0 ? $"full, {Format.Power(sim.CurtailedW)} wasted" : "idle";
            battery.text = $"{soc * 100.0:0} % · {flow}";
            kLabel.text = $"K {sim.KardashevK:0.00} · Type I at K {targetK:0.00} ({progress * 100.0:0} %)";

            brownout.EnableInClassList("hidden", !sim.IsBrownout);
            if (sim.IsBrownout)
            {
                double shortBy = Math.Max(0.0, sim.DemandW - sim.GenerationW);
                brownout.text = $"Brownout: the base is {Format.Power(shortBy)} short and the battery is empty. Build more panels.";
            }
        }

        /// <summary>K = (log₁₀ P − 6) / 10.</summary>
        static double Kardashev(double watts) => (Math.Log10(Math.Max(watts, 1.0)) - 6.0) / 10.0;
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

        public static string Energy(double wh)
        {
            double a = Math.Abs(wh);
            if (a >= 1e9) return $"{wh / 1e9:0.00} GWh";
            if (a >= 1e6) return $"{wh / 1e6:0.00} MWh";
            return $"{wh / 1e3:0} kWh";
        }
    }
}

using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// First steps for a new player: one hint at a time, each done by doing it (open a site, build, speed up time,
    /// open the colony overview, reach milestone 1). Waits while the planet chooser is open; Skip hides it.
    /// UI Toolkit: UI/Hints/Hints.uxml + .uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HintsView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] TilePicker picker;
        [SerializeField] PanelPlacer placer;
        [SerializeField] ColonyView colony;
        [SerializeField] PlanetChooserView chooser;

        VisualElement panelRoot;
        Label stepLabel, textLabel;
        int step;
        bool skipped, colonyOpened;
        float nextCheck;

        const int Steps = 5;

        /// <summary>The hint being shown (0–4), or 5 when they are all done.</summary>
        public int Step => step;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);
            panelRoot = root.Q("hints");
            stepLabel = root.Q<Label>("step");
            textLabel = root.Q<Label>("text");
            Button skip = root.Q<Button>("skip");
            if (skip != null)
                skip.clicked += () => skipped = true;
        }

        void OnDisable()
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        void Update()
        {
            if (panelRoot == null)
                return;
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (colony != null && colony.Visible)
                colonyOpened = true;
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + 0.25f;
                while (sim != null && step < Steps && Done(step, sim))
                    step++;
            }

            bool show = !skipped && sim != null && step < Steps && (chooser == null || !chooser.Visible);
            panelRoot.EnableInClassList("hidden", !show);
            if (!show)
                return;
            stepLabel.text = $"FIRST STEPS {step + 1} / {Steps}";
            textLabel.text = Text(step, sim);
        }

        bool Done(int i, IPlanetSimulation sim)
        {
            switch (i)
            {
                case 0: return picker != null && picker.SelectedTile >= 0;
                case 1: return AnyPanel(sim);
                case 2: return runner.Clock != null && runner.Clock.Speed >= ClockSpeed.DayPerSecond;
                case 3: return colonyOpened;
                case 4: return sim.MilestonesReached >= 1;
                default: return true;
            }
        }

        static bool AnyPanel(IPlanetSimulation sim)
        {
            foreach (BuildingRecord r in sim.CaptureState().Buildings)
                if (r.Type == BuildingType.SolarPanel)
                    return true;
            return false;
        }

        string Text(int i, IPlanetSimulation sim)
        {
            switch (i)
            {
                case 0:
                    return "Click a land tile near your base to open its building site. M shows where sunlight is best.";
                case 1:
                    string key = placer != null ? placer.PlaceKey.ToString() : "P";
                    return $"Click a slot, then press {key} to build a solar panel. B sets the best angle; Tab switches to battery yards for the night.";
                case 2:
                    return "Press 4 to run a day per second. Watch POWER at night: the battery has to carry the colony until sunrise.";
                case 3:
                    return $"Press {(colony != null ? colony.ToggleKey.ToString() : "C")} for the colony overview: a week of power, your K and the next milestone.";
                case 4:
                    double next = Kardashev.PowerForMilestone(1, sim.TypeITargetW);
                    return $"Reach milestone 1: {Format.Power(next)} used through a whole day. Spare power makes build parts, so every panel pays for more.";
                default:
                    return "";
            }
        }
    }
}

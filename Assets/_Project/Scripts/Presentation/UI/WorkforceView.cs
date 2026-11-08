using System;
using System.Collections.Generic;
using System.Text;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Robots;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The workforce overview (G): groups, their state and task priorities, and each robot's task (spec §5).
    /// Pause/Resume per group; click a priority to step it (Critical → … → Disabled). Each robot shows its charge,
    /// battery health and condition. UI Toolkit: UI/Workforce/Workforce.uxml + .uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class WorkforceView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] KeyCode toggleKey = KeyCode.G;
        [Tooltip("Seconds between refreshes while open.")]
        [Range(0.1f, 2f)] [SerializeField] float refreshInterval = 0.5f;

        VisualElement panelRoot, groupsHolder;
        Label subtitle, summary, notes;
        float nextRefresh;
        string shownShape;
        readonly Dictionary<int, Label> taskLabels = new Dictionary<int, Label>();
        readonly Dictionary<int, Label> vitalLabels = new Dictionary<int, Label>();

        public bool Visible { get; private set; }
        public KeyCode ToggleKey => toggleKey;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            ScreenInput.Register(root.panel);
            panelRoot = root.Q("workforce");
            groupsHolder = root.Q("groups");
            subtitle = root.Q<Label>("subtitle");
            summary = root.Q<Label>("summary");
            notes = root.Q<Label>("notes");
            Button close = root.Q<Button>("close");
            if (close != null)
                close.clicked += () => SetVisible(false);
            shownShape = null;
            SetVisible(Visible);
        }

        void OnDisable()
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            panelRoot?.EnableInClassList("hidden", !visible);
            nextRefresh = 0f;
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetVisible(!Visible);
            if (!Visible || panelRoot == null || Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            IPlanetSimulation sim = runner != null ? runner.Simulation : null;
            if (sim != null)
                Refresh(sim);
        }

        void Refresh(IPlanetSimulation sim)
        {
            Workforce w = sim.Workforce;
            int working = 0;
            foreach (Robot r in w.Robots)
                if (r.State == RobotState.Working) working++;
            subtitle.text = $"{toggleKey}: close";
            summary.text = $"{w.Robots.Count} robot{(w.Robots.Count == 1 ? "" : "s")} · {working} working · " +
                           $"{sim.ConstructionQueueCount} site{(sim.ConstructionQueueCount == 1 ? "" : "s")} waiting · " +
                           $"build parts {Format.Energy(sim.BuildStockWh)}";
            notes.text =
                "Robots build construction sites (a site only takes build parts while a robot works on it) and take down " +
                "buildings you mark with Del. Each robot takes the most urgent task it can do; only Critical tasks pull a " +
                "busy robot off its work.\n" +
                "Robots run on their own battery: below 20 % they charge at the lander or a battery yard until 80 %, or go " +
                "back to work sooner if no spare power reaches a battery yard. Work " +
                "wears them; below 30 % condition they are repaired at the lander with build parts, and a worn-out battery " +
                "is replaced there. At 0 % condition a robot shuts down until it is repaired.\n" +
                $"Not yet: movement, making new robots (phase 3). Group capacity {w.GroupCapacity}, power use, wear and " +
                "repair rates are placeholder values.";

            string shape = Shape(w);
            if (shape != shownShape)
            {
                Rebuild(sim, w);
                shownShape = shape;
            }
            foreach (Robot r in w.Robots)
            {
                if (taskLabels.TryGetValue(r.Id, out Label l))
                    l.text = TaskText(sim, r);
                if (vitalLabels.TryGetValue(r.Id, out Label v))
                    v.text = Vitals(r);
            }
        }

        /// <summary>Everything the buttons and rows depend on; the list is rebuilt only when it changes.</summary>
        static string Shape(Workforce w)
        {
            var sb = new StringBuilder();
            foreach (RobotGroup g in w.Groups)
            {
                sb.Append(g.Id).Append(g.Name).Append(g.State).Append(g.Program);
                foreach (TaskKind k in Kinds) sb.Append(g.PriorityOf(k));
                foreach (int id in g.Members) sb.Append(',').Append(id);
                sb.Append(';');
            }
            foreach (Robot r in w.Robots)
            {
                sb.Append(r.Id).Append(r.State).Append(r.OnTemporaryTask);
                foreach (TaskKind k in Kinds) sb.Append(r.PriorityOverride(k));
            }
            return sb.ToString();
        }

        static readonly TaskKind[] Kinds = (TaskKind[])Enum.GetValues(typeof(TaskKind));

        void Rebuild(IPlanetSimulation sim, Workforce w)
        {
            groupsHolder.Clear();
            taskLabels.Clear();
            vitalLabels.Clear();
            foreach (RobotGroup g in w.Groups)
            {
                if (g.Count == 0 && g.Automatic)
                    continue;
                var box = new VisualElement();
                box.AddToClassList("group");

                var head = new VisualElement();
                head.AddToClassList("group-head");
                head.Add(MakeLabel($"{g.Name}  ({g.Count}/{w.GroupCapacity})", "group-name"));
                Label state = MakeLabel(StateText(g), "group-state");
                state.EnableInClassList("paused", g.State == GroupState.Paused);
                state.EnableInClassList("interrupted", g.State == GroupState.Interrupted);
                head.Add(state);
                int id = g.Id;
                if (g.State == GroupState.Automatic)
                    head.Add(MakeButton("Pause", () => { w.Pause(id); Changed(sim); }));
                else
                    head.Add(MakeButton("Resume", () => { w.Resume(id); Changed(sim); }));
                box.Add(head);

                var priorities = new VisualElement();
                priorities.AddToClassList("priority-row");
                priorities.Add(MakeLabel($"{(g.Automatic ? "Automatic group" : "Your group")} · program {g.Program} ·", "priority-label"));
                foreach (TaskKind k in Kinds)
                {
                    TaskKind kind = k;
                    TaskPriority p = g.PriorityOf(k);
                    Button chip = MakeButton($"{KindName(k)}: {p}", () =>
                    {
                        w.SetGroupPriority(id, kind, Next(w.GetGroup(id).PriorityOf(kind)));
                        Changed(sim);
                    });
                    chip.AddToClassList("chip");
                    chip.EnableInClassList("critical", p == TaskPriority.Critical);
                    chip.EnableInClassList("disabled-priority", p == TaskPriority.Disabled);
                    chip.tooltip = "Click to change: Critical, High, Normal, Low, Disabled";
                    priorities.Add(chip);
                }
                box.Add(priorities);

                foreach (int robotId in g.Members)
                {
                    Robot r = w.GetRobot(robotId);
                    if (r != null)
                        box.Add(MakeRobot(sim, w, r));
                }
                groupsHolder.Add(box);
            }
        }

        VisualElement MakeRobot(IPlanetSimulation sim, Workforce w, Robot r)
        {
            var row = new VisualElement();
            row.AddToClassList("robot");
            row.EnableInClassList("working", r.State == RobotState.Working);
            row.Add(MakeLabel($"{r.Name} · {r.Kind} · {r.State}{(r.OnTemporaryTask ? " (temporary task)" : "")}", "robot-name"));
            Label task = MakeLabel(TaskText(sim, r), "robot-line");
            taskLabels[r.Id] = task;
            row.Add(task);
            var own = new List<string>();
            foreach (TaskKind k in Kinds)
            {
                TaskPriority? p = r.PriorityOverride(k);
                if (p.HasValue) own.Add($"{KindName(k)} {p.Value}");
            }
            if (r.ProgramOverride.HasValue) own.Add($"program {r.ProgramOverride.Value}");
            if (own.Count > 0)
                row.Add(MakeLabel("Own settings: " + string.Join(", ", own), "robot-line"));
            Label vitals = MakeLabel(Vitals(r), "robot-line");
            vitals.AddToClassList("muted");
            vitalLabels[r.Id] = vitals;
            row.Add(vitals);
            return row;
        }

        static string Vitals(Robot r) =>
            $"Battery {r.Battery.StateOfCharge * 100.0:0} % · health {r.Battery.Health * 100.0:0} % " +
            $"({r.Battery.EquivalentFullCycles:0} cycles) · condition {r.Condition * 100.0:0} %";

        static string TaskText(IPlanetSimulation sim, Robot r)
        {
            string at = !r.Facility.HasValue ? ""
                : r.Facility.Value.Slot < 0 ? "at the lander" : $"at the battery yard in slot {r.Facility.Value.Slot}";
            if (r.State == RobotState.Shutdown)
                return "Shut down (condition 0 %): waiting for a place at the lander to be repaired";
            if (r.Activity == RobotActivity.Charging)
                return $"Charging {at} until 80 %";
            if (r.Activity == RobotActivity.Maintenance)
                return r.Battery.WornOut
                    ? $"Maintenance {at}: new battery ({Format.Energy(BuildCosts.Battery(r.Battery.NominalWh))} of build parts)"
                    : $"Maintenance {at}: repairs use build parts";
            if (r.NeedsCharge)
                return "Battery below 20 %: waiting for a free charger";
            string repairNote = !r.NeedsMaintenance ? ""
                : r.Battery.WornOut ? " · battery worn out: waits for a place at the lander"
                : " · needs repair: waits for a place at the lander";
            if (!r.Task.HasValue)
                return "No task: waiting for work" + repairNote;
            WorkTask t = r.Task.Value;
            string what = BuildingName(sim.GetBuilding(t.Tile, t.Slot));
            if (t.Kind == TaskKind.Build)
            {
                double progress = sim.ConstructionProgress(t.Tile, t.Slot);
                string waiting = sim.BuildStockWh <= 0.0 && sim.BuildPowerW <= 0.0 ? " · waiting for spare power" : "";
                return $"Building a {what} · tile {t.Tile}, slot {t.Slot} · {progress * 100.0:0} %{waiting}{repairNote}";
            }
            return $"Taking down a {what} · tile {t.Tile}, slot {t.Slot}{repairNote}";
        }

        static string BuildingName(BuildingType type) =>
            type == BuildingType.Battery ? "battery yard" : type == BuildingType.SolarPanel ? "solar panel" : "building";

        static string KindName(TaskKind k) => k == TaskKind.Build ? "Build" : "Salvage";

        static string StateText(RobotGroup g) =>
            g.State == GroupState.Paused ? "Paused" : g.State == GroupState.Interrupted ? "Interrupted" : "Running";

        /// <summary>Critical → High → Normal → Low → Disabled → Critical.</summary>
        static TaskPriority Next(TaskPriority p) => p == TaskPriority.Disabled ? TaskPriority.Critical : p + 1;

        void Changed(IPlanetSimulation sim)
        {
            sim.RefreshWork();
            nextRefresh = 0f;
        }

        static Label MakeLabel(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        static Button MakeButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("small-button");
            return b;
        }
    }
}

using KardashevAscent.Simulation.Energy;
using System;
using System.Collections.Generic;
using System.Text;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;
using Material = KardashevAscent.Simulation.Resources.Material;
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
        [Tooltip("For the Facility program: the selected tile becomes a group's facility.")]
        [SerializeField] TilePicker picker;
        [SerializeField] KeyCode toggleKey = KeyCode.G;
        [Tooltip("Seconds between refreshes while open.")]
        [Range(0.1f, 2f)] [SerializeField] float refreshInterval = 0.5f;

        VisualElement panelRoot, groupsHolder, groupActions, tasksHolder;
        /// <summary>The group the TASKS buttons act on (interrupt, queue).</summary>
        int targetGroupId = -1;
        /// <summary>The robot the TASKS Reserve buttons act on.</summary>
        int targetRobotId = -1;
        Label subtitle, summary, notes, workshopMessage;
        InfoTip summaryTip;
        readonly System.Collections.Generic.HashSet<int> openGroups = new System.Collections.Generic.HashSet<int>();
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
            if (summary != null)
                summaryTip = InfoTip.After(summary, "");   // the full line behind "i" (director 2026-11-15)
            workshopMessage = root.Q<Label>("workshop-message");
            groupActions = root.Q("group-actions");
            tasksHolder = root.Q("tasks");
            Button close = root.Q<Button>("close");
            if (close != null)
                close.clicked += () => SetVisible(false);
            shownShape = null;
            SetVisible(Visible);
            SystemTabs.Register(new SystemTabs.Tab
            {
                Id = "robots", Title = "Robots", Key = toggleKey, Order = 20,
                IsOpen = () => Visible, SetOpen = SetVisible,
            });
        }

        void OnDisable()
        {
            SystemTabs.Unregister("robots");
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
            if (ScreenInput.KeyDown(toggleKey))
                SystemTabs.Toggle("robots");
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
            int blocked = w.Tasks.Count(TaskState.Blocked), help = w.Tasks.Count(TaskState.AssistanceNeeded);
            bool shortOf = !sim.ConstructionShortfall.IsEmpty;
            summary.text = $"{w.Robots.Count} robots · {working} working · {sim.ConstructionQueueCount} sites" +
                           (shortOf ? " · short" : "") + (blocked + help > 0 ? $" · {blocked + help} stuck" : "");
            summary.style.color = shortOf || blocked + help > 0 ? UiPalette.Warning : StyleKeyword.Null;
            if (summaryTip != null) summaryTip.Text = $"{w.Robots.Count} robot{(w.Robots.Count == 1 ? "" : "s")} · {working} working · " +
                           $"{sim.ConstructionQueueCount} site{(sim.ConstructionQueueCount == 1 ? "" : "s")} waiting · " +
                           (sim.ConstructionShortfall.IsEmpty ? "materials in stock" : $"sites short of {sim.ConstructionShortfall.Describe()}") +
                           (w.Tasks.Count(TaskState.Blocked) > 0 ? $" · {w.Tasks.Count(TaskState.Blocked)} blocked" : "") +
                           (w.Tasks.Count(TaskState.AssistanceNeeded) > 0 ? $" · {w.Tasks.Count(TaskState.AssistanceNeeded)} need help" : "");
            notes.text =
                "Robots build construction sites (a site only takes build parts while a robot works on it) and take down " +
                "buildings you mark with Del. Each robot takes the most urgent task it can do; only Critical tasks pull a " +
                "busy robot off its work.\n" +
                "Robots drive tile to tile (never across the sea), slower over highland, mountains and ice; driving uses " +
                "battery, more on high-gravity planets. They only take work they can reach and still drive back to a " +
                "charger from with 20 % left. Select a tile and press Send to move a robot yourself.\n" +
                "Robots run on their own battery and can only recharge at a charging station or a battery yard (the lander only repairs): below 20 % they charge until 80 %, or go " +
                "back to work sooner if no spare power reaches a battery yard. Work " +
                "wears them; below 30 % condition they are repaired at the lander with build parts, and a worn-out battery " +
                "is replaced there. At 0 % condition a robot shuts down until it is repaired.\n" +
                $"Not yet: transport and rescue (later features). Group capacity {w.GroupCapacity} (rises at milestones 2, " +
                "4 and 6), power use, driving pace, wear, repair rates, recipes and unlock milestones are placeholder values.";

            var openKeys = new StringBuilder();
            foreach (WorkTask t in sim.OpenWork())
            {
                TaskRecord rec = w.Tasks.Get(t);
                openKeys.Append(t).Append(rec?.State).Append(rec?.RobotId).Append(rec?.ReservedFor).Append(';');
            }
            openKeys.Append(w.Changes.Count > 0 ? w.Changes[w.Changes.Count - 1].Hours : -1.0).Append(targetRobotId);
            string shape = Shape(w) + "|" + openKeys
                           + "|" + targetGroupId + "|" + (picker != null ? picker.SelectedTile : -1);
            if (shape != shownShape)
            {
                Rebuild(sim, w);
                shownShape = shape;
            }
            foreach (Robot r in w.Robots)
            {
                if (taskLabels.TryGetValue(r.Id, out Label l))
                {
                    l.text = ShortTask(sim, r);
                    l.tooltip = TaskText(sim, r);   // the full wording on hover
                }
                if (gaugeUpdaters.TryGetValue(r.Id, out System.Action<Robot> update))
                    update(r);
            }
        }

        /// <summary>Everything the buttons and rows depend on; the list is rebuilt only when it changes.</summary>
        static string Shape(Workforce w)
        {
            var sb = new StringBuilder();
            foreach (RobotGroup g in w.Groups)
            {
                sb.Append(g.Id).Append(g.Name).Append(g.State).Append(g.Program).Append(g.FacilityTile).Append(g.MaintenanceBelow);
                foreach (TaskKind k in Kinds) sb.Append(g.PriorityOf(k)).Append(g.RankOf(k));
                foreach (WorkTask q in g.Queue) sb.Append(q);
                foreach (int id in g.Members) sb.Append(',').Append(id);
                sb.Append(';');
            }
            foreach (Robot r in w.Robots)
            {
                sb.Append(r.Id).Append(r.State).Append(r.OnTemporaryTask);
                foreach (TaskKind k in Kinds) sb.Append(r.PriorityOverride(k));
                sb.Append(r.ProgramOverride).Append(r.GroupId).Append(r.Tile).Append(r.ManualTarget).Append(r.IsTravelling);
            }
            return sb.ToString();
        }

        static readonly TaskKind[] Kinds = (TaskKind[])Enum.GetValues(typeof(TaskKind));

        void Rebuild(IPlanetSimulation sim, Workforce w)
        {
            groupsHolder.Clear();
            taskLabels.Clear();
            vitalLabels.Clear();
            gaugeUpdaters.Clear();
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

                // Priorities, program and repair rules are folded away: most players only need the robots below.
                var settings = new Foldout { text = "Group settings: priorities, program, repairs", value = openGroups.Contains(id) };
                settings.AddToClassList("how-it-works");
                settings.RegisterValueChangedCallback(e =>
                {
                    if (e.target != settings) return;
                    if (e.newValue) openGroups.Add(id); else openGroups.Remove(id);
                });
                box.Add(settings);

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
                settings.Add(priorities);
                settings.Add(MakeProgramRow(sim, w, g));
                double below = g.MaintenanceBelow;
                double nextBelow = below < 0.25 ? 0.30 : below < 0.4 ? 0.50 : 0.20;
                var rules = new VisualElement();
                rules.AddToClassList("button-row");
                Button rule = MakeButton($"Repair below {below * 100.0:0} % condition", () => { w.SetGroupMaintenance(id, nextBelow); Changed(sim); });
                rule.tooltip = "Maintenance rule for this group: click to change (20 %, 30 %, 50 %). Repairs use build parts and iron.";
                rules.Add(rule);
                settings.Add(rules);
                settings.Add(MakeRankingRow(sim, w, g));
                if (g.Program == OperatingProgram.TaskQueue)
                    settings.Add(MakeQueue(sim, w, g));

                foreach (int robotId in g.Members)
                {
                    Robot r = w.GetRobot(robotId);
                    if (r != null)
                        box.Add(MakeRobot(sim, w, r));
                }
                groupsHolder.Add(box);
            }

            groupActions.Clear();
            groupActions.Add(MakeButton("New group", () => { w.CreateGroup($"Group {w.Groups.Count + 1}"); Changed(sim); }));
            RebuildTasks(sim, w);
        }

        // ---- Phase 4: programs, ranking, queue, groups, tasks ----

        static OperatingProgram NextProgram(OperatingProgram p) =>
            p == OperatingProgram.TaskQueue ? OperatingProgram.Priority : p + 1;

        static string ProgramHelp(OperatingProgram p)
        {
            switch (p)
            {
                case OperatingProgram.Facility: return "only tasks on its facility tile";
                case OperatingProgram.Demand: return "follows what the colony needs: building first when power runs short or sites wait, mining last";
                case OperatingProgram.TaskQueue: return "works through its own list (queue tasks below)";
                default: return "the most urgent task, by the priorities and order here";
            }
        }

        VisualElement MakeProgramRow(IPlanetSimulation sim, Workforce w, RobotGroup g)
        {
            int id = g.Id;
            var row = new VisualElement();
            row.AddToClassList("button-row");
            row.Add(MakeButton($"Program: {g.Program}", () => { w.SetGroupProgram(id, NextProgram(w.GetGroup(id).Program)); Changed(sim); }));
            if (g.Program == OperatingProgram.Facility)
            {
                row.Add(MakeLabel(g.FacilityTile.HasValue ? $"facility: tile {g.FacilityTile.Value}" : "facility: anywhere", "priority-label"));
                int selected = picker != null ? picker.SelectedTile : -1;
                if (selected >= 0 && g.FacilityTile != selected)
                    row.Add(MakeButton($"Use tile {selected}", () => { w.SetGroupFacility(id, selected); Changed(sim); }));
                if (g.FacilityTile.HasValue)
                    row.Add(MakeButton("Anywhere", () => { w.SetGroupFacility(id, null); Changed(sim); }));
            }
            if (!g.Automatic)
            {
                var name = new TextField { value = g.Name };
                name.AddToClassList("name-field");
                name.RegisterCallback<FocusOutEvent>(_ => { if (w.RenameGroup(id, name.value)) Changed(sim); });
                row.Add(name);
                row.Add(MakeButton("Delete group", () => { w.DeleteGroup(id); Changed(sim); }));
            }
            Label help = MakeLabel(ProgramHelp(g.Program), "robot-line");
            help.AddToClassList("muted");
            var wrap = new VisualElement();
            wrap.Add(row);
            wrap.Add(help);
            return wrap;
        }

        VisualElement MakeRankingRow(IPlanetSimulation sim, Workforce w, RobotGroup g)
        {
            int id = g.Id;
            var row = new VisualElement();
            row.AddToClassList("priority-row");
            row.Add(MakeLabel("Order for ties:", "priority-label"));
            List<TaskKind> order = g.RankedKinds();
            for (int i = 0; i < order.Count; i++)
            {
                TaskKind kind = order[i];
                Button b = MakeButton(i == 0 ? KindName(kind) : "↑ " + KindName(kind), () => { w.RaiseInRanking(id, kind); Changed(sim); });
                b.AddToClassList("chip");
                b.tooltip = "Click to move it one place earlier. Priorities still come first.";
                row.Add(b);
            }
            if (g.HasCustomRanking)
                row.Add(MakeButton("Default", () => { w.ResetRanking(id); Changed(sim); }));
            return row;
        }

        VisualElement MakeQueue(IPlanetSimulation sim, Workforce w, RobotGroup g)
        {
            int id = g.Id;
            var box = new VisualElement();
            if (g.Queue.Count == 0)
                box.Add(MakeLabel("Queue empty: pick this group under TASKS and press Queue.", "robot-line"));
            for (int i = 0; i < g.Queue.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("task-row");
                row.Add(MakeLabel($"{i + 1}. {Describe(sim, g.Queue[i])}", "task-text"));
                row.Add(MakeButton("Remove", () => { w.RemoveFromQueue(id, index); Changed(sim); }));
                box.Add(row);
            }
            return box;
        }

        static string Describe(IPlanetSimulation sim, WorkTask t)
        {
            switch (t.Kind)
            {
                case TaskKind.Build: return $"Build a {BuildingName(sim.GetBuilding(t.Tile, t.Slot))} · tile {t.Tile}, slot {t.Slot}";
                case TaskKind.Salvage: return $"Take down a {BuildingName(sim.GetBuilding(t.Tile, t.Slot))} · tile {t.Tile}, slot {t.Slot}";
                case TaskKind.Adjust: return $"Turn a solar panel to new angles · tile {t.Tile}, slot {t.Slot}";
                case TaskKind.Electrical:
                    switch ((KardashevAscent.Simulation.Game.ElectricalAction)(t.Slot / 1000))
                    {
                        case KardashevAscent.Simulation.Game.ElectricalAction.InspectLine: return $"Inspect a power line · tile {t.Tile}";
                        case KardashevAscent.Simulation.Game.ElectricalAction.InstallMeter: return $"Install a meter on a power line · tile {t.Tile}";
                        case KardashevAscent.Simulation.Game.ElectricalAction.RepairLine: return $"Repair a power line · tile {t.Tile}, slot {t.Slot % 1000}";
                        default: return $"Install a circuit in a charging station · tile {t.Tile}, slot {t.Slot % 1000}";
                    }
                case TaskKind.Survey: return $"Survey tile {t.Tile}";
                case TaskKind.Mine: return $"Mine {Materials.Name((Material)t.Slot).ToLowerInvariant()} · tile {t.Tile}";
                case TaskKind.Manufacture:
                    return t.Order < sim.Jobs.Count ? $"Workshop: {sim.Jobs[t.Order].Describe()}" : "Workshop job";
                case TaskKind.Measure:
                    return $"Measure {KardashevAscent.Simulation.Solar.SolarUnknowns.Name((KardashevAscent.Simulation.Solar.SolarParameter)t.Slot).ToLowerInvariant()} · Research Station, tile {t.Tile}";
                case TaskKind.Experiment:
                    return $"Experiment: {KardashevAscent.Simulation.Planet.Experiments.Name((KardashevAscent.Simulation.Planet.ExperimentKind)t.Slot).ToLowerInvariant()} · Research Station, tile {t.Tile}";
                case TaskKind.Excavate:
                    return $"{KardashevAscent.Simulation.Resources.Passages.Name((KardashevAscent.Simulation.Resources.PassageKind)t.Slot)} · tile {t.Tile} ({sim.PassageProgress(t.Tile) * 100.0:0} %)";
                case TaskKind.Haul:
                    foreach (var h in sim.HaulOrders())
                        if (h.Id == t.Slot / 2)
                            return $"Haul {h.Kg:0} kg {Materials.Name(h.Material).ToLowerInvariant()}: {(t.Slot % 2 == 0 ? $"load at tile {h.From}" : $"deliver to tile {h.To}")}";
                    return "Haul";
                default: return t.ToString();
            }
        }

        void RebuildTasks(IPlanetSimulation sim, Workforce w)
        {
            tasksHolder.Clear();
            var groups = new List<RobotGroup>();
            foreach (RobotGroup g in w.Groups)
                if (g.Count > 0 || !g.Automatic) groups.Add(g);
            if (groups.Count == 0)
                return;
            if (w.GetGroup(targetGroupId) == null)
                targetGroupId = groups[0].Id;
            RobotGroup target = w.GetGroup(targetGroupId);

            var head = new VisualElement();
            head.AddToClassList("button-row");
            head.Add(MakeLabel("Buttons act on:", "priority-label"));
            head.Add(MakeButton(target.Name, () =>
            {
                int i = groups.IndexOf(w.GetGroup(targetGroupId));
                targetGroupId = groups[(i + 1) % groups.Count].Id;
                nextRefresh = 0f;
            }));
            tasksHolder.Add(head);

            if (w.GetRobot(targetRobotId) == null && w.Robots.Count > 0)
                targetRobotId = w.Robots[0].Id;
            Robot targetRobot = w.GetRobot(targetRobotId);
            if (targetRobot != null)
            {
                head.Add(MakeLabel("· reserve for:", "priority-label"));
                head.Add(MakeButton(targetRobot.Name, () =>
                {
                    int i = 0;
                    for (int k = 0; k < w.Robots.Count; k++) if (w.Robots[k].Id == targetRobotId) i = k;
                    targetRobotId = w.Robots[(i + 1) % w.Robots.Count].Id;
                    nextRefresh = 0f;
                }));
            }

            IReadOnlyList<WorkTask> open = sim.OpenWork();
            if (open.Count == 0)
            {
                Label none = MakeLabel("No open tasks.", "robot-line");
                none.tooltip = "Place buildings, mark salvage (Del), order surveys or mining (tile panel) or Workshop jobs.";
                tasksHolder.Add(none);
            }
            foreach (WorkTask t in open)
            {
                WorkTask task = t;
                TaskRecord rec = w.Tasks.Get(t);
                Robot worker = w.WorkerOf(t);
                Robot reserved = w.ReservedRobotFor(t);
                var row = new VisualElement();
                row.AddToClassList("task-row");
                string state = rec != null ? StateName(rec.State) : "";
                string who = worker != null ? $" · {worker.Name}" : "";
                if (rec != null && rec.Capacity > 1) who += $" · crew {rec.Workers}/{rec.Capacity}";
                string why = rec != null && rec.Reason.Length > 0 && worker == null ? $" · {rec.Reason}" : "";
                string prio = rec != null && rec.Priority != TaskPriority.Normal ? $" · {rec.Priority}" : "";
                Label text = MakeLabel($"{Describe(sim, t)} · {state}{who}{prio}{why}", "task-text");
                text.EnableInClassList("blocked", rec != null && (rec.State == TaskState.Blocked || rec.State == TaskState.AssistanceNeeded));
                row.Add(text);
                if (reserved != null)
                    row.Add(MakeButton($"Unreserve {reserved.Name}", () => { w.Unreserve(reserved.Id); Changed(sim); }));
                else if (targetRobot != null)
                    row.Add(MakeButton($"Reserve {targetRobot.Name}", () =>
                    {
                        workshopMessage.text = w.Reserve(targetRobotId, task)
                            ? $"{w.GetRobot(targetRobotId).Name} will take: {Describe(sim, task)}."
                            : $"{w.GetRobot(targetRobotId).Name} can't do this kind of task.";
                        Changed(sim);
                    }));
                row.Add(MakeButton("Interrupt", () =>
                {
                    workshopMessage.text = w.Interrupt(targetGroupId, task) ? $"{w.GetGroup(targetGroupId).Name} interrupted for: {Describe(sim, task)}." : "That group can't do this task now (paused, or no robot able).";
                    Changed(sim);
                }));
                if (target.Program == OperatingProgram.TaskQueue)
                    row.Add(MakeButton("Queue", () => { w.Enqueue(targetGroupId, task); Changed(sim); }));
                tasksHolder.Add(row);
            }

            // The history is a folded section (director 2026-11-20: less text on screen).
            var history = new Foldout { text = "Recent activity", value = openHistory };
            history.RegisterValueChangedCallback(e => { if (e.target == history) openHistory = e.newValue; });
            tasksHolder.Add(history);
            int finished = w.Tasks.Finished.Count;
            if (finished > 0)
            {
                string done = "";
                for (int i = Math.Max(0, finished - 3); i < finished; i++)
                {
                    TaskRecord f = w.Tasks.Finished[i];
                    done += (done.Length > 0 ? " · " : "Recently: ") + $"{Describe(sim, f.Task)} {StateName(f.State).ToLowerInvariant()}";
                }
                history.Add(MakeLabel(done, "robot-line"));
            }
            if (w.Changes.Count > 0)
            {
                string log = "Changes: ";
                for (int i = Math.Max(0, w.Changes.Count - 4); i < w.Changes.Count; i++)
                {
                    AssignmentChange c = w.Changes[i];
                    Robot r = w.GetRobot(c.RobotId);
                    log += $"{(r != null ? r.Name : "R" + c.RobotId)} {(c.To.HasValue ? "→ " + Describe(sim, c.To.Value) : "left " + (c.From.HasValue ? Describe(sim, c.From.Value) : "its task"))} ({c.Reason}); ";
                }
                Label l = MakeLabel(log, "robot-line");
                l.AddToClassList("muted");
                history.Add(l);
            }
        }

        static string StateName(TaskState s)
        {
            switch (s)
            {
                case TaskState.AssistanceNeeded: return "Needs help";
                default: return s.ToString();
            }
        }

        // ---- the robot card (director 2026-11-20: separate upgrade and command, symbols, less text) ----

        bool openHistory;
        readonly Dictionary<int, System.Action<Robot>> gaugeUpdaters = new Dictionary<int, System.Action<Robot>>();

        /// <summary>A small coloured square with a short symbol, like the material icons in the Manufacturing window.</summary>
        static Label Square(string symbol, Color fill, Color text, float size = 22f)
        {
            var l = new Label(symbol);
            l.style.width = size;
            l.style.height = size;
            l.style.minWidth = size;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.fontSize = size >= 22f ? 12 : 10;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.backgroundColor = fill;
            l.style.color = text;
            l.style.marginRight = 3;
            l.style.paddingLeft = l.style.paddingRight = 0;
            return l;
        }

        /// <summary>A gauge: a symbol, a thin bar and the percentage. Returns the row and a function that sets it.</summary>
        static VisualElement Gauge(string symbol, string tip, out System.Action<double> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginRight = 8;
            row.tooltip = tip;
            row.Add(Square(symbol, new Color(1f, 1f, 1f, 0.10f), UiPalette.Muted, 16f));
            var track = new VisualElement();
            track.style.width = 40;
            track.style.height = 6;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            var fill = new VisualElement();
            fill.style.height = 6;
            track.Add(fill);
            row.Add(track);
            var value = new Label("");
            value.style.fontSize = 10;
            value.style.color = UiPalette.Muted;
            value.style.marginLeft = 3;
            row.Add(value);
            set = share =>
            {
                share = System.Math.Max(0.0, System.Math.Min(1.0, share));
                fill.style.width = (float)(40.0 * share);
                fill.style.backgroundColor = share < 0.2 ? UiPalette.Critical : share < 0.5 ? UiPalette.Warning : UiPalette.Good;
                value.text = $"{share * 100.0:0}";
            };
            return row;
        }

        static (string Symbol, Color Colour) ModuleLook(RobotModule m)
        {
            switch (m)
            {
                case RobotModule.BigBattery: return ("B+", new Color(0.55f, 0.85f, 0.62f));
                case RobotModule.FastDrive: return ("»", new Color(1.00f, 0.78f, 0.36f));
                case RobotModule.Shielding: return ("Sh", new Color(0.78f, 0.70f, 0.95f));
                case RobotModule.ThermalInsulation: return ("Th", new Color(0.95f, 0.60f, 0.45f));
                case RobotModule.Processor: return ("P", new Color(0.47f, 0.75f, 1.00f));
                case RobotModule.AdvancedProcessor: return ("P+", new Color(0.47f, 0.85f, 1.00f));
                case RobotModule.Boat: return ("~", new Color(0.45f, 0.72f, 0.95f));
                default: return ("?", UiPalette.Muted);
            }
        }

        static (string Symbol, Color Colour) KindLook(RobotKind k)
        {
            string name = k.ToString();
            Color c = k == RobotKind.General ? new Color(1.00f, 0.78f, 0.36f)
                    : k == RobotKind.Mining ? new Color(0.85f, 0.50f, 0.28f)
                    : k == RobotKind.Research ? new Color(0.47f, 0.75f, 1.00f)
                    : new Color(0.62f, 0.66f, 0.72f);
            return (name.Substring(0, 1), c);
        }

        static string ProgramLetter(OperatingProgram p) => p == OperatingProgram.Priority ? "P" : p == OperatingProgram.Facility ? "F" : p == OperatingProgram.Demand ? "D" : "Q";

        /// <summary>A short line on what the robot is doing (the full wording is on hover).</summary>
        static string ShortTask(IPlanetSimulation sim, Robot r)
        {
            if (r.IsStranded) return "stranded: battery empty";
            if (r.ManualTarget.HasValue) return $"→ tile {r.ManualTarget.Value} · {r.HopsLeft} to go";
            string what = DoingText(sim, r);
            return r.IsTravelling ? $"→ tile {r.Destination} · {r.HopsLeft} to go · {what}" : what;
        }

        VisualElement MakeRobot(IPlanetSimulation sim, Workforce w, Robot r)
        {
            var row = new VisualElement();
            row.AddToClassList("robot");
            row.EnableInClassList("working", r.State == RobotState.Working);

            // --- identity and state ---
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            var (kindSymbol, kindColour) = KindLook(r.Kind);
            Label kindIcon = Square(kindSymbol, kindColour, UiPalette.OnAccent, 24f);
            kindIcon.tooltip = $"{r.Kind} robot";
            head.Add(kindIcon);
            head.Add(MakeLabel(r.Name, "robot-name"));
            Color stateColour = r.State == RobotState.Working ? UiPalette.Good : r.IsShutDown || r.IsStranded ? UiPalette.Critical : UiPalette.Muted;
            var dot = new Label("●");
            dot.style.color = stateColour;
            dot.style.marginLeft = 6;
            dot.style.marginRight = 4;
            dot.tooltip = r.State.ToString() + (r.OnTemporaryTask ? " (temporary task)" : "");
            head.Add(dot);
            Label task = MakeLabel(ShortTask(sim, r), "robot-line");
            task.style.flexGrow = 1;
            task.style.flexShrink = 1;
            task.style.whiteSpace = WhiteSpace.NoWrap;
            task.style.overflow = Overflow.Hidden;
            task.tooltip = TaskText(sim, r);
            taskLabels[r.Id] = task;
            head.Add(task);
            row.Add(head);

            // --- vitals: battery, health, condition ---
            var gauges = new VisualElement();
            gauges.style.flexDirection = FlexDirection.Row;
            gauges.style.marginTop = 2;
            gauges.Add(Gauge("B", "Battery charge", out System.Action<double> setBattery));
            gauges.Add(Gauge("H", "Battery health", out System.Action<double> setHealth));
            gauges.Add(Gauge("C", "Condition (repaired at the lander below 30 %)", out System.Action<double> setCondition));
            gaugeUpdaters[r.Id] = rr =>
            {
                setBattery(rr.Battery.StateOfCharge);
                setHealth(rr.Battery.Health);
                setCondition(rr.Condition);
                gauges.tooltip = Vitals(rr);
            };
            gaugeUpdaters[r.Id](r);
            row.Add(gauges);

            // --- COMMAND: what the robot does (move, send, program) ---
            int robotId = r.Id;
            var command = ZoneRow("▶", "Commands: where the robot works", UiPalette.Cool);
            RobotGroup next = NextGroupFor(w, r);
            if (next != null)
                command.Add(IconButton("↔", $"Move to {next.Name}", false, () =>
                {
                    if (!w.MoveRobot(robotId, next.Id)) workshopMessage.text = $"{next.Name} is full.";
                    Changed(sim);
                }));
            int selectedTile = picker != null ? picker.SelectedTile : -1;
            if (selectedTile >= 0 && selectedTile != r.Tile && !r.IsShutDown && r.Activity == RobotActivity.None)
            {
                double hours = sim.DriveHours(r.Tile, selectedTile);
                if (!double.IsInfinity(hours))
                    command.Add(IconButton("→", $"Send to the selected tile {selectedTile} ({hours:0} h of driving)", false, () =>
                    {
                        workshopMessage.text = sim.SendRobot(robotId, selectedTile) ? $"{r.Name} is driving to tile {selectedTile}." : "It can't go there now.";
                        Changed(sim);
                    }));
            }
            OperatingProgram? ownProgram = r.ProgramOverride;
            OperatingProgram? nextOwn = !ownProgram.HasValue ? OperatingProgram.Priority
                : ownProgram.Value == OperatingProgram.TaskQueue ? (OperatingProgram?)null : ownProgram.Value + 1;
            OperatingProgram shown = ownProgram ?? w.EffectiveProgram(r);
            command.Add(IconButton(ProgramLetter(shown),
                ownProgram.HasValue ? $"Own program: {ownProgram.Value} (click: next; after the last, back to the group's)" : $"Program: the group's ({shown}). Click to give this robot its own",
                ownProgram.HasValue, () =>
                {
                    w.SetRobotProgramOverride(robotId, nextOwn);
                    Changed(sim);
                }));
            if (r.ReservedTask.HasValue || r.ResumeTask.HasValue)
            {
                string own = (r.ReservedTask.HasValue ? $"reserved for: {Describe(sim, r.ReservedTask.Value)}" : "") +
                             (r.ReservedTask.HasValue && r.ResumeTask.HasValue ? " · " : "") +
                             (r.ResumeTask.HasValue ? $"goes back to: {Describe(sim, r.ResumeTask.Value)}" : "");
                Label flag = Square("*", new Color(1f, 1f, 1f, 0.10f), UiPalette.Accent, 22f);
                flag.tooltip = own;
                command.Add(flag);
            }

            // --- UPGRADE: what the robot is made of (modules, battery) ---
            var upgrade = ZoneRow("▲", "Upgrades: filled = installed, outlined = order it at the Workshop", UiPalette.Good);
            if (r.SodiumBattery)
            {
                Label na = Square("Na", new Color(0.95f, 0.85f, 0.40f), UiPalette.OnAccent, 22f);
                na.tooltip = "Sodium-ion battery";
                upgrade.Add(na);
            }
            foreach (RobotModule m in new[] { RobotModule.BigBattery, RobotModule.FastDrive, RobotModule.Shielding, RobotModule.ThermalInsulation,
                                              RobotModule.Processor, RobotModule.AdvancedProcessor, RobotModule.Boat })
            {
                var (symbol, colour) = ModuleLook(m);
                string name = RobotTech.ModuleName(m);
                if (r.Has(m))
                {
                    Label have = Square(symbol, colour, UiPalette.OnAccent, 22f);
                    have.tooltip = $"{name}: installed";
                    upgrade.Add(have);
                    continue;
                }
                if (m == RobotModule.AdvancedProcessor && !r.Has(RobotModule.Processor))
                    continue;
                bool locked = sim.MilestonesReached < RobotTech.UnlockMilestone(m);
                RobotModule module = m;
                int id = r.Id;
                Button order = new Button(() => Order(sim, sim.OrderUpgrade(id, module), $"{RobotTech.ModuleName(module)} ordered for R{id}.")) { text = symbol };
                order.style.width = 22; order.style.height = 22; order.style.minWidth = 22;
                order.style.paddingLeft = order.style.paddingRight = order.style.paddingTop = order.style.paddingBottom = 0;
                order.style.marginLeft = 0; order.style.marginRight = 3; order.style.marginTop = order.style.marginBottom = 0;
                order.style.fontSize = 11;
                order.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                order.style.color = locked ? UiPalette.Muted : colour;
                order.style.borderTopWidth = order.style.borderBottomWidth = order.style.borderLeftWidth = order.style.borderRightWidth = 1;
                order.style.borderTopColor = order.style.borderBottomColor = order.style.borderLeftColor = order.style.borderRightColor = locked ? UiPalette.Border : colour;
                order.tooltip = locked ? $"{name}: opens at milestone {RobotTech.UnlockMilestone(m)}" : $"Order {name} (Workshop job)";
                order.SetEnabled(!locked);
                upgrade.Add(order);
            }
            // commands on the left, upgrades on the right: two zones in one row
            var zones = new VisualElement();
            zones.style.flexDirection = FlexDirection.Row;
            zones.style.flexWrap = Wrap.Wrap;
            zones.style.justifyContent = Justify.SpaceBetween;
            zones.style.marginTop = 4;
            zones.style.paddingTop = 3;
            zones.style.borderTopWidth = 1;
            zones.style.borderTopColor = new Color(1f, 1f, 1f, 0.08f);
            zones.Add(command);
            zones.Add(upgrade);
            row.Add(zones);
            return row;
        }

        /// <summary>A row with a small caption symbol on the left: one for commands, one for upgrades.</summary>
        static VisualElement ZoneRow(string caption, string tip, Color colour)
        {
            var zone = new VisualElement();
            zone.style.flexDirection = FlexDirection.Row;
            zone.style.alignItems = Align.Center;
            zone.style.marginRight = 8;
            var c = new Label(caption);
            c.style.width = 16;
            c.style.color = colour;
            c.style.fontSize = 11;
            c.tooltip = tip;
            zone.Add(c);
            return zone;
        }

        /// <summary>A square icon button; "on" paints it amber (a setting that differs from the default).</summary>
        static Button IconButton(string symbol, string tip, bool on, Action onClick)
        {
            var b = new Button(onClick) { text = symbol };
            b.AddToClassList("small-button");
            b.style.width = 24;
            b.style.height = 22;
            b.style.minWidth = 24;
            b.style.paddingLeft = b.style.paddingRight = 0;
            b.style.marginLeft = 0;
            b.style.marginRight = 4;
            b.style.marginTop = b.style.marginBottom = 0;
            b.tooltip = tip;
            if (on) b.AddToClassList("selected");
            return b;
        }

        /// <summary>The next group this robot could join: manual groups, or the automatic group of its kind.</summary>
        static RobotGroup NextGroupFor(Workforce w, Robot r)
        {
            var options = new List<RobotGroup>();
            foreach (RobotGroup g in w.Groups)
                if (g.Id == r.GroupId || !g.Automatic || g.AutoKind == r.Kind)
                    options.Add(g);
            if (options.Count <= 1)
                return null;
            int i = options.FindIndex(g => g.Id == r.GroupId);
            return options[(i + 1) % options.Count];
        }

        /// <summary>One short line: battery, health and condition (the details are in the tooltip).</summary>
        static string ShortVitals(Robot r) =>
            $"Battery {r.Battery.StateOfCharge * 100.0:0} % · health {r.Battery.Health * 100.0:0} % · condition {r.Condition * 100.0:0} %" +
            (r.Battery.ColdFactor < 0.999 ? " · cold" : "");

        static string Vitals(Robot r) =>
            $"Battery {r.Battery.StateOfCharge * 100.0:0} % · health {r.Battery.Health * 100.0:0} % " +
            $"({r.Battery.EquivalentFullCycles:0} cycles" + (r.Battery.ZeroDischarges > 0 ? $", {r.Battery.ZeroDischarges}× run flat" : "") + ")" +
            (r.Battery.ColdFactor < 0.999 ? $" · cold: {(1.0 - r.Battery.ColdFactor) * 100.0:0} % of capacity locked" : "") +
            $" · condition {r.Condition * 100.0:0} %";

        /// <summary>What a robot is doing, with where it is driving first (spec §10).</summary>
        static string TaskText(IPlanetSimulation sim, Robot r)
        {
            if (r.IsStranded)
                return $"Stranded on tile {r.Tile}: battery empty · build a battery yard there";
            if (r.ManualTarget.HasValue)
                return $"Driving to tile {r.ManualTarget.Value} (sent by you) · {r.HopsLeft} tile{(r.HopsLeft == 1 ? "" : "s")} to go";
            string what = DoingText(sim, r);
            return r.IsTravelling
                ? $"Driving to tile {r.Destination} · {r.HopsLeft} tile{(r.HopsLeft == 1 ? "" : "s")} to go · then: {what}"
                : $"{what} · on tile {r.Tile}";
        }

        static string DoingText(IPlanetSimulation sim, Robot r)
        {
            string at = !r.Facility.HasValue ? ""
                : r.Facility.Value.Slot < 0 ? "at the lander"
                : sim.GetBuilding(r.Facility.Value.Tile, r.Facility.Value.Slot) == BuildingType.ChargingStation
                    ? $"at the charging station in slot {r.Facility.Value.Slot}" : $"at the battery yard in slot {r.Facility.Value.Slot}";
            switch (r.Shutdown)
            {
                case ShutdownKind.Mechanical: return "Mechanical shutdown (condition 0 %): waiting for a place at the lander to be repaired";
                case ShutdownKind.CriticalDamage: return "Critical damage: shut down until it is repaired at the lander";
                case ShutdownKind.Energy: return "Energy shutdown (battery 0 %): waiting for a free charger";
            }
            if (r.Activity == RobotActivity.Charging)
                return $"Charging {at} until 80 %";
            if (r.Activity == RobotActivity.Maintenance)
                return r.Battery.WornOut
                    ? $"Maintenance {at}: new battery ({BuildCosts.BatteryCells(r.Battery.NominalWh).Describe()})"
                    : $"Maintenance {at}: repairs use iron";
            if (r.NeedsCharge)
                return "Battery below 20 %: waiting for a free charger";
            string repairNote = !r.NeedsMaintenance ? ""
                : r.Battery.WornOut ? " · battery worn out: waits for a place at the lander"
                : " · needs repair: waits for a place at the lander";
            if (!r.Task.HasValue)
                return "No task: waiting for work" + repairNote;
            WorkTask t = r.Task.Value;
            string what = BuildingName(sim.GetBuilding(t.Tile, t.Slot));
            if (t.Kind == TaskKind.Manufacture)
            {
                string job = "a job";
                for (int i = 0; i < sim.Jobs.Count && i < sim.WorkshopCount; i++)
                    if (i == t.Order) job = $"{sim.Jobs[i].Describe()} · {sim.Jobs[i].Progress * 100.0:0} %";
                return $"In the Workshop (slot {t.Slot}): {job}{repairNote}";
            }
            if (t.Kind == TaskKind.Build)
            {
                double progress = sim.ConstructionProgress(t.Tile, t.Slot);
                KardashevAscent.Simulation.Resources.MaterialBill lack = sim.ConstructionRemainingAt(t.Tile, t.Slot).Minus(sim.Materials);
                string waiting = lack.IsEmpty ? "" : $" · waiting for {lack.Describe()}";
                return $"Building a {what} · tile {t.Tile}, slot {t.Slot} · {progress * 100.0:0} %{waiting}{repairNote}";
            }
            if (t.Kind == TaskKind.Survey)
                return $"Surveying tile {t.Tile} · {sim.SurveyProgress(t.Tile) * 100.0:0} %{repairNote}";
            if (t.Kind == TaskKind.Mine)
                return $"Mining {KardashevAscent.Simulation.Resources.Materials.Name((KardashevAscent.Simulation.Resources.Material)t.Slot).ToLowerInvariant()} · tile {t.Tile}{repairNote}";
            return $"Taking down a {what} · tile {t.Tile}, slot {t.Slot}{repairNote}";
        }

        static string BuildingName(BuildingType type) => BuildingCatalog.NameOf(type);

        static string KindName(TaskKind k) => k.ToString();

        static string StateText(RobotGroup g) =>
            g.State == GroupState.Paused ? "Paused" : g.State == GroupState.Interrupted ? "Interrupted" : "Running";

        /// <summary>Critical → High → Normal → Low → Disabled → Critical.</summary>
        static TaskPriority Next(TaskPriority p) => p == TaskPriority.Disabled ? TaskPriority.Critical : p + 1;

        void Order(IPlanetSimulation sim, string why, string ok)
        {
            workshopMessage.text = why ?? ok;
            Changed(sim);
        }

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

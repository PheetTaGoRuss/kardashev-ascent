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

        VisualElement panelRoot, groupsHolder, workshopHolder, groupActions, tasksHolder;
        /// <summary>The group the TASKS buttons act on (interrupt, queue).</summary>
        int targetGroupId = -1;
        Label subtitle, summary, notes, workshopStatus, workshopMessage;
        readonly List<Label> jobLabels = new List<Label>();
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
            workshopHolder = root.Q("workshop");
            groupActions = root.Q("group-actions");
            tasksHolder = root.Q("tasks");
            workshopStatus = root.Q<Label>("workshop-status");
            workshopMessage = root.Q<Label>("workshop-message");
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
                "Robots drive tile to tile (never across the sea), slower over highland, mountains and ice; driving uses " +
                "battery, more on high-gravity planets. They only take work they can reach and still drive back to a " +
                "charger from with 20 % left. Select a tile and press Send to move a robot yourself.\n" +
                "Robots run on their own battery: below 20 % they charge at the lander or a battery yard until 80 %, or go " +
                "back to work sooner if no spare power reaches a battery yard. Work " +
                "wears them; below 30 % condition they are repaired at the lander with build parts, and a worn-out battery " +
                "is replaced there. At 0 % condition a robot shuts down until it is repaired.\n" +
                $"Not yet: transport and rescue (later features). Group capacity {w.GroupCapacity} (rises at milestones 2, " +
                "4 and 6), power use, driving pace, wear, repair rates, recipes and unlock milestones are placeholder values.";

            var openKeys = new StringBuilder();
            foreach (WorkTask t in sim.OpenWork()) openKeys.Append(t).Append(';');
            string shape = Shape(w) + "|" + sim.MilestonesReached + "|" + sim.WorkshopCount + "|" + sim.Jobs.Count + "|" + openKeys
                           + "|" + targetGroupId + "|" + (picker != null ? picker.SelectedTile : -1);
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
            workshopStatus.text =
                $"{sim.WorkshopCount} Workshop{(sim.WorkshopCount == 1 ? "" : "s")} · milestone {sim.MilestonesReached} · materials: {Stock(sim)}" +
                (sim.WorkshopCount == 0 ? "\nBuild a Robot Workshop on a site (Tab to Workshop, then P)." : "");
            for (int i = 0; i < jobLabels.Count && i < sim.Jobs.Count; i++)
                jobLabels[i].text = JobText(sim, i);
        }

        // ---- Workshop (phase 3b) ----

        static string Stock(IPlanetSimulation sim)
        {
            string s = "";
            foreach (Material m in Materials.All)
                if (sim.Materials[m] > 0.0)
                    s += (s.Length > 0 ? ", " : "") + $"{Materials.Name(m).ToLowerInvariant()} {sim.Materials[m]:0} kg";
            return s.Length > 0 ? s : "none (survey and mine from the tile panel)";
        }

        static string JobText(IPlanetSimulation sim, int i)
        {
            ManufacturingJob j = sim.Jobs[i];
            string where = i < sim.WorkshopCount ? $"{j.Progress * 100.0:0} % of {j.Recipe.WorkHours:0} h of work" : "waiting for a free Workshop";
            return $"{i + 1}. {j.Describe()} · {where}";
        }

        static readonly RobotKind[] Buildable =
            { RobotKind.General, RobotKind.Construction, RobotKind.Mining, RobotKind.Research, RobotKind.Maintenance };

        void RebuildWorkshop(IPlanetSimulation sim, Workforce w)
        {
            workshopHolder.Clear();
            jobLabels.Clear();
            for (int i = 0; i < sim.Jobs.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("job");
                Label text = MakeLabel(JobText(sim, i), "job-text");
                jobLabels.Add(text);
                row.Add(text);
                row.Add(MakeButton("Cancel", () => { sim.CancelJob(index); Changed(sim); }));
                workshopHolder.Add(row);
            }

            var robots = new VisualElement();
            robots.AddToClassList("button-row");
            robots.Add(MakeLabel("New robot:", "priority-label"));
            foreach (RobotKind kind in Buildable)
            {
                RobotKind k = kind;
                int unlock = RobotTech.UnlockMilestone(kind);
                Button b = MakeButton(kind.ToString(), () => Order(sim, sim.OrderRobot(k), $"{k} robot ordered."));
                b.EnableInClassList("locked", sim.MilestonesReached < unlock);
                b.tooltip = $"Iron 400 kg, rock 200 kg, copper (or aluminium) 40 kg, lithium 2 kg (or sodium 6 kg), 50 kWh of parts, 24 h of work. Unlocks at milestone {unlock}.";
                robots.Add(b);
            }
            workshopHolder.Add(robots);

            var refine = new VisualElement();
            refine.AddToClassList("button-row");
            refine.Add(MakeLabel("Refine from rock:", "priority-label"));
            foreach (Material m in new[] { Material.Aluminium, Material.Sodium })
            {
                Material material = m;
                Button b = MakeButton($"{RobotTech.RefineBatchKg(m):0} kg {Materials.Name(m).ToLowerInvariant()}",
                                      () => Order(sim, sim.OrderRefine(material), $"{Materials.Name(material)} batch ordered."));
                b.EnableInClassList("locked", sim.MilestonesReached < RobotTech.UnlockMilestone(m));
                b.tooltip = Materials.Origin(m) + $" Unlocks at milestone {RobotTech.UnlockMilestone(m)}.";
                refine.Add(b);
            }
            workshopHolder.Add(refine);
        }

        void Order(IPlanetSimulation sim, string why, string ok)
        {
            workshopMessage.text = why ?? ok;
            Changed(sim);
        }

        /// <summary>Everything the buttons and rows depend on; the list is rebuilt only when it changes.</summary>
        static string Shape(Workforce w)
        {
            var sb = new StringBuilder();
            foreach (RobotGroup g in w.Groups)
            {
                sb.Append(g.Id).Append(g.Name).Append(g.State).Append(g.Program).Append(g.FacilityTile);
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
                box.Add(MakeProgramRow(sim, w, g));
                box.Add(MakeRankingRow(sim, w, g));
                if (g.Program == OperatingProgram.TaskQueue)
                    box.Add(MakeQueue(sim, w, g));

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
            RebuildWorkshop(sim, w);
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
                case TaskKind.Survey: return $"Survey tile {t.Tile}";
                case TaskKind.Mine: return $"Mine {Materials.Name((Material)t.Slot).ToLowerInvariant()} · tile {t.Tile}";
                case TaskKind.Manufacture:
                    return t.Order < sim.Jobs.Count ? $"Workshop: {sim.Jobs[t.Order].Describe()}" : "Workshop job";
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

            IReadOnlyList<WorkTask> open = sim.OpenWork();
            if (open.Count == 0)
                tasksHolder.Add(MakeLabel("No open tasks: place buildings, mark salvage (Del), order surveys or mining (tile panel) or Workshop jobs.", "robot-line"));
            foreach (WorkTask t in open)
            {
                WorkTask task = t;
                Robot worker = w.WorkerOf(t);
                var row = new VisualElement();
                row.AddToClassList("task-row");
                row.Add(MakeLabel($"{Describe(sim, t)}{(worker != null ? $" · {worker.Name}" : "")}", "task-text"));
                row.Add(MakeButton("Interrupt", () =>
                {
                    workshopMessage.text = w.Interrupt(targetGroupId, task) ? $"{w.GetGroup(targetGroupId).Name} interrupted for: {Describe(sim, task)}." : "That group can't do this task now (paused, or no robot able).";
                    Changed(sim);
                }));
                if (target.Program == OperatingProgram.TaskQueue)
                    row.Add(MakeButton("Queue", () => { w.Enqueue(targetGroupId, task); Changed(sim); }));
                tasksHolder.Add(row);
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

            var upgrades = new VisualElement();
            upgrades.AddToClassList("button-row");
            string have = "";
            foreach (RobotModule m in new[] { RobotModule.BigBattery, RobotModule.FastDrive, RobotModule.Shielding })
            {
                if (r.Has(m))
                {
                    have += (have.Length > 0 ? ", " : "") + RobotTech.ModuleName(m).ToLowerInvariant();
                    continue;
                }
                if (sim.MilestonesReached < RobotTech.UnlockMilestone(m))
                    continue;
                RobotModule module = m;
                int id = r.Id;
                upgrades.Add(MakeButton("+ " + RobotTech.ModuleName(m), () => Order(sim, sim.OrderUpgrade(id, module), $"{RobotTech.ModuleName(module)} ordered for R{id}.")));
            }
            if (r.SodiumBattery || have.Length > 0)
                row.Add(MakeLabel((r.SodiumBattery ? "Na-ion battery" + (have.Length > 0 ? " · " : "") : "") + have, "robot-line"));
            if (upgrades.childCount > 0)
                row.Add(upgrades);

            var manage = new VisualElement();
            manage.AddToClassList("button-row");
            int robotId = r.Id;
            RobotGroup next = NextGroupFor(w, r);
            if (next != null)
                manage.Add(MakeButton($"Move to {next.Name}", () =>
                {
                    if (!w.MoveRobot(robotId, next.Id)) workshopMessage.text = $"{next.Name} is full.";
                    Changed(sim);
                }));
            OperatingProgram? ownProgram = r.ProgramOverride;
            OperatingProgram? nextOwn = !ownProgram.HasValue ? OperatingProgram.Priority
                : ownProgram.Value == OperatingProgram.TaskQueue ? (OperatingProgram?)null : ownProgram.Value + 1;
            int selectedTile = picker != null ? picker.SelectedTile : -1;
            if (selectedTile >= 0 && selectedTile != r.Tile && !r.IsShutDown && r.Activity == RobotActivity.None)
            {
                double hours = sim.DriveHours(r.Tile, selectedTile);
                if (!double.IsInfinity(hours))
                    manage.Add(MakeButton($"Send to tile {selectedTile} ({hours:0} h)", () =>
                    {
                        workshopMessage.text = sim.SendRobot(robotId, selectedTile) ? $"{r.Name} is driving to tile {selectedTile}." : "It can't go there now.";
                        Changed(sim);
                    }));
            }
            manage.Add(MakeButton(ownProgram.HasValue ? $"Own program: {ownProgram.Value}" : "Program: group's", () =>
            {
                w.SetRobotProgramOverride(robotId, nextOwn);
                Changed(sim);
            }));
            row.Add(manage);
            return row;
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

        static string Vitals(Robot r) =>
            $"Battery {r.Battery.StateOfCharge * 100.0:0} % · health {r.Battery.Health * 100.0:0} % " +
            $"({r.Battery.EquivalentFullCycles:0} cycles) · condition {r.Condition * 100.0:0} %";

        /// <summary>What a robot is doing, with where it is driving first (spec §10).</summary>
        static string TaskText(IPlanetSimulation sim, Robot r)
        {
            if (r.IsStranded)
                return $"Stranded on tile {r.Tile}: battery empty away from a charger. Rescue is a later feature; " +
                       "a battery yard built on this tile would charge it.";
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
                : r.Facility.Value.Slot < 0 ? "at the lander" : $"at the battery yard in slot {r.Facility.Value.Slot}";
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
                string waiting = sim.BuildStockWh <= 0.0 && sim.BuildPowerW <= 0.0 ? " · waiting for spare power" : "";
                return $"Building a {what} · tile {t.Tile}, slot {t.Slot} · {progress * 100.0:0} %{waiting}{repairNote}";
            }
            if (t.Kind == TaskKind.Survey)
                return $"Surveying tile {t.Tile} · {sim.SurveyProgress(t.Tile) * 100.0:0} %{repairNote}";
            if (t.Kind == TaskKind.Mine)
                return $"Mining {KardashevAscent.Simulation.Resources.Materials.Name((KardashevAscent.Simulation.Resources.Material)t.Slot).ToLowerInvariant()} · tile {t.Tile}{repairNote}";
            return $"Taking down a {what} · tile {t.Tile}, slot {t.Slot}{repairNote}";
        }

        static string BuildingName(BuildingType type) =>
            type == BuildingType.Battery ? "battery yard" : type == BuildingType.SolarPanel ? "solar panel" : type == BuildingType.Workshop ? "Robot Workshop" : "building";

        static string KindName(TaskKind k) => k.ToString();

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

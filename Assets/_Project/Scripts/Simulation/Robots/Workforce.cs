using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// The colony's robots and groups, and the priority automation that hands out work (spec §3–§5, phase 1).
    /// <list type="bullet">
    /// <item>New robots join the automatic group of their kind; when it is full a new one is made (design D-AO).</item>
    /// <item>Each task takes one robot. Every <see cref="Assign"/> an idle robot in a running group takes the most
    /// urgent task it can do (its own priority override, else its group's); ties go by task kind, then task order.</item>
    /// <item>Only a Critical task makes a busy robot drop what it does (spec §5); otherwise robots finish their task.</item>
    /// <item>A group can be interrupted for one temporary task; when that task is gone the group goes back to its
    /// program and re-evaluates. Pausing stops a group's robots. Neither changes the group's settings.</item>
    /// </list>
    /// </summary>
    public sealed class Workforce
    {
        /// <summary>
        /// Robots per group (design D-CAP). PLACEHOLDER value pending the group-capacity decision: it carries no
        /// balance claim. Technology will raise it (D8).
        /// </summary>
        public const int DefaultGroupCapacity = 4; // TBD (D-CAP)

        readonly List<Robot> robots = new List<Robot>();
        readonly List<RobotGroup> groups = new List<RobotGroup>();
        int nextRobotId = 1, nextGroupId = 1;
        int groupCapacity = DefaultGroupCapacity;

        public IReadOnlyList<Robot> Robots => robots;
        public IReadOnlyList<RobotGroup> Groups => groups;

        /// <summary>Most robots a group can hold (at least 1). Lowering it doesn't remove robots already in a group.</summary>
        public int GroupCapacity
        {
            get => groupCapacity;
            set => groupCapacity = Math.Max(1, value);
        }

        /// <summary>Only the Priority program works so far; the others are phase 4 placeholders.</summary>
        public static bool IsImplemented(OperatingProgram program) => program == OperatingProgram.Priority;

        public Robot GetRobot(int id)
        {
            foreach (Robot r in robots)
                if (r.Id == id) return r;
            return null;
        }

        public RobotGroup GetGroup(int id)
        {
            foreach (RobotGroup g in groups)
                if (g.Id == id) return g;
            return null;
        }

        /// <summary>The robot working on a task, or null.</summary>
        public Robot WorkerOf(WorkTask task)
        {
            foreach (Robot r in robots)
                if (r.Task.HasValue && r.Task.Value.Equals(task)) return r;
            return null;
        }

        public bool IsWorked(WorkTask task) => WorkerOf(task) != null;

        // ---- Robots and groups ----

        /// <summary>A new robot on a tile; it joins the automatic group of its kind.</summary>
        public Robot AddRobot(RobotKind kind, int tile)
        {
            var r = new Robot(nextRobotId, "R" + nextRobotId, kind, tile);
            nextRobotId++;
            robots.Add(r);
            Join(r, AutoGroupWithRoom(kind));
            return r;
        }

        /// <summary>A new manual group, empty, with the default settings.</summary>
        public RobotGroup CreateGroup(string name)
        {
            var g = new RobotGroup(nextGroupId++, string.IsNullOrWhiteSpace(name) ? "Group" : name.Trim(), false, RobotKind.General);
            groups.Add(g);
            return g;
        }

        public bool RenameGroup(int groupId, string name)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || string.IsNullOrWhiteSpace(name))
                return false;
            g.Name = name.Trim();
            return true;
        }

        /// <summary>Delete a manual group; its robots go back to the automatic groups of their kind.</summary>
        public bool DeleteGroup(int groupId)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || g.Automatic)
                return false;
            groups.Remove(g);
            foreach (int id in g.MemberIds)
            {
                Robot r = GetRobot(id);
                r.OnTemporaryTask = false;
                Join(r, AutoGroupWithRoom(r.Kind));
            }
            return true;
        }

        /// <summary>
        /// Move a robot to a manual group, or back to an automatic group of its own kind. Fails if the group is
        /// full. The robot keeps its overrides and its current task.
        /// </summary>
        public bool MoveRobot(int robotId, int groupId)
        {
            Robot r = GetRobot(robotId);
            RobotGroup to = GetGroup(groupId);
            if (r == null || to == null || r.GroupId == groupId)
                return false;
            if (to.Automatic && to.AutoKind != r.Kind)
                return false;
            if (to.Count >= groupCapacity)
                return false;
            GetGroup(r.GroupId)?.MemberIds.Remove(r.Id);
            r.OnTemporaryTask = false;
            Join(r, to);
            return true;
        }

        void Join(Robot r, RobotGroup g)
        {
            g.MemberIds.Add(r.Id);
            r.GroupId = g.Id;
            if (g.State == GroupState.Paused)
                r.Task = null;
        }

        RobotGroup AutoGroupWithRoom(RobotKind kind)
        {
            int count = 0;
            foreach (RobotGroup g in groups)
                if (g.Automatic && g.AutoKind == kind)
                {
                    count++;
                    if (g.Count < groupCapacity)
                        return g;
                }
            string name = kind + " robots" + (count > 0 ? " " + (count + 1) : "");
            var made = new RobotGroup(nextGroupId++, name, true, kind);
            groups.Add(made);
            return made;
        }

        // ---- Settings ----

        public bool SetGroupPriority(int groupId, TaskKind kind, TaskPriority priority)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null) return false;
            g.Priorities[kind] = priority;
            return true;
        }

        /// <summary>Change a group's program. Programs that aren't implemented yet are refused.</summary>
        public bool SetGroupProgram(int groupId, OperatingProgram program)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || !IsImplemented(program)) return false;
            g.Program = program;
            return true;
        }

        /// <summary>Give one robot its own priority for a kind of task (null: follow the group again).</summary>
        public bool SetRobotPriorityOverride(int robotId, TaskKind kind, TaskPriority? priority)
        {
            Robot r = GetRobot(robotId);
            if (r == null) return false;
            if (priority.HasValue) r.PriorityOverrides[kind] = priority.Value;
            else r.PriorityOverrides.Remove(kind);
            return true;
        }

        /// <summary>Give one robot its own program (null: follow the group again). Unimplemented programs are refused.</summary>
        public bool SetRobotProgramOverride(int robotId, OperatingProgram? program)
        {
            Robot r = GetRobot(robotId);
            if (r == null || (program.HasValue && !IsImplemented(program.Value))) return false;
            r.ProgramOverride = program;
            return true;
        }

        public TaskPriority EffectivePriority(Robot r, TaskKind kind) =>
            r.PriorityOverride(kind) ?? GetGroup(r.GroupId)?.PriorityOf(kind) ?? TaskPriority.Normal;

        public OperatingProgram EffectiveProgram(Robot r) =>
            r.ProgramOverride ?? GetGroup(r.GroupId)?.Program ?? OperatingProgram.Priority;

        // ---- Manual control (spec §5) ----

        /// <summary>Stop a group: its robots drop their tasks and take no new ones. Settings are kept.</summary>
        public bool Pause(int groupId)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null) return false;
            g.State = GroupState.Paused;
            g.InterruptTask = null;
            foreach (int id in g.MemberIds)
            {
                Robot r = GetRobot(id);
                r.Task = null;
                r.OnTemporaryTask = false;
            }
            return true;
        }

        /// <summary>Put a paused or interrupted group back on its program; it re-evaluates at the next assignment.</summary>
        public bool Resume(int groupId)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || g.State == GroupState.Automatic) return false;
            EndInterrupt(g);
            g.State = GroupState.Automatic;
            return true;
        }

        /// <summary>
        /// Interrupt a group for one temporary task: one of its robots that can do it takes it at the next
        /// assignment, dropping its current task. When the task is gone the group resumes its program.
        /// </summary>
        public bool Interrupt(int groupId, WorkTask task)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || g.State == GroupState.Paused) return false;
            bool able = false;
            foreach (int id in g.MemberIds)
                able |= RobotAbilities.CanDo(GetRobot(id).Kind, task.Kind);
            if (!able) return false;
            EndInterrupt(g);
            g.State = GroupState.Interrupted;
            g.InterruptTask = task;
            return true;
        }

        void EndInterrupt(RobotGroup g)
        {
            g.InterruptTask = null;
            foreach (int id in g.MemberIds)
            {
                Robot r = GetRobot(id);
                if (r.OnTemporaryTask)
                {
                    r.Task = null;
                    r.OnTemporaryTask = false;
                }
            }
        }

        // ---- Automation ----

        /// <summary>Hand out the open tasks (see the class summary). Tasks not in the list are finished or gone.</summary>
        public void Assign(IReadOnlyList<WorkTask> tasks)
        {
            var open = new Dictionary<WorkTask, WorkTask>();
            foreach (WorkTask t in tasks)
                open[t] = t;
            var taken = new HashSet<WorkTask>();

            // Interrupted groups whose temporary task is gone go back to their program.
            foreach (RobotGroup g in groups)
                if (g.State == GroupState.Interrupted && (!g.InterruptTask.HasValue || !open.ContainsKey(g.InterruptTask.Value)))
                {
                    EndInterrupt(g);
                    g.State = GroupState.Automatic;
                }

            // Drop finished tasks and the tasks of paused robots; keep the rest (with their current order).
            foreach (Robot r in robots)
            {
                if (!r.Task.HasValue)
                    continue;
                if (!open.TryGetValue(r.Task.Value, out WorkTask now) || GetGroup(r.GroupId).State == GroupState.Paused || !taken.Add(now))
                {
                    r.Task = null;
                    r.OnTemporaryTask = false;
                    continue;
                }
                r.Task = now;
            }

            // Temporary tasks of interrupted groups.
            foreach (RobotGroup g in groups)
            {
                if (g.State != GroupState.Interrupted)
                    continue;
                WorkTask t = open[g.InterruptTask.Value];
                if (taken.Contains(t))
                {
                    // Already worked on: by one of ours as the temporary task, or by another robot, which keeps it.
                    Robot w = WorkerOf(t);
                    if (w != null && w.GroupId == g.Id) w.OnTemporaryTask = true;
                    continue;
                }
                Robot pick = null;
                foreach (int id in g.MemberIds)
                {
                    Robot r = GetRobot(id);
                    if (!RobotAbilities.CanDo(r.Kind, t.Kind))
                        continue;
                    if (pick == null || Busyness(r) < Busyness(pick))
                        pick = r;
                }
                if (pick == null)
                    continue;
                if (pick.Task.HasValue) taken.Remove(pick.Task.Value);
                pick.Task = t;
                pick.OnTemporaryTask = true;
                taken.Add(t);
            }

            // Priority program: idle robots take the most urgent task; Critical tasks pull busy robots off theirs.
            bool changed = true;
            for (int pass = 0; changed && pass <= robots.Count; pass++)
            {
                changed = false;
                foreach (Robot r in robots)
                {
                    if (r.OnTemporaryTask || GetGroup(r.GroupId).State == GroupState.Paused)
                        continue;
                    WorkTask? best = BestFor(r, open.Values, taken);
                    if (!best.HasValue)
                        continue;
                    if (r.Task.HasValue)
                    {
                        if (EffectivePriority(r, best.Value.Kind) != TaskPriority.Critical
                            || EffectivePriority(r, r.Task.Value.Kind) == TaskPriority.Critical)
                            continue;
                        taken.Remove(r.Task.Value);
                    }
                    r.Task = best;
                    taken.Add(best.Value);
                    changed = true;
                }
            }
        }

        /// <summary>How reluctant a robot is to drop what it does: idle 0, else 1 + how urgent its task is.</summary>
        int Busyness(Robot r) => r.Task.HasValue ? 1 + (int)TaskPriority.Disabled - (int)EffectivePriority(r, r.Task.Value.Kind) : 0;

        WorkTask? BestFor(Robot r, IEnumerable<WorkTask> open, HashSet<WorkTask> taken)
        {
            WorkTask? best = null;
            TaskPriority bestP = TaskPriority.Disabled;
            foreach (WorkTask t in open)
            {
                if (taken.Contains(t) || !RobotAbilities.CanDo(r.Kind, t.Kind))
                    continue;
                TaskPriority p = EffectivePriority(r, t.Kind);
                if (p == TaskPriority.Disabled)
                    continue;
                if (!best.HasValue || p < bestP
                    || (p == bestP && (t.Kind < best.Value.Kind || (t.Kind == best.Value.Kind && t.Order < best.Value.Order))))
                {
                    best = t;
                    bestP = p;
                }
            }
            return best;
        }

        // ---- Saving ----

        public WorkforceState Capture()
        {
            var s = new WorkforceState { NextRobotId = nextRobotId, NextGroupId = nextGroupId, GroupCapacity = groupCapacity };
            foreach (RobotGroup g in groups)
            {
                var gs = new GroupRecord
                {
                    Id = g.Id, Name = g.Name, Automatic = g.Automatic, AutoKind = g.AutoKind, Program = g.Program, State = g.State,
                    HasInterruptTask = g.InterruptTask.HasValue,
                };
                if (g.InterruptTask.HasValue)
                {
                    gs.InterruptKind = g.InterruptTask.Value.Kind;
                    gs.InterruptTile = g.InterruptTask.Value.Tile;
                    gs.InterruptSlot = g.InterruptTask.Value.Slot;
                }
                foreach (var kv in g.Priorities)
                    gs.Priorities.Add(new PriorityRecord { Kind = kv.Key, Priority = kv.Value });
                s.Groups.Add(gs);
            }
            foreach (Robot r in robots)
            {
                var rs = new RobotRecord
                {
                    Id = r.Id, Name = r.Name, Kind = r.Kind, GroupId = r.GroupId, Tile = r.Tile,
                    HasTask = r.Task.HasValue, OnTemporaryTask = r.OnTemporaryTask,
                    HasProgramOverride = r.ProgramOverride.HasValue,
                    ProgramOverride = r.ProgramOverride ?? OperatingProgram.Priority,
                };
                if (r.Task.HasValue)
                {
                    rs.TaskKind = r.Task.Value.Kind;
                    rs.TaskTile = r.Task.Value.Tile;
                    rs.TaskSlot = r.Task.Value.Slot;
                }
                foreach (var kv in r.PriorityOverrides)
                    rs.PriorityOverrides.Add(new PriorityRecord { Kind = kv.Key, Priority = kv.Value });
                s.Robots.Add(rs);
            }
            return s;
        }

        /// <summary>Replace everything with a saved workforce. Returns false (and changes nothing) if it is unusable.</summary>
        public bool Restore(WorkforceState s)
        {
            if (s == null || s.Robots == null || s.Groups == null || s.Robots.Count == 0)
                return false;
            var newGroups = new List<RobotGroup>();
            foreach (GroupRecord gs in s.Groups)
            {
                var g = new RobotGroup(gs.Id, gs.Name, gs.Automatic, gs.AutoKind)
                {
                    Program = IsImplemented(gs.Program) ? gs.Program : OperatingProgram.Priority,
                    State = gs.State,
                    InterruptTask = gs.HasInterruptTask ? new WorkTask(gs.InterruptKind, gs.InterruptTile, gs.InterruptSlot) : (WorkTask?)null,
                };
                if (g.State == GroupState.Interrupted && !g.InterruptTask.HasValue)
                    g.State = GroupState.Automatic;
                if (gs.Priorities != null)
                    foreach (PriorityRecord p in gs.Priorities)
                        g.Priorities[p.Kind] = p.Priority;
                newGroups.Add(g);
            }
            groups.Clear();
            groups.AddRange(newGroups);
            robots.Clear();
            foreach (RobotRecord rs in s.Robots)
            {
                var r = new Robot(rs.Id, rs.Name, rs.Kind, rs.Tile)
                {
                    Task = rs.HasTask ? new WorkTask(rs.TaskKind, rs.TaskTile, rs.TaskSlot) : (WorkTask?)null,
                    OnTemporaryTask = rs.HasTask && rs.OnTemporaryTask,
                    ProgramOverride = rs.HasProgramOverride && IsImplemented(rs.ProgramOverride) ? rs.ProgramOverride : (OperatingProgram?)null,
                };
                if (rs.PriorityOverrides != null)
                    foreach (PriorityRecord p in rs.PriorityOverrides)
                        r.PriorityOverrides[p.Kind] = p.Priority;
                robots.Add(r);
                RobotGroup g = GetGroup(rs.GroupId);
                if (g != null) { g.MemberIds.Add(r.Id); r.GroupId = g.Id; }
                else Join(r, AutoGroupWithRoom(r.Kind));
            }
            nextRobotId = Math.Max(s.NextRobotId, MaxId(robots) + 1);
            nextGroupId = Math.Max(s.NextGroupId, MaxGroupId() + 1);
            groupCapacity = Math.Max(1, s.GroupCapacity > 0 ? s.GroupCapacity : DefaultGroupCapacity);
            return true;
        }

        /// <summary>Back to the start of a colony: one General robot at <paramref name="tile"/> (spec: starting_units 1).</summary>
        public void Reset(int tile)
        {
            robots.Clear();
            groups.Clear();
            nextRobotId = nextGroupId = 1;
            groupCapacity = DefaultGroupCapacity;
            AddRobot(RobotKind.General, tile);
        }

        static int MaxId(List<Robot> list)
        {
            int m = 0;
            foreach (Robot r in list) m = Math.Max(m, r.Id);
            return m;
        }

        int MaxGroupId()
        {
            int m = 0;
            foreach (RobotGroup g in groups) m = Math.Max(m, g.Id);
            return m;
        }
    }
}

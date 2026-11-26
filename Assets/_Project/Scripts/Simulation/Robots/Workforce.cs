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

        /// <summary>Every program is implemented (phase 4).</summary>
        public static bool IsImplemented(OperatingProgram program) => Enum.IsDefined(typeof(OperatingProgram), program);

        // ---- Stage B: task records, reservations, reasons ----

        /// <summary>The task monitor: every open task's state and reason, and recently finished tasks.</summary>
        public TaskBook Tasks { get; } = new TaskBook();

        readonly List<AssignmentChange> changes = new List<AssignmentChange>();
        public const int ChangesKept = 50;

        /// <summary>Recent assignment changes with their reasons, oldest first (spec v2 §4: no silent reassignment).</summary>
        public IReadOnlyList<AssignmentChange> Changes => changes;

        /// <summary>Game time, hours, for the records (set by the colony before each assignment).</summary>
        public double NowHours { get; set; }

        /// <summary>A task has been blocked too long and asks for assistance (the player is told).</summary>
        public event Action<TaskRecord> AssistanceNeeded;

        void Log(Robot r, WorkTask? from, WorkTask? to, string reason)
        {
            changes.Add(new AssignmentChange(NowHours, r.Id, from, to, reason));
            if (changes.Count > ChangesKept) changes.RemoveAt(0);
        }

        /// <summary>A robot leaves its task to look after itself: it remembers the task to go back to.</summary>
        internal void LeftTask(Robot r, string why)
        {
            if (!r.Task.HasValue)
                return;
            if (!r.OnTemporaryTask)
                r.ResumeTask = r.Task;
            Log(r, r.Task, null, why);
        }

        /// <summary>
        /// Reserve a task for a robot (spec v2 §4): it takes it as soon as it is free, before any automatic choice, and
        /// no other robot takes it automatically. One robot per task: an older reservation of the same task is dropped.
        /// </summary>
        public bool Reserve(int robotId, WorkTask task)
        {
            Robot r = GetRobot(robotId);
            if (r == null || !Capable(r, task.Kind))
                return false;
            foreach (Robot other in robots)
                if (other != r && other.ReservedTask.HasValue && other.ReservedTask.Value.Equals(task))
                    other.ReservedTask = null;
            r.ReservedTask = task;
            return true;
        }

        public bool Unreserve(int robotId)
        {
            Robot r = GetRobot(robotId);
            if (r == null || !r.ReservedTask.HasValue)
                return false;
            r.ReservedTask = null;
            return true;
        }

        public Robot ReservedRobotFor(WorkTask task)
        {
            foreach (Robot r in robots)
                if (r.ReservedTask.HasValue && r.ReservedTask.Value.Equals(task)) return r;
            return null;
        }

        bool Paused(Robot r) => GetGroup(r.GroupId).State == GroupState.Paused;

        /// <summary>Milestones reached: they unlock secondary capabilities in <see cref="RobotCatalog"/>.</summary>
        public int Milestones { get; set; }

        bool Capable(Robot r, TaskKind kind) => RobotCatalog.CanDo(r.Kind, kind, Milestones);

        /// <summary>Critical operational damage (spec v2 §7): the robot shuts down until it is repaired at the lander.</summary>
        public bool DamageCritically(int robotId)
        {
            Robot r = GetRobot(robotId);
            if (r == null) return false;
            r.CriticallyDamaged = true;
            r.Task = null;
            r.OnTemporaryTask = false;
            return true;
        }

        /// <summary>The colony's needs at the last assignment (for the Demand program).</summary>
        public ColonyNeeds Needs { get; private set; }

        /// <summary>
        /// Routes and chargers for driving (spec §10). Null: robots don't move (they work any tile from where they
        /// are), as before movement existed.
        /// </summary>
        public Movement Movement { get; set; }

        /// <summary>Is a robot on the task at its tile and not driving (only then does the work count)?</summary>
        public bool IsWorkedOnSite(WorkTask task)
        {
            foreach (Robot r in robots)
                if (r.Task.HasValue && r.Task.Value.Equals(task) && OnSite(r, task)) return true;
            return false;
        }

        bool OnSite(Robot r, WorkTask task) => Movement == null || (!r.IsTravelling && r.Tile == task.Tile);

        /// <summary>The robots on a task (a facility can have several, spec v2 §5).</summary>
        public List<Robot> WorkersOf(WorkTask task)
        {
            var list = new List<Robot>();
            foreach (Robot r in robots)
                if (r.Task.HasValue && r.Task.Value.Equals(task)) list.Add(r);
            return list;
        }

        /// <summary>How many robots a task can take at once (spec v2 §5); null: one each.</summary>
        public Func<WorkTask, int> CapacityOf { get; set; }

        int Capacity(WorkTask t) => CapacityOf != null ? Math.Max(1, CapacityOf(t)) : 1;

        /// <summary>Robots on each task during an assignment; a task is "taken" when it is full.</summary>
        sealed class Staffing
        {
            readonly Dictionary<WorkTask, int> count = new Dictionary<WorkTask, int>();
            readonly Workforce owner;

            public Staffing(Workforce owner) => this.owner = owner;

            public int Of(WorkTask t) => count.TryGetValue(t, out int n) ? n : 0;
            public bool Contains(WorkTask t) => Of(t) >= owner.Capacity(t);
            public bool Has(WorkTask t) => Of(t) > 0;
            public void Add(WorkTask t) => count[t] = Of(t) + 1;
            public void Remove(WorkTask t)
            {
                if (Of(t) > 0) count[t] = Of(t) - 1;
            }
        }

        /// <summary>Free a place on a full task for <paramref name="incoming"/>: the least suited automatic worker lets go.</summary>
        void MakeRoom(WorkTask t, Robot incoming, Staffing taken, string reason)
        {
            Robot leave = null;
            double worst = double.PositiveInfinity;
            foreach (Robot w in WorkersOf(t))
            {
                if (w == incoming || w.OnTemporaryTask || (w.ReservedTask.HasValue && w.ReservedTask.Value.Equals(t)))
                    continue;
                double p = RobotCatalog.Productivity(w, t.Kind, Milestones);
                if (leave == null || p < worst - 1e-12 || (Math.Abs(p - worst) <= 1e-12 && w.Id > leave.Id))
                {
                    leave = w;
                    worst = p;
                }
            }
            if (leave == null)
                return;
            Log(leave, t, null, reason);
            leave.Task = null;
            taken.Remove(t);
        }

        /// <summary>Send a robot to a tile (hybrid movement): it drops its task and drives there, then works from there.</summary>
        /// <summary>Debugging: when set, every order that sets or clears a watched robot's target or route reports here (tests only).</summary>
        public static Action<string> DebugTrace; public static int DebugWatch = -1;
        internal static void Trace(Robot r, string what) { if (DebugTrace != null && r.Id == DebugWatch) DebugTrace($"R{r.Id} tile={r.Tile} route={r.Route.Count} manual={r.ManualTarget} act={r.Activity} {what} | " + string.Join(" < ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(new System.Diagnostics.StackTrace(2, false).GetFrames(), 4), f => f.GetMethod().Name))); }

        public bool SendTo(int robotId, int tile)
        {
            Robot r = GetRobot(robotId);
            if (r == null || Movement == null || r.Tile == tile || r.IsShutDown || Movement.RoutesFor(r).Route(r.Tile, tile) == null)
                return false;
            // A robot on charge goes when you send it, if it has the charge to get there and back to a charger
            // keeping 20 % (director 2026-11-14: robots must answer orders); in maintenance it stays.
            if (r.Activity == RobotActivity.Charging)
            {
                if (!InRange(r, new WorkTask(TaskKind.Survey, tile, -1)))
                    return false;
                r.Activity = RobotActivity.None;
                r.Facility = null;
                r.TripCharge = 0.0;
            }
            else if (r.Activity != RobotActivity.None)
                return false;
            r.Task = null;
            r.OnTemporaryTask = false;
            r.ManualTarget = tile;
            Trace(r, $"SendTo {tile}");
            return true;
        }

        /// <summary>Start driving to <paramref name="tile"/> (or stop, if it is there).</summary>
        void Head(Robot r, int tile)
        {
            if (Movement == null)
                return;
            if (r.Tile == tile)
            {
                r.Route.Clear();
                r.HopProgress = 0.0;
                return;
            }
            if (r.Destination == tile)
                return;
            Trace(r, $"Head {tile}");
            List<int> route = Movement.RoutesFor(r).Route(r.Tile, tile);
            r.Route.Clear();
            r.HopProgress = 0.0;
            if (route != null)
                r.Route.AddRange(route);
        }

        /// <summary>The numbers behind <see cref="InRange"/> for a trip to a tile (tests and the Why? chain).</summary>
        public string RangeReport(Robot r, int tile)
        {
            if (Movement == null) return "no movement";
            double speed = r.Attribute(RobotAttribute.Speed);
            double go = Movement.RoutesFor(r).HoursBetween(r.Tile, tile) / speed, back = Movement.HoursToChargerFor(r)[tile] / speed;
            double heater = OutdoorTripHeater != null ? OutdoorTripHeater(r, tile, 1.0).Wh : 0.0;
            return $"go {go:0.0} h, back {back:0.0} h, drive {Movement.EnergyWh(go + back):0} Wh, heater {heater:0} W, climb {Movement.TripClimbWh(r, r.Tile, tile):0} Wh, have {r.Battery.UsableWh:0} Wh, cap {r.Battery.CapacityWh:0}, cold {r.Battery.ColdFactor:0.00}, speed {speed:0.00}";
        }

        /// <summary>Can it get to a task's tile and back to a charger, keeping 20 %? (Always true without movement.)</summary>
        bool InRange(Robot r, WorkTask t)
        {
            if (Movement == null)
                return true;
            // KI-46 at dispatch (decided 2026-11-26): a haul not yet picked up needs the battery for the whole first segment from where the robot is now: the empty leg to the pickup, the loaded leg
            // to the next charger or shelter (or the drop and the way back when there is none), at the robot's current charge and the body temperature on the way
            if (t.Kind == TaskKind.Haul && HaulSegmentNeed != null && Movement.HasCharger)   // with no charger anywhere there is nothing to keep charge for (as for other tasks)
            {
                var need = HaulSegmentNeed(r, t);
                if (need.HasValue)
                {
                    double coldLock = r.Battery.CapacityWh * Math.Max(0.0, r.Battery.ColdFactor - RobotBattery.ColdCapacityFactor(need.Value.MinC));
                    double reserve = RobotSpecs.ReturnToChargeFraction * r.Battery.CapacityWh * RobotBattery.ColdCapacityFactor(need.Value.MinC);
                    if (need.Value.Wh * HaulDispatchMargin > r.Battery.UsableWh - coldLock - reserve + 1e-9) return false;
                }
            }
            double speed = r.Attribute(RobotAttribute.Speed);
            double go = Movement.RoutesFor(r).HoursBetween(r.Tile, t.Tile) / speed;
            if (!Movement.HasCharger)
                // No charger anywhere: nothing to keep charge for; it goes while it has charge to get there.
                return !double.IsInfinity(go) && Movement.EnergyWh(go) <= r.Battery.UsableWh + 1e-9;
            double back = Movement.HoursToChargerFor(r)[t.Tile] / speed;
            if (double.IsInfinity(go) || double.IsInfinity(back))
                return false;
            // KI-46 for every outdoor task, not only hauls: the heater and the cold battery are part of the trip (see TripChargeFor)
            var (heaterWh, cold) = OutdoorTrip(r, t.Tile, go + back);
            double usableCapacity = Math.Min(r.Battery.ColdFactor, cold) * r.Battery.CapacityWh;
            // the energy it holds counts only up to what the cold battery will give
            double have = Math.Max(0.0, r.Battery.UsableWh - r.Battery.CapacityWh * Math.Max(0.0, r.Battery.ColdFactor - cold));   // energy in a warm battery is partly locked once it has cooled outdoors
            double spare = have - RobotSpecs.ReturnToChargeFraction * usableCapacity;
            if (Movement.EnergyWh(go + back) / r.Attribute(RobotAttribute.EnergyEfficiency) + Movement.TripClimbWh(r, r.Tile, t.Tile) + heaterWh <= spare + 1e-9)
                return true;
            // the only exception to the round-trip rule: a one-way trip to an outpost whose own panels will charge the robot (see PlanetSimulation.Outposts)
            return t.Kind == TaskKind.Build && OneWayOutpostOk != null && OneWayOutpostOk(r, t.Tile);
        }

        // ---- Phase 4 settings ----

        /// <summary>Facility program: work only on this tile (null: anywhere).</summary>
        public bool SetGroupFacility(int groupId, int? tile)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null) return false;
            g.FacilityTile = tile;
            return true;
        }

        /// <summary>Move a kind of task one place earlier in the group's own order (spec §5 ranked ordering).</summary>
        public bool RaiseInRanking(int groupId, TaskKind kind)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null) return false;
            List<TaskKind> order = g.RankedKinds();
            int i = order.IndexOf(kind);
            if (i > 0)
            {
                order.RemoveAt(i);
                order.Insert(i - 1, kind);
            }
            g.Ranking = order;
            return true;
        }

        /// <summary>Back to the default order.</summary>
        public bool ResetRanking(int groupId)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null) return false;
            g.Ranking = null;
            return true;
        }

        /// <summary>Add a task to the end of a group's queue (Task queue program). False if it is already queued.</summary>
        public bool Enqueue(int groupId, WorkTask task)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || g.QueueItems.Contains(task)) return false;
            g.QueueItems.Add(task);
            return true;
        }

        public bool RemoveFromQueue(int groupId, int index)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || index < 0 || index >= g.QueueItems.Count) return false;
            g.QueueItems.RemoveAt(index);
            return true;
        }

        // ---- Colony network (stage E) ----

        /// <summary>The network, or null when not modelled (tests without one: every robot is connected).</summary>
        public ColonyNetwork Network { get; set; }

        /// <summary>Robots that lost or regained the network this check (robot, connected now).</summary>
        public event System.Action<Robot, bool> ConnectionChanged;

        /// <summary>
        /// Check every robot against the network: a connected robot syncs its rules (and anything it found);
        /// a robot that drops out keeps the rules it last synced. Call once per tick, after movement.
        /// </summary>
        public void UpdateConnections(double hours)
        {
            foreach (Robot r in robots)
            {
                r.ExchangeVia = null;
                bool now = Network == null || Network.IsConnected(r.Tile);
                if (now)
                {
                    Sync(r);
                    if (!r.Connected)
                    {
                        r.Connected = true;
                        r.HoursOffline = 0.0;
                        ConnectionChanged?.Invoke(r, true);
                    }
                }
                else
                {
                    if (r.Connected)
                    {
                        if (r.CachedPriorities.Count == 0)
                            Sync(r);   // never synced (a new robot): it starts with what it was made with
                        r.Connected = false;
                        ConnectionChanged?.Invoke(r, false);
                    }
                    r.HoursOffline += hours;
                }
            }
            // Nearby exchange (spec: robots talk directly during outages): a cut-off robot near a connected one syncs
            // its rules through it and hands over what it found. It stays off the network itself.
            foreach (Robot r in robots)
            {
                if (r.Connected || Network == null)
                    continue;
                int range = ExchangeRange(EffectiveAILevel(r));
                foreach (Robot o in robots)
                    if (o.Connected && o != r && Network.Within(r.Tile, o.Tile, range))
                    {
                        Sync(r);
                        r.ExchangeVia = o.Id;
                        break;
                    }
            }
        }

        // ---- AI levels and colony computing (stage E) ----

        /// <summary>
        /// False while colony computing is cut (a power shortage): robots then fall back to AI level 1. Basic autonomy
        /// never needs it. Set by the orchestrator each tick.
        /// </summary>
        public bool ComputingAvailable { get; set; } = true;

        /// <summary>The AI level a robot works at now: its processors', or 1 while colony computing is cut.</summary>
        public int EffectiveAILevel(Robot r) => ComputingAvailable ? r.AILevel : 1;

        /// <summary>How far a cut-off robot reaches another robot directly, tiles: 1, or 2 at AI level 3. TBD.</summary>
        public static int ExchangeRange(int aiLevel) => aiLevel >= 3 ? 2 : 1;

        /// <summary>Can this robot hand in findings now: on the network, or via a robot nearby.</summary>
        public bool CanSync(Robot r) => r.Connected || r.ExchangeVia.HasValue;

        /// <summary>Colony computing the robots need, W: each AI level above 1 adds a share. TBD.</summary>
        public const double ComputingWPerLevel = 300.0;

        public double ComputingRequestW
        {
            get
            {
                double w = 0.0;
                foreach (Robot r in robots)
                    w += (r.AILevel - 1) * ComputingWPerLevel;
                return w;
            }
        }

        /// <summary>A connected robot takes the live program and priorities, and hands over what it found.</summary>
        void Sync(Robot r)
        {
            r.CachedProgram = EffectiveProgram(r);
            foreach (TaskKind k in (TaskKind[])System.Enum.GetValues(typeof(TaskKind)))
                r.CachedPriorities[k] = EffectivePriority(r, k);
            r.CachedNeeds = Needs;
        }

        /// <summary>How many robots are cut off now.</summary>
        public int DisconnectedCount
        {
            get
            {
                int n = 0;
                foreach (Robot r in robots) if (!r.Connected) n++;
                return n;
            }
        }

        /// <summary>
        /// The priority a robot gives a kind of task under its program: its own or its group's priority, except
        /// under Demand, where the colony's needs decide (a Disabled kind stays disabled).
        /// A cut-off robot follows the rules it last synced, and can't react to the colony's needs (it doesn't
        /// hear about them): under a cached Demand program it works at Normal priority.
        /// </summary>
        public TaskPriority ProgramPriority(Robot r, TaskKind kind)
        {
            if (!r.Connected)
            {
                TaskPriority cached = r.CachedPriorities.TryGetValue(kind, out TaskPriority c) ? c : TaskPriority.Normal;
                if (r.CachedProgram != OperatingProgram.Demand || cached == TaskPriority.Disabled)
                    return cached;
                // AI level 2+: keeps working by the colony's needs as it last knew them. Level 1 has no notion of them.
                if (EffectiveAILevel(r) >= 2 && kind == TaskKind.Build && (r.CachedNeeds.BrownoutRisk || r.CachedNeeds.SitesWaiting))
                    return TaskPriority.High;
                return kind == TaskKind.Mine ? TaskPriority.Low : TaskPriority.Normal;
            }
            TaskPriority p = EffectivePriority(r, kind);
            if (EffectiveProgram(r) != OperatingProgram.Demand || p == TaskPriority.Disabled)
                return p;
            if (kind == TaskKind.Build && (Needs.BrownoutRisk || Needs.SitesWaiting))
                return TaskPriority.High;
            return kind == TaskKind.Mine ? TaskPriority.Low : TaskPriority.Normal;
        }

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

        /// <summary>A repeating task (a mining batch) finished one round: its robot lets go and re-evaluates.</summary>
        public void Release(WorkTask task)
        {
            foreach (Robot r in robots)
                if (r.Task.HasValue && r.Task.Value.Equals(task))
                    r.Task = null;
        }

        // ---- Robots and groups ----

        /// <summary>A new robot on a tile; it joins the automatic group of its kind.</summary>
        /// <summary>A destroyed robot leaves the colony: its place in its group and the task it held go back to the list. False when there is no such robot.</summary>
        public bool RemoveRobot(int robotId)
        {
            Robot r = GetRobot(robotId);
            if (r == null) return false;
            if (r.Task.HasValue) Log(r, r.Task, null, "destroyed");
            GetGroup(r.GroupId)?.MemberIds.Remove(r.Id);
            robots.Remove(r);
            return true;
        }

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

        /// <summary>A group's maintenance rule (spec v2 §8): repair below this condition (0.05–0.9).</summary>
        public bool SetGroupMaintenance(int groupId, double belowCondition)
        {
            RobotGroup g = GetGroup(groupId);
            if (g == null || belowCondition < 0.05 || belowCondition > 0.9) return false;
            g.MaintenanceBelow = belowCondition;
            return true;
        }

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
                able |= Capable(GetRobot(id), task.Kind);
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
        public void Assign(IReadOnlyList<WorkTask> tasks) => Assign(tasks, default);

        /// <summary>Hand out the open tasks, with the colony's needs for the Demand program.</summary>
        public void Assign(IReadOnlyList<WorkTask> tasks, ColonyNeeds needs)
        {
            Needs = needs;
            var open = new Dictionary<WorkTask, WorkTask>();
            foreach (WorkTask t in tasks)
                open[t] = t;
            var taken = new Staffing(this);

            // Queued tasks that are done (no longer open) leave the queues.
            foreach (RobotGroup g in groups)
                g.QueueItems.RemoveAll(q => !open.ContainsKey(q));

            // Robots the player sent somewhere drive there first.
            foreach (Robot r in robots)
            {
                if (!r.ManualTarget.HasValue)
                    continue;
                if (Movement == null || r.Tile == r.ManualTarget.Value || r.Activity != RobotActivity.None)
                {
                    r.ManualTarget = null;
                    continue;
                }
                r.Task = null;
                Head(r, r.ManualTarget.Value);
                if (!r.IsTravelling)
                    r.ManualTarget = null;   // no way there
            }

            // Interrupted groups whose temporary task is gone go back to their program.
            foreach (RobotGroup g in groups)
                if (g.State == GroupState.Interrupted && (!g.InterruptTask.HasValue || !open.ContainsKey(g.InterruptTask.Value)))
                {
                    EndInterrupt(g);
                    g.State = GroupState.Automatic;
                }

            // Drop finished tasks and the tasks of robots that can't go on, saying why; keep the rest.
            foreach (Robot r in robots)
            {
                if (!r.Task.HasValue)
                    continue;
                WorkTask was = r.Task.Value;
                bool isOpen = open.TryGetValue(was, out WorkTask now);
                string why = !isOpen ? null
                    : !r.Available ? (r.IsShutDown ? "shut down" : r.ManualTarget.HasValue ? "sent elsewhere by you"
                                      : r.Activity == RobotActivity.Maintenance ? "went for maintenance" : "went to charge")
                    : Paused(r) ? "group paused"
                    : taken.Contains(now) ? (Capacity(now) > 1 ? "the facility is full" : "another robot already has it")
                    : null;
                if (!isOpen || why != null)
                {
                    if (why != null)
                    {
                        if (!r.Available && !r.OnTemporaryTask && !r.ManualTarget.HasValue)
                            r.ResumeTask = was;
                        Log(r, was, null, why);
                    }
                    r.Task = null;
                    r.OnTemporaryTask = false;
                    continue;
                }
                taken.Add(now);
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
                    if (!r.Available || !Capable(r, t.Kind))
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

            // Manual reservations come first (spec v2 §4); a robot doing a reserved task for someone else lets go, with the reason.
            var reservedTasks = new HashSet<WorkTask>();
            foreach (Robot r in robots)
            {
                if (!r.ReservedTask.HasValue)
                    continue;
                if (!open.TryGetValue(r.ReservedTask.Value, out WorkTask rt))
                {
                    r.ReservedTask = null;   // done or gone
                    continue;
                }
                reservedTasks.Add(rt);
                if (!r.Available || r.OnTemporaryTask || Paused(r) || (r.Task.HasValue && r.Task.Value.Equals(rt)))
                    continue;
                if (taken.Contains(rt))
                    MakeRoom(rt, r, taken, $"reserved for {r.Name} by you");
                if (taken.Contains(rt))
                    continue;   // everyone on it is reserved or temporary: wait
                if (r.Task.HasValue) taken.Remove(r.Task.Value);
                Log(r, r.Task, rt, "your reservation");
                r.Task = rt;
                r.ResumeTask = null;
                taken.Add(rt);
            }

            // Robots back from charging or maintenance return to the task they left; a stand-in lets go (spec v2 §4).
            foreach (Robot r in robots)
            {
                if (!r.ResumeTask.HasValue || !r.Available || r.OnTemporaryTask || Paused(r) || r.ReservedTask.HasValue)
                    continue;
                WorkTask want = r.ResumeTask.Value;
                r.ResumeTask = null;
                if (r.Task.HasValue || !open.TryGetValue(want, out WorkTask back) || reservedTasks.Contains(back)
                    || !Capable(r, back.Kind) || !InRange(r, back))
                    continue;
                if (taken.Contains(back))
                    MakeRoom(back, r, taken, $"{r.Name} came back to it");
                if (taken.Contains(back))
                    continue;
                r.Task = back;
                taken.Add(back);
                Log(r, null, back, "back to its task after charging or maintenance");
            }

            // Task queue robots take the first open task on their list.
            foreach (Robot r in robots)
            {
                if (!IsFree(r) || EffectiveProgram(r) != OperatingProgram.TaskQueue)
                    continue;
                WorkTask? q = BestFor(r, open.Values, taken, reservedTasks);
                if (!q.HasValue)
                    continue;
                r.Task = q;
                taken.Add(q.Value);
            }

            // Everyone else: the best robot–task pairs first (priority, the group's order, the robot best suited, the
            // nearest, the fullest battery, then fixed tie-breaks), so specialists get their work and nobody waits idle.
            HandOut(open, taken, reservedTasks);

            // Critical tasks pull a busy robot off a task that may be interrupted (spec v2 §3); the freed task is handed on.
            bool changed = true;
            for (int pass = 0; changed && pass <= robots.Count; pass++)
            {
                changed = false;
                foreach (WorkTask t in open.Values)
                {
                    if (taken.Has(t) || reservedTasks.Contains(t))
                        continue;
                    Robot pick = null;
                    Score pickScore = default;
                    foreach (Robot r in robots)
                    {
                        if (!r.Available || !r.Task.HasValue || r.OnTemporaryTask || Paused(r) || r.ReservedTask.HasValue
                            || EffectiveProgram(r) == OperatingProgram.TaskQueue)
                            continue;
                        if (ProgramPriority(r, t.Kind) != TaskPriority.Critical || ProgramPriority(r, r.Task.Value.Kind) == TaskPriority.Critical)
                            continue;
                        if (!TaskPolicy.For(r.Task.Value.Kind).MayBeInterrupted || !Eligible(r, t))
                            continue;
                        Score s = ScoreOf(r, t, 0);
                        if (pick == null || s.CompareTo(pickScore) < 0)
                        {
                            pick = r;
                            pickScore = s;
                        }
                    }
                    if (pick == null)
                        continue;
                    WorkTask old = pick.Task.Value;
                    taken.Remove(old);
                    Log(pick, old, t, "a critical task");
                    pick.Task = t;
                    taken.Add(t);
                    changed = true;
                }
                if (changed)
                    HandOut(open, taken, reservedTasks);
            }

            PlanTripCharges(open);
            UpdateBook(open);

            // Drive to the task; robots with nothing to do stop where they are.
            if (Movement != null)
                foreach (Robot r in robots)
                {
                    if (r.Activity != RobotActivity.None || r.ManualTarget.HasValue || r.IsShutDown)
                        continue;
                    if (r.Task.HasValue)
                        Head(r, r.Task.Value.Tile);
                    else if (r.IsTravelling)
                    {
                        r.Route.Clear();
                        r.HopProgress = 0.0;
                    }
                }
        }

        /// <summary>Free for automatic work: available, idle, not on a temporary task, running, not waiting for a reservation.</summary>
        bool IsFree(Robot r) => r.Available && !r.Task.HasValue && !r.OnTemporaryTask && !Paused(r) && !r.ReservedTask.HasValue;

        /// <summary>Can this robot take this task by itself (compatibility, work area, range, not disabled)?</summary>
        bool Eligible(Robot r, WorkTask t)
        {
            if (!Capable(r, t.Kind) || !InRange(r, t) || ProgramPriority(r, t.Kind) == TaskPriority.Disabled)
                return false;
            RobotGroup g = GetGroup(r.GroupId);
            return !(EffectiveProgram(r) == OperatingProgram.Facility && g.FacilityTile.HasValue && t.Tile != g.FacilityTile.Value);
        }

        /// <summary>Ordering of robot–task pairs (lower is better); every tie is broken, so results are deterministic.</summary>
        readonly struct Score : IComparable<Score>
        {
            readonly int priority, rank, staffed, order, robot;
            readonly double suitability, hours, charge;

            public Score(int priority, int rank, int staffed, double suitability, double hours, double charge, int order, int robot)
            {
                this.priority = priority; this.rank = rank; this.staffed = staffed; this.suitability = suitability; this.hours = hours;
                this.charge = charge; this.order = order; this.robot = robot;
            }

            /// <summary>The same pair with the task's crew at <paramref name="n"/>.</summary>
            public Score WithStaffed(int n) => new Score(priority, rank, n, suitability, hours, charge, order, robot);

            public int CompareTo(Score o)
            {
                if (priority != o.priority) return priority.CompareTo(o.priority);
                // Every task of the same priority gets one robot before any gets a second (spec v2 §4 balanced allocation).
                if ((staffed > 0) != (o.staffed > 0)) return staffed > 0 ? 1 : -1;
                if (rank != o.rank) return rank.CompareTo(o.rank);
                if (staffed != o.staffed) return staffed.CompareTo(o.staffed);
                if (Math.Abs(suitability - o.suitability) > 1e-9) return o.suitability.CompareTo(suitability);   // better suited first
                if (Math.Abs(hours - o.hours) > 1e-9) return hours.CompareTo(o.hours);                           // nearer first
                if (Math.Abs(charge - o.charge) > 1e-9) return o.charge.CompareTo(charge);                       // fuller battery first
                if (order != o.order) return order.CompareTo(o.order);
                return robot.CompareTo(o.robot);
            }
        }

        Score ScoreOf(Robot r, WorkTask t, int staffed)
        {
            double hours = Movement != null ? Movement.RoutesFor(r).HoursBetween(r.Tile, t.Tile) / r.Attribute(RobotAttribute.Speed) : 0.0;
            return new Score((int)ProgramPriority(r, t.Kind), GetGroup(r.GroupId).RankOf(t.Kind), staffed,
                             RobotCatalog.Productivity(r, t.Kind, Milestones), hours, r.Battery.StateOfCharge, t.Order, r.Id);
        }

        /// <summary>Give free robots work, best pair first, until no free robot can take an open task.</summary>
        void HandOut(Dictionary<WorkTask, WorkTask> open, Staffing taken, HashSet<WorkTask> reservedTasks)
        {
            // Every free robot's eligible tasks, scored once: only the crews change while pairs are handed out, so a
            // pair's score is its base score with the task's crew now (the same order as scoring each round afresh).
            var pairs = new List<(Robot R, WorkTask T, Score S)>();
            foreach (Robot r in robots)
            {
                if (!IsFree(r) || EffectiveProgram(r) == OperatingProgram.TaskQueue)
                    continue;
                foreach (WorkTask t in open.Values)
                    if (!reservedTasks.Contains(t) && Eligible(r, t))
                        pairs.Add((r, t, ScoreOf(r, t, 0)));
            }
            while (true)
            {
                int bestI = -1;
                Score best = default;
                for (int i = 0; i < pairs.Count; i++)
                {
                    var (r, t, baseScore) = pairs[i];
                    if (r.Task.HasValue || taken.Contains(t))
                        continue;
                    Score s = baseScore.WithStaffed(taken.Of(t));
                    if (bestI < 0 || s.CompareTo(best) < 0)
                    {
                        bestI = i;
                        best = s;
                    }
                }
                if (bestI < 0)
                    return;
                pairs[bestI].R.Task = pairs[bestI].T;
                taken.Add(pairs[bestI].T);
            }
        }

        /// <summary>Refresh the task monitor: each open task's state and reason; ended tasks are recorded.</summary>
        void UpdateBook(Dictionary<WorkTask, WorkTask> open)
        {
            Tasks.Close(new HashSet<WorkTask>(open.Keys), NowHours);
            foreach (WorkTask t in open.Values)
            {
                TaskRecord rec = Tasks.Touch(t);
                List<Robot> crew = WorkersOf(t);
                Robot w = null;
                foreach (Robot c in crew)
                    if (w == null || (OnSite(c, t) && !OnSite(w, t))) w = c;
                Robot reserved = ReservedRobotFor(t);
                rec.Workers = crew.Count;
                rec.Capacity = Capacity(t);
                rec.RobotId = w?.Id;
                rec.ReservedFor = reserved?.Id;
                TaskPriority p = TaskPriority.Disabled;
                foreach (Robot r in robots)
                    if (Capable(r, t.Kind) && ProgramPriority(r, t.Kind) < p) p = ProgramPriority(r, t.Kind);
                rec.Priority = p;
                TaskState state;
                string reason = "";
                if (w != null)
                {
                    bool there = OnSite(w, t);
                    state = there ? TaskState.Working : TaskState.Travelling;
                    if (!there) reason = $"{w.Name} is driving there";
                    else if (rec.Capacity > 1) reason = $"crew {crew.Count}/{rec.Capacity}";
                }
                else if (reserved != null)
                {
                    state = Paused(reserved) ? TaskState.Paused : TaskState.Reserved;
                    reason = Paused(reserved) ? $"reserved for {reserved.Name}, whose group is paused"
                           : $"reserved for {reserved.Name}" + (reserved.Available ? ", finishing its work" : $" ({reserved.State.ToString().ToLowerInvariant()})");
                }
                else
                    state = WhyWaiting(t, out reason);

                // A facility with nobody on it is short of workers (spec v2 §5); if that lasts, the player is told.
                bool shortage = state == TaskState.Available && rec.Capacity > 1;
                if (shortage) reason = $"short of workers (0/{rec.Capacity}): every robot that could do it is busy";
                if (state == TaskState.Blocked || shortage)
                {
                    if (rec.BlockedSinceHours < 0.0) rec.BlockedSinceHours = NowHours;
                    if (NowHours - rec.BlockedSinceHours >= TaskBook.AssistanceAfterHours - 1e-9)
                    {
                        bool first = rec.State != TaskState.AssistanceNeeded;
                        state = TaskState.AssistanceNeeded;
                        rec.State = state;
                        rec.Reason = reason;
                        if (first) AssistanceNeeded?.Invoke(rec);
                    }
                }
                else
                    rec.BlockedSinceHours = -1.0;
                rec.State = state;
                rec.Reason = reason;
            }
        }

        /// <summary>Why nobody is on a task: waiting for a free robot, paused, or blocked (and by what).</summary>
        TaskState WhyWaiting(WorkTask t, out string reason)
        {
            bool anyKind = false, anyEnabled = false, anyInRange = false, anyRunning = false;
            foreach (Robot r in robots)
            {
                if (!Capable(r, t.Kind)) continue;
                anyKind = true;
                if (ProgramPriority(r, t.Kind) == TaskPriority.Disabled) continue;
                anyEnabled = true;
                if (!InRangeWhenCharged(r, t)) continue;
                anyInRange = true;
                if (!Paused(r)) anyRunning = true;
            }
            if (!anyKind)
            {
                var kinds = new List<string>();
                foreach (Capability c in RobotCatalog.Capabilities)
                    if (c.Task == t.Kind && Milestones >= c.UnlockMilestone && !kinds.Contains(c.Robot.ToString())) kinds.Add(c.Robot.ToString());
                reason = $"no robot here can do it (needs a {string.Join(" or ", kinds)} robot)";
                return TaskState.Blocked;
            }
            if (!anyEnabled) { reason = "disabled in the priorities of every robot that could do it"; return TaskState.Blocked; }
            if (!anyInRange) { reason = "too far: no robot can drive there and back to a charger"; return TaskState.Blocked; }
            if (!anyRunning) { reason = "every robot that could do it is paused"; return TaskState.Paused; }
            reason = "waiting for a free robot";
            return TaskState.Available;
        }

        /// <summary>Range with a full battery (a robot charging now could go later; long trips charge to full).</summary>
        bool InRangeWhenCharged(Robot r, WorkTask t) => TripChargeFor(r, t) <= 1.0 + 1e-9;

        /// <summary>
        /// Share of capacity a robot needs at the start to get to a task and back to a charger keeping 20 %
        /// (∞ when it can't get there at all).
        /// </summary>
        /// <summary>Heater or cooler power a robot would run outdoors at a tile, W (set by the simulation from the weather; null: none counted).</summary>
        public System.Func<Robot, int, double> OutdoorHeaterWAt { get; set; }
        /// <summary>The energy of the first segment of a haul for this robot now, Wh, and the lowest body temperature on it (null: not a pickup).</summary>
        public System.Func<Robot, WorkTask, (double Wh, double MinC)?> HaulSegmentNeed { get; set; }
        /// <summary>Margin on the planned energy at dispatch ([rule] TBD).</summary>
        public const double HaulDispatchMargin = 1.1;
        /// <summary>One-way build trips to a leapfrog outpost: true when the robot may go to the tile and not come straight back (the simulation checks the survival window).</summary>
        public System.Func<Robot, int, bool> OneWayOutpostOk { get; set; }
        /// <summary>True when the air at a tile is below the operating minimum (the heater is then holding the battery cold).</summary>
        public System.Func<int, bool> OutdoorAirIsCold { get; set; }
        /// <summary>The heater energy of a trip and the lowest body temperature on it (one lumped body, 2026-11-26): the robot's body temperature now, the air at the destination, the hours outdoors.</summary>
        public System.Func<Robot, int, double, (double Wh, double MinC)> OutdoorTripHeater { get; set; }

        /// <summary>Heater Wh of a trip of this many hours and the battery capacity share it will keep at the lowest body temperature on it.</summary>
        (double Wh, double ColdFactor) OutdoorTrip(Robot r, int tile, double hours)
        {
            if (OutdoorTripHeater == null) return (0.0, 1.0);
            var (wh, minC) = OutdoorTripHeater(r, tile, hours);
            return (wh, RobotBattery.ColdCapacityFactor(minC));
        }

        double TripChargeFor(Robot r, WorkTask t)
        {
            if (Movement == null || !Movement.HasCharger)
                return 0.0;
            if (t.Kind == TaskKind.Haul && HaulSegmentNeed != null)
            {
                var need = HaulSegmentNeed(r, t);
                if (need.HasValue)
                {
                    double cap = r.Battery.CapacityWh * RobotBattery.ColdCapacityFactor(need.Value.MinC);
                    if (cap <= 0.0) return double.PositiveInfinity;
                    return RobotSpecs.ReturnToChargeFraction + need.Value.Wh * HaulDispatchMargin / cap;   // charge for the whole first segment, not only the way to the pickup
                }
            }
            double speed = r.Attribute(RobotAttribute.Speed);
            double go = Movement.RoutesFor(r).HoursBetween(r.Tile, t.Tile) / speed;
            double back = Movement.HoursToChargerFor(r)[t.Tile] / speed;
            // outdoors on a cold world the battery is held at the operating limit, not at its sheltered temperature, so the capacity it will really have is the cold one (KI-46)
            var (heaterWh, outdoorCold) = OutdoorTrip(r, t.Tile, go + back);
            double usableCapacity = Math.Min(r.Battery.ColdFactor, outdoorCold) * r.Battery.CapacityWh;
            if (double.IsInfinity(go) || double.IsInfinity(back) || usableCapacity <= 0.0)
                return double.PositiveInfinity;
            // KI-46 for every outdoor task: the trip also pays the heater or cooler, hours outdoors times its power at the destination (and on the way back),
            // so on a cold world a robot does not set out with a battery that only covers the driving.
            return RobotSpecs.ReturnToChargeFraction + (Movement.EnergyWh(go + back) / r.Attribute(RobotAttribute.EnergyEfficiency) + Movement.TripClimbWh(r, r.Tile, t.Tile) + heaterWh) / usableCapacity;
        }

        /// <summary>
        /// An idle robot whose only reachable work is beyond its usual 80 % charge (a far sea, say) charges fully for it
        /// first (game rule, 2026-11-14). Robots with a task, or with nothing that far, drop the plan.
        /// </summary>
        void PlanTripCharges(Dictionary<WorkTask, WorkTask> open)
        {
            var staffed = new HashSet<WorkTask>();
            foreach (Robot r in robots)
                if (r.Task.HasValue) staffed.Add(r.Task.Value);
            foreach (Robot r in robots)
            {
                if (r.Activity == RobotActivity.Charging && r.TripCharge > 0.0)
                    continue;   // already charging for it
                double need = 0.0;
                if (Movement != null && !r.Task.HasValue && !r.IsShutDown && !Paused(r) && !r.ManualTarget.HasValue
                    && r.Activity == RobotActivity.None)
                    foreach (WorkTask t in open.Keys)
                    {
                        if (staffed.Contains(t) || !Capable(r, t.Kind) || ProgramPriority(r, t.Kind) == TaskPriority.Disabled || InRange(r, t))
                            continue;
                        double c = TripChargeFor(r, t);
                        if (c > Math.Min(RobotSpecs.ResumeWorkFraction, r.Battery.StateOfCharge + 1e-6) && c <= 1.0 + 1e-9 && (need == 0.0 || c < need))   // a task the robot cannot reach at its present charge makes it charge for it, even below the usual 80 %
                            need = Math.Min(1.0, c + 0.02);   // a little margin
                    }
                r.TripCharge = need;
            }
        }

        /// <summary>How reluctant a robot is to drop what it does: idle 0, else 1 + how urgent its task is.</summary>
        int Busyness(Robot r) => r.Task.HasValue ? 1 + (int)TaskPriority.Disabled - (int)ProgramPriority(r, r.Task.Value.Kind) : 0;

        WorkTask? BestFor(Robot r, IEnumerable<WorkTask> open, Staffing taken, HashSet<WorkTask> reservedTasks = null)
        {
            RobotGroup g = GetGroup(r.GroupId);
            OperatingProgram program = EffectiveProgram(r);
            if (program == OperatingProgram.TaskQueue)
            {
                var openSet = new HashSet<WorkTask>(open);
                foreach (WorkTask q in g.QueueItems)
                    if (openSet.Contains(q) && !taken.Contains(q) && (reservedTasks == null || !reservedTasks.Contains(q))
                        && Capable(r, q.Kind) && InRange(r, q))
                        foreach (WorkTask t in open)
                            if (t.Equals(q)) return t;
                return null;
            }
            WorkTask? best = null;
            TaskPriority bestP = TaskPriority.Disabled;
            foreach (WorkTask t in open)
            {
                if (taken.Contains(t) || !Capable(r, t.Kind))
                    continue;
                if (program == OperatingProgram.Facility && g.FacilityTile.HasValue && t.Tile != g.FacilityTile.Value)
                    continue;
                if (!InRange(r, t))
                    continue;
                TaskPriority p = ProgramPriority(r, t.Kind);
                if (p == TaskPriority.Disabled)
                    continue;
                if (!best.HasValue || p < bestP
                    || (p == bestP && (g.RankOf(t.Kind) < g.RankOf(best.Value.Kind)
                                       || (t.Kind == best.Value.Kind && t.Order < best.Value.Order))))
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
                gs.MaintenanceBelow = g.MaintenanceBelow;
                gs.HasFacility = g.FacilityTile.HasValue;
                gs.FacilityTile = g.FacilityTile ?? 0;
                if (g.Ranking != null) gs.Ranking.AddRange(g.Ranking);
                foreach (WorkTask q in g.QueueItems)
                    gs.Queue.Add(new QueueItemRecord { Kind = q.Kind, Tile = q.Tile, Slot = q.Slot });
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
                    CriticallyDamaged = r.CriticallyDamaged,
                    HasReserved = r.ReservedTask.HasValue,
                    ReservedKind = r.ReservedTask?.Kind ?? TaskKind.Build,
                    ReservedTile = r.ReservedTask?.Tile ?? 0,
                    ReservedSlot = r.ReservedTask?.Slot ?? 0,
                    HasResume = r.ResumeTask.HasValue,
                    ResumeKind = r.ResumeTask?.Kind ?? TaskKind.Build,
                    ResumeTile = r.ResumeTask?.Tile ?? 0,
                    ResumeSlot = r.ResumeTask?.Slot ?? 0,
                    HasUpkeep = true,
                    BatteryStoredWh = r.Battery.StoredWh,
                    BatteryDeliveredWh = r.Battery.DeliveredWh,
                    HasBatteryAgeing = true,
                    BatteryDamage = r.Battery.Damage,
                    BatteryCycleTop = r.Battery.CycleTop,
                    BatteryCycleLow = r.Battery.CycleLow,
                    BatteryZeroDischarges = r.Battery.ZeroDischarges,
                    Condition = r.Condition,
                    Activity = r.Activity,
                    Modules = r.Modules,
                    SodiumBattery = r.SodiumBattery,
                    HopProgress = r.HopProgress,
                    HasManualTarget = r.ManualTarget.HasValue,
                    ManualTarget = r.ManualTarget ?? 0,
                    HasFacility = r.Facility.HasValue,
                    FacilityTile = r.Facility?.Tile ?? 0,
                    FacilitySlot = r.Facility?.Slot ?? 0,
                };
                if (r.Task.HasValue)
                {
                    rs.TaskKind = r.Task.Value.Kind;
                    rs.TaskTile = r.Task.Value.Tile;
                    rs.TaskSlot = r.Task.Value.Slot;
                }
                foreach (var kv in r.PriorityOverrides)
                    rs.PriorityOverrides.Add(new PriorityRecord { Kind = kv.Key, Priority = kv.Value });
                rs.Connected = r.Connected;
                rs.HoursOffline = r.HoursOffline;
                rs.SyncedRulebookVersion = r.SyncedRulebookVersion;
                rs.CachedProgram = r.CachedProgram;
                rs.UnsyncedFindings = r.UnsyncedFindings;
                rs.ExposedEmptyHours = r.ExposedEmptyHours;
                rs.BatteryTempC = r.BatteryTempC;
                rs.BodyTempC = r.BodyTempC;
                rs.CachedBrownoutRisk = r.CachedNeeds.BrownoutRisk;
                rs.CachedSitesWaiting = r.CachedNeeds.SitesWaiting;
                foreach (var kv in r.CachedPriorities)
                    rs.CachedPriorities.Add(new PriorityRecord { Kind = kv.Key, Priority = kv.Value });
                rs.Route.AddRange(r.Route);
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
                // Saves before version 11 have no facility, ranking or queue.
                g.FacilityTile = gs.HasFacility ? gs.FacilityTile : (int?)null;
                // Saves before version 15 have no maintenance rule: the default.
                g.MaintenanceBelow = gs.MaintenanceBelow > 0.0 ? gs.MaintenanceBelow : RobotSpecs.MaintenanceBelowCondition;
                if (gs.Ranking != null && gs.Ranking.Count > 0) g.Ranking = new List<TaskKind>(gs.Ranking);
                if (gs.Queue != null)
                    foreach (QueueItemRecord q in gs.Queue)
                        g.QueueItems.Add(new WorkTask(q.Kind, q.Tile, q.Slot));
                newGroups.Add(g);
            }
            groups.Clear();
            groups.AddRange(newGroups);
            Tasks.Clear();
            changes.Clear();
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
                // Saves before version 22 have no network: the robot comes back connected (UpdateConnections syncs it).
                r.Connected = rs.Connected;
                r.HoursOffline = rs.HoursOffline;
                r.SyncedRulebookVersion = rs.SyncedRulebookVersion;
                r.CachedProgram = rs.CachedProgram;
                r.UnsyncedFindings = rs.UnsyncedFindings;
                r.ExposedEmptyHours = Math.Max(0.0, rs.ExposedEmptyHours);
                r.BatteryTempC = rs.BatteryTempC;
                r.BodyTempC = rs.BodyTempC;
                r.CachedNeeds = new ColonyNeeds(rs.CachedBrownoutRisk, rs.CachedSitesWaiting);
                if (rs.CachedPriorities != null)
                    foreach (PriorityRecord p in rs.CachedPriorities)
                        r.CachedPriorities[p.Kind] = p.Priority;
                // Saves before version 8 have no battery or condition: the robot comes back charged and like new.
                r.Modules = rs.Modules;
                r.SodiumBattery = rs.SodiumBattery;
                r.CriticallyDamaged = rs.CriticallyDamaged;
                // Saves before version 14 have no reservations or tasks to go back to.
                r.ReservedTask = rs.HasReserved ? new WorkTask(rs.ReservedKind, rs.ReservedTile, rs.ReservedSlot) : (WorkTask?)null;
                r.ResumeTask = rs.HasResume ? new WorkTask(rs.ResumeKind, rs.ResumeTile, rs.ResumeSlot) : (WorkTask?)null;
                // Saves before version 12 have no routes: robots stand where they were.
                if (rs.Route != null) r.Route.AddRange(rs.Route);
                r.HopProgress = Math.Max(0.0, Math.Min(1.0, rs.HopProgress));
                r.ManualTarget = rs.HasManualTarget ? rs.ManualTarget : (int?)null;
                if (Math.Abs(r.DesignBatteryWh - r.Battery.NominalWh) > 1e-9)
                    r.Battery = new RobotBattery(r.DesignBatteryWh);
                if (rs.HasUpkeep)
                {
                    // Saves before version 15 count health from full cycles only.
                    if (rs.HasBatteryAgeing)
                        r.Battery.Restore(rs.BatteryDeliveredWh, rs.BatteryStoredWh, rs.BatteryDamage, rs.BatteryCycleTop,
                                          rs.BatteryCycleLow, rs.BatteryZeroDischarges);
                    else
                        r.Battery.Restore(rs.BatteryDeliveredWh, rs.BatteryStoredWh);
                    r.Condition = Math.Max(0.0, Math.Min(1.0, rs.Condition));
                    if (rs.Activity != RobotActivity.None && rs.HasFacility)
                    {
                        r.Activity = rs.Activity;
                        r.Facility = (rs.FacilityTile, rs.FacilitySlot);
                    }
                    if (r.Activity != RobotActivity.None)
                    {
                        r.Task = null;
                        r.OnTemporaryTask = false;
                    }
                }
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
        /// <param name="starters">General robots to start with (tests that follow one robot use 1).</param>
        public void Reset(int tile, int starters = StarterRobots)
        {
            robots.Clear();
            groups.Clear();
            nextRobotId = nextGroupId = 1;
            groupCapacity = DefaultGroupCapacity;
            for (int i = 0; i < Math.Max(1, starters); i++)
                AddRobot(RobotKind.General, tile);
        }

        /// <summary>General robots the lander brings (director, 2026-11-14; was 1).</summary>
        public const int StarterRobots = 3;

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

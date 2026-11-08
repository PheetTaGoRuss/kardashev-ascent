using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// A group of robots that share a program and task priorities (spec §3). Automatic groups are made and filled
    /// by the workforce, one kind of robot each (design D-AO); manual groups are the player's and are never
    /// reorganised.
    /// </summary>
    public sealed class RobotGroup
    {
        internal readonly Dictionary<TaskKind, TaskPriority> Priorities = new Dictionary<TaskKind, TaskPriority>();
        internal readonly List<int> MemberIds = new List<int>();

        internal RobotGroup(int id, string name, bool automatic, RobotKind autoKind)
        {
            Id = id;
            Name = name;
            Automatic = automatic;
            AutoKind = autoKind;
            foreach (TaskKind k in (TaskKind[])Enum.GetValues(typeof(TaskKind)))
                Priorities[k] = TaskPriority.Normal;
        }

        public int Id { get; }
        public string Name { get; internal set; }
        /// <summary>True for a group the workforce made by robot kind; false for one the player made.</summary>
        public bool Automatic { get; }
        /// <summary>The robot kind an automatic group holds.</summary>
        public RobotKind AutoKind { get; }
        public OperatingProgram Program { get; internal set; } = OperatingProgram.Priority;
        public GroupState State { get; internal set; } = GroupState.Automatic;
        /// <summary>The temporary task the group was interrupted for, while <see cref="State"/> is Interrupted.</summary>
        public WorkTask? InterruptTask { get; internal set; }

        public TaskPriority PriorityOf(TaskKind kind) =>
            Priorities.TryGetValue(kind, out TaskPriority p) ? p : TaskPriority.Normal;

        /// <summary>Facility program: the tile the group works on (null: anywhere).</summary>
        public int? FacilityTile { get; internal set; }

        /// <summary>
        /// The group's own order of task kinds for ties (spec §5 "advanced ranked ordering"); null: the default order
        /// (build, salvage, Workshop, survey, mining).
        /// </summary>
        internal List<TaskKind> Ranking;

        /// <summary>Task queue program: the group's tasks in order.</summary>
        internal readonly List<WorkTask> QueueItems = new List<WorkTask>();

        public IReadOnlyList<WorkTask> Queue => QueueItems;

        /// <summary>Where a kind of task stands in this group's order (lower first).</summary>
        public int RankOf(TaskKind kind)
        {
            if (Ranking == null)
                return RobotAbilities.TieRank(kind);
            int i = Ranking.IndexOf(kind);
            return i >= 0 ? i : 100 + RobotAbilities.TieRank(kind);
        }

        /// <summary>The kinds in this group's order.</summary>
        public List<TaskKind> RankedKinds()
        {
            var kinds = new List<TaskKind>((TaskKind[])Enum.GetValues(typeof(TaskKind)));
            kinds.Sort((a, b) => RankOf(a).CompareTo(RankOf(b)));
            return kinds;
        }

        public bool HasCustomRanking => Ranking != null;

        public IReadOnlyList<int> Members => MemberIds;
        public int Count => MemberIds.Count;
    }
}

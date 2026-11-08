using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// One robot (spec §3). Its settings come from its group unless it has its own overrides, which survive any
    /// change to the group's settings (spec §3, §5). Battery and condition arrive in phase 2.
    /// </summary>
    public sealed class Robot
    {
        internal readonly Dictionary<TaskKind, TaskPriority> PriorityOverrides = new Dictionary<TaskKind, TaskPriority>();

        internal Robot(int id, string name, RobotKind kind, int tile)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Tile = tile;
        }

        public int Id { get; }
        public string Name { get; internal set; }
        public RobotKind Kind { get; }
        public int GroupId { get; internal set; }
        /// <summary>Tile the robot is on (robots don't move yet; movement is phase 2).</summary>
        public int Tile { get; internal set; }
        /// <summary>The task it is on, if any.</summary>
        public WorkTask? Task { get; internal set; }
        /// <summary>True while it does a temporary task its group was interrupted for (spec §5).</summary>
        public bool OnTemporaryTask { get; internal set; }

        public RobotState State => Task.HasValue ? RobotState.Working : RobotState.Idle;

        /// <summary>The robot's own program, or null to follow its group.</summary>
        public OperatingProgram? ProgramOverride { get; internal set; }

        /// <summary>The robot's own priority for a kind of task, or null to follow its group.</summary>
        public TaskPriority? PriorityOverride(TaskKind kind) =>
            PriorityOverrides.TryGetValue(kind, out TaskPriority p) ? p : (TaskPriority?)null;

        public bool HasOverrides => ProgramOverride.HasValue || PriorityOverrides.Count > 0;
    }
}

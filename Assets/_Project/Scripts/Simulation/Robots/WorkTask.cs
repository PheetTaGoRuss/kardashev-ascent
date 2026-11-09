using System;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>Robot types (spec §3). Only <see cref="General"/> exists until manufacturing (phase 3).</summary>
    public enum RobotKind { General, Mining, Construction, Maintenance, Transport, Research }

    /// <summary>What a robot is doing (spec §5 overview). Phase 1 uses Idle and Working; the rest arrive with phase 2.</summary>
    public enum RobotState { Idle, Working, Charging, Maintenance, Shutdown }

    /// <summary>Task priority levels (spec §5). Lower value = more urgent; Disabled tasks are never taken.</summary>
    public enum TaskPriority { Critical, High, Normal, Low, Disabled }

    /// <summary>
    /// Kinds of work (design D1): finishing a construction site, taking down a building (phase 1), surveying a tile's
    /// ground and mining a material (phase 3a), and working a Workshop job (phase 3b). For Mine, the task's slot
    /// holds the material; for Manufacture, the task is at the Workshop's tile and slot.
    /// </summary>
    public enum TaskKind { Build, Salvage, Survey, Mine, Manufacture, Adjust }

    /// <summary>
    /// Operating programs (spec §4):
    /// <list type="bullet">
    /// <item><see cref="Priority"/> (default): the most urgent task by the group's priorities and ranking.</item>
    /// <item><see cref="Facility"/>: like Priority, but only tasks on the group's facility tile.</item>
    /// <item><see cref="Demand"/>: priorities follow what the colony needs now (design D16): building first when a
    /// brownout threatens or sites are waiting, mining last. A Disabled kind stays disabled.</item>
    /// <item><see cref="TaskQueue"/>: the group's own list of tasks, first open one first; idle when it is empty.</item>
    /// </list>
    /// </summary>
    public enum OperatingProgram { Priority, Facility, Demand, TaskQueue }

    /// <summary>What the colony needs now, for the Demand program (design D16).</summary>
    public readonly struct ColonyNeeds
    {
        /// <summary>A brownout now, or the grid battery low at night.</summary>
        public readonly bool BrownoutRisk;
        public readonly bool SitesWaiting;

        public ColonyNeeds(bool brownoutRisk, bool sitesWaiting)
        {
            BrownoutRisk = brownoutRisk;
            SitesWaiting = sitesWaiting;
        }
    }

    /// <summary>Group states (spec §5): running its program, on a temporary manual task, or stopped.</summary>
    public enum GroupState { Automatic, Interrupted, Paused }

    /// <summary>
    /// One piece of work a robot can take: a kind and the slot it is at. <see cref="Order"/> breaks ties between
    /// tasks of the same priority and kind (lower first, e.g. the construction queue order). Two tasks are the same
    /// task when kind, tile and slot match.
    /// </summary>
    public readonly struct WorkTask : IEquatable<WorkTask>
    {
        public readonly TaskKind Kind;
        public readonly int Tile;
        public readonly int Slot;
        public readonly int Order;

        public WorkTask(TaskKind kind, int tile, int slot, int order = 0)
        {
            Kind = kind;
            Tile = tile;
            Slot = slot;
            Order = order;
        }

        public bool Equals(WorkTask other) => Kind == other.Kind && Tile == other.Tile && Slot == other.Slot;
        public override bool Equals(object obj) => obj is WorkTask t && Equals(t);
        public override int GetHashCode() => ((int)Kind * 397 ^ Tile) * 397 ^ Slot;
        public override string ToString() => $"{Kind} {Tile}.{Slot}";
    }

    /// <summary>Which robot kinds can do which tasks: a thin front for <see cref="RobotCatalog"/> (spec v2 §2, data-driven).</summary>
    public static class RobotAbilities
    {
        /// <summary>
        /// Which kind of task goes first when priorities are equal: building, salvage, the Workshop, surveys, then
        /// mining (open-ended background work).
        /// </summary>
        public static int TieRank(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.Build: return 0;
                case TaskKind.Salvage:
                case TaskKind.Adjust: return 1;
                case TaskKind.Manufacture: return 2;
                case TaskKind.Survey: return 3;
                default: return 4;
            }
        }

        /// <summary>How fast a kind works at a task with no milestones (hours of work per hour), from <see cref="RobotCatalog"/>.</summary>
        public static double Speed(RobotKind robot, TaskKind task) => RobotCatalog.Efficiency(robot, task, 0);

        /// <summary>Can a kind do a task with no milestones (its primary roles)? See <see cref="RobotCatalog.Capabilities"/>.</summary>
        public static bool CanDo(RobotKind robot, TaskKind task) => RobotCatalog.CanDo(robot, task, 0);
    }
}

using System;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>Robot types (spec §3). Only <see cref="General"/> exists until manufacturing (phase 3).</summary>
    public enum RobotKind { General, Mining, Construction, Maintenance, Transport, Research }

    /// <summary>What a robot is doing (spec §5 overview). Phase 1 uses Idle and Working; the rest arrive with phase 2.</summary>
    public enum RobotState { Idle, Working, Charging, Maintenance, Shutdown }

    /// <summary>Task priority levels (spec §5). Lower value = more urgent; Disabled tasks are never taken.</summary>
    public enum TaskPriority { Critical, High, Normal, Low, Disabled }

    /// <summary>Kinds of work in phase 1 (design D1): finishing a construction site and taking down a building.</summary>
    public enum TaskKind { Build, Salvage }

    /// <summary>
    /// Operating programs (spec §4). Only <see cref="Priority"/> (the default) is implemented; the others are
    /// placeholders for phase 4 and cannot be selected yet.
    /// </summary>
    public enum OperatingProgram { Priority, Facility, Demand, TaskQueue }

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

    /// <summary>Which robot kinds can do which tasks (design D1: the General robot does basic work of every kind).</summary>
    public static class RobotAbilities
    {
        public static bool CanDo(RobotKind robot, TaskKind task)
        {
            switch (robot)
            {
                case RobotKind.General: return true;
                case RobotKind.Construction: return task == TaskKind.Build || task == TaskKind.Salvage;
                default: return false;
            }
        }
    }
}

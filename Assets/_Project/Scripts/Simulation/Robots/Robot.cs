using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>What a robot is doing for itself rather than for the colony (spec §6, §7).</summary>
    public enum RobotActivity { None, Charging, Maintenance }

    /// <summary>
    /// One robot (spec §3). Its settings come from its group unless it has its own overrides, which survive any
    /// change to the group's settings (spec §3, §5). It has its own battery and a condition (spec §6–§8).
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

        /// <summary>Upgrades fitted at the Workshop.</summary>
        public RobotModule Modules { get; internal set; }

        /// <summary>True if it was built with a Na-ion battery (less capacity: <see cref="RobotTech.SodiumCapacityRatio"/>).</summary>
        public bool SodiumBattery { get; internal set; }

        public bool Has(RobotModule m) => (Modules & m) != 0;

        /// <summary>Battery capacity when new for this robot's chemistry and upgrades, Wh.</summary>
        public double DesignBatteryWh =>
            RobotSpecs.BatteryCapacityWh * (SodiumBattery ? RobotTech.SodiumCapacityRatio : 1.0)
            * (Has(RobotModule.BigBattery) ? RobotTech.BigBatteryFactor : 1.0);

        /// <summary>Its own battery: charge and, separately, health.</summary>
        public RobotBattery Battery { get; internal set; } = new RobotBattery();

        /// <summary>Condition, 0–1 (1 = new). At 0 the robot shuts down until it is repaired (spec §7).</summary>
        public double Condition { get; internal set; } = 1.0;

        /// <summary>Charging or in maintenance at a facility, or neither.</summary>
        public RobotActivity Activity { get; internal set; }

        /// <summary>Facility it charges or is repaired at (tile, slot; slot −1 = the lander), while it has one.</summary>
        public (int Tile, int Slot)? Facility { get; internal set; }

        /// <summary>Charging power it took in the last step at its charger, W (not saved).</summary>
        internal double LastChargeW;
        /// <summary>True once it has had a step at its present charger (not saved).</summary>
        internal bool ChargedBefore;

        // ---- Movement (spec §10) ----

        /// <summary>Tiles still to drive through, in order (empty: not driving).</summary>
        internal readonly List<int> Route = new List<int>();
        /// <summary>How far across the next tile it is, 0–1.</summary>
        internal double HopProgress;
        /// <summary>Charge it must keep to drive back to the nearest charger, as a share of capacity (set each step).</summary>
        internal double ReserveFraction;

        public bool IsTravelling => Route.Count > 0;
        public int HopsLeft => Route.Count;
        public int? Destination => Route.Count > 0 ? Route[Route.Count - 1] : (int?)null;

        /// <summary>A tile the player sent it to (hybrid movement); it takes no tasks until it gets there.</summary>
        public int? ManualTarget { get; internal set; }

        /// <summary>
        /// Out of charge away from any charger: it can't move. Rescue is a later feature (spec §12), so it waits for
        /// the player (a charger built on its tile brings it back).
        /// </summary>
        public bool IsStranded { get; internal set; }

        public bool IsShutDown => (Condition <= 0.0 && Activity != RobotActivity.Maintenance) || IsStranded;

        /// <summary>
        /// Below the charge threshold (20 %, plus what it needs to drive back to a charger) and not yet charging.
        /// </summary>
        public bool NeedsCharge => Activity == RobotActivity.None
                                   && Battery.StateOfCharge < RobotSpecs.ReturnToChargeFraction + ReserveFraction;

        public bool NeedsMaintenance => Condition < RobotSpecs.MaintenanceBelowCondition || Battery.WornOut;

        /// <summary>Free to take colony work: not shut down, charging, in maintenance or low on charge.</summary>
        public bool Available => Activity == RobotActivity.None && !IsShutDown && !NeedsCharge && !ManualTarget.HasValue;

        public RobotState State =>
            IsShutDown ? RobotState.Shutdown
            : Activity == RobotActivity.Charging ? RobotState.Charging
            : Activity == RobotActivity.Maintenance ? RobotState.Maintenance
            : Task.HasValue ? RobotState.Working : RobotState.Idle;

        /// <summary>The robot's own program, or null to follow its group.</summary>
        public OperatingProgram? ProgramOverride { get; internal set; }

        /// <summary>The robot's own priority for a kind of task, or null to follow its group.</summary>
        public TaskPriority? PriorityOverride(TaskKind kind) =>
            PriorityOverrides.TryGetValue(kind, out TaskPriority p) ? p : (TaskPriority?)null;

        public bool HasOverrides => ProgramOverride.HasValue || PriorityOverrides.Count > 0;
    }
}

using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>The workforce in a saved game (save version 7). Public fields and lists so JsonUtility can write it.</summary>
    [Serializable]
    public sealed class WorkforceState
    {
        public int NextRobotId;
        public int NextGroupId;
        public int GroupCapacity;
        public List<RobotRecord> Robots = new List<RobotRecord>();
        public List<GroupRecord> Groups = new List<GroupRecord>();
    }

    [Serializable]
    public sealed class RobotRecord
    {
        public int Id;
        public string Name;
        public RobotKind Kind;
        public int GroupId;
        public int Tile;
        public bool HasTask;
        public TaskKind TaskKind;
        public int TaskTile;
        public int TaskSlot;
        public bool OnTemporaryTask;
        public bool HasProgramOverride;
        public OperatingProgram ProgramOverride;
        public List<PriorityRecord> PriorityOverrides = new List<PriorityRecord>();
        /// <summary>True when battery, condition and activity below were saved (save version 8).</summary>
        public bool HasUpkeep;
        public double BatteryStoredWh;
        public double BatteryDeliveredWh;
        public double Condition;
        public RobotActivity Activity;
        public bool HasFacility;
        public int FacilityTile;
        public int FacilitySlot;
        /// <summary>Phase 3b (save version 10): upgrades and battery chemistry.</summary>
        public RobotModule Modules;
        public bool SodiumBattery;
    }

    /// <summary>A Workshop job in a saved game (version 10); its recipe is rebuilt from these fields.</summary>
    [Serializable]
    public sealed class JobRecord
    {
        public JobKind Kind;
        public RobotKind RobotKind;
        public bool SodiumBattery;
        public bool AluminiumWiring;
        public int RobotId;
        public RobotModule Module;
        public Resources.Material Output;
        public double OutputKg;
        public double DoneHours;
    }

    [Serializable]
    public sealed class GroupRecord
    {
        public int Id;
        public string Name;
        public bool Automatic;
        public RobotKind AutoKind;
        public OperatingProgram Program;
        public GroupState State;
        public bool HasInterruptTask;
        public TaskKind InterruptKind;
        public int InterruptTile;
        public int InterruptSlot;
        public List<PriorityRecord> Priorities = new List<PriorityRecord>();
    }

    [Serializable]
    public sealed class PriorityRecord
    {
        public TaskKind Kind;
        public TaskPriority Priority;
    }

    /// <summary>A building the player asked a robot to take down (save version 7).</summary>
    [Serializable]
    public sealed class SalvageOrderRecord
    {
        public int Tile;
        public int Slot;
    }
}

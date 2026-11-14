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
        /// <summary>Colony network (stage E, save version 22).</summary>
        public bool Connected = true;
        public double HoursOffline;
        public OperatingProgram CachedProgram;
        public List<PriorityRecord> CachedPriorities = new List<PriorityRecord>();
        public int UnsyncedFindings;
        /// <summary>Stage E second part (save version 25): the colony's needs at the last sync.</summary>
        public bool CachedBrownoutRisk;
        public bool CachedSitesWaiting;
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
        /// <summary>Movement (save version 12): the tiles still to drive, progress across the next one, and a player's target.</summary>
        public List<int> Route = new List<int>();
        public double HopProgress;
        public bool HasManualTarget;
        public int ManualTarget;
        /// <summary>Stage A (save version 13): critical operational damage.</summary>
        public bool CriticallyDamaged;
        /// <summary>Stage B (save version 14): the player's reservation and the task to go back to.</summary>
        public bool HasReserved;
        public TaskKind ReservedKind;
        public int ReservedTile;
        public int ReservedSlot;
        public bool HasResume;
        public TaskKind ResumeKind;
        public int ResumeTile;
        public int ResumeSlot;
        /// <summary>Stage D (save version 15): battery ageing by depth of discharge.</summary>
        public bool HasBatteryAgeing;
        public double BatteryDamage;
        public double BatteryCycleTop;
        public double BatteryCycleLow;
        public int BatteryZeroDischarges;
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
        /// <summary>Phase 4 (save version 11): facility, own ranking and task queue.</summary>
        public bool HasFacility;
        public int FacilityTile;
        public List<TaskKind> Ranking = new List<TaskKind>();
        public List<QueueItemRecord> Queue = new List<QueueItemRecord>();
        /// <summary>Stage D (save version 15): the group's maintenance rule (0: default).</summary>
        public double MaintenanceBelow;
    }

    [Serializable]
    public sealed class QueueItemRecord
    {
        public TaskKind Kind;
        public int Tile;
        public int Slot;
    }

    [Serializable]
    public sealed class PriorityRecord
    {
        public TaskKind Kind;
        public TaskPriority Priority;
    }

    /// <summary>A panel the player asked a robot to turn to new angles (save version 17), radians.</summary>
    [Serializable]
    public sealed class AdjustOrderRecord
    {
        public int Tile;
        public int Slot;
        public double Tilt;
        public double Azimuth;
    }

    /// <summary>A building the player asked a robot to take down (save version 7).</summary>
    [Serializable]
    public sealed class SalvageOrderRecord
    {
        public int Tile;
        public int Slot;
    }
}

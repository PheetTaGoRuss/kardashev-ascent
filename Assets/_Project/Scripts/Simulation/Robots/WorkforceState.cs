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

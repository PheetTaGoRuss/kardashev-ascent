using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Operations
{
    /// <summary>What a rule condition reads (mission operations 5.1). Fixed vocabulary, no scripting. Saved as integers: append only.</summary>
    public enum ConditionKind
    {
        /// <summary>The robot's own battery charge is below Value percent.</summary>
        RobotBatteryBelow,
        RobotBatteryAbove,
        /// <summary>The robot's own condition (wear) is below Value percent.</summary>
        RobotConditionBelow,
        /// <summary>The colony battery is below Value percent (known only while the robot or plant is connected).</summary>
        GridBatteryBelow,
        /// <summary>The grid is in a brownout (known only while connected).</summary>
        BrownoutNow,
        IsNight,
        IsDay,
        /// <summary>Cut off from the colony network for more than Value hours.</summary>
        CutOffForHours,
        /// <summary>The depot on Tile holds less than Value kg of Material (known only while connected).</summary>
        DepotStockBelow,
        DepotStockAbove,
        /// <summary>The plant is blocked by Blocker for more than Value hours (Value 0: at all).</summary>
        PlantBlockedBy,
        /// <summary>The plant's output buffer is over Value percent full.</summary>
        PlantOutputBufferAbove,
        /// <summary>The measured surface temperature is outside Value to Value2 degrees C. Unknown, and never true, until it has been measured.</summary>
        SurfaceTemperatureOutside,
        /// <summary>A flare is raging at the red dwarf (flaring stars only).</summary>
        StellarFlareActive,
    }

    /// <summary>What a rule does (mission operations 5.1). Fixed vocabulary. Saved as integers: append only.</summary>
    public enum ResponseKind
    {
        /// <summary>Give the robots in scope this priority for a kind of task (an override on top of their group).</summary>
        SetTaskPriority,
        /// <summary>Switch the robots' operating program.</summary>
        SetProgram,
        /// <summary>Go to the nearest charger, take no work, and resume when the rule stops holding.</summary>
        SafeMode,
        /// <summary>Stay where you are and take no work.</summary>
        HoldPosition,
        /// <summary>Keep at least Kg of Material in the depot on Tile.</summary>
        SetStockTarget,
        /// <summary>Haul Kg of Material from tile From to tile To once.</summary>
        RequestHaul,
        PausePlant,
        ResumePlant,
        SetByproductPolicy,
        /// <summary>Only tell the player (an Operations log entry).</summary>
        Notify,
    }

    public enum RuleScopeKind { AllRobots, Group, RobotKind, Plant }

    /// <summary>One test on known state. Public fields so Unity's JsonUtility can save it.</summary>
    [Serializable]
    public sealed class RuleCondition
    {
        public ConditionKind Kind;
        public double Value;
        public double Value2;
        public int Tile;
        public Material Material;
        public IndustrialBlocker Blocker;

        public RuleCondition Copy() => (RuleCondition)MemberwiseClone();
    }

    /// <summary>What a rule does when it holds.</summary>
    [Serializable]
    public sealed class RuleResponse
    {
        public ResponseKind Kind;
        public TaskKind Task;
        public TaskPriority Priority;
        public OperatingProgram Program;
        public int Tile;
        public int ToTile;
        public Material Material;
        public double Kg;
        public ByproductPolicy Policy;
        public string Text = "";

        public RuleResponse Copy() => (RuleResponse)MemberwiseClone();
    }

    /// <summary>
    /// A flight rule (mission operations 5.1): when up to three conditions hold on known state, do one thing, for a stated
    /// reason. The rationale is required: a rule without a reason cannot be published. Rules are read top to bottom, and
    /// the first that holds in a scope wins for that scope and step.
    /// </summary>
    [Serializable]
    public sealed class Rule
    {
        public int Id;
        public string Name = "";
        /// <summary>Why the rule exists (NASA flight rules carry rationales); shown with every firing.</summary>
        public string Rationale = "";
        public RuleScopeKind Scope;
        /// <summary>The group id, or the robot kind as an integer, for those scopes.</summary>
        public int ScopeId;
        /// <summary>The plant a Plant-scope rule is about; -1 and -1 mean every plant.</summary>
        public int PlantTile = -1;
        public int PlantSlot = -1;
        public List<RuleCondition> Conditions = new List<RuleCondition>();
        public RuleResponse Response = new RuleResponse();
        /// <summary>The same rule cannot start holding again sooner than this after it last did (stops flapping).</summary>
        public TaskPriority Priority = TaskPriority.Normal;
        public double CooldownHours;
        public bool Enabled = true;

        public const int MaxConditions = 3;

        public Rule Copy()
        {
            var r = (Rule)MemberwiseClone();
            r.Conditions = new List<RuleCondition>();
            foreach (RuleCondition c in Conditions) r.Conditions.Add(c.Copy());
            r.Response = Response.Copy();
            return r;
        }

        /// <summary>Why this rule cannot be published, or null.</summary>
        public string Problem()
        {
            if (string.IsNullOrWhiteSpace(Rationale)) return $"Rule \"{Name}\" needs a rationale: say why it exists.";
            if (Conditions.Count < 1 || Conditions.Count > MaxConditions) return $"Rule \"{Name}\" needs one to {MaxConditions} conditions.";
            if (Response == null) return $"Rule \"{Name}\" needs a response.";
            bool plant = Scope == RuleScopeKind.Plant;
            foreach (RuleCondition c in Conditions)
            {
                bool plantOnly = c.Kind == ConditionKind.PlantBlockedBy || c.Kind == ConditionKind.PlantOutputBufferAbove;
                bool robotOnly = c.Kind == ConditionKind.RobotBatteryBelow || c.Kind == ConditionKind.RobotBatteryAbove
                                 || c.Kind == ConditionKind.RobotConditionBelow || c.Kind == ConditionKind.CutOffForHours;
                if (plantOnly && !plant) return $"Rule \"{Name}\": a plant condition needs a plant scope.";
                if (robotOnly && plant) return $"Rule \"{Name}\": a robot condition needs a robot scope.";
            }
            bool plantResponse = Response.Kind == ResponseKind.PausePlant || Response.Kind == ResponseKind.ResumePlant || Response.Kind == ResponseKind.SetByproductPolicy;
            bool robotResponse = Response.Kind == ResponseKind.SetTaskPriority || Response.Kind == ResponseKind.SetProgram
                                 || Response.Kind == ResponseKind.SafeMode || Response.Kind == ResponseKind.HoldPosition;
            if (plantResponse && !plant) return $"Rule \"{Name}\": a plant response needs a plant scope.";
            if (robotResponse && plant) return $"Rule \"{Name}\": a robot response needs a robot scope.";
            if (CooldownHours < 0.0 || double.IsNaN(CooldownHours)) return $"Rule \"{Name}\": the cooldown cannot be negative.";
            return null;
        }
    }

    /// <summary>One edit of the rulebook: the whole rulebook as published, numbered (mission operations 5.2).</summary>
    [Serializable]
    public sealed class RulebookVersion
    {
        public int Number;
        public double EditedAtHours;
        public string Note = "";
        public List<Rule> Rules = new List<Rule>();
    }
}

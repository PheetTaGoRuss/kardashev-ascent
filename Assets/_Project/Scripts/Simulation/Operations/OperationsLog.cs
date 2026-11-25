using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Operations
{
    /// <summary>
    /// One Operations log record, in the observation-record shape of information-architecture 4 (mission operations 5.4,
    /// MO-13): who or what it is about, what was observed, its value and unit, the source, when it was observed and when the
    /// colony came to know it. A record observed by a robot that was cut off has no "known at" until that robot syncs.
    /// </summary>
    [Serializable]
    /// <summary>
    /// How much a log entry needs the player (UI information design, stage 7, decided 2026-11-24): act now (a toast shows too), watch, or info. Saved as an
    /// integer: append new members at the end only; a record from an older save is Info.
    /// </summary>
    public enum LogPriority { Info = 0, Watch = 1, ActNow = 2 }

    /// <summary>The priority of each kind of record, by what it is about.</summary>
    public static class OperationPriority
    {
        public static LogPriority Of(string quantity)
        {
            switch (quantity)
            {
                case "Robot destroyed":
                case "Rescue impossible":
                case "Rescue failed":
                case "Reactive metal exposed":
                case "Cold or heat warning":
                case "Storm":
                case "Notice": return LogPriority.ActNow;
                case "Storage loss":
                case "Rescue":
                case "Haul waiting":
                case "Player order wins":
                case "Milestone reached":
                case "Dusty season":
                case "Global dust storm": return LogPriority.Watch;
                default: return LogPriority.Info;
            }
        }
    }

    public sealed class OperationRecord
    {
        public int Id;
        /// <summary>Version 55: how much the entry needs the player.</summary>
        public LogPriority Priority;
        /// <summary>"Robot 12", "Plant 471.5", "Colony".</summary>
        public string Subject = "";
        /// <summary>What the record is about: "Rule fired", "Rulebook synced", "Plant blocked", "Vent".</summary>
        public string Quantity = "";
        public double Value;
        public string Unit = "";
        public string Source = "";
        public double ObservedAtHours;
        /// <summary>Game hours when the colony knew it; negative until then.</summary>
        public double KnownAtHours = -1.0;
        /// <summary>The robot that observed it, or -1.</summary>
        public int RobotId = -1;
        public int RulebookVersion;
        public int RuleId;
        public string Text = "";

        public bool IsKnown => KnownAtHours >= 0.0;
    }

    /// <summary>The Operations log: bounded, in order, with ids that never repeat. Pure bookkeeping.</summary>
    public sealed class OperationsLog
    {
        public const int Capacity = 400;
        readonly List<OperationRecord> records = new List<OperationRecord>();
        int nextId = 1;

        public IReadOnlyList<OperationRecord> Records => records;
        public int NextId => nextId;

        public OperationRecord Add(OperationRecord r)
        {
            r.Id = nextId++;
            records.Add(r);
            if (records.Count > Capacity) records.RemoveAt(0);
            return r;
        }

        /// <summary>The robot has synced: what it observed while cut off is known now.</summary>
        public int MarkKnown(int robotId, double nowHours)
        {
            int n = 0;
            foreach (OperationRecord r in records)
                if (r.RobotId == robotId && r.KnownAtHours < 0.0)
                {
                    r.KnownAtHours = nowHours;
                    n++;
                }
            return n;
        }

        public void Restore(IEnumerable<OperationRecord> saved, int next)
        {
            records.Clear();
            int top = 0;
            if (saved != null)
                foreach (OperationRecord r in saved)
                    if (r != null) { records.Add(r); top = Math.Max(top, r.Id); }
            nextId = Math.Max(next, top + 1);
        }
    }

    /// <summary>The rulebook a new mission ships with: three rules with rationales, physically motivated (mission operations 5.5, MO-14). Numbers are TBD game rules.</summary>
    public static class StarterRulebook
    {
        public const string Note = "Starter rulebook";

        public static List<Rule> Rules()
        {
            return new List<Rule>
            {
                new Rule
                {
                    Id = 1, Name = "Night safing below 25 % battery",
                    Rationale = "Cold cuts the usable capacity of an LFP battery, so a robot caught out at night below a quarter charge can strand and freeze its cells.",
                    Scope = RuleScopeKind.AllRobots, CooldownHours = 2.0,
                    Conditions = new List<RuleCondition>
                    {
                        new RuleCondition { Kind = ConditionKind.RobotBatteryBelow, Value = 25.0 },
                        new RuleCondition { Kind = ConditionKind.IsNight },
                    },
                    Response = new RuleResponse { Kind = ResponseKind.SafeMode },
                },
                new Rule
                {
                    Id = 2, Name = "Return to the hub when cut off",
                    Rationale = "A robot that has been out of contact for hours can no longer be told anything: it comes in to a charger, where it is safe and can be reached again.",
                    Scope = RuleScopeKind.AllRobots, CooldownHours = 6.0,
                    Conditions = new List<RuleCondition> { new RuleCondition { Kind = ConditionKind.CutOffForHours, Value = 6.0 } },
                    Response = new RuleResponse { Kind = ResponseKind.SafeMode },
                },
                new Rule
                {
                    Id = 3, Name = "Tell me when a plant is stuck",
                    Rationale = "A plant blocked for half a day is wasting power and time; the colony asks for help after that long, as it does for a stuck task.",
                    Scope = RuleScopeKind.Plant, PlantTile = -1, PlantSlot = -1,   // every plant
                    Conditions = new List<RuleCondition>
                    {
                        new RuleCondition { Kind = ConditionKind.PlantBlockedBy, Blocker = IndustrialBlocker.OutputStorageUnavailable, Value = 12.0 },
                    },
                    Response = new RuleResponse { Kind = ResponseKind.Notify, Text = "A plant has been blocked for over 12 hours." },
                    CooldownHours = 12.0,
                },
            };
        }
    }
}

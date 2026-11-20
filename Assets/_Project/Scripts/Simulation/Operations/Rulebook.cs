using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Operations
{
    /// <summary>Three-valued truth: a condition on something not yet known is Unknown and never fires a rule (U4).</summary>
    public enum Truth { False, True, Unknown }

    /// <summary>
    /// Everything a rule may look at for one subject (a robot or a plant), and nothing else: values the colony or the
    /// robot actually knows. A null value is unknown. The orchestrator builds one per subject per step (U4).
    /// </summary>
    public sealed class KnownState
    {
        public double? RobotBatteryPct;
        public double? RobotConditionPct;
        public bool? IsNight;
        public double? HoursCutOff;
        public double? GridBatteryPct;
        public bool? Brownout;
        public double? SurfaceTemperatureC;
        public bool? StellarFlare;
        public IndustrialBlocker? PlantBlocker;
        public double? PlantBlockedHours;
        public double? PlantOutputBufferPct;
        /// <summary>Kg of a material in a tile's depot, or null when not known now.</summary>
        public Func<int, Material, double?> DepotKg = (t, m) => null;
    }

    /// <summary>Reads one condition against known state. Pure.</summary>
    public static class RuleEvaluator
    {
        public static Truth Evaluate(RuleCondition c, KnownState s)
        {
            switch (c.Kind)
            {
                case ConditionKind.RobotBatteryBelow: return Compare(s.RobotBatteryPct, v => v < c.Value);
                case ConditionKind.RobotBatteryAbove: return Compare(s.RobotBatteryPct, v => v > c.Value);
                case ConditionKind.RobotConditionBelow: return Compare(s.RobotConditionPct, v => v < c.Value);
                case ConditionKind.GridBatteryBelow: return Compare(s.GridBatteryPct, v => v < c.Value);
                case ConditionKind.BrownoutNow: return s.Brownout.HasValue ? (s.Brownout.Value ? Truth.True : Truth.False) : Truth.Unknown;
                case ConditionKind.IsNight: return s.IsNight.HasValue ? (s.IsNight.Value ? Truth.True : Truth.False) : Truth.Unknown;
                case ConditionKind.IsDay: return s.IsNight.HasValue ? (s.IsNight.Value ? Truth.False : Truth.True) : Truth.Unknown;
                case ConditionKind.CutOffForHours: return Compare(s.HoursCutOff, v => v > c.Value);
                case ConditionKind.DepotStockBelow: return Compare(s.DepotKg?.Invoke(c.Tile, c.Material), v => v < c.Value);
                case ConditionKind.DepotStockAbove: return Compare(s.DepotKg?.Invoke(c.Tile, c.Material), v => v > c.Value);
                case ConditionKind.PlantBlockedBy:
                    if (!s.PlantBlocker.HasValue) return Truth.Unknown;
                    // Value 0: blocked at all; else blocked for more than Value hours.
                    return s.PlantBlocker.Value == c.Blocker && (c.Value <= 0.0 || (s.PlantBlockedHours ?? 0.0) > c.Value) ? Truth.True : Truth.False;
                case ConditionKind.PlantOutputBufferAbove: return Compare(s.PlantOutputBufferPct, v => v > c.Value);
                case ConditionKind.SurfaceTemperatureOutside: return Compare(s.SurfaceTemperatureC, v => v < c.Value || v > c.Value2);
                case ConditionKind.StellarFlareActive: return s.StellarFlare.HasValue ? (s.StellarFlare.Value ? Truth.True : Truth.False) : Truth.Unknown;
                default: return Truth.Unknown;
            }
        }

        static Truth Compare(double? value, Func<double, bool> test) =>
            !value.HasValue ? Truth.Unknown : (test(value.Value) ? Truth.True : Truth.False);

        /// <summary>All conditions hold. Any unknown one makes it not hold (a rule never fires on a guess).</summary>
        public static bool Holds(Rule rule, KnownState s)
        {
            foreach (RuleCondition c in rule.Conditions)
                if (Evaluate(c, s) != Truth.True) return false;
            return true;
        }
    }

    /// <summary>
    /// The versioned rulebook (mission operations 5.2). Every publish makes version n + 1 and the full history is kept, so the
    /// player can see what changed and when. A robot runs the version the colony network last synced to it.
    /// </summary>
    public sealed class Rulebook
    {
        readonly List<RulebookVersion> versions = new List<RulebookVersion>();

        /// <summary>The newest version number, or 0 when no rulebook has been published.</summary>
        public int CurrentNumber => versions.Count == 0 ? 0 : versions[versions.Count - 1].Number;

        public IReadOnlyList<RulebookVersion> History => versions;

        public RulebookVersion Current => versions.Count == 0 ? null : versions[versions.Count - 1];

        /// <summary>The rules of a version (null for 0 or an unknown number).</summary>
        public RulebookVersion Get(int number)
        {
            foreach (RulebookVersion v in versions)
                if (v.Number == number) return v;
            return null;
        }

        /// <summary>
        /// Publish the rules as the next version. Returns null on success, else why not (a rule without a rationale is refused,
        /// and nothing is published). Rules are copied: the caller's objects are not kept.
        /// </summary>
        public string Publish(IEnumerable<Rule> rules, string note, double nowHours)
        {
            var copy = new List<Rule>();
            int nextId = 1;
            var used = new HashSet<int>();
            foreach (Rule r in rules ?? new Rule[0])
            {
                string problem = r.Problem();
                if (problem != null) return problem;
                Rule c = r.Copy();
                if (c.Id <= 0 || !used.Add(c.Id)) c.Id = 0;
                copy.Add(c);
            }
            foreach (Rule c in copy) if (c.Id > 0) nextId = Math.Max(nextId, c.Id + 1);
            foreach (Rule c in copy) if (c.Id == 0) { c.Id = nextId++; used.Add(c.Id); }
            versions.Add(new RulebookVersion { Number = CurrentNumber + 1, EditedAtHours = nowHours, Note = note ?? "", Rules = copy });
            return null;
        }

        public void Restore(IEnumerable<RulebookVersion> saved)
        {
            versions.Clear();
            if (saved == null) return;
            foreach (RulebookVersion v in saved)
                if (v != null) versions.Add(v);
            versions.Sort((a, b) => a.Number.CompareTo(b.Number));
        }

        /// <summary>
        /// The rule that wins for a subject: the first, top to bottom, that applies to it, is enabled, holds on its known state,
        /// and either is already active or has cooled down (<paramref name="canStart"/>).
        /// </summary>
        public static Rule Winner(RulebookVersion version, Func<Rule, bool> appliesTo, KnownState state,
                                  Func<Rule, bool> isActive, Func<Rule, bool> canStart)
        {
            if (version == null) return null;
            foreach (Rule r in version.Rules)
            {
                if (!r.Enabled || !appliesTo(r)) continue;
                if (!RuleEvaluator.Holds(r, state)) continue;
                if (isActive(r) || canStart(r)) return r;
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// Robot attributes (spec v2 §6). 1 = a General robot. Core attributes apply to every robot; specialized ones
    /// set how well a robot does one kind of work.
    /// </summary>
    public enum RobotAttribute
    {
        // Core
        Speed,
        Strength,
        EnergyEfficiency,
        Durability,
        Processing,
        // Specialized
        MiningPower,
        Construction,
        RepairEfficiency,
        TransportCapacity,
        ResearchCapability,
        SensorQuality,
    }

    /// <summary>A robot kind's attributes (1 = General robot).</summary>
    public sealed class RobotProfile
    {
        readonly double[] values = new double[Enum.GetValues(typeof(RobotAttribute)).Length];

        public RobotProfile(RobotKind kind)
        {
            Kind = kind;
            for (int i = 0; i < values.Length; i++) values[i] = 1.0;
        }

        public RobotKind Kind { get; }

        public double this[RobotAttribute a] => values[(int)a];

        internal RobotProfile Set(RobotAttribute a, double v)
        {
            values[(int)a] = v;
            return this;
        }
    }

    /// <summary>
    /// One row of the compatibility table (spec v2 §2): a robot kind may do a kind of task from a milestone on,
    /// at <see cref="Factor"/> × the task's attribute. Secondary capabilities are unlocked later and work at a
    /// lower factor.
    /// </summary>
    public readonly struct Capability
    {
        public readonly RobotKind Robot;
        public readonly TaskKind Task;
        public readonly double Factor;
        public readonly int UnlockMilestone;
        public readonly bool Secondary;

        public Capability(RobotKind robot, TaskKind task, double factor = 1.0, int unlockMilestone = 0, bool secondary = false)
        {
            Robot = robot;
            Task = task;
            Factor = factor;
            UnlockMilestone = unlockMilestone;
            Secondary = secondary;
        }
    }

    /// <summary>Why a robot is shut down (spec v2 §7): each needs a different fix.</summary>
    public enum ShutdownKind
    {
        None,
        /// <summary>Battery at 0 %: it needs charge (or, away from any charger, it is stranded).</summary>
        Energy,
        /// <summary>Condition at 0 %: it needs repair.</summary>
        Mechanical,
        /// <summary>Critical operational damage: it needs repair even with condition left.</summary>
        CriticalDamage,
    }

    /// <summary>
    /// The one place for robot data (spec v2 §0 "centralize tunable configuration"): attributes per kind, the
    /// compatibility table, which attribute drives each task, and how condition lowers performance. Every number is a
    /// TBD balance placeholder (D4) except where the code says otherwise; the values keep phase 1–4 behaviour
    /// (General 1, specialists 2 at their own job).
    /// </summary>
    public static class RobotCatalog
    {
        static readonly Dictionary<RobotKind, RobotProfile> Profiles = new Dictionary<RobotKind, RobotProfile>
        {
            { RobotKind.General, new RobotProfile(RobotKind.General) },
            { RobotKind.Construction, new RobotProfile(RobotKind.Construction).Set(RobotAttribute.Construction, 2.0).Set(RobotAttribute.Strength, 1.5) },
            { RobotKind.Mining, new RobotProfile(RobotKind.Mining).Set(RobotAttribute.MiningPower, 2.0).Set(RobotAttribute.Strength, 1.5)
                                                                   .Set(RobotAttribute.Durability, 1.25).Set(RobotAttribute.Speed, 0.8) },
            { RobotKind.Maintenance, new RobotProfile(RobotKind.Maintenance).Set(RobotAttribute.RepairEfficiency, 2.0).Set(RobotAttribute.SensorQuality, 1.25) },
            { RobotKind.Transport, new RobotProfile(RobotKind.Transport).Set(RobotAttribute.TransportCapacity, 3.0).Set(RobotAttribute.Speed, 1.5)
                                                                         .Set(RobotAttribute.Strength, 1.5) },
            { RobotKind.Research, new RobotProfile(RobotKind.Research).Set(RobotAttribute.ResearchCapability, 2.0).Set(RobotAttribute.SensorQuality, 2.0)
                                                                       .Set(RobotAttribute.Processing, 1.5) },
        };

        /// <summary>
        /// Compatibility table. General robots do every basic task. Specialists start with their own role only;
        /// later milestones add secondary roles at half rate (TBD). Transport has no tasks until logistics is designed.
        /// </summary>
        public static readonly IReadOnlyList<Capability> Capabilities = new List<Capability>
        {
            new Capability(RobotKind.General, TaskKind.Build),
            new Capability(RobotKind.General, TaskKind.Salvage),
            new Capability(RobotKind.General, TaskKind.Survey),
            new Capability(RobotKind.General, TaskKind.Mine),
            new Capability(RobotKind.General, TaskKind.Manufacture),

            new Capability(RobotKind.Construction, TaskKind.Build),
            new Capability(RobotKind.Construction, TaskKind.Salvage),
            new Capability(RobotKind.Construction, TaskKind.Manufacture),
            new Capability(RobotKind.Mining, TaskKind.Mine),
            new Capability(RobotKind.Research, TaskKind.Survey),

            // Secondary capabilities, unlocked by research (milestones, D8). TBD.
            new Capability(RobotKind.Mining, TaskKind.Salvage, 0.5, 4, true),
            new Capability(RobotKind.Research, TaskKind.Mine, 0.5, 4, true),
            new Capability(RobotKind.Construction, TaskKind.Survey, 0.5, 5, true),
            new Capability(RobotKind.Maintenance, TaskKind.Build, 0.5, 5, true),
        };

        public static RobotProfile Profile(RobotKind kind) => Profiles.TryGetValue(kind, out RobotProfile p) ? p : Profiles[RobotKind.General];

        /// <summary>The attribute that sets how fast a kind of task goes.</summary>
        public static RobotAttribute TaskAttribute(TaskKind task)
        {
            switch (task)
            {
                case TaskKind.Mine: return RobotAttribute.MiningPower;
                case TaskKind.Survey: return RobotAttribute.SensorQuality;
                default: return RobotAttribute.Construction;   // build, salvage, Workshop work
            }
        }

        /// <summary>The capability a kind has for a task at this many milestones, or null if it can't do it.</summary>
        public static Capability? Find(RobotKind robot, TaskKind task, int milestones)
        {
            Capability? best = null;
            foreach (Capability c in Capabilities)
                if (c.Robot == robot && c.Task == task && milestones >= c.UnlockMilestone && (!best.HasValue || c.Factor > best.Value.Factor))
                    best = c;
            return best;
        }

        public static bool CanDo(RobotKind robot, TaskKind task, int milestones) => Find(robot, task, milestones).HasValue;

        /// <summary>A kind's work rate at a task, before condition and upgrades (0 if it can't do it).</summary>
        public static double Efficiency(RobotKind robot, TaskKind task, int milestones)
        {
            Capability? c = Find(robot, task, milestones);
            return c.HasValue ? c.Value.Factor * Profile(robot)[TaskAttribute(task)] : 0.0;
        }

        // ---- Condition and performance (spec v2 §8) ----

        /// <summary>Above this condition a robot works at full rate. TBD.</summary>
        public const double HealthyCondition = 0.70;
        /// <summary>Rate at condition 0 from the gradual decline alone. TBD.</summary>
        public const double WornRate = 0.5;
        /// <summary>Critical thresholds and the extra factor below each. TBD.</summary>
        public const double CriticalCondition = 0.30, CriticalFactor = 0.8, SevereCondition = 0.10, SevereFactor = 0.6;

        /// <summary>
        /// Performance from condition: full above <see cref="HealthyCondition"/>, falling linearly to
        /// <see cref="WornRate"/> at 0, with extra penalties below the critical thresholds.
        /// </summary>
        public static double ConditionFactor(double condition)
        {
            double c = Math.Max(0.0, Math.Min(1.0, condition));
            double f = c >= HealthyCondition ? 1.0 : WornRate + (1.0 - WornRate) * c / HealthyCondition;
            if (c < CriticalCondition) f *= CriticalFactor;
            if (c < SevereCondition) f *= SevereFactor;
            return f;
        }

        /// <summary>
        /// How much work a robot does per hour at a task (spec v2 §6 combined productivity): kind and attribute
        /// (via the table), upgrades, condition, and an environment factor (1 until stage D).
        /// </summary>
        public static double Productivity(Robot r, TaskKind task, int milestones, double environment = 1.0)
        {
            double e = Efficiency(r.Kind, task, milestones);
            if (e <= 0.0) return 0.0;
            double upgrades = r.Has(RobotModule.FastDrive) ? RobotTech.FastDriveFactor : 1.0;
            return e * upgrades * ConditionFactor(r.Condition) * environment;
        }

        /// <summary>A robot's attribute with its upgrades (faster drive raises Speed).</summary>
        public static double AttributeOf(Robot r, RobotAttribute a)
        {
            double v = Profile(r.Kind)[a];
            if (a == RobotAttribute.Speed && r.Has(RobotModule.FastDrive)) v *= RobotTech.FastDriveFactor;
            return v;
        }
    }
}

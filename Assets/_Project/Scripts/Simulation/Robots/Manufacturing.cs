using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>Upgrades fitted at the Workshop (design D14: modules are upgrade slots, not separately worn parts).</summary>
    [Flags]
    public enum RobotModule
    {
        None = 0,
        /// <summary>A bigger battery pack: capacity × <see cref="RobotTech.BigBatteryFactor"/>.</summary>
        BigBattery = 1,
        /// <summary>Stronger motors: survey, mining and Workshop work × <see cref="RobotTech.FastDriveFactor"/>.</summary>
        FastDrive = 2,
        /// <summary>Shielding against flares: flare wear × <see cref="RobotTech.ShieldingFactor"/>.</summary>
        Shielding = 4,
        /// <summary>Thermal insulation: the battery stays at least at <see cref="RobotTech.InsulatedBatteryC"/> (spec v2 §11).</summary>
        ThermalInsulation = 8,
    }

    public enum JobKind { Robot, Upgrade, Refine }

    /// <summary>What a Workshop job takes: materials (kg), build parts (Wh) and robot work (hours).</summary>
    public sealed class Recipe
    {
        public readonly List<(Material Material, double Kg)> Inputs = new List<(Material, double)>();
        public double PartsWh;
        public double WorkHours;

        public Recipe Add(Material m, double kg)
        {
            if (kg > 0.0) Inputs.Add((m, kg));
            return this;
        }
    }

    /// <summary>One job in the Workshop queue. Inputs are taken when it is ordered and given back if it is cancelled.</summary>
    public sealed class ManufacturingJob
    {
        public JobKind Kind;
        public RobotKind RobotKind;
        public bool SodiumBattery;
        public bool AluminiumWiring;
        public int RobotId;
        public RobotModule Module;
        public Material Output;
        public double OutputKg;
        public Recipe Recipe;
        public double DoneHours;

        public double Progress => Recipe.WorkHours > 0.0 ? Math.Min(1.0, DoneHours / Recipe.WorkHours) : 1.0;

        public string Describe()
        {
            switch (Kind)
            {
                case JobKind.Robot:
                    return $"{RobotKind} robot ({(SodiumBattery ? "Na-ion" : "Li-ion")} battery, {(AluminiumWiring ? "aluminium" : "copper")} wiring)";
                case JobKind.Upgrade:
                    return $"{RobotTech.ModuleName(Module)} for R{RobotId}";
                default:
                    return $"{OutputKg:0} kg {Materials.Name(Output).ToLowerInvariant()}";
            }
        }
    }

    /// <summary>
    /// Technology (design D8): milestones (energy quotas, D7) decide what the Workshop can make; recipes and module
    /// effects. Every number here is a TBD placeholder (D4) except where a source is named.
    /// </summary>
    public static class RobotTech
    {
        // ---- Unlocks by milestone (TBD) ----

        /// <summary>Milestone that unlocks making each robot kind. Transport stays locked: transportation is deferred (spec §11).</summary>
        public static int UnlockMilestone(RobotKind kind)
        {
            switch (kind)
            {
                case RobotKind.General: return 0;
                case RobotKind.Construction:
                case RobotKind.Mining:
                case RobotKind.Research: return 1;
                case RobotKind.Maintenance: return 2;
                default: return int.MaxValue;
            }
        }

        public static int UnlockMilestone(RobotModule module) =>
            module == RobotModule.BigBattery || module == RobotModule.ThermalInsulation ? 2
            : module == RobotModule.FastDrive || module == RobotModule.Shielding ? 3 : int.MaxValue;

        /// <summary>An insulated battery is kept at least this warm, °C. TBD.</summary>
        public const double InsulatedBatteryC = 0.0;

        /// <summary>Charging station building: build parts, Wh. TBD.</summary>
        public const double ChargingStationPartsWh = 500_000.0;

        /// <summary>Refining a substitute: aluminium (for copper) at 1, sodium (for lithium) at 2.</summary>
        public static int UnlockMilestone(Material refined) =>
            refined == Material.Aluminium ? 1 : refined == Material.Sodium ? 2 : int.MaxValue;

        /// <summary>Group capacity: the placeholder start, +2 at milestones 2, 4 and 6 (TBD).</summary>
        public static int GroupCapacity(int milestones) =>
            Workforce.DefaultGroupCapacity + 2 * ((milestones >= 2 ? 1 : 0) + (milestones >= 4 ? 1 : 0) + (milestones >= 6 ? 1 : 0));

        // ---- Module effects (TBD) ----

        public const double BigBatteryFactor = 1.5;
        public const double FastDriveFactor = 1.5;
        public const double ShieldingFactor = 0.5;

        /// <summary>
        /// A Na-ion battery of the same mass holds ~160/250 of a Li-ion one (CATL gen-1 160 Wh/kg vs ~250 Wh/kg Li-ion
        /// cells: docs/physics/notes/energy_sources.md §13).
        /// </summary>
        public const double SodiumCapacityRatio = 160.0 / 250.0;

        public static string ModuleName(RobotModule m) =>
            m == RobotModule.BigBattery ? "Bigger battery" : m == RobotModule.FastDrive ? "Faster drive" : m == RobotModule.Shielding ? "Flare shielding"
            : m == RobotModule.ThermalInsulation ? "Thermal insulation" : m.ToString();

        // ---- Recipes (TBD) ----

        /// <summary>Workshop building: build parts, Wh. TBD.</summary>
        public const double WorkshopPartsWh = 2_000_000.0;

        /// <summary>A new robot; manufacture costs more parts than a full repair (spec §7).</summary>
        public static Recipe Robot(RobotKind kind, bool sodium, bool aluminium)
        {
            var r = new Recipe { PartsWh = 50_000.0, WorkHours = 24.0 }
                .Add(Material.Iron, 400.0).Add(Material.Rock, 200.0)
                .Add(aluminium ? Material.Aluminium : Material.Copper, 40.0)
                .Add(sodium ? Material.Sodium : Material.Lithium, sodium ? 6.0 : 2.0);
            if (kind == RobotKind.Mining) r.Add(Material.Iron, 100.0);   // heavier tools
            return r;
        }

        public static Recipe Upgrade(RobotModule module, bool sodium, bool aluminium)
        {
            var r = new Recipe { PartsWh = 5_000.0, WorkHours = 6.0 };
            switch (module)
            {
                case RobotModule.BigBattery: return r.Add(sodium ? Material.Sodium : Material.Lithium, sodium ? 3.0 : 1.0);
                case RobotModule.FastDrive: return r.Add(aluminium ? Material.Aluminium : Material.Copper, 10.0).Add(Material.Iron, 20.0);
                case RobotModule.Shielding: return r.Add(Material.Iron, 50.0).Add(Material.Water, 100.0);
                case RobotModule.ThermalInsulation: return r.Add(Material.Rock, 30.0);
                default: return r;
            }
        }

        /// <summary>Batch size of a refining job, kg.</summary>
        public static double RefineBatchKg(Material output) => output == Material.Aluminium ? 10.0 : 2.0;

        /// <summary>
        /// Aluminium from silicate rock (~16 % Al in anorthosite, so ~6 kg rock per kg; electrolysis ~15 kWh/kg: background
        /// figures, not re-sourced) and sodium from rock (~2 % Na in plagioclase; ~45 kg rock per kg). TBD.
        /// </summary>
        public static Recipe Refine(Material output)
        {
            double kg = RefineBatchKg(output);
            return output == Material.Aluminium
                ? new Recipe { PartsWh = 15_000.0 * kg, WorkHours = 4.0 }.Add(Material.Rock, 6.0 * kg)
                : new Recipe { PartsWh = 15_000.0 * kg, WorkHours = 4.0 }.Add(Material.Rock, 45.0 * kg);
        }
    }
}

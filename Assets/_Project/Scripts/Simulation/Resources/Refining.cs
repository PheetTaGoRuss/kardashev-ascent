using System.Collections.Generic;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// The refining processes (director 2026-11-15: refining as it is done on Earth, in steps, taking days to weeks).
    /// Saved as integers: append new members at the end only.
    /// </summary>
    public enum RefineProcess
    {
        /// <summary>Step 1 for aluminium, from highland anorthosite (about 30 % alumina): lime-soda sintering and leaching.</summary>
        AluminaFromAnorthosite,
        /// <summary>Step 1 from ordinary rock (basalt, about 14 % alumina): the same process, twice the rock.</summary>
        AluminaFromRock,
        /// <summary>Step 2: Hall–Héroult electrolysis of alumina dissolved in molten cryolite at about 960 °C.</summary>
        Aluminium,
        /// <summary>Step 3 (optional): three-layer electrolytic refining (Hoopes process) to 99.99 % aluminium.</summary>
        PureAluminium,
        /// <summary>Sodium from salt: Downs cell, electrolysis of molten sodium chloride at about 600 °C.</summary>
        SodiumFromSalt,
        /// <summary>Sodium from rock where there is no salt: chlorinating feldspar, then electrolysis. Slow and costly (TBD).</summary>
        SodiumFromRock,
    }

    /// <summary>What a plant does with the gas its process gives off (mission operations MO-06). Saved as an integer.</summary>
    public enum ByproductPolicy
    {
        /// <summary>Release it (logged; chlorine is toxic and is warned about). The default, as refining always was.</summary>
        Vent,
        /// <summary>Keep it: it goes to the output buffer and from there to a gas tank, and a full store stops the plant.</summary>
        Store,
    }

    /// <summary>Which refinery module runs a process.</summary>
    public enum RefineryKind { AluminaPlant, Electrolyser }

    /// <summary>One process: what a batch takes and makes, how long and how much electricity.</summary>
    public sealed class RefineRecipe
    {
        public RefineProcess Process;
        public RefineryKind Module;
        public string Name;
        public string What;
        /// <summary>Material in, kg per batch (one input per process).</summary>
        public Material Input;
        public double InputKg;
        public Material Output;
        public double OutputKg;
        /// <summary>Solid leftovers kept as slag (what is left of the rock), kg.</summary>
        public double SlagKg;
        /// <summary>What leaves as gas (oxygen or CO₂ from electrolysis, chlorine from salt), kg; not kept yet.</summary>
        public double VentedKg;
        public string VentedName;
        /// <summary>The gas that leaves (<see cref="VentedKg"/> of it): stored in a tank or vented, by the plant's policy. None when nothing leaves.</summary>
        public Material? Byproduct;
        /// <summary>Reaction enthalpy per kg of output, Wh: the chemical energy the product holds above the input (NIST-JANAF / CRC standard formation enthalpies). Zero where the process is not a sourced chemical change.</summary>
        public double ReactionWhPerKgOutput;
        public double EnergyWh;
        public double Hours;

        /// <summary>Waste heat of a batch, Wh: the electricity that did not end up as chemical energy in the product (first law).</summary>
        public double WasteHeatWh => EnergyWh - ReactionWhPerKgOutput * OutputKg;

        /// <summary>Waste heat while a batch runs, W.</summary>
        public double WasteHeatW => Hours > 0.0 ? WasteHeatWh / Hours : 0.0;

        /// <summary>Grid power while a batch runs, W (its energy spread over its hours).</summary>
        public double PowerW => Hours > 0.0 ? EnergyWh / Hours : 0.0;
    }

    /// <summary>
    /// The numbers behind each process. Chemistry and energy are real (sources in each comment and in
    /// docs/physics/physics-verification.md); batch sizes and hours are game rules sized so a batch takes days to weeks.
    /// Mass is kept: input = output + slag + vented gas.
    /// </summary>
    public static class Refining
    {
        /// <summary>Al₂O₃ in anorthite (CaAl₂Si₂O₈): 101.96 / 278.21 = 36.6 %; anorthosite rock about 30 % (lunar highland soils 26–28 %).</summary>
        public const double AnorthositeAluminaShare = 0.30;
        /// <summary>Al₂O₃ in basalt and ordinary crust, about 14 % (Rudnick &amp; Gao 2003 upper crust: 15.4 %).</summary>
        public const double RockAluminaShare = 0.14;
        /// <summary>Share of the alumina a sinter-and-leach process recovers (game rule, TBD: 80–90 % is reported for lime-soda sintering).</summary>
        public const double AluminaRecovery = 0.85;
        /// <summary>Alumina per kg of aluminium in a Hall–Héroult pot line: 1.93 kg (stoichiometric 2 × 26.98 / 101.96 → 1.889).</summary>
        public const double AluminaPerAluminium = 1.93;
        /// <summary>Hall–Héroult electricity, Wh per kg of aluminium: about 13.5 kWh/kg (world average 13–15; IAI).</summary>
        public const double SmeltWhPerKg = 13_500.0;
        /// <summary>
        /// Making alumina from anorthosite, Wh per kg of alumina. The Bayer process on bauxite takes about 3 kWh/kg
        /// (10–12 GJ/t); sintering anorthosite at about 1 300 °C takes more: game rule, TBD.
        /// </summary>
        public const double AluminaWhPerKg = 6_000.0;
        /// <summary>Three-layer refining to 99.99 %, Wh per kg: about 17–18 kWh/kg.</summary>
        public const double PureWhPerKg = 17_500.0;
        /// <summary>Salt per kg of sodium: 58.44 / 22.99 = 2.54 kg; the rest leaves as chlorine gas.</summary>
        public const double SaltPerSodium = 58.44 / 22.99;
        /// <summary>Downs cell electricity, Wh per kg of sodium: about 10 kWh/kg.</summary>
        public const double SodiumWhPerKg = 10_000.0;
        /// <summary>Enthalpy to split alumina into aluminium and oxygen: 1675.7 kJ per mol Al2O3 = 837.9 kJ per mol Al, 31.06 MJ/kg = 8 628 Wh/kg (formation enthalpy from NIST-JANAF / CRC; recalled, to verify).</summary>
        public const double AluminiumReactionWhPerKg = 8_628.0;
        /// <summary>Enthalpy to split salt into sodium and chlorine: 411.2 kJ per mol NaCl, 17.89 MJ/kg of Na = 4 969 Wh/kg (recalled, to verify).</summary>
        public const double SodiumReactionWhPerKg = 4_969.0;

        static readonly Dictionary<RefineProcess, RefineRecipe> Recipes = Make();

        static Dictionary<RefineProcess, RefineRecipe> Make()
        {
            var d = new Dictionary<RefineProcess, RefineRecipe>();
            double alumina = 40.0;
            double anorthosite = alumina / (AnorthositeAluminaShare * AluminaRecovery);
            d[RefineProcess.AluminaFromAnorthosite] = new RefineRecipe
            {
                Process = RefineProcess.AluminaFromAnorthosite, Module = RefineryKind.AluminaPlant, Name = "Alumina from anorthosite",
                What = "Sinter anorthosite with lime and soda at about 1 300 °C, leach out the aluminate, then grow alumina crystals over 2–3 days.",
                Input = Material.Anorthosite, InputKg = anorthosite, Output = Material.Alumina, OutputKg = alumina,
                SlagKg = anorthosite - alumina, EnergyWh = AluminaWhPerKg * alumina, Hours = 72.0,
            };
            double rock = alumina / (RockAluminaShare * AluminaRecovery);
            d[RefineProcess.AluminaFromRock] = new RefineRecipe
            {
                Process = RefineProcess.AluminaFromRock, Module = RefineryKind.AluminaPlant, Name = "Alumina from rock",
                What = "The same process on ordinary rock: it holds half the alumina, so it takes twice the rock and longer.",
                Input = Material.Rock, InputKg = rock, Output = Material.Alumina, OutputKg = alumina,
                SlagKg = rock - alumina, EnergyWh = AluminaWhPerKg * alumina * 1.5, Hours = 96.0,
            };
            double al = 20.0;
            d[RefineProcess.Aluminium] = new RefineRecipe
            {
                Process = RefineProcess.Aluminium, Module = RefineryKind.Electrolyser, Name = "Aluminium (99.7 %)",
                What = "Hall–Héroult: alumina dissolved in molten cryolite at about 960 °C, split by a big current. The oxygen leaves as gas.",
                Input = Material.Alumina, InputKg = al * AluminaPerAluminium, Output = Material.Aluminium, OutputKg = al,
                VentedKg = al * AluminaPerAluminium - al, VentedName = "oxygen", Byproduct = Material.Oxygen, EnergyWh = SmeltWhPerKg * al, Hours = 168.0,
                ReactionWhPerKgOutput = AluminiumReactionWhPerKg,
            };
            double pure = 10.0;
            d[RefineProcess.PureAluminium] = new RefineRecipe
            {
                Process = RefineProcess.PureAluminium, Module = RefineryKind.Electrolyser, Name = "High-purity aluminium (99.99 %)",
                What = "Three-layer refining: molten aluminium alloy at the bottom, salt in the middle, pure aluminium collects on top.",
                Input = Material.Aluminium, InputKg = pure * 1.02, Output = Material.PureAluminium, OutputKg = pure,
                SlagKg = pure * 0.02, EnergyWh = PureWhPerKg * pure, Hours = 240.0,
            };
            double na = 10.0;
            d[RefineProcess.SodiumFromSalt] = new RefineRecipe
            {
                Process = RefineProcess.SodiumFromSalt, Module = RefineryKind.Electrolyser, Name = "Sodium from salt",
                What = "Downs cell: molten salt at about 600 °C split into liquid sodium and chlorine gas.",
                Input = Material.Salt, InputKg = na * SaltPerSodium, Output = Material.Sodium, OutputKg = na,
                VentedKg = na * SaltPerSodium - na, VentedName = "chlorine", Byproduct = Material.Chlorine, EnergyWh = SodiumWhPerKg * na, Hours = 96.0,
                ReactionWhPerKgOutput = SodiumReactionWhPerKg,
            };
            double naRock = 5.0;
            d[RefineProcess.SodiumFromRock] = new RefineRecipe
            {
                Process = RefineProcess.SodiumFromRock, Module = RefineryKind.Electrolyser, Name = "Sodium from rock",
                What = "Where there is no salt: chlorinate the feldspar in rock (about 2–3 % sodium), then electrolyse. Slow and power-hungry.",
                Input = Material.Rock, InputKg = naRock * 45.0, Output = Material.Sodium, OutputKg = naRock,
                SlagKg = naRock * 44.0, EnergyWh = 30_000.0 * naRock, Hours = 240.0,
            };
            return d;
        }

        public static RefineRecipe Of(RefineProcess p) => Recipes[p];

        /// <summary>
        /// The recipe as an industrial process: every output named, product, slag and the gas that leaves (mission
        /// operations MO-06), with its heat balance (MO-08): electricity = chemical energy in the product + waste heat.
        /// </summary>
        public static IndustrialProcess ProcessOf(RefineProcess p)
        {
            RefineRecipe r = Of(p);
            var proc = new IndustrialProcess(p.ToString(), r.Name);
            proc.AddInput(r.Input, r.InputKg);
            proc.AddOutput(r.Output, r.OutputKg);
            if (r.SlagKg > 0.0) proc.AddOutput(Material.Slag, r.SlagKg);
            if (r.VentedKg > 0.0) proc.AddOutput(r.Byproduct ?? Material.Slag, r.VentedKg);
            proc.ElectricityKWhPerBatch = r.EnergyWh / 1000.0;
            proc.ProductEnthalpyKWhPerBatch = r.ReactionWhPerKgOutput * r.OutputKg / 1000.0;
            proc.WasteHeatKWhPerBatch = r.WasteHeatWh / 1000.0;
            return proc;
        }

        public static IEnumerable<RefineRecipe> All => Recipes.Values;

        public static readonly RefineProcess[] Processes =
        {
            RefineProcess.AluminaFromAnorthosite, RefineProcess.AluminaFromRock, RefineProcess.Aluminium,
            RefineProcess.PureAluminium, RefineProcess.SodiumFromSalt, RefineProcess.SodiumFromRock,
        };

        public static string ModuleName(RefineryKind k) => k == RefineryKind.AluminaPlant ? "Alumina plant" : "Electrolysis cell";

        /// <summary>The usual process that makes a material (for old saves and simple orders).</summary>
        public static RefineProcess? For(Material output) =>
            output == Material.Aluminium ? RefineProcess.Aluminium
            : output == Material.Sodium ? RefineProcess.SodiumFromSalt
            : output == Material.Alumina ? RefineProcess.AluminaFromAnorthosite
            : output == Material.PureAluminium ? RefineProcess.PureAluminium
            : (RefineProcess?)null;
    }
}

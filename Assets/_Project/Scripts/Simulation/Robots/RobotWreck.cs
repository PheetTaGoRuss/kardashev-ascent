using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>Why a robot was destroyed (saved as an integer: append only).</summary>
    public enum DeathCause { ColdOrEmptyBattery, Heat, Radiation }

    /// <summary>
    /// What is left of a destroyed robot (OQ-9, decided 2026-11-25, director: "it becomes a material and some become electronic waste which up to how it die, for example,
    /// if it gets too much heat til melt down completely, there won't be anything left"). Pure functions of the robot's material bill, the cause and the peak temperature.
    /// Mass is conserved except above the boiling point of iron, where nothing is left.
    /// </summary>
    public static class WreckModel
    {
        // Thresholds, deg C. Sources: solder: SAC305 melts at 217-220 C and Sn-Pb eutectic at 183 C (IPC J-STD-006); polymers and insulation decompose at 300-400 C
        // (typical PVC, polyimide-free wire insulation and cell separators); Al 660.3, Nd 1024, Cu 1084.6, Fe 1538 melting and Fe 2862 boiling (CRC Handbook of Chemistry
        // and Physics); the rock of a hull (silicate) is taken to melt with the iron (game rule: it slumps to slag at 1200-1500 C).
        public const double SolderC = 220.0, PolymerC = 350.0, AluminiumMeltC = 660.3, RareEarthMeltC = 1024.0, CopperMeltC = 1084.6, IronMeltC = 1538.0, IronBoilC = 2862.0;

        /// <summary>Share of a robot's copper or aluminium wiring that is circuit boards and harness (the electronics), the rest being structural conductor and motor windings. Assumption, TBD.</summary>
        public const double ElectronicsShareOfWiring = 0.25;

        /// <summary>The robot's material bill, kg by material (the manufacturing recipe with copper wiring and a lithium cell; a Fast drive adds its rare-earth magnets).</summary>
        public static Dictionary<Material, double> BillOf(RobotKind kind, bool fastDrive)
        {
            var bill = new Dictionary<Material, double>();
            foreach (var (m, kg) in RobotTech.Robot(kind, false, false).Inputs) bill[m] = (bill.TryGetValue(m, out double v) ? v : 0.0) + kg;
            if (fastDrive) bill[Material.RareEarth] = (bill.TryGetValue(Material.RareEarth, out double r) ? r : 0.0) + RobotTech.RareEarthPerDriveKg;
            return bill;
        }

        /// <summary>
        /// What lies in the wreck: the materials that can be recovered, kg. Cold, an empty battery and radiation leave the structure intact and turn the electronics and the cell into
        /// <see cref="Material.ElectronicWaste"/>; heat melts parts above their thresholds into <see cref="Material.Slag"/> (electronics above solder, aluminium at 660, rare earths at
        /// 1024, copper at 1085, iron and hull rock at 1538), and above iron's boiling point nothing is left.
        /// </summary>
        public static Dictionary<Material, double> Residue(Dictionary<Material, double> bill, double peakC)
        {
            var left = new Dictionary<Material, double>();
            void Put(Material m, double kg) { if (kg > 1e-12) left[m] = (left.TryGetValue(m, out double v) ? v : 0.0) + kg; }
            double Of(Material m) => bill.TryGetValue(m, out double v) ? v : 0.0;
            if (peakC > IronBoilC) return left;   // nothing at all
            double wiring = Of(Material.Copper) + Of(Material.Aluminium);
            double electronics = (Of(Material.Lithium) + Of(Material.Sodium)) + ElectronicsShareOfWiring * wiring;
            double cuStruct = Of(Material.Copper) * (1.0 - ElectronicsShareOfWiring), alStruct = Of(Material.Aluminium) * (1.0 - ElectronicsShareOfWiring);
            double slag = 0.0;
            // electronics and the cell
            if (peakC > SolderC) slag += electronics; else Put(Material.ElectronicWaste, electronics);
            // structure
            if (peakC > AluminiumMeltC) slag += alStruct; else Put(Material.Aluminium, alStruct);
            if (peakC > CopperMeltC) slag += cuStruct; else Put(Material.Copper, cuStruct);
            if (peakC > RareEarthMeltC) slag += Of(Material.RareEarth); else Put(Material.RareEarth, Of(Material.RareEarth));
            if (peakC > IronMeltC) slag += Of(Material.Iron) + Of(Material.Rock); else { Put(Material.Iron, Of(Material.Iron)); Put(Material.Rock, Of(Material.Rock)); }
            // anything else in the bill (water in shielding and so on) is kept as it is
            foreach (var kv in bill)
                if (kv.Key != Material.Iron && kv.Key != Material.Rock && kv.Key != Material.Copper && kv.Key != Material.Aluminium && kv.Key != Material.Lithium && kv.Key != Material.Sodium && kv.Key != Material.RareEarth)
                    Put(kv.Key, kv.Value);
            Put(Material.Slag, slag);
            return left;
        }

        /// <summary>The electronics survive (so unsynced findings can be read back) when the cause was not heat and the peak stayed under the solder threshold.</summary>
        public static bool ElectronicsSurvive(DeathCause cause, double peakC) => cause != DeathCause.Heat && peakC <= SolderC;

        public static double Total(Dictionary<Material, double> d) { double t = 0.0; foreach (var kv in d) t += kv.Value; return t; }
    }
}

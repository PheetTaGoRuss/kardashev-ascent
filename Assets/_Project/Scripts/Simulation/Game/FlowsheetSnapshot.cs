using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>What one process did over the window (mission operations 6): mass in, what came out, and whether it closes.</summary>
    public sealed class ProcessFlow
    {
        public RefineProcess Process;
        public string Name = "";
        public Material Input;
        public double InKg;
        public Material Product;
        public double ProductKg;
        public double SlagKg;
        public Material? Gas;
        public double GasStoredKg;
        public double VentedKg;

        public double OutKg => ProductKg + SlagKg + GasStoredKg + VentedKg;

        /// <summary>Out over in, percent. A batch is counted whole when it finishes (its input with its outputs), so this reads 100 when mass is conserved.</summary>
        public double ClosurePct => InKg > 0.0 ? 100.0 * OutKg / InKg : 100.0;
    }

    /// <summary>A plant that is stopped, and why.</summary>
    public sealed class BlockedPlant
    {
        public int Tile, Slot;
        public IndustrialBlocker Blocker;
        public string Reason = "";
    }

    /// <summary>
    /// The Flowsheet view's read model (mission operations 6, U8): the last week of mass flows from the ground through hauls
    /// and plants into products, byproducts and vents, with the closure, waste heat and blockers. A value, not a live object.
    /// </summary>
    public sealed class FlowsheetSnapshot
    {
        public int Days;
        public readonly List<(Material Material, double Kg)> Mined = new List<(Material, double)>();
        public readonly List<(Material Material, double Kg)> Hauled = new List<(Material, double)>();
        public readonly List<ProcessFlow> Processes = new List<ProcessFlow>();
        public readonly List<BlockedPlant> Blocked = new List<BlockedPlant>();
        public double WasteHeatKWh;
        public double WasteHeatNowW;

        public double TotalInKg { get { double t = 0.0; foreach (ProcessFlow p in Processes) t += p.InKg; return t; } }
        public double TotalOutKg { get { double t = 0.0; foreach (ProcessFlow p in Processes) t += p.OutKg; return t; } }

        /// <summary>Mass out over mass in across every process, percent (100 when nothing ran).</summary>
        public double ClosurePct => TotalInKg > 0.0 ? 100.0 * TotalOutKg / TotalInKg : 100.0;
    }
}

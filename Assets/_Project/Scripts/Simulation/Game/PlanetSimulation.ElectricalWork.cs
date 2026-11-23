using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>What a robot does in an electrical task (saved as integers: append only).</summary>
    public enum ElectricalAction { InspectLine = 1, RepairLine = 2, InstallCircuit = 3 }

    /// <summary>An electrical job waiting for a robot (saved in version 46).</summary>
    public sealed class ElectricalOrder
    {
        public ElectricalAction Action;
        public int Tile;
        /// <summary>The slot of the line (repair) or of the station (circuit); 0 for an inspection.</summary>
        public int Slot;
        /// <summary>The saved blueprint to install (circuit); 0 otherwise.</summary>
        public int Arg;
    }

    /// <summary>
    /// Electrical work as robot tasks (electrical redesign: inspect a line, repair a failed line, install a circuit). A robot walks to the
    /// tile, works for a time, and the same command that was instant before is applied. Repair takes its materials when it finishes; if the
    /// stock cannot pay by then the order is dropped and the reason is kept in <see cref="LastElectricalProblem"/>. Work hours are game
    /// rules, TBD; the cost of inspecting and installing is the robot's time only.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>Hours of robot work for each job (game rule, TBD).</summary>
        public static double ElectricalWorkHours(ElectricalAction a) => a == ElectricalAction.RepairLine ? 2.0 : 1.0;

        readonly List<ElectricalOrder> electricalOrders = new List<ElectricalOrder>();

        /// <summary>Why the last electrical order was dropped (empty when none was).</summary>
        public string LastElectricalProblem { get; private set; } = "";

        static WorkTask ElectricalTask(ElectricalOrder o) => new WorkTask(TaskKind.Electrical, o.Tile, (int)o.Action * 1000 + o.Slot);

        public IReadOnlyList<ElectricalOrder> ElectricalOrders() => electricalOrders.Select(o => new ElectricalOrder { Action = o.Action, Tile = o.Tile, Slot = o.Slot, Arg = o.Arg }).ToList();

        /// <summary>How much of an order is done, 0 to 1.</summary>
        public double ElectricalProgress(ElectricalOrder o) =>
            fieldWork.TryGetValue(ElectricalTask(o), out double h) ? Math.Min(1.0, h / ElectricalWorkHours(o.Action)) : 0.0;

        bool HasElectricalOrder(ElectricalAction a, int tile, int slot) => electricalOrders.Exists(o => o.Action == a && o.Tile == tile && o.Slot == slot);

        public bool OrderLineInspection(int tileId)
        {
            if (WiredGrid) EnsureWiring();
            if (!LineExists(tileId) || HasElectricalOrder(ElectricalAction.InspectLine, tileId, 0)) return false;
            electricalOrders.Add(new ElectricalOrder { Action = ElectricalAction.InspectLine, Tile = tileId });
            Settle(null);
            return true;
        }

        /// <summary>Orders a repair of a failed line. Null when ordered, else why not (the stock must already hold the repair bill).</summary>
        public string OrderLineRepair(int tileId, int slot)
        {
            if (!wireBook.CableState.ContainsKey((tileId, slot))) return "That line needs no repair.";
            if (HasElectricalOrder(ElectricalAction.RepairLine, tileId, slot)) return "A robot is already on it.";
            MaterialBillCheck(RepairBill(tileId, slot), out string missing);
            if (missing != null) return "Missing " + missing + ".";
            electricalOrders.Add(new ElectricalOrder { Action = ElectricalAction.RepairLine, Tile = tileId, Slot = slot });
            Settle(null);
            return null;
        }

        /// <summary>Orders a robot to install a saved circuit in a station. Null when ordered, else why not.</summary>
        public string OrderCircuitInstall(int tileId, int slot, int blueprintIndex)
        {
            if (!stations.Contains((tileId, slot))) return "There is no charging station here.";
            if (Build.IsQueued(tileId, slot)) return "The station is not finished.";
            if (blueprintIndex < 0 || blueprintIndex >= circuitLibrary.Count) return "No such blueprint.";
            string fit = CircuitFitProblem(circuitLibrary[blueprintIndex]);
            if (fit != null) return fit;
            if (HasElectricalOrder(ElectricalAction.InstallCircuit, tileId, slot)) return "A robot is already on it.";
            electricalOrders.Add(new ElectricalOrder { Action = ElectricalAction.InstallCircuit, Tile = tileId, Slot = slot, Arg = blueprintIndex });
            Settle(null);
            return null;
        }

        public bool CancelElectricalOrder(ElectricalAction action, int tileId, int slot)
        {
            int i = electricalOrders.FindIndex(o => o.Action == action && o.Tile == tileId && o.Slot == slot);
            if (i < 0) return false;
            WorkTask t = ElectricalTask(electricalOrders[i]);
            electricalOrders.RemoveAt(i);
            Workforce.Tasks.MarkCancelled(t);
            fieldWork.Remove(t);
            Settle(null);
            return true;
        }

        void MaterialBillCheck(Resources.MaterialBill bill, out string missing)
        {
            var parts = new List<string>();
            foreach (Resources.Material m in Enum.GetValues(typeof(Resources.Material)))
                if (bill[m] > 0.0 && Materials[m] < bill[m] - 1e-9)
                    parts.Add($"{bill[m] - Materials[m]:0.#} kg {Resources.Materials.Name(m).ToLowerInvariant()}");
            missing = parts.Count == 0 ? null : string.Join(", ", parts);
        }

        void AddElectricalTasks(List<WorkTask> tasks)
        {
            for (int k = 0; k < electricalOrders.Count; k++)
            {
                var t = ElectricalTask(electricalOrders[k]);
                tasks.Add(new WorkTask(TaskKind.Electrical, t.Tile, t.Slot, k));
            }
        }

        /// <summary>Finishes the orders whose work is done.</summary>
        void FinishElectricalOrders()
        {
            foreach (var o in electricalOrders.ToArray())
            {
                WorkTask t = ElectricalTask(o);
                if (!fieldWork.TryGetValue(t, out double h) || h < ElectricalWorkHours(o.Action) - 1e-9) continue;
                fieldWork.Remove(t);
                electricalOrders.Remove(o);
                string problem = null;
                switch (o.Action)
                {
                    case ElectricalAction.InspectLine: if (!InspectLine(o.Tile)) problem = "The line is gone."; break;
                    case ElectricalAction.RepairLine: problem = RepairLine(o.Tile, o.Slot); break;
                    case ElectricalAction.InstallCircuit: problem = InstallStationCircuit(o.Tile, o.Slot, o.Arg); break;
                }
                LastElectricalProblem = problem ?? "";
            }
        }

        void CaptureElectricalOrders(SimulationState state)
        {
            foreach (var o in electricalOrders)
                state.ElectricalOrders.Add(new ElectricalOrderRecord { Action = (int)o.Action, Tile = o.Tile, Slot = o.Slot, Arg = o.Arg });
        }

        void RestoreElectricalOrders(SimulationState state)
        {
            electricalOrders.Clear();
            if (state.Version < 46 || state.ElectricalOrders == null) return;
            foreach (var r in state.ElectricalOrders)
                electricalOrders.Add(new ElectricalOrder { Action = (ElectricalAction)r.Action, Tile = r.Tile, Slot = r.Slot, Arg = r.Arg });
        }
    }
}

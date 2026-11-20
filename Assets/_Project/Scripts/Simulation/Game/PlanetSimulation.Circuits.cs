using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Circuits;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Player-built circuits in the colony (electrical redesign stage 9, gate G4: the first host is the charging station).
    /// A station with an installed circuit charges robots only while the circuit's "enable" port is high; a station without one
    /// behaves as before. The circuit never writes any other system: the station reads one yes or no from it each step.
    /// </summary>
    public sealed partial class PlanetSimulation : ICircuitCommands
    {
        /// <summary>What the charging station offers a circuit (game rule, TBD): room for twelve parts, a 16 x 12 board, and an "enable" port.</summary>
        public static readonly CircuitHostSpec StationHost = new CircuitHostSpec
        {
            Name = "Charging station", MaxComponents = 12, MaxGridWidth = 16, MaxGridHeight = 12, RequiredPorts = new List<string> { "enable" }
        };

        /// <summary>The port is high when its pin sits at or above this share of the highest source voltage (game rule, TBD).</summary>
        public const double EnableThreshold = 0.5;

        readonly List<CircuitBlueprint> circuitLibrary = new List<CircuitBlueprint>();
        readonly SortedDictionary<(int Tile, int Slot), CircuitInstance> stationCircuits = new SortedDictionary<(int, int), CircuitInstance>();
        readonly Dictionary<(int Tile, int Slot), CircuitResult> circuitResults = new Dictionary<(int, int), CircuitResult>();
        readonly Dictionary<(int Tile, int Slot), bool> circuitEnabled = new Dictionary<(int, int), bool>();
        int nextCircuitInstanceId = 1;

        /// <summary>True for a station with no circuit, or with one whose enable port is high.</summary>
        bool StationEnabled((int Tile, int Slot) key) =>
            !stationCircuits.ContainsKey(key) || (circuitEnabled.TryGetValue(key, out bool on) && on);

        // ---- commands (values in, values out; nothing live crosses the boundary) ----

        public int SaveCircuitBlueprint(CircuitBlueprint design)
        {
            var copy = design.Clone();
            for (int i = 0; i < circuitLibrary.Count; i++)
                if (circuitLibrary[i].Name == copy.Name) { circuitLibrary[i] = copy; return i; }
            circuitLibrary.Add(copy);
            return circuitLibrary.Count - 1;
        }

        public IReadOnlyList<CircuitBlueprint> CircuitBlueprints() => circuitLibrary.Select(b => b.Clone()).ToList();

        public bool DeleteCircuitBlueprint(int index)
        {
            if (index < 0 || index >= circuitLibrary.Count) return false;
            circuitLibrary.RemoveAt(index);
            return true;
        }

        public CircuitResult TestCircuit(CircuitBlueprint design)
        {
            var inst = new CircuitInstance { Design = design.Clone() };
            inst.ResetState();
            return CircuitSolver.Solve(inst.Design, inst.State);
        }

        public string CircuitFitProblem(CircuitBlueprint design) => CircuitCompatibility.Check(design, StationHost);

        public string InstallStationCircuit(int tileId, int slot, int blueprintIndex)
        {
            if (!stations.Contains((tileId, slot))) return "There is no charging station here.";
            if (Build.IsQueued(tileId, slot)) return "The station is not finished.";
            if (blueprintIndex < 0 || blueprintIndex >= circuitLibrary.Count) return "No such blueprint.";
            var inst = CircuitInstance.Install(circuitLibrary[blueprintIndex], StationHost, nextCircuitInstanceId, tileId * 1000 + slot, out string refusal);
            if (inst == null) return refusal;
            nextCircuitInstanceId++;
            stationCircuits[(tileId, slot)] = inst;
            EvaluateCircuit((tileId, slot), 0.0);
            return null;
        }

        public bool RemoveStationCircuit(int tileId, int slot)
        {
            circuitResults.Remove((tileId, slot));
            circuitEnabled.Remove((tileId, slot));
            return stationCircuits.Remove((tileId, slot));
        }

        public bool SetCircuitSwitch(int tileId, int slot, int componentId, bool closed)
        {
            if (!stationCircuits.TryGetValue((tileId, slot), out var inst)) return false;
            var comp = inst.Design.Component(componentId);
            if (comp == null || (comp.Type != CircuitComponentType.Switch && comp.Type != CircuitComponentType.PushButton)) return false;
            inst.StateOf(componentId).Closed = closed;
            EvaluateCircuit((tileId, slot), 0.0);
            return true;
        }

        public StationCircuitView StationCircuit(int tileId, int slot)
        {
            var key = (tileId, slot);
            if (!stationCircuits.TryGetValue(key, out var inst)) return null;
            circuitResults.TryGetValue(key, out var res);
            return new StationCircuitView
            {
                Design = inst.Design.Clone(),
                BlueprintVersion = inst.BlueprintVersion,
                Enabled = StationEnabled(key),
                Result = res,
                Faults = inst.Faults.Select(f => new CircuitFault { Code = f.Code, Component = f.Component, AtHours = f.AtHours }).ToList(),
                SwitchClosed = inst.State.Where(s => inst.Design.Component(s.Id) != null &&
                    (inst.Design.Component(s.Id).Type == CircuitComponentType.Switch || inst.Design.Component(s.Id).Type == CircuitComponentType.PushButton))
                    .ToDictionary(s => s.Id, s => s.Closed),
            };
        }

        // ---- stepping ----

        void StepCircuits(double dtHours)
        {
            if (stationCircuits.Count == 0) return;
            foreach (var key in stationCircuits.Keys.ToList())
            {
                if (!stations.Contains(key)) { RemoveStationCircuit(key.Tile, key.Slot); continue; }
                EvaluateCircuit(key, dtHours);
            }
        }

        void EvaluateCircuit((int Tile, int Slot) key, double dtHours)
        {
            var inst = stationCircuits[key];
            var blown = new HashSet<int>(inst.State.Where(s => s.Blown).Select(s => s.Id));
            var res = CircuitSolver.Solve(inst.Design, inst.State, TimeHours);
            foreach (var s in inst.State)
                if (s.Blown && !blown.Contains(s.Id))
                    inst.Faults.Add(new CircuitFault { Code = "FuseBlown", Component = s.Id, AtHours = TimeHours });
            if (dtHours > 0.0)
            {
                CircuitSolver.Advance(inst.Design, inst.State, res, dtHours);
                inst.HoursRun += dtHours;
            }
            circuitResults[key] = res;
            circuitEnabled[key] = EnablePortHigh(inst.Design, res);
        }

        static bool EnablePortHigh(CircuitBlueprint design, CircuitResult res)
        {
            var port = design.Ports.Find(p => p.Name == "enable");
            if (port == null || !res.Solved) return false;
            double top = 0.0;
            foreach (var c in design.Components) if (CircuitParts.IsSource(c.Type)) top = Math.Max(top, c.P1);
            if (top <= 0.0) return false;
            return res.VoltageAt(port.Component, port.Pin) >= EnableThreshold * top;
        }

        void CaptureCircuits(SimulationState state)
        {
            foreach (var b in circuitLibrary) state.CircuitLibrary.Add(b.Clone());
            foreach (var kv in stationCircuits) state.CircuitInstances.Add(new CircuitInstanceRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Instance = kv.Value });
            state.NextCircuitInstanceId = nextCircuitInstanceId;
        }

        void RestoreCircuits(SimulationState state)
        {
            circuitLibrary.Clear();
            stationCircuits.Clear();
            circuitResults.Clear();
            circuitEnabled.Clear();
            nextCircuitInstanceId = Math.Max(1, state.NextCircuitInstanceId);
            if (state.Version < 44) return;
            if (state.CircuitLibrary != null) foreach (var b in state.CircuitLibrary) circuitLibrary.Add(b);
            if (state.CircuitInstances != null)
                foreach (var r in state.CircuitInstances)
                    if (r.Instance != null && stations.Contains((r.Tile, r.Slot)))
                    {
                        stationCircuits[(r.Tile, r.Slot)] = r.Instance;
                        nextCircuitInstanceId = Math.Max(nextCircuitInstanceId, r.Instance.Id + 1);
                        EvaluateCircuit((r.Tile, r.Slot), 0.0);
                    }
        }
    }
}

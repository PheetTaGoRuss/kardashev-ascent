using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What a plant reports (mission operations MO-07): why it is not running, and what its buffers hold.
    /// </summary>
    public sealed class PlantReport
    {
        public int Tile, Slot;
        public ByproductPolicy Policy;
        /// <summary>None while running or idle with nothing to do.</summary>
        public IndustrialBlocker Blocker;
        public string Reason = "";
        public double InputBufferKg, OutputBufferKg;
        /// <summary>Waste heat while a batch runs, W (MO-08).</summary>
        public double WasteHeatW;
    }

    /// <summary>
    /// Plants with buffers (mission operations 4.2, MO-06, MO-07, MO-08). A refinery module has an input buffer and an
    /// output buffer, each a place in the inventory. A batch starts only when its input is in the input buffer and every
    /// output, the gas included when it is stored, fits in the output buffer; otherwise the plant reports an industrial
    /// blocker. A conveyor on the same tile moves inputs in from the tile's depot and outputs out to it (a haul crosses
    /// tiles; a conveyor does not). The gas goes to a gas tank or is vented, by the plant's policy; venting is logged.
    /// Each batch's waste heat is counted (a readout; the planet does not warm yet).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly Dictionary<(int Tile, int Slot), ByproductPolicy> plantPolicies = new Dictionary<(int, int), ByproductPolicy>();
        readonly Dictionary<(int Tile, int Slot), (IndustrialBlocker Blocker, string Why)> plantStatus =
            new Dictionary<(int, int), (IndustrialBlocker, string)>();
        readonly double[] ventedKg = new double[Resources.Materials.All.Length];
        double industrialWasteHeatKWh;
        readonly FlowLedger flows = new FlowLedger();

        /// <summary>What an output buffer holds of each storage class, kg. Game rules, TBD.</summary>
        public const double PlantOutputBulkKg = 2000.0, PlantOutputHazardousKg = 500.0, PlantOutputGasKg = 200.0;

        /// <summary>Waste heat of the batches running now, W (a readout: no planet warming yet; EN-09 and EN-12 stay future).</summary>
        public double IndustrialWasteHeatW { get; private set; }

        /// <summary>Waste heat of every batch finished so far, kWh.</summary>
        public double IndustrialWasteHeatKWh => industrialWasteHeatKWh;

        /// <summary>The forcing that waste heat would add over the whole planet, W/m2 (a readout, EN-09).</summary>
        public double IndustrialWasteForcingWm2 => Climate.WasteHeat.ForcingWm2(IndustrialWasteHeatW, planetRadiusM);

        /// <summary>Gas released to the air so far, kg.</summary>
        public double VentedKg(Material material) => ventedKg[(int)material];

        static int BufferId(int tile, int slot) => tile * 128 + slot;
        static PlaceId InputOf((int Tile, int Slot) m) => new PlaceId(PlaceKind.BufferIn, BufferId(m.Tile, m.Slot));
        static PlaceId OutputOf((int Tile, int Slot) m) => new PlaceId(PlaceKind.BufferOut, BufferId(m.Tile, m.Slot));

        public ByproductPolicy ByproductPolicyOf(int tileId, int slot) =>
            plantPolicies.TryGetValue((tileId, A(tileId, slot)), out ByproductPolicy p) ? p : ByproductPolicy.Vent;

        /// <summary>Set what a plant does with its gas. Switching to vent frees a plant that a full store stopped, and says so.</summary>
        public bool SetByproductPolicy(int tileId, int slot, ByproductPolicy policy)
        {
            var key = (tileId, A(tileId, slot));
            if (!refineryModules.ContainsKey(key)) return false;
            if (policy == ByproductPolicy.Vent) plantPolicies.Remove(key);
            else plantPolicies[key] = policy;
            industrialReports.Add(TimeHours, Refining.ModuleName(KindOf(refineryModules[key])), IndustrialBlocker.None,
                policy == ByproductPolicy.Vent ? "Byproduct gas will be vented (and logged)." : "Byproduct gas will be stored: a full store stops the plant.");
            plantStatus.Remove(key);
            StartBatches();
            return true;
        }

        public PlantReport PlantReportAt(int tileId, int slot)
        {
            var key = (tileId, A(tileId, slot));
            if (!refineryModules.ContainsKey(key)) return null;
            var report = new PlantReport { Tile = key.Item1, Slot = key.Item2, Policy = ByproductPolicyOf(key.Item1, key.Item2) };
            if (plantStatus.TryGetValue(key, out var st)) { report.Blocker = st.Blocker; report.Reason = st.Why; }
            report.InputBufferKg = inventories.MassAt(InputOf(key));
            report.OutputBufferKg = inventories.MassAt(OutputOf(key));
            if (batches.TryGetValue(key, out var b)) report.WasteHeatW = Refining.Of(b.Process).WasteHeatW;
            return report;
        }

        /// <summary>The batch the next idle plant of this kind would take: the first queued one, else an auto one below its target; null if none.</summary>
        RefineProcess? PendingProcess(RefineryKind kind)
        {
            foreach (RefineProcess q in refineQueue)
                if (Refining.Of(q).Module == kind) return q;
            foreach (RefineProcess p in Refining.Processes)
            {
                RefineRecipe r = Refining.Of(p);
                if (r.Module != kind || AutoRefineTarget(p) <= 0.0) continue;
                if (Materials[r.Output] + Running(r.Output) >= AutoRefineTarget(p)) continue;
                return p;
            }
            return null;
        }

        // ---- starting a batch ----

        ProcessResult TryStartBatch((int Tile, int Slot) m, RefineProcess process)
        {
            RefineRecipe r = Refining.Of(process);
            PlaceId inP = InputOf(m), outP = OutputOf(m);
            // The conveyor tops the input buffer up from the tile's depot.
            MaterialStock depot = StockAt(m.Tile);
            double have = inventories.StockOf(inP)[r.Input];
            if (have < r.InputKg - 1e-9)
            {
                double take = depot.Take(r.Input, r.InputKg - have);
                if (take > 0.0) inventories.StockOf(inP).Add(r.Input, take);
            }
            bool store = ByproductPolicyOf(m.Tile, m.Slot) == ByproductPolicy.Store;
            var ports = new ProcessPorts
            {
                Available = mat => inventories.StockOf(inP)[mat],
                Take = (mat, kg) => inventories.StockOf(inP).TryTake(mat, kg),
                CanStoreAll = outputs => OutputsFit(outP, outputs, r.Byproduct, store),
                Store = (mat, kg) => { },   // the outputs are made when the batch ends
            };
            // Power is checked while the batch runs (it pauses in a brownout), not at its start.
            var env = new ProcessEnvironment(double.MaxValue / 4.0, double.MaxValue / 4.0, double.MaxValue / 4.0, true);
            ProcessResult started = IndustrialProcessor.TryRun(Refining.ProcessOf(process), ports, env);
            return started;
        }

        /// <summary>The last week of flows, for the Flowsheet view (a value; the view draws it and computes nothing).</summary>
        public FlowsheetSnapshot FlowsheetSnapshot(int days = FlowLedger.DaysKept)
        {
            var snap = new FlowsheetSnapshot { Days = days, WasteHeatKWh = industrialWasteHeatKWh, WasteHeatNowW = IndustrialWasteHeatW };
            var mined = new Dictionary<Material, double>();
            var hauled = new Dictionary<Material, double>();
            var proc = new Dictionary<RefineProcess, ProcessFlow>();
            ProcessFlow Of(int key)
            {
                var p = (RefineProcess)key;
                if (!proc.TryGetValue(p, out ProcessFlow f))
                {
                    RefineRecipe r = Refining.Of(p);
                    proc[p] = f = new ProcessFlow { Process = p, Name = r.Name, Input = r.Input, Product = r.Output, Gas = r.Byproduct };
                }
                return f;
            }
            foreach (FlowEntry e in flows.Last(TimeHours, days))
            {
                switch (e.Stage)
                {
                    case FlowStage.Mined: mined[e.Material] = (mined.TryGetValue(e.Material, out double a) ? a : 0.0) + e.Kg; break;
                    case FlowStage.Hauled: hauled[e.Material] = (hauled.TryGetValue(e.Material, out double b) ? b : 0.0) + e.Kg; break;
                    case FlowStage.PlantIn: Of(e.Key).InKg += e.Kg; break;
                    case FlowStage.PlantProduct: Of(e.Key).ProductKg += e.Kg; break;
                    case FlowStage.PlantSlag: Of(e.Key).SlagKg += e.Kg; break;
                    case FlowStage.PlantGasStored: Of(e.Key).GasStoredKg += e.Kg; break;
                    case FlowStage.PlantVented: Of(e.Key).VentedKg += e.Kg; break;
                }
            }
            foreach (var kv in mined) snap.Mined.Add((kv.Key, kv.Value));
            foreach (var kv in hauled) snap.Hauled.Add((kv.Key, kv.Value));
            snap.Mined.Sort((x, y) => ((int)x.Material).CompareTo((int)y.Material));
            snap.Hauled.Sort((x, y) => ((int)x.Material).CompareTo((int)y.Material));
            foreach (RefineProcess p in Refining.Processes)
                if (proc.TryGetValue(p, out ProcessFlow f)) snap.Processes.Add(f);
            foreach (var key in new List<(int Tile, int Slot)>(refineryModules.Keys))
            {
                PlantReport rep = PlantReportAt(key.Tile, key.Slot);
                if (rep != null && rep.Blocker != IndustrialBlocker.None)
                    snap.Blocked.Add(new BlockedPlant { Tile = key.Tile, Slot = key.Slot, Blocker = rep.Blocker, Reason = rep.Reason });
            }
            snap.Blocked.Sort((x, y) => x.Tile != y.Tile ? x.Tile.CompareTo(y.Tile) : x.Slot.CompareTo(y.Slot));
            return snap;
        }

        bool OutputsFit(PlaceId outP, IReadOnlyList<MaterialAmount> outputs, Material? gas, bool storeGas)
        {
            var need = new Dictionary<StorageClass, double>();
            foreach (MaterialAmount o in outputs)
            {
                if (gas.HasValue && o.Material == gas.Value && !storeGas) continue;   // vented: it does not need room
                StorageClass c = MaterialCatalog.PropertiesOf(o.Material).StorageClass;
                need[c] = (need.TryGetValue(c, out double n) ? n : 0.0) + o.Kg;
            }
            foreach (var kv in need)
            {
                double free = (ClassCapacityKg(outP, kv.Key) ?? double.PositiveInfinity) - inventories.MassAt(outP, kv.Key);
                if (kv.Value > free + 1e-9) return false;
            }
            return true;
        }

        // ---- finishing a batch ----

        void FinishBatch((int Tile, int Slot) m, RefineRecipe r)
        {
            PlaceId outP = OutputOf(m);
            inventories.Add(outP, r.Output, r.OutputKg);
            Know(Knowledge.Subject.Building(m.Tile, m.Slot), Knowledge.Quantity.PlantOutput, r.OutputKg, 0.0, Knowledge.Confidence.High, new Knowledge.Source(Knowledge.SourceKind.Telemetry));
            flows.Add(TimeHours, FlowStage.PlantIn, (int)r.Process, r.Input, r.InputKg);   // counted with its outputs, when the batch is done, so the closure reads exactly
            flows.Add(TimeHours, FlowStage.PlantProduct, (int)r.Process, r.Output, r.OutputKg);
            if (r.SlagKg > 0.0) { inventories.Add(outP, Material.Slag, r.SlagKg); flows.Add(TimeHours, FlowStage.PlantSlag, (int)r.Process, Material.Slag, r.SlagKg); }
            string gasNote = "";
            if (r.VentedKg > 0.0)
            {
                Material gas = r.Byproduct ?? Material.Slag;
                if (ByproductPolicyOf(m.Tile, m.Slot) == ByproductPolicy.Store && inventories.FreeFor(outP, gas) >= r.VentedKg - 1e-9)
                {
                    inventories.Add(outP, gas, r.VentedKg);
                    flows.Add(TimeHours, FlowStage.PlantGasStored, (int)r.Process, gas, r.VentedKg);
                    gasNote = $", {r.VentedKg:0} kg {r.VentedName} stored";
                }
                else
                {
                    ventedKg[(int)gas] += r.VentedKg;
                    flows.Add(TimeHours, FlowStage.PlantVented, (int)r.Process, gas, r.VentedKg);
                    gasNote = $", {r.VentedKg:0} kg {r.VentedName} vented" + (gas == Material.Chlorine ? " (WARNING: chlorine is toxic)" : "");
                }
            }
            industrialReports.Add(TimeHours, Refining.ModuleName(r.Module), IndustrialBlocker.None,
                $"{r.Name}: {r.OutputKg:0} kg made" + (r.SlagKg > 0.0 ? $", {r.SlagKg:0} kg slag" : "") + gasNote + ".");
            FlushOutputs(m);
        }

        /// <summary>The conveyor: what an output buffer holds goes to the tile's depot as far as the depot has room.</summary>
        void FlushOutputs((int Tile, int Slot) m)
        {
            PlaceId outP = OutputOf(m);
            if (!inventories.Has(outP)) return;
            MaterialStock buffer = inventories.StockOf(outP), depot = StockAt(m.Tile);
            foreach (Material mat in Resources.Materials.All)
            {
                double kg = buffer[mat];
                if (kg <= 1e-12) continue;
                double moved = Math.Min(kg, RoomAt(m.Tile, mat));
                if (moved <= 1e-12) continue;
                buffer.Take(mat, moved);
                depot.Add(mat, moved);
            }
        }

        void FlushAllOutputs()
        {
            foreach (var key in new List<(int Tile, int Slot)>(refineryModules.Keys))
                FlushOutputs(key);
        }

        /// <summary>A module taken down gives what is in its buffers back to its tile's depot.</summary>
        void ReleasePlantBuffers(int tileId, int slot)
        {
            var m = (tileId, slot);
            MaterialStock depot = StockAt(tileId);
            foreach (PlaceId p in new[] { InputOf(m), OutputOf(m) })
            {
                if (!inventories.Has(p)) continue;
                MaterialStock s = inventories.StockOf(p);
                foreach (Material mat in Resources.Materials.All)
                    if (s[mat] > 0.0) depot.Add(mat, s[mat]);
                inventories.Remove(p);
            }
            plantPolicies.Remove(m);
            plantStatus.Remove(m);
        }

        // ---- save ----

        void CapturePlants(SimulationState state)
        {
            foreach (var kv in plantPolicies)
                state.PlantPolicies.Add(new PlantPolicyRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Policy = kv.Value });
            foreach (Material m in Resources.Materials.All)
                if (ventedKg[(int)m] > 0.0)
                    state.VentedKg.Add(new MaterialRecord { Material = m, Kg = ventedKg[(int)m] });
            state.IndustrialWasteHeatKWh = industrialWasteHeatKWh;
            state.FlowEntries.AddRange(flows.Entries);
        }

        void RestorePlants(SimulationState state)
        {
            plantPolicies.Clear();
            plantStatus.Clear();
            Array.Clear(ventedKg, 0, ventedKg.Length);
            industrialWasteHeatKWh = 0.0;
            flows.Restore(state.Version >= 42 ? state.FlowEntries : null);
            if (state.Version < 40) return;
            if (state.PlantPolicies != null)
                foreach (PlantPolicyRecord r in state.PlantPolicies)
                    if (refineryModules.ContainsKey((r.Tile, r.Slot)) && r.Policy == ByproductPolicy.Store)
                        plantPolicies[(r.Tile, r.Slot)] = r.Policy;
            if (state.VentedKg != null)
                foreach (MaterialRecord r in state.VentedKg)
                    ventedKg[(int)r.Material] = Math.Max(0.0, r.Kg);
            industrialWasteHeatKWh = Math.Max(0.0, state.IndustrialWasteHeatKWh);
        }
    }
}

using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Refineries (director 2026-11-15): refining as on Earth, in steps that take days to weeks. Refinery modules (an
    /// Alumina plant, an Electrolysis cell) are built in slots touching a Robot Workshop, like upgrades to it; each
    /// finished module runs one batch at a time on grid power, so more modules make more at once. Batches start from the
    /// order queue, or by themselves to keep a material at a chosen amount ("auto"). A batch takes its input when it
    /// starts and pauses in a brownout.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly Dictionary<(int Tile, int Slot), BuildingType> refineryModules = new Dictionary<(int, int), BuildingType>();
        readonly Dictionary<(int Tile, int Slot), (RefineProcess Process, double DoneHours)> batches = new Dictionary<(int, int), (RefineProcess, double)>();
        readonly List<RefineProcess> refineQueue = new List<RefineProcess>();
        readonly Dictionary<RefineProcess, double> autoTargets = new Dictionary<RefineProcess, double>();

        /// <summary>A refining batch finished (process, kg made).</summary>
        public event Action<RefineProcess, double> RefineDone;

        /// <summary>Grid power the running batches draw, W.</summary>
        public double RefineryPowerW { get; private set; }

        public static bool IsRefinery(BuildingType t) => t == BuildingType.AluminaPlant || t == BuildingType.Electrolyser;

        public static RefineryKind KindOf(BuildingType t) => t == BuildingType.AluminaPlant ? RefineryKind.AluminaPlant : RefineryKind.Electrolyser;

        public static BuildingType BuildingOf(RefineryKind k) => k == RefineryKind.AluminaPlant ? BuildingType.AluminaPlant : BuildingType.Electrolyser;

        /// <summary>True when some slot of this footprint touches a slot of a Workshop on the same tile.</summary>
        bool TouchesWorkshop(int tileId, List<int> footprint)
        {
            TileSite site = SiteOf(tileId);
            double reach = site.Spacing * 1.25;
            foreach (var w in workshops)
            {
                if (w.Tile != tileId) continue;
                foreach (int ws in FootprintFor(BuildingType.Workshop, w.Tile, w.Slot))
                    foreach (int s in footprint)
                        if (System.Numerics.Vector2.Distance(site.SlotOffset(ws), site.SlotOffset(s)) <= reach)
                            return true;
            }
            return false;
        }

        /// <summary>Why a refinery module can't go in this slot (beyond the usual checks), or null.</summary>
        public string RefineryBlocker(BuildingType type, int tileId, int slot)
        {
            if (!IsRefinery(type)) return null;
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId)) return "No such slot.";
            if (!TouchesWorkshop(tileId, FootprintFor(type, tileId, slot)))
                return "A refinery module must touch a Robot Workshop (a slot next to it).";
            return null;
        }

        public bool PlaceRefinery(BuildingType type, int tileId, int slot)
        {
            if (!IsRefinery(type) || !CanPlace(type, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(type));
            refineryModules[(tileId, slot)] = type;
            Occupy(tileId, FootprintFor(type, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        bool RemoveRefinery(int tileId, int slot)
        {
            if (!refineryModules.TryGetValue((tileId, slot), out BuildingType type))
                return false;
            Dismantle(tileId, slot, type);
            refineryModules.Remove((tileId, slot));
            // A batch in the pot is lost with the module, as when a real cell is shut down cold (game rule).
            batches.Remove((tileId, slot));
            Vacate(tileId, slot);
            return true;
        }

        /// <summary>Finished modules of this kind (sorted, so batches go in a fixed order).</summary>
        List<(int Tile, int Slot)> FinishedModules(RefineryKind kind)
        {
            var list = new List<(int Tile, int Slot)>();
            foreach (var kv in refineryModules)
                if (KindOf(kv.Value) == kind && !Build.IsQueued(kv.Key.Tile, kv.Key.Slot))
                    list.Add(kv.Key);
            list.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : a.Slot.CompareTo(b.Slot));
            return list;
        }

        public int RefineryCount(RefineryKind kind) => FinishedModules(kind).Count;

        /// <summary>Why this process can't start now, or null (it can).</summary>
        public string RefineBlocker(RefineProcess process)
        {
            RefineRecipe r = Refining.Of(process);
            if (RefineryCount(r.Module) == 0)
                return $"Build an {Refining.ModuleName(r.Module)} next to the Robot Workshop.";
            if (Materials[r.Input] < r.InputKg - 1e-9)
                return $"Needs {r.InputKg:0} kg {Resources.Materials.Name(r.Input).ToLowerInvariant()} (have {Materials[r.Input]:0}).";
            return null;
        }

        /// <summary>Queue one batch. Returns null, or why it can't be queued (no module of its kind).</summary>
        public string OrderRefine(RefineProcess process)
        {
            RefineRecipe r = Refining.Of(process);
            if (RefineryCount(r.Module) == 0 && !HasModuleSite(r.Module))
            {
                string why = $"Build an {Refining.ModuleName(r.Module)} next to the Robot Workshop first.";
                ReportIndustry(IndustrialBlocker.EnvironmentUnavailable, why);
                return why;
            }
            refineQueue.Add(process);
            ReportIndustry(IndustrialBlocker.None, $"Queued {r.Name.ToLowerInvariant()}: {r.OutputKg:0} kg in {Days(r.Hours)}.");
            StartBatches();
            return null;
        }

        bool HasModuleSite(RefineryKind kind)
        {
            foreach (var kv in refineryModules)
                if (KindOf(kv.Value) == kind) return true;
            return false;
        }

        public IReadOnlyList<RefineProcess> RefineQueue => refineQueue;

        public bool CancelRefineOrder(int index)
        {
            if (index < 0 || index >= refineQueue.Count) return false;
            refineQueue.RemoveAt(index);
            return true;
        }

        /// <summary>Keep this much of the process's output in stock (0: off). Batches start by themselves when below it.</summary>
        public void SetAutoRefine(RefineProcess process, double keepKg)
        {
            if (keepKg > 0.0) autoTargets[process] = keepKg;
            else autoTargets.Remove(process);
            StartBatches();
        }

        public double AutoRefineTarget(RefineProcess process) => autoTargets.TryGetValue(process, out double kg) ? kg : 0.0;

        /// <summary>Every finished or planned module: where, its kind, what it runs and how far (0–1).</summary>
        public List<RefineryLine> RefineryLines()
        {
            var list = new List<RefineryLine>();
            foreach (var kv in refineryModules)
            {
                bool built = !Build.IsQueued(kv.Key.Tile, kv.Key.Slot);
                var line = new RefineryLine { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Kind = KindOf(kv.Value), Built = built };
                if (batches.TryGetValue(kv.Key, out var b))
                {
                    line.Process = b.Process;
                    line.Progress = Math.Min(1.0, b.DoneHours / Refining.Of(b.Process).Hours);
                    line.HoursLeft = Math.Max(0.0, Refining.Of(b.Process).Hours - b.DoneHours);
                }
                list.Add(line);
            }
            list.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : a.Slot.CompareTo(b.Slot));
            return list;
        }

        /// <summary>Idle finished modules take the next queued batch of their kind, else an auto one, if the input is there.</summary>
        void StartBatches()
        {
            foreach (RefineryKind kind in new[] { RefineryKind.AluminaPlant, RefineryKind.Electrolyser })
                foreach (var m in FinishedModules(kind))
                {
                    if (batches.ContainsKey(m)) continue;
                    RefineProcess? next = null;
                    for (int i = 0; i < refineQueue.Count; i++)
                    {
                        RefineRecipe r = Refining.Of(refineQueue[i]);
                        if (r.Module != kind || Materials[r.Input] < r.InputKg - 1e-9) continue;
                        next = refineQueue[i];
                        refineQueue.RemoveAt(i);
                        break;
                    }
                    if (!next.HasValue)
                        foreach (RefineProcess p in Refining.Processes)
                        {
                            RefineRecipe r = Refining.Of(p);
                            if (r.Module != kind || AutoRefineTarget(p) <= 0.0) continue;
                            if (Materials[r.Output] + Running(r.Output) >= AutoRefineTarget(p)) continue;
                            if (Materials[r.Input] < r.InputKg - 1e-9) continue;
                            next = p;
                            break;
                        }
                    if (!next.HasValue) continue;
                    RefineRecipe take = Refining.Of(next.Value);
                    Materials.Take(take.Input, take.InputKg);
                    batches[m] = (next.Value, 0.0);
                }
        }

        /// <summary>Output already being made, kg (so auto doesn't start too many).</summary>
        double Running(Material output)
        {
            double kg = 0.0;
            foreach (var b in batches.Values)
                if (Refining.Of(b.Process).Output == output) kg += Refining.Of(b.Process).OutputKg;
            return kg;
        }

        /// <summary>Run the batches for <paramref name="dt"/> hours: they pause in a brownout and draw their power otherwise.</summary>
        void AdvanceRefineries(double dt)
        {
            StartBatches();
            double powerW = 0.0;
            bool powered = RefineryPowerForTests ?? !Grid.IsBrownout;
            var done = new List<(int, int)>();
            foreach (var key in new List<(int Tile, int Slot)>(batches.Keys))
            {
                var b = batches[key];
                RefineRecipe r = Refining.Of(b.Process);
                if (!powered) continue;
                powerW += r.PowerW;
                b.DoneHours += dt;
                batches[key] = b;
                if (b.DoneHours >= r.Hours - 1e-9) done.Add(key);
            }
            RefineryPowerW = powerW;
            foreach (var key in done)
            {
                RefineRecipe r = Refining.Of(batches[key].Process);
                batches.Remove(key);
                Materials.Add(r.Output, r.OutputKg);
                if (r.SlagKg > 0.0) Materials.Add(Material.Slag, r.SlagKg);
                industrialReports.Add(TimeHours, Refining.ModuleName(r.Module), IndustrialBlocker.None,
                    $"{r.Name}: {r.OutputKg:0} kg made" + (r.SlagKg > 0.0 ? $", {r.SlagKg:0} kg slag" : "") +
                    (r.VentedKg > 0.0 ? $", {r.VentedKg:0} kg {r.VentedName} vented." : "."));
                RefineDone?.Invoke(r.Process, r.OutputKg);
            }
            if (done.Count > 0) StartBatches();
        }

        /// <summary>Tests: true or false overrides whether the grid can run the batches.</summary>
        internal bool? RefineryPowerForTests;

        static string Days(double hours) => hours >= 48.0 ? $"{hours / 24.0:0.#} days" : $"{hours:0} h";

        // ---- save ----

        void CaptureRefining(SimulationState state)
        {
            foreach (var kv in batches)
                state.RefineBatches.Add(new RefineBatchRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Process = kv.Value.Process, DoneHours = kv.Value.DoneHours });
            state.RefineQueue.AddRange(refineQueue);
            foreach (var kv in autoTargets)
                state.AutoRefine.Add(new AutoRefineRecord { Process = kv.Key, KeepKg = kv.Value });
        }

        void RestoreRefining(SimulationState state)
        {
            batches.Clear();
            refineQueue.Clear();
            autoTargets.Clear();
            if (state.RefineBatches != null)   // version 34
                foreach (RefineBatchRecord r in state.RefineBatches)
                    if (r != null && refineryModules.ContainsKey((r.Tile, r.Slot)) && Enum.IsDefined(typeof(RefineProcess), r.Process))
                        batches[(r.Tile, r.Slot)] = (r.Process, Math.Max(0.0, r.DoneHours));
            if (state.RefineQueue != null)
                foreach (RefineProcess p in state.RefineQueue)
                    if (Enum.IsDefined(typeof(RefineProcess), p)) refineQueue.Add(p);
            if (state.AutoRefine != null)
                foreach (AutoRefineRecord a in state.AutoRefine)
                    if (a != null && a.KeepKg > 0.0) autoTargets[a.Process] = a.KeepKg;
        }
    }

    /// <summary>One refinery module as the Manufacturing window shows it.</summary>
    public sealed class RefineryLine
    {
        public int Tile, Slot;
        public RefineryKind Kind;
        public bool Built;
        /// <summary>The batch running now, or null (idle).</summary>
        public RefineProcess? Process;
        public double Progress, HoursLeft;
    }
}

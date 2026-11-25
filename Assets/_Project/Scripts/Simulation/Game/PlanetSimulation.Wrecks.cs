using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>A destroyed robot lying where it stopped (OQ-9, decided 2026-11-25): its stock lies in the inventory place <c>Wreck id</c>, an open pile.</summary>
    public sealed class Wreck
    {
        public int Id, Tile, RobotId;
        public RobotKind Kind;
        public DeathCause Cause;
        /// <summary>The highest temperature the wreck has seen, C.</summary>
        public double PeakC;
        /// <summary>The robot's bill, kg (what the wreck started as), and the unsynced findings its electronics hold while they survive.</summary>
        public Dictionary<Material, double> BillKg = new Dictionary<Material, double>();
        public List<HeldFinding> Findings = new List<HeldFinding>();
        public bool FindingsLost;
        /// <summary>The metals have been brought home; the wreck waits to be rebuilt (only for a wreck whose structure survived).</summary>
        public bool Recovered;
        /// <summary>The structure survived: neither melted nor burnt (a soak death, not a heat one).</summary>
        public bool Rebuildable => Cause != DeathCause.Heat && PeakC <= WreckModel.AluminiumMeltC;
        public bool ElectronicsIntact => WreckModel.ElectronicsSurvive(Cause, PeakC);
    }

    public sealed partial class PlanetSimulation
    {
        readonly List<Wreck> wrecks = new List<Wreck>();
        int nextWreckId = 1;

        public IReadOnlyList<Wreck> Wrecks => wrecks;

        /// <summary>The cause from the air temperature where the robot stopped: hot air above the robot's range, cold air below it, else an empty battery.</summary>
        static DeathCause CauseAt(double airC) => airC > WreckModel.SolderC ? DeathCause.Heat : DeathCause.ColdOrEmptyBattery;   // a soak below 220 C leaves the electronics whole

        /// <summary>The robot's remains: lay the wreck, move its cargo into it, keep unsynced findings when the electronics survive. Returns what is left in kg and the lost findings.</summary>
        Wreck LayWreck(Robot r, double airC, out int lostFindings)
        {
            var w = new Wreck { Id = nextWreckId++, Tile = r.Tile, RobotId = r.Id, Kind = r.Kind, Cause = CauseAt(airC), PeakC = airC };
            w.BillKg = WreckModel.BillOf(r.Kind, r.Has(RobotModule.FastDrive));
            var place = PlaceId.Wreck(w.Id);
            var stock = inventories.StockOf(place);
            foreach (var kv in WreckModel.Residue(w.BillKg, w.PeakC)) inventories.Add(place, kv.Key, kv.Value);
            // the cargo it carried lies in the same pile
            var cargo = PlaceId.Cargo(r.Id);
            if (inventories.Has(cargo))
            {
                foreach (Material m in Resources.Materials.All) { double kg = inventories.StockOf(cargo)[m]; if (kg > 0.0) inventories.Move(cargo, place, m, kg); }
                inventories.Remove(cargo);
            }
            var mine = heldFindings.Where(f => f.RobotId == r.Id).ToList();
            heldFindings.RemoveAll(f => f.RobotId == r.Id);
            lostFindings = 0;
            if (w.ElectronicsIntact) w.Findings.AddRange(mine); else { lostFindings = mine.Count; w.FindingsLost = mine.Count > 0; KnowledgeStore.DropUnsynced(r.Id); }
            wrecks.Add(w);
            return w;
        }

        /// <summary>Test hook: a robot's remains at a temperature, as DestroyRobot would lay them (the robot is removed).</summary>
        internal Wreck DestroyForTests(int robotId, double airC)
        {
            Robot r = Workforce.GetRobot(robotId);
            Wreck w = LayWreck(r, airC, out _);
            Workforce.RemoveRobot(robotId);
            return w;
        }

        public double WreckKg(Wreck w) => inventories.Has(PlaceId.Wreck(w.Id)) ? inventories.MassAt(PlaceId.Wreck(w.Id)) : 0.0;

        public double WreckStockKg(Wreck w, Material m) => inventories.Has(PlaceId.Wreck(w.Id)) ? inventories.StockOf(PlaceId.Wreck(w.Id))[m] : 0.0;

        /// <summary>A wreck keeps degrading at the temperature of its tile: a hotter peak turns more of it to slag (the parts only move one way).</summary>
        void ExposeWrecks()
        {
            if (wrecks.Count == 0) return;
            foreach (Wreck w in wrecks)
            {
                double air = LiveAirTemperatureAt(w.Tile);
                if (air <= w.PeakC) continue;
                double before = w.PeakC;
                w.PeakC = air;
                if (air <= WreckModel.SolderC) continue;
                // convert what is now above its threshold: compare the residue at the old and new peak, and move the difference to slag (or to nothing above iron's boiling point)
                var place = PlaceId.Wreck(w.Id);
                if (!inventories.Has(place)) continue;
                var oldRes = WreckModel.Residue(w.BillKg, before); var newRes = WreckModel.Residue(w.BillKg, air);
                foreach (Material m in Resources.Materials.All)
                {
                    if (m == Material.Slag) continue;
                    double was = oldRes.TryGetValue(m, out double a) ? a : 0.0, now = newRes.TryGetValue(m, out double b) ? b : 0.0;
                    double lose = Math.Min(was - now, WreckStockKg(w, m));
                    if (lose <= 1e-12) continue;
                    inventories.StockOf(place).Take(m, lose);
                }
                // slag in the model is a total: bring the stock's slag to the new residue's, never below what it holds
                double slagWas = oldRes.TryGetValue(Material.Slag, out double sa) ? sa : 0.0, slagNow = newRes.TryGetValue(Material.Slag, out double sn) ? sn : 0.0;
                if (slagNow > slagWas) inventories.Add(place, Material.Slag, slagNow - slagWas);
            }
        }

        /// <summary>The recovered findings of a wreck whose electronics survived are read back when the wreck is cleared; otherwise they are gone.</summary>
        void ClearEmptyWrecks()
        {
            for (int i = 0; i < wrecks.Count; i++)
            {
                Wreck w = wrecks[i];
                if (w.Recovered || WreckKg(w) > 1e-6 || haulBook.Orders.Any(o => o.WreckId == w.Id)) continue;
                foreach (HeldFinding f in w.Findings) { Deliver(f); }
                if (w.Findings.Count > 0) KnowledgeStore.Sync(w.RobotId, TimeHours);
                if (w.Findings.Count > 0) Log($"Wreck {w.Id}", "Findings recovered", w.Findings.Count, "", $"W{w.Id}", $"{w.Findings.Count} unsynced finding(s) read back from the wreck of robot {w.RobotId}.", -1);
                inventories.Remove(PlaceId.Wreck(w.Id));
                if (w.Rebuildable) { w.Recovered = true; w.Findings.Clear(); continue; }   // the chassis can be rebuilt at the Workshop
                wrecks.RemoveAt(i--);
            }
        }

        /// <summary>One recover order per material in a wreck (a load up to a hauler's payload): the normal haul planner then plans, stages and checks the energy round trip.</summary>
        void PlanWreckRecovery()
        {
            if (wrecks.Count == 0 || !LogisticsEnabled) return;
            foreach (Wreck w in wrecks)
            {
                if (w.Tile == BaseTileId)
                {
                    // already at the base: it goes into the depot at once
                    foreach (Material m in Resources.Materials.All) { double kg = WreckStockKg(w, m); if (kg > 0.0) inventories.Move(PlaceId.Wreck(w.Id), PlaceId.Depot(BaseTileId), m, kg); }
                    continue;
                }
                foreach (Material m in Resources.Materials.All)
                {
                    double kg = WreckStockKg(w, m);
                    double ordered = 0.0;
                    foreach (HaulOrder o in haulBook.Orders) if (o.WreckId == w.Id && o.Material == m) ordered += o.Kg;
                    double rest = kg - ordered;
                    if (m == Material.Slag || rest <= 1e-6) continue;   // slag is not worth a trip
                    if (haulBook.Orders.Count(o => o.WreckId == w.Id) >= MaxHaulsPerDemand) break;
                    HaulOrder made = haulBook.Add(w.Tile, BaseTileId, m, Math.Min(rest, BasePayloadKg));
                    made.WreckId = w.Id;
                    made.Stops = PlanHaul(w.Tile, BaseTileId, made.Kg).Stops;
                }
            }
        }

        public IReadOnlyList<string> WreckLines()
        {
            var lines = new List<string>();
            foreach (Wreck w in wrecks)
            {
                string cause = w.Cause == DeathCause.Heat ? "heat" : "cold or an empty battery";
                string order = haulBook.Orders.Where(o => o.WreckId == w.Id).Select(o => o.Blocked).FirstOrDefault(b => b != null);
                lines.Add($"Wreck of robot {w.RobotId} on tile {w.Tile} ({cause}, peak {w.PeakC:0} C): {WreckKg(w):0} kg" + (WreckStockKg(w, Material.ElectronicWaste) > 0.0 ? $", {WreckStockKg(w, Material.ElectronicWaste):0.0} kg electronic waste" : "")
                          + (WreckStockKg(w, Material.Slag) > 0.0 ? $", {WreckStockKg(w, Material.Slag):0} kg slag (not worth a trip)" : "")
                          + (WreckRecoveryOrdered(w.Id) ? (order != null ? $" · recovery waiting: {order}" : " · recovery under way") : "")
                          + (w.Findings.Count > 0 ? $" · {w.Findings.Count} finding(s) can be read back" : w.FindingsLost ? " · findings lost" : ""));
            }
            return lines;
        }

        void RemoveWreck(int wreckId) { wrecks.RemoveAll(w => w.Id == wreckId); inventories.Remove(PlaceId.Wreck(wreckId)); }

        /// <summary>Why a wreck cannot be rebuilt now, or null.</summary>
        public string RebuildBlocker(int wreckId)
        {
            Wreck w = wrecks.FirstOrDefault(x => x.Id == wreckId);
            if (w == null) return "there is no such wreck";
            if (!w.Rebuildable) return w.Cause == DeathCause.Heat ? "the electronics and the cell melted: only the salvaged metals are left" : $"the structure was heated to {w.PeakC:0} C and melted: only the salvaged metals are left";
            if (!w.Recovered) return "the wreck has not been recovered yet: haul its metals home first";
            if (WorkshopCount == 0 && workshops.Count == 0) return "build a Robot Workshop first";
            if (jobs.Any(j => j.Kind == JobKind.Rebuild && j.WreckId == wreckId)) return "it is already being rebuilt";
            var need = RobotTech.Rebuild(w.Kind, false);
            string missing = "";
            foreach (var (m, kg) in need.Inputs) if (Materials[m] < kg - 1e-9) missing += (missing.Length > 0 ? ", " : "") + $"{kg - Materials[m]:0.#} kg {Resources.Materials.Name(m).ToLowerInvariant()}";
            return missing.Length > 0 ? "needs " + missing : null;
        }

        public IReadOnlyList<(int Id, int RobotId, string Kind, bool Rebuildable, string Blocker)> RecoveredWrecks() =>
            wrecks.Where(w => w.Recovered || !w.Rebuildable).Where(w => WreckKg(w) <= 1e-6 || w.Recovered).Select(w => (w.Id, w.RobotId, w.Kind.ToString(), w.Rebuildable, RebuildBlocker(w.Id))).ToList();

        public string OrderRebuild(int wreckId)
        {
            string why = RebuildBlocker(wreckId);
            if (why != null) return "Cannot rebuild: " + why + ".";
            Wreck w = wrecks.First(x => x.Id == wreckId);
            Recipe recipe = RobotTech.Rebuild(w.Kind, false);
            TakeInputs(recipe);
            jobs.Add(new ManufacturingJob { Kind = JobKind.Rebuild, RobotKind = w.Kind, RobotId = w.RobotId, WreckId = wreckId, Recipe = recipe });
            Settle(null);
            return null;
        }

        public bool WreckRecoveryOrdered(int wreckId) => haulBook.Orders.Any(o => o.WreckId == wreckId);

        void CaptureWrecks(SimulationState state)
        {
            state.NextWreckId = nextWreckId;
            foreach (Wreck w in wrecks)
                state.Wrecks.Add(new WreckRecord
                {
                    Id = w.Id, Tile = w.Tile, RobotId = w.RobotId, Kind = w.Kind, Cause = w.Cause, PeakC = w.PeakC, FindingsLost = w.FindingsLost, Recovered = w.Recovered,
                    Bill = w.BillKg.Select(kv => new MaterialRecord { Material = kv.Key, Kg = kv.Value }).ToList(), Findings = new List<HeldFinding>(w.Findings),
                });
        }

        void RestoreWrecks(SimulationState state)
        {
            wrecks.Clear();
            nextWreckId = Math.Max(1, state.Version >= 59 ? state.NextWreckId : 1);
            if (state.Version < 59 || state.Wrecks == null) return;
            foreach (WreckRecord r in state.Wrecks)
            {
                var w = new Wreck { Id = r.Id, Tile = r.Tile, RobotId = r.RobotId, Kind = r.Kind, Cause = r.Cause, PeakC = r.PeakC, FindingsLost = r.FindingsLost, Recovered = r.Recovered };
                foreach (var b in r.Bill) w.BillKg[b.Material] = b.Kg;
                if (r.Findings != null) w.Findings.AddRange(r.Findings);
                wrecks.Add(w);
                nextWreckId = Math.Max(nextWreckId, r.Id + 1);
            }
        }
    }
}

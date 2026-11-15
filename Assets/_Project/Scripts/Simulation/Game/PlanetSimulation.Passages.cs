using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Barriers (director 2026-11-15): mountains and canyons can't be driven across, built on or mined until the colony
    /// opens them: a mountain with a tunnel or by blasting, a canyon with a bridge. The materials are taken when the
    /// work is ordered (given back if it is cancelled); robots then work at the tile's edge until it is open.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly List<(int Tile, PassageKind Kind)> passageOrders = new List<(int, PassageKind)>();
        readonly Dictionary<int, PassageKind> passages = new Dictionary<int, PassageKind>();

        /// <summary>A barrier tile was opened (tile, how).</summary>
        public event Action<int, PassageKind> PassageOpened;

        public bool IsBarrier(int tileId) => tileId >= 0 && tileId < TileCount && Terrains.IsBarrier(surface.Terrain[tileId]);

        /// <summary>True when robots can use this tile: no barrier, or one the colony has opened.</summary>
        public bool IsOpenGround(int tileId) => !IsBarrier(tileId) || passages.ContainsKey(tileId);

        /// <summary>How this tile was opened (None if it wasn't, or is no barrier).</summary>
        public PassageKind PassageAt(int tileId) => passages.TryGetValue(tileId, out PassageKind k) ? k : PassageKind.None;

        /// <summary>The opening ordered on this tile and not finished yet (None if there is none).</summary>
        public PassageKind PassageOrdered(int tileId)
        {
            foreach (var o in passageOrders)
                if (o.Tile == tileId) return o.Kind;
            return PassageKind.None;
        }

        public double PassageProgress(int tileId)
        {
            PassageKind k = PassageOrdered(tileId);
            if (k == PassageKind.None) return passages.ContainsKey(tileId) ? 1.0 : 0.0;
            return fieldWork.TryGetValue(PassageTask(tileId, k), out double h) ? Math.Min(1.0, h / PassageHours(tileId, k)) : 0.0;
        }

        /// <summary>Why nothing can be built on a closed barrier, for the player.</summary>
        public string ClosedGroundReason(int tileId) =>
            surface.Terrain[tileId] == TerrainType.Canyon ? "A canyon: bridge it first (tile panel)."
            : "A mountain: tunnel through or blast a pass first (tile panel).";

        bool CaveAt(int tileId) => Deposits.HasCave(tileId) && Deposits.IsSurveyed(tileId);

        /// <summary>Materials it takes to open this tile this way (a known cave makes a tunnel cheaper).</summary>
        public MaterialBill PassageBill(int tileId, PassageKind kind) => Passages.Bill(kind, CaveAt(tileId));

        public double PassageHours(int tileId, PassageKind kind) => Passages.WorkHours(kind, CaveAt(tileId));

        static WorkTask PassageTask(int tile, PassageKind kind) => new WorkTask(TaskKind.Excavate, tile, (int)kind);

        /// <summary>Why this tile can't be opened this way now, or null.</summary>
        public string PassageBlocker(int tileId, PassageKind kind)
        {
            if (!IsBarrier(tileId)) return "Nothing to open here.";
            if (passages.ContainsKey(tileId)) return "Already open.";
            if (PassageOrdered(tileId) != PassageKind.None) return "Already under way.";
            if (Array.IndexOf(Passages.For(surface.Terrain[tileId]), kind) < 0)
                return $"A {Terrains.Name(surface.Terrain[tileId]).ToLowerInvariant()} can't be opened that way.";
            if (double.IsInfinity(Routes.HoursFrom(BaseTileId)[tileId]))
                return "Robots can't reach this tile's edge yet.";
            MaterialBill short_ = PassageBill(tileId, kind).Minus(Materials);
            if (!short_.IsEmpty) return "Missing " + short_.Describe() + ".";
            return null;
        }

        /// <summary>Order a barrier opened; takes its materials now. Returns null, or why not.</summary>
        public string OrderPassage(int tileId, PassageKind kind)
        {
            string why = PassageBlocker(tileId, kind);
            if (why != null) return why;
            foreach (var (m, kg) in PassageBill(tileId, kind).Items())
                Materials.Take(m, kg);
            passageOrders.Add((tileId, kind));
            Settle(null);
            return null;
        }

        /// <summary>Stop the work and give back the materials (the work done is lost).</summary>
        public bool CancelPassage(int tileId)
        {
            int i = passageOrders.FindIndex(o => o.Tile == tileId);
            if (i < 0) return false;
            var o = passageOrders[i];
            passageOrders.RemoveAt(i);
            WorkTask t = PassageTask(o.Tile, o.Kind);
            Workforce.Tasks.MarkCancelled(t);
            fieldWork.Remove(t);
            foreach (var (m, kg) in PassageBill(o.Tile, o.Kind).Items())
                Materials.Add(m, kg);
            Settle(null);
            return true;
        }

        void AddPassageTasks(List<WorkTask> tasks)
        {
            for (int k = 0; k < passageOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Excavate, passageOrders[k].Tile, (int)passageOrders[k].Kind, k));
        }

        /// <summary>Finished openings: the tile becomes open ground, and a mountain gives up the rock that was cut.</summary>
        void AdvancePassages()
        {
            foreach (var o in passageOrders.ToArray())
            {
                WorkTask t = PassageTask(o.Tile, o.Kind);
                if (!fieldWork.TryGetValue(t, out double h) || h < PassageHours(o.Tile, o.Kind) - 1e-9)
                    continue;
                Materials.Add(Material.Rock, Passages.MuckKg(o.Kind, CaveAt(o.Tile)));
                fieldWork.Remove(t);
                passageOrders.Remove(o);
                Workforce.Release(t);
                Open(o.Tile, o.Kind);
                PassageOpened?.Invoke(o.Tile, o.Kind);
            }
        }

        void Open(int tileId, PassageKind kind)
        {
            passages[tileId] = kind;
            Routes.SetOpened(tileId, true);
            Workforce.Movement = CurrentMovement();
        }

        internal void OpenForTests(int tileId, PassageKind kind) => Open(tileId, kind);

        void CapturePassages(SimulationState state)
        {
            foreach (var kv in passages)
                state.Passages.Add(new PassageRecord { Tile = kv.Key, Kind = kv.Value, Open = true });
            foreach (var o in passageOrders)
                state.Passages.Add(new PassageRecord { Tile = o.Tile, Kind = o.Kind, Open = false });
            state.Passages.Sort((a, b) => a.Tile.CompareTo(b.Tile));
        }

        void RestorePassages(SimulationState state)
        {
            foreach (int t in passages.Keys) Routes.SetOpened(t, false);
            passages.Clear();
            passageOrders.Clear();
            // Before version 33 mountains were open ground: a colony that built or mined on one keeps it open.
            if (state.Version < 33)
            {
                var used = new HashSet<int>();
                if (state.Buildings != null) foreach (var b in state.Buildings) used.Add(b.Tile);
                if (state.MiningOrders != null) foreach (var m in state.MiningOrders) used.Add(m.Tile);
                foreach (int t in used)
                    if (IsBarrier(t)) { passages[t] = PassageKind.Tunnel; Routes.SetOpened(t, true); }
            }
            if (state.Passages == null) return;   // version 33
            foreach (PassageRecord r in state.Passages)
            {
                if (r == null || !IsBarrier(r.Tile) || r.Kind == PassageKind.None) continue;
                if (r.Open)
                {
                    passages[r.Tile] = r.Kind;
                    Routes.SetOpened(r.Tile, true);
                }
                else if (PassageOrdered(r.Tile) == PassageKind.None && !passages.ContainsKey(r.Tile))
                    passageOrders.Add((r.Tile, r.Kind));
            }
        }
    }
}

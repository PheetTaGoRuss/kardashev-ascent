using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Hauls (mission operations 3.2, MO-03): hauls come from demand (a site short of a material on its tile, a depot
    /// stock target), never from player trips. Each is two stops of one robot: it loads at the source depot, drives with
    /// the cargo (more drive power, MO-04) and unloads at the target depot. The cargo is the robot's own place in the
    /// inventory, so it stays with the robot if the haul is interrupted.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly HaulBook haulBook = new HaulBook();

        /// <summary>What a robot with a transport capacity of 1 can carry, kg. Candidate from the Apollo rover's payload rating (about 490 kg); game rule, TBD.</summary>
        public const double BasePayloadKg = 490.0;

        /// <summary>Most hauls planned for one demand in a pass.</summary>
        const int MaxHaulsPerDemand = 4;

        /// <summary>What this robot carries, kg: its base payload times its transport capacity.</summary>
        double PayloadKg(Robot r) => BasePayloadKg * r.Attribute(RobotAttribute.TransportCapacity);

        static WorkTask HaulTaskOf(HaulOrder o, int index = 0) =>
            new WorkTask(TaskKind.Haul, o.Stage == 0 ? o.FromTile : o.ToTile, o.Id * 2 + o.Stage, index);

        // ---- commands and read models ----

        /// <summary>Keep at least <paramref name="kg"/> of a material in a tile's depot: hauls are made until it is met. Zero removes the target.</summary>
        public bool SetStockTarget(int tileId, Material material, double kg)
        {
            if (tileId < 0 || tileId >= TileCount || kg < 0.0 || double.IsNaN(kg)) return false;
            haulBook.SetTarget(tileId, material, kg);
            return true;
        }

        public double StockTarget(int tileId, Material material) => haulBook.TargetAt(tileId, material);

        public IReadOnlyList<(int Tile, Material Material, double Kg)> StockTargets()
        {
            var list = new List<(int, Material, double)>();
            foreach (StockTarget t in haulBook.Targets()) list.Add((t.Tile, t.Material, t.Kg));
            return list;
        }

        /// <summary>The hauls under way: where from and to, what, how much, and who carries it once loaded.</summary>
        public IReadOnlyList<(int Id, int From, int To, Material Material, double Kg, bool Loaded, int RobotId)> HaulOrders()
        {
            var list = new List<(int, int, int, Material, double, bool, int)>();
            foreach (HaulOrder o in haulBook.Orders) list.Add((o.Id, o.FromTile, o.ToTile, o.Material, o.Kg, o.Stage == 1, o.RobotId));
            return list;
        }

        // ---- planning (once a step) ----

        void PlanHauls()
        {
            if (!LogisticsEnabled)
                return;
            // A carrier that is gone returns its load to where it came from; the others keep their delivery reserved.
            foreach (HaulOrder o in new List<HaulOrder>(haulBook.Orders))
            {
                if (o.Stage != 1) continue;
                if (Workforce.GetRobot(o.RobotId) == null)
                {
                    inventories.Move(PlaceId.Cargo(o.RobotId), PlaceId.Depot(o.FromTile), o.Material, o.Kg);
                    haulBook.Remove(o.Id);
                }
                else if (Workforce.ReservedRobotFor(HaulTaskOf(o)) == null)
                    Workforce.Reserve(o.RobotId, HaulTaskOf(o));
            }

            // What each tile still needs: the sites on it plus its stock targets.
            var wanted = new Dictionary<(int Tile, Material Material), double>();
            foreach (var site in Build.Sites())
                foreach (var (m, kg) in Build.RemainingAt(site.Tile, site.Slot).Items())
                    wanted[(site.Tile, m)] = (wanted.TryGetValue((site.Tile, m), out double w) ? w : 0.0) + kg;
            foreach (StockTarget t in haulBook.Targets())
                wanted[(t.Tile, t.Material)] = (wanted.TryGetValue((t.Tile, t.Material), out double w) ? w : 0.0) + t.Kg;
            // An idle plant with a batch waiting wants its input at its tile (what its input buffer already holds counts).
            foreach (RefineryKind kind in new[] { RefineryKind.AluminaPlant, RefineryKind.Electrolyser })
            {
                RefineProcess? pending = PendingProcess(kind);
                if (!pending.HasValue) continue;
                RefineRecipe r = Refining.Of(pending.Value);
                foreach (var m in FinishedModules(kind))
                {
                    if (batches.ContainsKey(m)) continue;
                    double need = r.InputKg - inventories.StockOf(InputOf(m))[r.Input];
                    if (need > 1e-9)
                        wanted[(m.Tile, r.Input)] = (wanted.TryGetValue((m.Tile, r.Input), out double w2) ? w2 : 0.0) + need;
                }
            }

            var keys = new List<(int Tile, Material Material)>(wanted.Keys);
            keys.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : ((int)a.Material).CompareTo((int)b.Material));
            var demands = new List<HaulDemand>();
            foreach (var k in keys)
            {
                double lack = wanted[k] - StockAt(k.Tile)[k.Material] - haulBook.InFlightTo(k.Tile, k.Material);
                // Only what the depot has room for: a haul into a full store would leave the robot with its cargo.
                lack = Math.Min(lack, RoomAt(k.Tile, k.Material) - haulBook.InFlightTo(k.Tile, k.Material));
                if (lack > 1e-6) demands.Add(new HaulDemand(k.Tile, k.Material, lack));
            }
            if (demands.Count == 0)
                return;

            // Sources: tiles that hold a depot, nearest first by route hours. A tile keeps what it needs itself.
            var sources = new List<int> { BaseTileId };
            foreach (PlaceId p in inventories.Places)
                if (p.Kind == PlaceKind.Depot && !sources.Contains(p.Id)) sources.Add(p.Id);
            sources.Sort();
            double Available(int tile, Material m) =>
                StockAt(tile)[m] - (wanted.TryGetValue((tile, m), out double keep) ? keep : 0.0) - haulBook.ReservedAt(tile, m);
            double Hours(int a, int b) => Routes == null ? double.PositiveInfinity : Routes.HoursBetween(a, b);

            double capacity = 1.0;   // plan loads the biggest hauler can carry
            foreach (Robot r in Workforce.Robots)
                if (RobotCatalog.CanDo(r.Kind, TaskKind.Haul, UnlockLevel))
                    capacity = Math.Max(capacity, r.Attribute(RobotAttribute.TransportCapacity));
            foreach (PlannedHaul h in HaulPlanner.Plan(demands, sources, Available, Hours, BasePayloadKg * capacity, MaxHaulsPerDemand))
                haulBook.Add(h.From, h.To, h.Material, h.Kg);
        }

        /// <summary>The open haul tasks: a load at each source, and an unload at the target of each loaded haul.</summary>
        void AddHaulTasks(List<WorkTask> tasks)
        {
            if (!LogisticsEnabled) return;
            int i = 0;
            foreach (HaulOrder o in haulBook.Orders)
                tasks.Add(HaulTaskOf(o, i++));
        }

        // ---- doing the work (called from the field work of a step) ----

        /// <summary>A crew that has finished its handling hours at a haul stop loads or unloads.</summary>
        void AdvanceHauls()
        {
            if (!LogisticsEnabled) return;
            foreach (HaulOrder o in new List<HaulOrder>(haulBook.Orders))
            {
                WorkTask t = HaulTaskOf(o);
                if (!fieldWork.TryGetValue(t, out double h) || h < RobotSpecs.HaulHandlingHours - 1e-9)
                    continue;
                fieldWork.Remove(t);
                if (o.Stage == 0)
                {
                    Robot r = Workforce.WorkerOf(t);
                    if (r == null) continue;
                    double moved = inventories.Move(PlaceId.Depot(o.FromTile), PlaceId.Cargo(r.Id), o.Material, Math.Min(o.Kg, PayloadKg(r)));
                    Workforce.Release(t);
                    if (moved <= 1e-9)
                    {
                        haulBook.Remove(o.Id);   // the source ran dry: the demand is planned again
                        continue;
                    }
                    o.Kg = moved;
                    o.Stage = 1;
                    o.RobotId = r.Id;
                    Workforce.Reserve(r.Id, HaulTaskOf(o));
                }
                else
                {
                    double moved = inventories.Move(PlaceId.Cargo(o.RobotId), PlaceId.Depot(o.ToTile), o.Material, o.Kg);
                    if (moved > 0.0)
                        haulLedger.RecordMove(o.Material, moved, DistanceKm(o.FromTile, o.ToTile));
                    o.Kg -= moved;
                    Workforce.Release(t);
                    if (o.Kg <= 1e-9)
                    {
                        Workforce.Unreserve(o.RobotId);
                        haulBook.Remove(o.Id);
                    }
                    // else: the target depot is full; the carrier keeps what is left and tries again
                }
            }
        }

        /// <summary>Straight-line distance between two tile centres, km (a lower bound of the route).</summary>
        double DistanceKm(int a, int b)
        {
            var ca = Sphere.Centers[a];
            var cb = Sphere.Centers[b];
            double dot = ca.X * cb.X + ca.Y * cb.Y + ca.Z * cb.Z;
            double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
            return angle * planetRadiusM / 1000.0;
        }

        void CaptureHauls(SimulationState state)
        {
            state.NextHaulId = haulBook.NextId;
            foreach (HaulOrder o in haulBook.Orders)
                state.HaulOrders.Add(new HaulOrderRecord { Id = o.Id, FromTile = o.FromTile, ToTile = o.ToTile, Material = o.Material, Kg = o.Kg, Stage = o.Stage, RobotId = o.RobotId });
            foreach (StockTarget t in haulBook.Targets())
                state.StockTargets.Add(new StockTargetRecord { Tile = t.Tile, Material = t.Material, Kg = t.Kg });
        }

        void RestoreHauls(SimulationState state)
        {
            var orders = new List<HaulOrder>();
            var targets = new List<StockTarget>();
            if (state.Version >= 39)
            {
                if (state.HaulOrders != null)
                    foreach (HaulOrderRecord r in state.HaulOrders)
                        if (r.FromTile >= 0 && r.FromTile < TileCount && r.ToTile >= 0 && r.ToTile < TileCount && r.Kg > 0.0)
                            orders.Add(new HaulOrder { Id = r.Id, FromTile = r.FromTile, ToTile = r.ToTile, Material = r.Material, Kg = r.Kg, Stage = r.Stage == 1 ? 1 : 0, RobotId = r.RobotId });
                if (state.StockTargets != null)
                    foreach (StockTargetRecord r in state.StockTargets)
                        if (r.Tile >= 0 && r.Tile < TileCount && r.Kg > 0.0)
                            targets.Add(new StockTarget(r.Tile, r.Material, r.Kg));
            }
            haulBook.Restore(orders, targets, state.NextHaulId);
        }
    }
}

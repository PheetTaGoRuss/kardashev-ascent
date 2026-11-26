using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>What a robot with a transport capacity of 1 can carry, kg. Candidate from the Apollo rover design payload (490 kg, NASA NSSDC; its operations handbook limit is about 440 kg); game rule, TBD.</summary>
        public const double BasePayloadKg = 490.0;

        /// <summary>Most hauls planned for one demand in a pass.</summary>
        const int MaxHaulsPerDemand = 4;

        /// <summary>Mines push their output home when it makes a full load (on by default; tests of a full store turn it off).</summary>
        public bool MinePushEnabled { get; set; } = true;

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
        /// <summary>Why a haul is waiting (its plan no longer holds), or null; and the stops it is staged through.</summary>
        public string HaulBlockedReason(int haulId) { foreach (HaulOrder o in haulBook.Orders) if (o.Id == haulId) return o.Blocked; return null; }
        public (int Kind, double NeedWh, double BudgetWh, int StopTile) HaulWhyOf(int haulId)
        {
            foreach (HaulOrder o in haulBook.Orders)
                if (o.Id == haulId && o.WeatherWait && HaulStormWait(haulId) is var sw && sw.HasValue)
                    return ((int)HaulWhyKind.StormForecast, sw.Value.WindMs, sw.Value.ThresholdMs, sw.Value.Day);
                else if (o.Id == haulId) { var w = WhyHaul(o.FromTile, o.ToTile, o.Kg, o.Stops); return ((int)w.Kind, w.NeedWh, w.BudgetWh, w.StopTile); }
            return ((int)HaulWhyKind.Ok, 0.0, 0.0, -1);
        }

        public IReadOnlyList<int> HaulStops(int haulId) { foreach (HaulOrder o in haulBook.Orders) if (o.Id == haulId) return o.Stops; return new List<int>(); }

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

            PlanWreckRecovery();
            ClearEmptyWrecks();
            // What each tile still needs: the sites on it plus its stock targets.
            // Reservation order (decided 2026-11-26): sites under construction and plant inputs are planned first and keep stock back; stock targets are buffers, planned after and never keep stock back
            // from a site (a copper target at the base starved the first outpost kit).
            var wanted = new Dictionary<(int Tile, Material Material), double>();
            foreach (var site in Build.Sites())
                foreach (var (m, kg) in Build.RemainingAt(site.Tile, site.Slot).Items())
                    wanted[(site.Tile, m)] = (wanted.TryGetValue((site.Tile, m), out double w) ? w : 0.0) + kg;
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

            var siteWanted = new Dictionary<(int Tile, Material Material), double>(wanted);
            var targetWanted = new Dictionary<(int Tile, Material Material), double>();
            foreach (StockTarget t in haulBook.Targets())
                targetWanted[(t.Tile, t.Material)] = (targetWanted.TryGetValue((t.Tile, t.Material), out double tw) ? tw : 0.0) + t.Kg;

            // Sources: tiles that hold a depot, nearest first by route hours. A tile keeps what its sites need itself (and, in the second phase, its own targets).
            var sources = new List<int> { BaseTileId };
            foreach (PlaceId p in inventories.Places)
                if (p.Kind == PlaceKind.Depot && !sources.Contains(p.Id)) sources.Add(p.Id);
            sources.Sort();
            double capacity = 1.0;   // plan loads the biggest hauler can carry
            foreach (Robot r in Workforce.Robots)
                if (RobotCatalog.CanDo(r.Kind, TaskKind.Haul, UnlockLevel))
                    capacity = Math.Max(capacity, r.Attribute(RobotAttribute.TransportCapacity));
            // Sources are ranked by what the haul really costs: driving hours plus the climb and the heater, as hours (logistics-routes.md).
            // A load a robot's battery cannot carry through the stops is split into smaller loads (more trips), down to the largest load that fits, at least MinHaulLoadKg (L5 2026-11-24, floor lifted 2026-11-26).
            double PayloadBetween(int a, int b) => Routes == null ? 0.0 : FeasiblePayloadKg(a, b, BasePayloadKg * capacity);
            double Hours(int a, int b) => Routes == null ? double.PositiveInfinity : HaulCostHours(a, b, PayloadBetween(a, b));

            for (int phase = 0; phase < 2; phase++)
            {
                var allWanted = new Dictionary<(int Tile, Material Material), double>(siteWanted);
                if (phase == 1) foreach (var kv in targetWanted) allWanted[kv.Key] = (allWanted.TryGetValue(kv.Key, out double aw) ? aw : 0.0) + kv.Value;
                var keys = new List<(int Tile, Material Material)>(allWanted.Keys);
                keys.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : ((int)a.Material).CompareTo((int)b.Material));
                var demands = new List<HaulDemand>();
                foreach (var k in keys)
                {
                    double need = phase == 0 ? siteWanted[k] : allWanted[k];
                    double lack = need - StockAt(k.Tile)[k.Material] - haulBook.InFlightTo(k.Tile, k.Material);
                    // Only what the depot has room for: a haul into a full store would leave the robot with its cargo.
                    lack = Math.Min(lack, RoomAt(k.Tile, k.Material) - haulBook.InFlightTo(k.Tile, k.Material));
                    if (lack > 1e-6) demands.Add(new HaulDemand(k.Tile, k.Material, lack));
                }
                if (demands.Count == 0) continue;
                var keep = phase == 0 ? siteWanted : allWanted;
                double Available(int tile, Material m) =>
                    StockAt(tile)[m] - (keep.TryGetValue((tile, m), out double kept) ? kept : 0.0) - haulBook.ReservedAt(tile, m);
                foreach (PlannedHaul h in HaulPlanner.Plan(demands, sources, Available, Hours, BasePayloadKg * capacity, MaxHaulsPerDemand, PayloadBetween))
                {
                    HaulOrder made = haulBook.Add(h.From, h.To, h.Material, h.Kg);
                    if (made != null) made.Stops = PlanHaul(h.From, h.To, h.Kg).Stops;
                }
            }

            // Mines push their output (decided 2026-11-26): ore lying at a mine tile is hauled home when it makes at least one full load, or when the base has any waiting demand for it, even with no
            // stock target at the base. One haul at a time per (mine, ore), so the planner does not flood the route.
            foreach (int mine in MinePushEnabled ? sources : new List<int>())
            {
                if (mine == BaseTileId) continue;
                double load = PayloadBetween(mine, BaseTileId);
                if (load <= 0.0) continue;
                foreach (Material m in Resources.Materials.Mined)
                {
                    if (m == Material.Rock) continue;
                    double kept = siteWanted.TryGetValue((mine, m), out double kw) ? kw : 0.0;
                    double avail = StockAt(mine)[m] - kept - haulBook.ReservedAt(mine, m);
                    if (avail <= 1e-6) continue;
                    double baseLack = (siteWanted.TryGetValue((BaseTileId, m), out double bw) ? bw : 0.0) - StockAt(BaseTileId)[m] - haulBook.InFlightTo(BaseTileId, m);
                    if (avail < load - 1e-6 && baseLack <= 1e-6) continue;
                    if (haulBook.Orders.Any(o => o.FromTile == mine && o.ToTile == BaseTileId && o.Material == m && o.WreckId == 0 && o.Stage == 0)) continue;
                    double room = RoomAt(BaseTileId, m) - haulBook.InFlightTo(BaseTileId, m);
                    double kg = Math.Min(Math.Min(avail, load), room);
                    if (kg < MinHaulLoadKg - 1e-6 && kg < avail - 1e-6) continue;
                    if (kg <= 1e-6) continue;
                    HaulOrder made = haulBook.Add(mine, BaseTileId, m, kg);
                    if (made != null) made.Stops = PlanHaul(mine, BaseTileId, kg).Stops;
                }
            }
        }

        /// <summary>
        /// The first segment of a haul for a robot at its current tile (KI-46 at dispatch): the empty leg to the pickup plus the loaded leg to the first stop (or to the drop and then back to a charger
        /// when the haul is direct), with the lowest body temperature on it. Null for a haul already loaded (the robot is committed) or an unknown order.
        /// </summary>
        (double Wh, double MinC)? HaulSegmentNeedFor(Robot r, WorkTask t)
        {
            if (t.Slot % 2 != 0) return null;
            HaulOrder o = haulBook.Find(t.Slot / 2);
            if (o == null || o.Stage != 0 || Routes == null) return null;
            var plan = PlanHaul(o.FromTile, o.ToTile, o.Kg);
            if (!plan.Feasible) return null;
            double wh = 0.0, minC = RobotThermal.ShelterBatteryC;
            if (r.Tile != o.FromTile) { var empty = HaulLegCost(r.Tile, o.FromTile, 0.0); if (!empty.Reachable) return null; wh += empty.TotalWh; minC = Math.Min(minC, empty.MinBodyC); }
            int next = plan.Stops.Count > 0 ? plan.Stops[0] : o.ToTile;
            var loaded = HaulLegCost(o.FromTile, next, o.Kg);
            if (!loaded.Reachable) return null;
            wh += loaded.TotalWh; minC = Math.Min(minC, loaded.MinBodyC);
            if (plan.Stops.Count == 0)
            {
                double back = double.MaxValue; var stops = StagingStops();
                foreach (int st in stops) { if (st == o.ToTile) { back = 0.0; break; } var leg = HaulLegCost(o.ToTile, st, 0.0); if (leg.Reachable && leg.TotalWh < back) { back = leg.TotalWh; minC = Math.Min(minC, leg.MinBodyC); } }
                if (back < double.MaxValue) wh += back;
            }
            return (wh, minC);
        }

        /// <summary>The open haul tasks: a load at each source, and an unload at the target of each loaded haul.</summary>
        void AddHaulTasks(List<WorkTask> tasks)
        {
            if (!LogisticsEnabled) return;
            int i = 0;
            foreach (HaulOrder o in haulBook.Orders)
            {
                // A robot starts only if the plan still holds: a load not yet picked up whose stops or battery chain failed waits, with its reason.
                if (o.Stage == 0)
                {
                    HaulPlan plan = PlanHaul(o.FromTile, o.ToTile, o.Kg);
                    // a load planned when the battery could carry more (a warmer hour) that no longer fits: cut it to what fits now (the rest is planned again as a new haul)
                    if (!plan.Feasible && o.WreckId == 0 && o.Kg > MinHaulLoadKg)
                    {
                        double fits = FeasiblePayloadKg(o.FromTile, o.ToTile, o.Kg);
                        if (fits >= MinHaulLoadKg - 1e-9 && fits < o.Kg) { o.Kg = fits; plan = PlanHaul(o.FromTile, o.ToTile, o.Kg); }
                    }
                    string was = o.Blocked;
                    if (!plan.Feasible)
                    {
                        string stopsDown = o.Stops.Count > 0 && o.Stops.Any(s => !StagingStops().Contains(s)) ? "a stop on its route lost power or is gone" : "no chain of legs between chargers or shelters fits the battery now";
                        o.Blocked = stopsDown; o.WeatherWait = false;
                    }
                    else
                    {
                        o.Blocked = null; o.Stops = plan.Stops; o.WeatherWait = false;
                        // P2: the forecast. A storm on the route within the trip holds the haul unless the player said go anyway; the energy check above is never skipped.
                        if (!o.GoAnyway)
                        {
                            var storm = StormForecastOnRoute(o.FromTile, o.ToTile, TripHours(o.FromTile, o.ToTile));
                            if (storm.HasValue)
                            {
                                o.WeatherWait = true;
                                o.Blocked = $"a storm is forecast in {storm.Value.Day} day(s) on the route (◐ wind {storm.Value.WindMs:0} m/s ±{storm.Value.SdMs:0}, a storm is above {storm.Value.ThresholdMs:0})";
                            }
                        }
                    }
                    if (o.Blocked != null && was == null)
                        Log($"Haul {o.Id}", "Haul waiting", 0.0, "", $"H{o.Id}", $"Haul {o.Id} ({Resources.Materials.Name(o.Material)} {o.FromTile} to {o.ToTile}) is waiting: {o.Blocked}.", -1);
                    if (o.Blocked != null) { i++; continue; }
                }
                tasks.Add(HaulTaskOf(o, i++));
            }
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
                    double moved = inventories.Move(o.WreckId != 0 ? PlaceId.Wreck(o.WreckId) : PlaceId.Depot(o.FromTile), PlaceId.Cargo(r.Id), o.Material, Math.Min(o.Kg, PayloadKg(r)));
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
                    {
                        haulLedger.RecordMove(o.Material, moved, DistanceKm(o.FromTile, o.ToTile));
                        flows.Add(TimeHours, FlowStage.Hauled, -1, o.Material, moved);
                    }
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
                state.HaulOrders.Add(new HaulOrderRecord { Id = o.Id, FromTile = o.FromTile, ToTile = o.ToTile, Material = o.Material, Kg = o.Kg, Stage = o.Stage, RobotId = o.RobotId, GoAnyway = o.GoAnyway, WreckId = o.WreckId });
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
                            orders.Add(new HaulOrder { Id = r.Id, FromTile = r.FromTile, ToTile = r.ToTile, Material = r.Material, Kg = r.Kg, Stage = r.Stage == 1 ? 1 : 0, RobotId = r.RobotId, GoAnyway = state.Version >= 57 && r.GoAnyway, WreckId = state.Version >= 59 ? r.WreckId : 0 });
                if (state.StockTargets != null)
                    foreach (StockTargetRecord r in state.StockTargets)
                        if (r.Tile >= 0 && r.Tile < TileCount && r.Kg > 0.0)
                            targets.Add(new StockTarget(r.Tile, r.Material, r.Kg));
            }
            haulBook.Restore(orders, targets, state.NextHaulId);
        }
    }
}

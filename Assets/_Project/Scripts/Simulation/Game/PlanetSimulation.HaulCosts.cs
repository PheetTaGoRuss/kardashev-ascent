using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What a haul really costs a robot (logistics on the climb and weather costs, decided 2026-11-24, director delegated;
    /// docs/design/logistics-routes.md). The planner chooses sources by this cost, not by driving hours alone: a source up a mountain or in the
    /// cold costs more than one on the flat in mild air. All numbers are game rules, TBD, built on the climb term (KI-42) and the robot
    /// weather rules (KI-43).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The energy of one leg of a haul, Wh, split by cause.</summary>
        public readonly struct HaulLeg
        {
            public readonly double Hours, DriveWh, ClimbWh, HeaterWh;
            public HaulLeg(double hours, double drive, double climb, double heater) { Hours = hours; DriveWh = drive; ClimbWh = climb; HeaterWh = heater; }
            public double TotalWh => DriveWh + ClimbWh + HeaterWh;
            public bool Reachable => !double.IsInfinity(Hours);
        }

        /// <summary>One leg of a haul from one tile to another with this much cargo (loaded out, empty back).</summary>
        public HaulLeg HaulLegCost(int from, int to, double cargoKg)
        {
            if (Routes == null || from == to) return new HaulLeg(0.0, 0.0, 0.0, 0.0);
            List<int> route = Routes.Route(from, to);
            if (route == null) return new HaulLeg(double.PositiveInfinity, 0.0, 0.0, 0.0);
            return LegAlong(from, route, cargoKg);
        }

        /// <summary>The energy of driving a given route (the tiles after the start, in order) with this much cargo.</summary>
        HaulLeg LegAlong(int from, List<int> route, double cargoKg, double startDelayHours = 0.0)
        {
            double clock = TimeHours + startDelayHours;
            double hours = 0.0, drive = 0.0, climb = 0.0, heater = 0.0;
            double power = cargoKg > 0.0 ? Routes.DrivePowerW * HaulPhysics.LoadFactor(HaulPhysics.DefaultRobotMassKg, cargoKg) : Routes.DrivePowerW;
            double[] heights = ClimbEnabled ? TileHeightsM() : null;
            // the climb is planned at the high end of the relief range until the terrain is surveyed (decided 2026-11-24)
            var high = HighestPointEstimate;
            double scale = high.Value > 0.0 ? Math.Max(1.0, high.High / high.Value) : 1.0;
            int at = from;
            foreach (int t in route)
            {
                double step = Routes.HopHoursAt(t);
                hours += step;
                drive += power * step;
                if (heights != null)
                    climb += HaulPhysics.ClimbEnergyWh(HaulPhysics.DefaultRobotMassKg, cargoKg, SurfaceGravityMs2, heights[t] - heights[at], 1.0) * HaulPhysics.ClimbEffortShare * scale;
                heater += RobotThermal.HeaterW(DiurnalAirTemperatureAt(t, clock), false) * step;
                clock += step;
                at = t;
            }
            // the battery pays the drive and the climb over the robot's drive efficiency (the planner assumes the least efficient robot)
            double eff = PlanningEfficiency;
            return new HaulLeg(hours, drive / eff, climb / eff, heater);
        }

        public string HaulCostText(int from, int to, double cargoKg)
        {
            var (o, back) = HaulTripCost(from, to, cargoKg);
            if (!o.Reachable) return "no route";
            HaulPlan plan = PlanHaul(from, to, cargoKg);
            string staging = !plan.Feasible ? " · REFUSED: no chain of legs between chargers or shelters fits the battery"
                           : plan.Stops.Count == 0 ? " · direct" : " · staged via tile " + string.Join(", ", plan.Stops);
            double drive = o.DriveWh + back.DriveWh, climb = o.ClimbWh + back.ClimbWh, heater = o.HeaterWh + back.HeaterWh;
            var (delay, saved) = BestHaulStart(from, to, cargoKg);
            string start = delay > 0.0 && saved > 0.05 * Math.Max(1.0, heater) && saved > 20.0 ? $" · starting in {delay:0} h would save {saved:0} Wh of heater" : "";
            return $"{o.Hours + back.Hours:0} h, {drive + climb + heater:0} Wh round trip (driving {drive:0}, climbing {climb:0}, heater {heater:0}){staging}{start}";
        }

        /// <summary>
        /// How a haul gets there (L2, decided 2026-11-24): the stops (chargers and shelters) it is staged through, and whether any chain of legs fits the battery.
        /// A leg fits when its energy (driving, climb at the high end of the relief range, heater) is at most the usable charge of the biggest battery
        /// in the workforce above the return-to-charge level. Robots recharge at the stops with the existing charging rules.
        /// </summary>
        public readonly struct HaulPlan
        {
            public readonly bool Feasible;
            public readonly List<int> Stops;
            public readonly double Hours;
            public HaulPlan(bool feasible, List<int> stops, double hours) { Feasible = feasible; Stops = stops; Hours = hours; }
        }

        /// <summary>The stops a haul can be staged through: the lander, workshops and battery yards, and charging stations (and bays) that have power.</summary>
        HashSet<int> StagingStops()
        {
            var tiles = new HashSet<int> { BaseTileId };
            foreach (var b in batteries) if (!Build.IsQueued(b.Tile, b.Slot)) tiles.Add(b.Tile);
            foreach (var c in stations) if (!Build.IsQueued(c.Tile, c.Slot) && WiredOrOff(c)) tiles.Add(c.Tile);
            foreach (var w in workshops) if (!Build.IsQueued(w.Tile, w.Slot)) tiles.Add(w.Tile);
            return tiles;
        }

        /// <summary>The most cargo one robot can take from one tile to another through the stops, kg: the full load if it fits, else the largest of 3/4, 1/2 or 1/4 that does, else 0.</summary>
        public double FeasiblePayloadKg(int from, int to, double fullKg)
        {
            foreach (double share in new[] { 1.0, 0.75, 0.5, 0.25 })
                if (PlanHaul(from, to, fullKg * share).Feasible) return fullKg * share;
            return 0.0;
        }

        /// <summary>Why a haul can or cannot be done: a small structured reason the interface turns into a why? chain (UI stage 5), not text to parse.</summary>
        public enum HaulWhyKind { Ok, NoRoute, NoStartStop, NoEndStop, LegTooLong, StopLost }

        public readonly struct HaulWhy
        {
            public readonly HaulWhyKind Kind;
            /// <summary>The energy of the longest leg the haul needs (the loaded leg from pick-up to drop), Wh, and what one charge gives, Wh.</summary>
            public readonly double NeedWh, BudgetWh;
            /// <summary>The planned stop that lost power or is gone, for <see cref="HaulWhyKind.StopLost"/>.</summary>
            public readonly int StopTile;
            public HaulWhy(HaulWhyKind kind, double need, double budget, int stop = -1) { Kind = kind; NeedWh = need; BudgetWh = budget; StopTile = stop; }
        }

        /// <summary>The reason behind a haul's plan. <paramref name="plannedStops"/> are the stops it was planned through, to tell a stop that was lost.</summary>
        public HaulWhy WhyHaul(int from, int to, double cargoKg, IReadOnlyList<int> plannedStops = null)
        {
            double budget = LegBudgetWh;
            if (Routes == null || Routes.Route(from, to) == null) return new HaulWhy(HaulWhyKind.NoRoute, 0.0, budget);
            var stops = StagingStops();
            if (plannedStops != null)
                foreach (int s in plannedStops)
                    if (!stops.Contains(s)) return new HaulWhy(HaulWhyKind.StopLost, 0.0, budget, s);
            if (PlanHaul(from, to, cargoKg).Feasible) return new HaulWhy(HaulWhyKind.Ok, 0.0, budget);
            bool Fits(int a, int b2, double kg) { if (a == b2) return true; var leg = HaulLegCost(a, b2, kg); return leg.Reachable && leg.TotalWh <= budget; }
            bool startOk = false, endOk = false;
            foreach (int st in stops) { startOk |= Fits(st, from, 0.0); endOk |= Fits(to, st, 0.0); }
            // what one charge must cover with no stop at the pick-up or the drop: the run out empty, the loaded leg, the run home (cheapest stop each way)
            double runOut = double.MaxValue, runHome = double.MaxValue;
            foreach (int st in stops) { runOut = Math.Min(runOut, st == from ? 0.0 : HaulLegCost(st, from, 0.0).TotalWh); runHome = Math.Min(runHome, st == to ? 0.0 : HaulLegCost(to, st, 0.0).TotalWh); }
            double need = HaulLegCost(from, to, cargoKg).TotalWh + (double.IsInfinity(runOut) || runOut == double.MaxValue ? 0.0 : runOut) + (runHome == double.MaxValue ? 0.0 : runHome);
            return new HaulWhy(!startOk ? HaulWhyKind.NoStartStop : !endOk ? HaulWhyKind.NoEndStop : HaulWhyKind.LegTooLong, need, budget);
        }

        /// <summary>The drive efficiency a plan assumes: the lowest of the colony's robots (1 with none).</summary>
        double PlanningEfficiency
        {
            get
            {
                double e = 1.0; bool any = false;
                foreach (Robot r in Workforce.Robots) { double x = r.Attribute(RobotAttribute.EnergyEfficiency); if (x > 0.0) { e = any ? Math.Min(e, x) : x; any = true; } }
                return Math.Max(0.05, e);
            }
        }

        long coldShareHour = long.MinValue; double coldShare = 1.0;

        /// <summary>
        /// The share of a battery that is usable out in the cold (Weather W8): a battery loses capacity when cold, and a heater only holds it at the
        /// operating limit (-20 C), so in the coldest hour of the next day at the base a leg gets the capacity factor at that limit. 1 in mild air.
        /// A field campaign on Mars lost robots whose legs were planned against the full capacity.
        /// </summary>
        double ColdCapacityShare()
        {
            long hour = (long)Math.Floor(TimeHours);
            if (hour == coldShareHour) return coldShare;
            coldShareHour = hour;
            double coldest = double.MaxValue;
            for (int k = 0; k < 12; k++) coldest = Math.Min(coldest, DiurnalAirTemperatureAt(BaseTileId, TimeHours + k * 2.0));
            coldShare = RobotBattery.ColdCapacityFactor(Math.Max(RobotThermal.OperatingMinC, coldest));
            return coldShare;
        }

        /// <summary>Usable charge for one leg between stops, Wh (infinity when the colony has no robots: nothing to limit).</summary>
        public double LegBudgetWh
        {
            get
            {
                double best = 0.0;
                foreach (Robot r in Workforce.Robots) best = Math.Max(best, r.Battery.CapacityWh);
                return best <= 0.0 ? double.PositiveInfinity : best * (RobotSpecs.ResumeWorkFraction - RobotSpecs.ReturnToChargeFraction) * ColdCapacityShare();   // a robot sets off from the 80 % charge and keeps its 20 % floor
            }
        }

        readonly Dictionary<(int, int, int), HaulPlan> haulPlanCache = new Dictionary<(int, int, int), HaulPlan>();
        int haulPlanStamp = int.MinValue, haulPlanStops = -1;

        public HaulPlan PlanHaul(int from, int to, double cargoKg)
        {
            // plans are remembered for a game hour (the heater follows the weather) and while the stops are the same
            int stamp = (int)TimeHours, stopCount = StagingStops().Count * 1000 + StagingStops().Sum();
            if (stamp != haulPlanStamp || stopCount != haulPlanStops) { haulPlanCache.Clear(); haulPlanStamp = stamp; haulPlanStops = stopCount; }
            var key = (from, to, (int)Math.Round(cargoKg / 10.0));
            if (!haulPlanCache.TryGetValue(key, out HaulPlan plan)) haulPlanCache[key] = plan = ComputeHaulPlan(from, to, cargoKg);
            return plan;
        }

        HaulPlan ComputeHaulPlan(int from, int to, double cargoKg)
        {
            if (Routes == null) return new HaulPlan(false, new List<int>(), double.PositiveInfinity);
            double budget = LegBudgetWh;
            var stops = new List<int>(StagingStops());
            stops.Sort();
            bool Fits(int a, int b2, double kg) { if (a == b2) return true; var leg = HaulLegCost(a, b2, kg); return leg.Reachable && leg.TotalWh <= budget; }
            // the robot starts at a stop and must be able to reach the pick-up, and to get back to a stop after the drop
            bool startOk = false, endOk = false;
            foreach (int st in stops) { startOk |= Fits(st, from, 0.0); endOk |= Fits(to, st, 0.0); }
            if (!startOk || !endOk) return new HaulPlan(false, new List<int>(), double.PositiveInfinity);
            // A safety rule (KI-46, decided 2026-11-25): a robot is on one charge from the stop it leaves until the next stop. The empty run to the pick-up counts with the
            // first loaded leg when the pick-up is not itself a stop, and the run home from the drop counts with the last loaded leg when the drop is not a stop.
            double startWh = double.MaxValue, endWh = double.MaxValue;
            foreach (int st in stops) { startWh = Math.Min(startWh, st == from ? 0.0 : HaulLegCost(st, from, 0.0).TotalWh); endWh = Math.Min(endWh, st == to ? 0.0 : HaulLegCost(to, st, 0.0).TotalWh); }
            // shortest chain from the pick-up to the drop over the stops, loaded legs only
            var nodes = new List<int> { from };
            foreach (int st in stops) if (st != from && st != to) nodes.Add(st);
            nodes.Add(to);
            int n = nodes.Count;
            var dist = new double[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = double.PositiveInfinity; prev[i] = -1; }
            dist[0] = 0.0;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || double.IsInfinity(dist[u])) break;
                done[u] = true;
                for (int v = 0; v < n; v++)
                {
                    if (done[v]) continue;
                    var leg = HaulLegCost(nodes[u], nodes[v], cargoKg);
                    double carried = leg.TotalWh + (u == 0 ? startWh : 0.0) + (v == n - 1 ? endWh : 0.0);
                    if (!leg.Reachable || carried > budget) continue;
                    double nd = dist[u] + leg.Hours + (Routes.DrivePowerW > 0.0 ? (leg.ClimbWh + leg.HeaterWh) / Routes.DrivePowerW : 0.0);
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }
            if (double.IsInfinity(dist[n - 1])) return new HaulPlan(false, new List<int>(), double.PositiveInfinity);
            var via = new List<int>();
            for (int i = prev[n - 1]; i > 0; i = prev[i]) via.Add(nodes[i]);
            via.Reverse();
            return new HaulPlan(true, via, dist[n - 1]);
        }

        /// <summary>
        /// When to start a haul to spend the least heater energy (decided 2026-11-24): the delay in hours, within one day, that minimises the heater
        /// energy of the whole round trip, and what it saves against starting now. Zero delay and zero saving when no start is better.
        /// </summary>
        public (double DelayHours, double SavedWh) BestHaulStart(int from, int to, double cargoKg)
        {
            if (Routes == null || DayLengthHours <= 0.0) return (0.0, 0.0);
            List<int> there = Routes.Route(from, to), back = Routes.Route(to, from);
            if (there == null || back == null) return (0.0, 0.0);
            double Heater(double delay)
            {
                var o = LegAlong(from, there, cargoKg, delay);
                return o.HeaterWh + LegAlong(to, back, 0.0, delay + o.Hours).HeaterWh;
            }
            double now = Heater(0.0), best = now, bestDelay = 0.0;
            for (int k = 1; k < 12; k++)
            {
                double d = DayLengthHours * k / 12.0, h = Heater(d);
                if (h < best - 1e-9) { best = h; bestDelay = d; }
            }
            return (bestDelay, now - best);
        }

        /// <summary>The round trip of a haul: loaded out, empty back.</summary>
        public (HaulLeg Out, HaulLeg Back) HaulTripCost(int from, int to, double cargoKg) => (HaulLegCost(from, to, cargoKg), HaulLegCost(to, from, 0.0));

        /// <summary>
        /// The cost the planner ranks sources by, in hours: the driving hours plus the extra energy of the climb and of the heater, as hours of
        /// flat driving at the drive power (Wh over W). Equal to the driving hours on flat ground in mild air.
        /// </summary>
        public double HaulCostHours(int from, int to, double cargoKg)
        {
            var plan = PlanHaul(from, to, cargoKg);
            return plan.Feasible ? plan.Hours : double.PositiveInfinity;
        }
    }
}

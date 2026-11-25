using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>How a stranded robot is helped (R1, decided 2026-11-25 as the director's delegate; saved as an integer).</summary>
    public enum RescueMethod { Jump, Tow }

    /// <summary>A rescue under way: a helper drives to a stranded robot, then charges it through a cable (jump) or carries it to shelter (tow).</summary>
    public sealed class RescueOrder
    {
        public int Id, StrandedId, RescuerId, Shelter;
        public RescueMethod Method;
        /// <summary>0: the rescuer is driving there; 1: the cable is on (jump) or the robot is being loaded (tow); 2: towing.</summary>
        public int Stage;
        /// <summary>Game time by which the stranded robot is destroyed if nothing is done (infinity when it is not in danger).</summary>
        public double DeadlineHours;
        public double PlannedWh, HandlingLeftHours, TransferWh;
    }

    /// <summary>The failure ladder state of a robot or a task (R2).</summary>
    public enum LadderState { None, TryingAlternatives, WaitingForHelp, NeedsYou }

    public sealed partial class PlanetSimulation
    {
        /// <summary>
        /// DC-DC conversion efficiency of the cable transfer, 0.93 (modern DC-DC converters run at 90 to 95 %: Erickson and Maksimovic, Fundamentals of Power Electronics, 3rd ed., ch. 1; a game rule inside that range).
        /// </summary>
        public const double JumpChargeEfficiency = 0.93;
        /// <summary>Hours a cable connection (or loading a robot onto the carrier) takes (game rule, TBD).</summary>
        public const double RescueHandlingHours = 1.0;
        /// <summary>The share of a battery a helper keeps for itself (the usual 20 % return charge).</summary>
        const double RescuerReserve = RobotSpecs.ReturnToChargeFraction;

        readonly List<RescueOrder> rescues = new List<RescueOrder>();
        int nextRescueId = 1;
        readonly Dictionary<int, string> rescueRefusals = new Dictionary<int, string>();
        readonly Dictionary<int, double> rescueDeadlineSeen = new Dictionary<int, double>();

        /// <summary>Counters for the balance runs (not saved).</summary>
        public int RescuesRequested { get; private set; }
        public int RescuesSucceeded { get; private set; }
        public int RescuesFailed { get; private set; }
        public int RescuesByJump { get; private set; }
        public int RescuesByTow { get; private set; }
        public double RescueEnergyWh { get; private set; }

        public IReadOnlyList<RescueOrder> Rescues => rescues;

        /// <summary>Force one method (balance runs and demos); null lets the planner pick the cheaper.</summary>
        public RescueMethod? RescueMethodOverride { get; set; }

        /// <summary>The time a robot has before it is destroyed (an empty battery outside its range lasts one hour; with charge left, the heater's time plus that hour): infinity when it is in no danger.</summary>
        double DeadlineOf(Robot r)
        {
            if (r.Sheltered) return double.PositiveInfinity;
            double heater = FutureHeaterW(r);
            double powered = r.Hibernating || r.Battery.UsableWh <= 1e-6 ? 0.0 : heater > 0.0 ? r.Battery.UsableWh / heater : double.PositiveInfinity;
            if (double.IsInfinity(powered) && !r.Hibernating) return double.PositiveInfinity;
            // after the battery runs out the body soaks toward the air that will be there: the next day's mean (the swing is smoothed away by the time constant) less a little for its remainder
            double air = SoakAirC(r.Tile);
            double body = r.Hibernating ? r.BodyTempC : Math.Max(RobotThermal.OperatingMinC, Math.Min(RobotThermal.OperatingMaxC, air));
            double tau = RobotThermal.BodyTauHours(1.0);
            double hib = RobotThermal.HibernationHoursUntilLimit(body, air, tau);
            return double.IsInfinity(hib) ? double.PositiveInfinity : TimeHours + powered + hib;
        }

        /// <summary>The air temperature a soaking body will feel: the mean of the next 24 hours (forecast or climatology, with the weather now on top) less a tenth of the swing.</summary>
        double SoakAirC(int tile)
        {
            double sum = 0.0, lo = double.MaxValue, hi = double.MinValue;
            double anomaly = LiveAirTemperatureAt(tile) - DiurnalAirTemperatureAt(tile, TimeHours);
            for (int k = 0; k < 12; k++) { double t = DiurnalAirTemperatureAt(tile, TimeHours + 2.0 * k) + anomaly; sum += t; lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
            return sum / 12.0 - 0.1 * (hi - lo) / 2.0;
        }

        /// <summary>The heater a robot out in the open will need over the next night: the coldest air of the next 12 hours (a rescue takes hours and the temperature falls), W.</summary>
        double FutureHeaterW(Robot r)
        {
            double coldest = double.MaxValue;
            double anomaly = LiveAirTemperatureAt(r.Tile) - DiurnalAirTemperatureAt(r.Tile, TimeHours);   // today's weather rides on the day-night swing
            for (int k = 0; k <= 6; k++) coldest = Math.Min(coldest, DiurnalAirTemperatureAt(r.Tile, TimeHours + 2.0 * k) + anomaly);
            return Math.Max(r.HeaterW, RobotThermal.HeaterW(coldest, r.Has(RobotModule.ThermalInsulation)));
        }

        bool NeedsRescue(Robot r) => r.IsStranded || (!r.Sheltered && FutureHeaterW(r) > 0.0 && r.Battery.UsableWh / FutureHeaterW(r) < RescueEarlyHours && r.ReserveFraction > r.Battery.StateOfCharge + 1e-9);

        /// <summary>A robot out in the cold or heat that cannot get back on what it has asks for help this many hours before its battery runs out (game rule, TBD).</summary>
        public const double RescueEarlyHours = 6.0;

        /// <summary>
        /// The charge a stranded robot must be given to drive itself to a charger: a cold battery locks part of its capacity, so the locked part is filled first and the reserve for the
        /// way back (heater, climb, efficiency) comes on top, plus the cell's own charging loss.
        /// </summary>
        double JumpNeedWh(Robot s)
        {
            double cold = ColdShare(s), cap = s.Battery.CapacityWh;
            double locked = cap * (1.0 - cold);
            double target = Math.Max(s.ReserveFraction, 0.15) * cap * cold + locked + 0.02 * cap;
            return Math.Max(0.0, target - s.Battery.StoredWh) * 1.06 + (RobotThermal.CanCharge(s.BatteryTempC) ? 0.0 : Math.Max(FutureHeaterW(s), 20.0) * 1.5);
        }

        double ColdShare(Robot r) => Math.Max(0.05, r.Battery.ColdFactor);

        /// <summary>The nearest shelter (lander, charger, battery yard or workshop) by route hours, or -1.</summary>
        int NearestShelter(int tile)
        {
            if (Routes == null) return -1;
            int best = -1; double bestH = double.PositiveInfinity;
            foreach (int s in ShelterTiles()) { double h = Routes.HoursBetween(tile, s); if (h < bestH) { bestH = h; best = s; } }
            return double.IsInfinity(bestH) ? -1 : best;
        }

        /// <summary>Energy a helper has to spare above its own return charge, Wh.</summary>
        double SpareWh(Robot h) => Math.Max(0.0, h.Battery.UsableWh - RescuerReserve * h.Battery.CapacityWh * ColdShare(h));

        /// <summary>The cheapest feasible way to help a stranded robot, or null with the reason in <paramref name="why"/>.</summary>
        (Robot Rescuer, RescueMethod Method, int Shelter, double Wh, double Hours)? PlanRescue(Robot s, out string why)
        {
            why = null;
            int shelter = NearestShelter(s.Tile);
            if (shelter < 0) { why = "there is no shelter the robot can be brought to"; return null; }
            double deadline = DeadlineOf(s);
            double budgetHours = double.IsInfinity(deadline) ? double.PositiveInfinity : deadline - TimeHours;
            double needWh = JumpNeedWh(s);   // what it needs to drive itself to a charger, with the heater running
            double warmWh = RobotThermal.CanCharge(s.BatteryTempC) ? 0.0 : Math.Max(s.HeaterW, 20.0) * 1.5;   // a cold battery is warmed from the cable before it takes charge (the existing rule)
            (Robot, RescueMethod, int, double, double)? best = null;
            string nearest = null; double nearestHours = double.PositiveInfinity; double bestNeed = double.PositiveInfinity, bestSpare = 0.0; bool timeOk = false;
            foreach (Robot h in Workforce.Robots)
            {
                if (h.Id == s.Id || h.IsShutDown || h.IsStranded || rescues.Any(o => o.RescuerId == h.Id) || h.Activity == RobotActivity.Maintenance) continue;
                double goH = Routes.HoursBetween(h.Tile, s.Tile) / Math.Max(0.1, h.Attribute(RobotAttribute.Speed));
                if (double.IsInfinity(goH)) continue;
                if (goH < nearestHours) { nearestHours = goH; nearest = $"robot {h.Id}"; }
                if (goH + RescueHandlingHours > budgetHours) continue;
                timeOk = true;
                double go = HaulLegCost(h.Tile, s.Tile, 0.0).TotalWh;
                double spare = SpareWh(h);
                // jump: out, the cable transfer through the converter, and home from there
                double jumpWh = go + (needWh + warmWh) / JumpChargeEfficiency;
                if (goH < nearestHours + 1e-9) { bestNeed = Math.Min(bestNeed, jumpWh); bestSpare = spare; }
                double back = HaulLegCost(s.Tile, NearestShelter(s.Tile) >= 0 ? NearestShelter(s.Tile) : s.Tile, 0.0).TotalWh;
                if ((!RescueMethodOverride.HasValue || RescueMethodOverride == RescueMethod.Jump) && 1.2 * (jumpWh + back) <= spare && (!best.HasValue || jumpWh + back < best.Value.Item4)) best = (h, RescueMethod.Jump, shelter, jumpWh + back, goH + RescueHandlingHours);
                // tow: out, then the stranded robot's mass as cargo to the shelter (the haul physics)
                var tow = HaulLegCost(s.Tile, shelter, HaulPhysics.DefaultRobotMassKg);
                double towWh = go + tow.TotalWh;
                double towH = goH + RescueHandlingHours + Routes.HoursBetween(s.Tile, shelter) / Math.Max(0.1, h.Attribute(RobotAttribute.Speed));   // the robot must live to the shelter
                if (towH > budgetHours) continue;
                if ((!RescueMethodOverride.HasValue || RescueMethodOverride == RescueMethod.Tow) && tow.Reachable && Math.Max(towWh, 2.0 * go) <= spare &&   // and enough to come home empty if the robot is lost before the pick-up
                     (!best.HasValue || towWh < best.Value.Item4)) best = (h, RescueMethod.Tow, shelter, towWh, towH);
            }
            if (best.HasValue) return best;
            why = nearest == null ? "no other robot can reach it" : (double.IsInfinity(budgetHours) || timeOk) ? $"no robot has the charge to reach it and come back (the nearest, {nearest}, is {nearestHours:0.#} h away and would need about {bestNeed:0} Wh for the jump, {bestSpare:0} Wh to spare)"
                : $"no robot can reach tile {s.Tile} before it {(s.HeaterW > 0.0 ? (s.BodyTempC < 15.0 || SoakAirC(s.Tile) < 15.0 ? "cold-soaks to destruction" : "heat-soaks to destruction") : "is lost")} in {budgetHours:0.#} h (the nearest, {nearest}, is {nearestHours:0.#} h away)";
            return null;
        }

        /// <summary>Rescue on or off (off only for balance comparisons).</summary>
        public bool RescueEnabled { get; set; } = true;

        void StepRescue(double dt)
        {
            if (!RescueEnabled || Workforce == null || Routes == null) return;
            // new requests
            foreach (Robot s in Workforce.Robots.ToList())
            {
                if (!NeedsRescue(s) || rescues.Any(o => o.StrandedId == s.Id)) { if (!NeedsRescue(s)) rescueRefusals.Remove(s.Id); continue; }
                if (StormAt(s.Tile) && double.IsInfinity(DeadlineOf(s))) { rescueRefusals[s.Id] = "a storm is over it: the rescue waits until it passes"; continue; }
                var plan = PlanRescue(s, out string why);
                if (!plan.HasValue)
                {
                    bool sameKind = rescueRefusals.TryGetValue(s.Id, out string prev) && prev.Split('(')[0] == why.Split('(')[0];   // the numbers move every step: tell the player once per reason
                    rescueRefusals[s.Id] = why;
                    if (!sameKind)
                    {
                        Log($"Robot {s.Id}", "Rescue impossible", 0.0, "", $"R{s.Id}", $"Robot {s.Id} on tile {s.Tile} needs help and nobody can give it: {why}. {RescueAdvice(s)}", s.Id);
                    }
                    continue;
                }
                rescueRefusals.Remove(s.Id);
                var p = plan.Value;
                var order = new RescueOrder { Id = nextRescueId++, StrandedId = s.Id, RescuerId = p.Rescuer.Id, Method = p.Method, Shelter = p.Shelter, DeadlineHours = DeadlineOf(s), PlannedWh = p.Wh, HandlingLeftHours = RescueHandlingHours };
                rescues.Add(order); RescuesRequested++;
                Workforce.SendTo(p.Rescuer.Id, s.Tile);
                Log($"Robot {s.Id}", "Rescue", p.Wh, "Wh", $"R{s.Id}", $"Robot {p.Rescuer.Id} goes to help robot {s.Id} on tile {s.Tile}: {(p.Method == RescueMethod.Jump ? "a jump charge through a cable" : "a tow to the shelter on tile " + p.Shelter)} (about {p.Wh:0} Wh, {p.Hours:0.#} h" + (double.IsInfinity(order.DeadlineHours) ? ")." : $", before it is lost in {order.DeadlineHours - TimeHours:0.#} h)."), s.Id);
            }
            // orders under way
            foreach (RescueOrder o in rescues.ToList())
            {
                Robot s = Workforce.GetRobot(o.StrandedId), h = Workforce.GetRobot(o.RescuerId);
                if (s == null) { Finish(o, false, "the robot was lost"); continue; }
                if (h == null || h.IsShutDown) { Finish(o, false, "the helper was lost"); continue; }
                if (!NeedsRescue(s) && o.Stage == 0) { Finish(o, true, "it no longer needs help"); continue; }
                if (o.Stage == 0)
                {
                    if (h.Tile == s.Tile) o.Stage = 1;
                    else if (!h.ManualTarget.HasValue && !h.IsTravelling) Workforce.SendTo(h.Id, s.Tile);
                }
                if (o.Stage == 1)
                {
                    o.HandlingLeftHours -= dt;
                    if (o.Method == RescueMethod.Jump)
                    {
                        double need = o.TransferWh + JumpNeedWh(s) + 0.0;   // the rest still needed, so a robot that already took charge is not given the same twice
                        // the helper keeps what it needs to get itself to the shelter (the return leg with a 20 % margin): it never gives that away
                        double keep = HaulLegCost(s.Tile, o.Shelter, 0.0).TotalWh * 1.6;   // the way home runs the heater at tonight's cold, which the plan only estimates
                        double canGive = Math.Max(0.0, (h.Battery.UsableWh - keep) * JumpChargeEfficiency);
                        double part = Math.Min(Math.Min(need - o.TransferWh, need * dt / RescueHandlingHours), canGive);
                        if (part > 0.0)
                        {
                            double drawn = h.Battery.Draw(part / JumpChargeEfficiency / dt, dt) * dt;
                            s.Battery.Charge(drawn * JumpChargeEfficiency / dt, dt);
                            o.TransferWh += drawn * JumpChargeEfficiency; RescueEnergyWh += drawn;
                        }
                        if (o.HandlingLeftHours <= 1e-9) Finish(o, true, "the cable is off");
                    }
                    else if (o.HandlingLeftHours <= 1e-9) { o.Stage = 2; Workforce.SendTo(h.Id, o.Shelter); }
                }
                else if (o.Stage == 2)
                {
                    s.Tile = h.Tile; s.Route.Clear();
                    if (h.Tile == o.Shelter) Finish(o, true, "it is at the shelter");
                    else if (!h.ManualTarget.HasValue && !h.IsTravelling) Workforce.SendTo(h.Id, o.Shelter);
                }
            }
        }

        /// <summary>
        /// Advice only (S3): where a charger would put the route to a tile a rescue could not reach within one charge. The existing depot advice is asked first (a depot with a charging bay where
        /// hauls already go); with none, the tile halfway along the way from the lander. Nothing is built.
        /// </summary>
        public string RescueAdvice(Robot s)
        {
            var sites = DepotSites(1);
            if (sites.Count > 0) return $"Advice: the depot advice names tile {sites[0].Tile} (a charging bay there would also put work near it within one charge).";
            var route = Routes?.Route(BaseTileId, s.Tile);
            int mid = route != null && route.Count > 1 ? route[route.Count / 2 - 1] : s.Tile;
            return $"Advice: a charging station or battery yard on tile {mid}, halfway from the lander, would put tile {s.Tile} within one charge of help.";
        }

        /// <summary>Extra cargo the drive carries: a robot being towed.</summary>
        public double TowedKgBy(int rescuerId) => rescues.Any(o => o.RescuerId == rescuerId && o.Stage == 2) ? HaulPhysics.DefaultRobotMassKg : 0.0;

        void Finish(RescueOrder o, bool ok, string why)
        {
            rescues.Remove(o);
            Robot s = Workforce.GetRobot(o.StrandedId);
            if (ok) { RescuesSucceeded++; if (o.Method == RescueMethod.Jump) RescuesByJump++; else RescuesByTow++; }
            else RescuesFailed++;
            if (o.Method == RescueMethod.Tow && o.Stage == 2) RescueEnergyWh += o.PlannedWh;
            Log($"Robot {o.StrandedId}", ok ? "Rescue done" : "Rescue failed", o.PlannedWh, "Wh", $"R{o.StrandedId}", $"Rescue of robot {o.StrandedId} ({(o.Method == RescueMethod.Jump ? "jump charge" : "tow")}): {why}.", o.StrandedId);
            Robot h = Workforce.GetRobot(o.RescuerId);
            if (h != null) { h.ManualTarget = null; if (ok && o.Method == RescueMethod.Jump) Workforce.SendTo(h.Id, o.Shelter); }   // the helper goes home at once
        }

        // ---- the failure ladder (R2): what a robot is doing about being stuck ----

        /// <summary>The ladder state and a line for the player: trying alternatives, waiting for help, or needs you.</summary>
        public (LadderState State, string Text) LadderOf(int robotId)
        {
            Robot r = Workforce.GetRobot(robotId);
            if (r == null) return (LadderState.None, "");
            RescueOrder o = rescues.FirstOrDefault(x => x.StrandedId == robotId);
            if (o != null)
                return (LadderState.WaitingForHelp, $"waiting for help: robot {o.RescuerId} is coming ({(o.Method == RescueMethod.Jump ? "jump charge" : "tow")})" + (double.IsInfinity(o.DeadlineHours) ? "" : $", {Math.Max(0.0, o.DeadlineHours - TimeHours):0.#} h before it is lost"));
            if (rescueRefusals.TryGetValue(robotId, out string why))
                return (LadderState.NeedsYou, "needs you: " + why);
            if (NeedsRescue(r)) return (LadderState.TryingAlternatives, "looking for a robot that can help");
            if (r.IsShutDown && (r.Shutdown == ShutdownKind.Mechanical || r.Shutdown == ShutdownKind.CriticalDamage)) return (LadderState.NeedsYou, "needs you: it is damaged and shut down; it needs maintenance");
            return (LadderState.None, "");
        }

        /// <summary>A stranded robot's deadline: hours until it is lost, or infinity.</summary>
        public double RescueDeadlineHours(int robotId) { Robot r = Workforce.GetRobot(robotId); return r == null ? double.PositiveInfinity : Math.Max(0.0, DeadlineOf(r) - TimeHours); }

        /// <summary>The player asks for a rescue now: it is planned at once (the same planner); returns false when nobody can help.</summary>
        public bool RequestRescue(int robotId)
        {
            Robot s = Workforce.GetRobot(robotId);
            if (s == null || rescues.Any(o => o.StrandedId == robotId)) return false;
            rescueRefusals.Remove(robotId);
            var plan = PlanRescue(s, out string why);
            if (!plan.HasValue) { rescueRefusals[robotId] = why; return false; }
            var p = plan.Value;
            rescues.Add(new RescueOrder { Id = nextRescueId++, StrandedId = s.Id, RescuerId = p.Rescuer.Id, Method = p.Method, Shelter = p.Shelter, DeadlineHours = DeadlineOf(s), PlannedWh = p.Wh, HandlingLeftHours = RescueHandlingHours });
            RescuesRequested++;
            Workforce.SendTo(p.Rescuer.Id, s.Tile);
            return true;
        }

        void CaptureRescues(SimulationState state)
        {
            state.NextRescueId = nextRescueId;
            foreach (RescueOrder o in rescues)
                state.Rescues.Add(new RescueRecord { Id = o.Id, StrandedId = o.StrandedId, RescuerId = o.RescuerId, Method = o.Method, Stage = o.Stage, Shelter = o.Shelter, DeadlineHours = double.IsInfinity(o.DeadlineHours) ? -1.0 : o.DeadlineHours, PlannedWh = o.PlannedWh, HandlingLeftHours = o.HandlingLeftHours, TransferWh = o.TransferWh });
        }

        void RestoreRescues(SimulationState state)
        {
            rescues.Clear(); rescueRefusals.Clear();
            nextRescueId = state.Version >= 62 ? Math.Max(1, state.NextRescueId) : 1;
            if (state.Version < 62 || state.Rescues == null) return;
            foreach (RescueRecord r in state.Rescues)
                if (Workforce.GetRobot(r.StrandedId) != null && Workforce.GetRobot(r.RescuerId) != null)
                    rescues.Add(new RescueOrder { Id = r.Id, StrandedId = r.StrandedId, RescuerId = r.RescuerId, Method = r.Method, Stage = r.Stage, Shelter = r.Shelter, DeadlineHours = r.DeadlineHours < 0.0 ? double.PositiveInfinity : r.DeadlineHours, PlannedWh = r.PlannedWh, HandlingLeftHours = r.HandlingLeftHours, TransferWh = r.TransferWh });
        }
    }
}

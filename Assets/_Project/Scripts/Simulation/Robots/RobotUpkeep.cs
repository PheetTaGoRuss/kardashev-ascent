using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>Somewhere robots' repairs and new batteries are paid from: the colony's build parts.</summary>
    public interface IPartsStore
    {
        double StockWh { get; }
        /// <summary>Take up to <paramref name="wh"/> of parts; returns what was taken, Wh.</summary>
        double Take(double wh);
    }

    /// <summary>
    /// Robots looking after themselves each step (spec §6–§8, phase 2):
    /// <list type="number">
    /// <item><see cref="Plan"/> (before the grid runs): robots that are done charging (≥ 80 %) or repaired go back to
    /// work; robots below 20 % go to a free charger; worn robots (low condition, worn-out battery, or shut down) go to a
    /// free facility that repairs. It returns the charging power asked for: <em>critical</em> for robots still below
    /// 20 %, <em>top-up</em> for the rest (design D6 puts critical charging right after the colony and top-up after
    /// the grid battery).</item>
    /// <item><see cref="Apply"/> (after the grid): charging robots take the power the grid gave them; the others run on
    /// their own battery (idle or working draw) and wear; robots in maintenance are repaired with build parts and get
    /// a new battery when theirs is worn out.</item>
    /// </list>
    /// </summary>
    public sealed class RobotUpkeep
    {
        readonly Dictionary<int, double> dockRequest = new Dictionary<int, double>();
        readonly Dictionary<int, double> criticalRequest = new Dictionary<int, double>();
        readonly Dictionary<int, double> topUpRequest = new Dictionary<int, double>();

        /// <summary>Charging asked for at the lander: its RTGs serve it first, so a robot can always recover (D10).</summary>
        public double DockRequestW { get; private set; }
        /// <summary>Charging asked for elsewhere by robots below 20 %: served right after the colony (D6).</summary>
        public double CriticalRequestW { get; private set; }
        /// <summary>Charging asked for elsewhere by robots above 20 %: served from spare power before build parts (D6).</summary>
        public double TopUpRequestW { get; private set; }

        /// <summary>Charging power robots took in the last <see cref="Apply"/>, W.</summary>
        public double ChargingW { get; private set; }

        public void Plan(Workforce workforce, IReadOnlyList<RobotFacility> facilities, double hours, Movement movement = null)
        {
            // Movement: how much charge each robot must keep to drive back, and who is stuck with an empty battery.
            foreach (Robot r in workforce.Robots)
            {
                double toCharger = movement != null ? movement.HoursToCharger[r.Tile] : 0.0;
                r.ReserveFraction = movement != null && r.Battery.CapacityWh > 0.0 && !double.IsInfinity(toCharger)
                    ? movement.EnergyWh(toCharger) / r.Battery.CapacityWh : 0.0;
                r.IsStranded = movement != null && r.Battery.StoredWh <= 1e-6 && toCharger > 1e-9;
            }
            var byKey = new Dictionary<(int, int), RobotFacility>();
            foreach (RobotFacility f in facilities)
                byKey[(f.Tile, f.Slot)] = f;
            var used = new Dictionary<(int, int), int>();

            foreach (Robot r in workforce.Robots)
            {
                // A facility that is gone (taken down) sends its robots back.
                if (r.Activity != RobotActivity.None && (!r.Facility.HasValue || !byKey.ContainsKey(r.Facility.Value)))
                    Leave(r);
                if (r.Activity == RobotActivity.Charging && r.Battery.StateOfCharge >= RobotSpecs.ResumeWorkFraction - 1e-9)
                    Leave(r);
                // Above 20 % a robot doesn't wait at a charger that gave it nothing (no spare power): it goes back to
                // work and charges again when it is below 20 % (design decision 2026-11-08: "keep working").
                if (r.Activity == RobotActivity.Charging && r.Battery.StateOfCharge >= RobotSpecs.ReturnToChargeFraction
                    && r.LastChargeW <= 0.0 && r.ChargedBefore)
                    Leave(r);
                if (r.Activity == RobotActivity.Maintenance && r.Condition >= 1.0 - 1e-12 && !r.Battery.WornOut)
                    Leave(r);
                if (r.Activity != RobotActivity.None)
                    used[r.Facility.Value] = Used(used, r.Facility.Value) + 1;
            }

            foreach (Robot r in workforce.Robots)
            {
                if (r.Activity != RobotActivity.None)
                    continue;
                if (r.IsStranded)
                    continue;
                if (r.NeedsMaintenance || r.IsShutDown)
                {
                    RobotFacility? shop = Free(r, facilities, used, true, movement);
                    if (shop.HasValue)
                    {
                        Enter(r, RobotActivity.Maintenance, shop.Value, used, movement);
                        continue;
                    }
                }
                if (r.NeedsCharge && !r.IsShutDown)
                {
                    RobotFacility? charger = Free(r, facilities, used, false, movement);
                    if (charger.HasValue)
                        Enter(r, RobotActivity.Charging, charger.Value, used, movement);
                }
            }

            dockRequest.Clear();
            criticalRequest.Clear();
            topUpRequest.Clear();
            DockRequestW = CriticalRequestW = TopUpRequestW = 0.0;
            foreach (Robot r in workforce.Robots)
            {
                if (r.Activity != RobotActivity.Charging || r.IsTravelling || (movement != null && r.Tile != r.Facility.Value.Tile))
                    continue;
                double req = r.Battery.ChargeRequestW(byKey[r.Facility.Value].Capability.PowerW, hours);
                if (r.Facility.Value.Slot < 0)
                {
                    dockRequest[r.Id] = req;
                    DockRequestW += req;
                }
                else if (r.Battery.StateOfCharge < RobotSpecs.ReturnToChargeFraction)
                {
                    criticalRequest[r.Id] = req;
                    CriticalRequestW += req;
                }
                else
                {
                    topUpRequest[r.Id] = req;
                    TopUpRequestW += req;
                }
            }
        }

        /// <summary>
        /// Run the robots for <paramref name="hours"/>: <paramref name="dockServedW"/>, <paramref name="criticalServedW"/>
        /// and <paramref name="topUpServedW"/> are what the grid gave the three kinds of charging (shared in proportion
        /// to what each robot asked for).
        /// </summary>
        public void Apply(Workforce workforce, double hours, double dockServedW, double criticalServedW, double topUpServedW,
                          bool flaringStar, IPartsStore parts, Movement movement = null)
        {
            if (hours <= 0.0)
                return;
            double fd = DockRequestW > 0.0 ? Math.Min(1.0, Math.Max(0.0, dockServedW) / DockRequestW) : 0.0;
            double fc = CriticalRequestW > 0.0 ? Math.Min(1.0, Math.Max(0.0, criticalServedW) / CriticalRequestW) : 0.0;
            double ft = TopUpRequestW > 0.0 ? Math.Min(1.0, Math.Max(0.0, topUpServedW) / TopUpRequestW) : 0.0;
            ChargingW = 0.0;
            // A Maintenance robot free at the base speeds up repairs (its speciality).
            bool mechanic = false;
            foreach (Robot r in workforce.Robots)
                mechanic |= r.Kind == RobotKind.Maintenance && r.Available;
            foreach (Robot r in workforce.Robots)
            {
                if (movement != null && r.IsTravelling)
                {
                    if (!r.IsShutDown)
                        Drive(r, hours, movement);
                }
                else switch (r.Activity)
                {
                    case RobotActivity.Charging:
                        double offer = dockRequest.TryGetValue(r.Id, out double d) ? d * fd
                                     : criticalRequest.TryGetValue(r.Id, out double c) ? c * fc
                                     : topUpRequest.TryGetValue(r.Id, out double t) ? t * ft : 0.0;
                        r.LastChargeW = r.Battery.Charge(offer, hours);
                        r.ChargedBefore = true;
                        ChargingW += r.LastChargeW;
                        break;
                    case RobotActivity.Maintenance:
                        if (movement == null || r.Tile == r.Facility.Value.Tile)
                            Repair(r, hours, parts, mechanic ? RobotSpecs.SpecialistSpeed : 1.0);
                        break;
                    default:
                        if (r.IsShutDown)
                            break;
                        bool working = r.Task.HasValue;
                        r.Battery.Draw(working ? RobotSpecs.WorkingPowerW(r.Task.Value.Kind) : RobotSpecs.IdlePowerW, hours);
                        if (working)
                            r.Condition -= RobotSpecs.WearPerWorkingHour * hours;
                        break;
                }
                if (flaringStar && r.Activity != RobotActivity.Maintenance)
                    r.Condition -= RobotSpecs.FlareWearPerHour * hours * (r.Has(RobotModule.Shielding) ? RobotTech.ShieldingFactor : 1.0);
                r.Condition = Math.Max(0.0, Math.Min(1.0, r.Condition));
            }
        }

        /// <summary>Drive along the route: draw drive power and cross tiles at their terrain's pace.</summary>
        static void Drive(Robot r, double hours, Movement movement)
        {
            double want = movement.DrivePowerW;
            double got = r.Battery.Draw(want, hours);
            double moved = want > 0.0 ? hours * got / want : hours;
            while (moved > 0.0 && r.Route.Count > 0)
            {
                double hop = movement.Routes.HopHoursAt(r.Route[0]);
                double left = (1.0 - r.HopProgress) * hop;
                if (moved < left - 1e-12)
                {
                    r.HopProgress += moved / hop;
                    break;
                }
                moved -= left;
                r.Tile = r.Route[0];
                r.Route.RemoveAt(0);
                r.HopProgress = 0.0;
            }
            if (r.Route.Count == 0 && r.ManualTarget.HasValue && r.Tile == r.ManualTarget.Value)
                r.ManualTarget = null;
        }

        static void Repair(Robot r, double hours, IPartsStore parts, double speed)
        {
            if (parts == null)
                return;
            if (r.Battery.WornOut)
            {
                double cost = BuildCosts.Battery(r.Battery.NominalWh);
                if (parts.StockWh >= cost)
                {
                    parts.Take(cost);
                    r.Battery.Replace();
                }
            }
            double want = Math.Min(1.0 - r.Condition, RobotSpecs.RepairPerHour * speed * hours);
            if (want <= 0.0)
                return;
            double got = parts.Take(want * RobotSpecs.RepairPartsWh);
            r.Condition += got / RobotSpecs.RepairPartsWh;
        }

        static int Used(Dictionary<(int, int), int> used, (int, int) key) => used.TryGetValue(key, out int n) ? n : 0;

        /// <summary>
        /// A facility with a free place: one that repairs, or a charger. With movement, the nearest one it can reach
        /// (then the most powerful); without, the most powerful.
        /// </summary>
        static RobotFacility? Free(Robot r, IReadOnlyList<RobotFacility> facilities, Dictionary<(int, int), int> used, bool repair, Movement movement)
        {
            RobotFacility? best = null;
            double bestHours = double.PositiveInfinity;
            foreach (RobotFacility f in facilities)
            {
                if (repair && !f.Capability.Repairs)
                    continue;
                if (Used(used, (f.Tile, f.Slot)) >= f.Capability.Slots)
                    continue;
                double h = movement != null ? movement.Routes.HoursBetween(r.Tile, f.Tile) : 0.0;
                if (double.IsInfinity(h))
                    continue;
                if (!best.HasValue || h < bestHours - 1e-9
                    || (Math.Abs(h - bestHours) <= 1e-9 && f.Capability.PowerW > best.Value.Capability.PowerW))
                {
                    best = f;
                    bestHours = h;
                }
            }
            return best;
        }

        static void Enter(Robot r, RobotActivity activity, RobotFacility f, Dictionary<(int, int), int> used, Movement movement)
        {
            r.Activity = activity;
            r.Facility = (f.Tile, f.Slot);
            r.Task = null;
            r.OnTemporaryTask = false;
            r.ManualTarget = null;
            used[(f.Tile, f.Slot)] = Used(used, (f.Tile, f.Slot)) + 1;
            r.Route.Clear();
            r.HopProgress = 0.0;
            if (movement != null && r.Tile != f.Tile)
            {
                List<int> route = movement.Routes.Route(r.Tile, f.Tile);
                if (route != null) r.Route.AddRange(route);
            }
        }

        static void Leave(Robot r)
        {
            r.Activity = RobotActivity.None;
            r.Facility = null;
            r.LastChargeW = 0.0;
            r.ChargedBefore = false;
        }
    }
}

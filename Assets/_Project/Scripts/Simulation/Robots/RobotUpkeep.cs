using KardashevAscent.Simulation.Energy;
using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// Robots looking after themselves each step (spec §6–§8, phase 2):
    /// <list type="number">
    /// <item><see cref="Plan"/> (before the grid runs): robots that are done charging (≥ 80 %) or repaired go back to
    /// work; robots below 20 % go to a free charger; worn robots (low condition, worn-out battery, or shut down) go to a
    /// free facility that repairs. It returns the charging power asked for: <em>critical</em> for robots still below
    /// 20 %, <em>top-up</em> for the rest (design D6 puts critical charging right after the colony and top-up after
    /// the grid battery).</item>
    /// <item><see cref="Apply"/> (after the grid): charging robots take the power the grid gave them; the others run on
    /// their own battery (idle or working draw) and wear; robots in maintenance are repaired with materials and get
    /// a new battery when theirs is worn out.</item>
    /// </list>
    /// </summary>
    public sealed class RobotUpkeep
    {
        readonly Dictionary<int, double> dockRequest = new Dictionary<int, double>();
        readonly Dictionary<int, double> criticalRequest = new Dictionary<int, double>();
        readonly Dictionary<int, double> topUpRequest = new Dictionary<int, double>();

        /// <summary>The cargo a robot carries, kg (set by the simulation); it loads the drive: heavier, so more power (MO-04).</summary>
        public System.Func<Robot, double> CargoKgOf { get; set; }

        /// <summary>Energy spent driving with cargo aboard since the simulation last read it, Wh (it zeroes it).</summary>
        public double HaulDriveWh { get; set; }

        /// <summary>Charging asked for at a charger on the landing tile: the lander's RTGs serve it first (D10).</summary>
        public double DockRequestW { get; private set; }
        /// <summary>Charging asked for elsewhere by robots below 20 %: served right after the colony (D6).</summary>
        public double CriticalRequestW { get; private set; }
        /// <summary>Charging asked for elsewhere by robots above 20 %: served from spare power before build parts (D6).</summary>
        public double TopUpRequestW { get; private set; }

        /// <summary>Charging power robots took in the last <see cref="Apply"/>, W.</summary>
        public double ChargingW { get; private set; }

        /// <summary>Charge a robot keeps to climb back to the nearest charger: the climb from its tile up to the charger's height, at the high end of the relief estimate.</summary>
        double ClimbReserveWh(Movement movement, Robot r)
        {
            if (movement.HeightM == null || movement.NearestChargerHeightM == null) return 0.0;
            double up = movement.AscentHomeM != null ? movement.AscentHomeM[r.Tile] : movement.NearestChargerHeightM[r.Tile] - movement.HeightM[r.Tile];
            if (up <= 0.0) return 0.0;
            return Logistics.HaulPhysics.ClimbEnergyWh(Logistics.HaulPhysics.DefaultRobotMassKg, CargoKgOf != null ? CargoKgOf(r) : 0.0, movement.GravityMs2, up, r.Attribute(RobotAttribute.EnergyEfficiency))
                   * Logistics.HaulPhysics.ClimbEffortShare * movement.ReliefReserveScale;
        }

        /// <summary>The trip-home reserve is kept this many times over (game rule, TBD): a lumpy climb is paid when a tile is entered, and the cargo makes the drive dearer; a field campaign (Weather W8) lost robots that kept the bare estimate.</summary>
        public const double ReserveSafety = 1.0;

        public void Plan(Workforce workforce, IReadOnlyList<RobotFacility> facilities, double hours, Movement movement = null)
        {
            // Each robot follows its group's maintenance rule (spec v2 §8).
            foreach (Robot r in workforce.Robots)
            {
                RobotGroup g = workforce.GetGroup(r.GroupId);
                r.MaintenanceBelow = g != null ? g.MaintenanceBelow : RobotSpecs.MaintenanceBelowCondition;
            }
            // Movement: how much charge each robot must keep to drive back, and who is stuck with an empty battery.
            foreach (Robot r in workforce.Robots)
            {
                double toCharger = movement != null ? movement.HoursToChargerFor(r)[r.Tile] / r.Attribute(RobotAttribute.Speed) : 0.0;
                r.ReserveFraction = movement != null && r.Battery.CapacityWh > 0.0 && !double.IsInfinity(toCharger)
                    ? ReserveSafety * (movement.EnergyWh(toCharger) / r.Attribute(RobotAttribute.EnergyEfficiency) * Logistics.HaulPhysics.LoadFactor(Logistics.HaulPhysics.DefaultRobotMassKg, CargoKgOf != null ? CargoKgOf(r) : 0.0) + ClimbReserveWh(movement, r) + r.HeaterW * toCharger) / Math.Max(1e-9, r.Battery.CapacityWh * r.Battery.ColdFactor) : 0.0;   // a cold battery holds less: the state of charge is a share of what it can hold now   // the way back also runs the heater (Weather W8: robots were lost on Mars driving home on an empty battery)
                r.IsStranded = movement != null && r.Battery.UsableWh <= 1e-6 && toCharger > 1e-9;
                r.ChargerExists = movement == null ? HasChargingFacility(facilities) : movement.HasCharger;
            }
            var byKey = new Dictionary<(int, int), RobotFacility>();
            int landerTile = -1;
            foreach (RobotFacility f in facilities)
            {
                byKey[(f.Tile, f.Slot)] = f;
                if (f.Slot < 0) landerTile = f.Tile;
            }
            var used = new Dictionary<(int, int), int>();

            foreach (Robot r in workforce.Robots)
            {
                // A facility that is gone (taken down) sends its robots back.
                if (r.Activity != RobotActivity.None && (!r.Facility.HasValue || !byKey.ContainsKey(r.Facility.Value)))
                    Leave(r);
                // Charged to 80 %, or to what a long trip needs (TripCharge).
                if (r.Activity == RobotActivity.Charging
                    && r.Battery.StateOfCharge >= Math.Max(RobotSpecs.ResumeWorkFraction, r.TripCharge) - 1e-6)
                    Leave(r);
                // Above 20 % a robot doesn't wait at a charger that gave it nothing (no spare power): it goes back to
                // work and charges again when it is below 20 % (design decision 2026-11-08: "keep working").
                // A robot charging for a long trip waits: there is nothing else it can reach.
                if (r.Activity == RobotActivity.Charging && r.Battery.StateOfCharge >= RobotSpecs.ReturnToChargeFraction
                    && r.LastChargeW <= 0.0 && r.ChargedBefore && r.TripCharge <= 0.0)
                    Leave(r);
                if (r.Activity == RobotActivity.Maintenance && r.Condition >= 1.0 - 1e-12 && !r.Battery.WornOut && !r.CriticallyDamaged)
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
                if (r.NeedsMaintenance || r.Shutdown == ShutdownKind.Mechanical || r.Shutdown == ShutdownKind.CriticalDamage)
                {
                    RobotFacility? shop = Free(r, facilities, used, true, movement);
                    if (shop.HasValue)
                    {
                        workforce.LeftTask(r, "went for maintenance");
                        Enter(r, RobotActivity.Maintenance, shop.Value, used, movement);
                        continue;
                    }
                }
                // An empty battery (energy shutdown) still lets a robot at a charger plug in.
                if (r.NeedsCharge && (r.Shutdown == ShutdownKind.None || r.Shutdown == ShutdownKind.Energy))
                {
                    RobotFacility? charger = Free(r, facilities, used, false, movement);
                    if (charger.HasValue)
                    {
                        workforce.LeftTask(r, "went to charge");
                        Enter(r, RobotActivity.Charging, charger.Value, used, movement);
                    }
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
                if (!RobotThermal.CanCharge(r.BatteryTempC))
                    continue;   // a battery too cold or hot to charge is warmed first and asks for no power (otherwise the grid would serve energy nobody takes)
                double req = r.Battery.ChargeRequestW(byKey[r.Facility.Value].Capability.PowerW, hours);
                // A charger on the landing tile is wired to the lander: its RTGs serve it first, so robots can always
                // recover there (D10; the lander itself no longer charges: director 2026-11-15).
                if (r.Facility.Value.Slot < 0 || r.Facility.Value.Tile == landerTile)
                {
                    dockRequest[r.Id] = req;
                    DockRequestW += req;
                }
                // A robot that has plugged in charges at priority, right after the colony, up to 80 % (or what a long
                // trip needs): director 2026-11-14, robots were leaving the charger at ~40 % because top-up power
                // only came once the grid battery was full. Top-up (after the grid battery) is only above that.
                else if (r.Battery.StateOfCharge < Math.Max(RobotSpecs.ResumeWorkFraction, r.TripCharge) - 1e-6)
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
        /// <summary>The wind factor on a robot's heater at a tile (null: 1). Set by the simulation from the day's wind.</summary>
        public System.Func<int, double> WindFactorAt { get; set; }

        public void Apply(Workforce workforce, double hours, double dockServedW, double criticalServedW, double topUpServedW,
                          bool flaringStar, Movement movement = null,
                          Resources.MaterialStock materials = null, double airTemperatureC = 25.0,
                          System.Func<int, double> airTemperatureAt = null, System.Func<int, bool> isShelter = null)
        {
            destroyed.Clear();
            // Cold locks part of each battery's capacity; insulated batteries stay warmer (spec v2 §11).
            foreach (Robot r in workforce.Robots)
            {
                double t = r.Has(RobotModule.ThermalInsulation) ? Math.Max(airTemperatureC, RobotTech.InsulatedBatteryC) : airTemperatureC;
                r.Battery.ColdFactor = RobotBattery.ColdCapacityFactor(airTemperatureAt != null ? r.BatteryTempC : t);
            }
            if (hours <= 0.0)
                return;
            double fd = DockRequestW > 0.0 ? Math.Min(1.0, Math.Max(0.0, dockServedW) / DockRequestW) : 0.0;
            double fc = CriticalRequestW > 0.0 ? Math.Min(1.0, Math.Max(0.0, criticalServedW) / CriticalRequestW) : 0.0;
            double ft = TopUpRequestW > 0.0 ? Math.Min(1.0, Math.Max(0.0, topUpServedW) / TopUpRequestW) : 0.0;
            ChargingW = 0.0;
            // A free Maintenance robot speeds up repairs (its repair efficiency, from the catalogue).
            double mechanic = 1.0;
            foreach (Robot r in workforce.Robots)
                if (r.Kind == RobotKind.Maintenance && r.Available)
                    mechanic = Math.Max(mechanic, r.Attribute(RobotAttribute.RepairEfficiency));
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
                        // A lithium-ion battery is not charged outside 0 to 45 C: a cold robot at a charger is warmed first, then charged.
                        r.WarmingUp = airTemperatureAt != null && !RobotThermal.CanCharge(r.BatteryTempC);
                        r.LastChargeW = r.WarmingUp ? 0.0 : r.Battery.Charge(offer, hours);
                        r.ChargedBefore = true;
                        ChargingW += r.LastChargeW;
                        break;
                    case RobotActivity.Maintenance:
                        if (movement == null || r.Tile == r.Facility.Value.Tile)
                            Repair(r, hours, mechanic, materials);
                        break;
                    default:
                        if (r.IsShutDown)
                            break;
                        bool working = r.Task.HasValue;
                        double efficiency = r.Attribute(RobotAttribute.EnergyEfficiency);
                        r.Battery.Draw(((working ? RobotSpecs.WorkingPowerW(r.Task.Value.Kind) : RobotSpecs.IdlePowerW)
                                        + RobotSpecs.OnboardAIW(r.AILevel, !r.Connected)) / efficiency, hours);
                        if (working)
                            r.Condition -= RobotSpecs.WearPerWorkingHour * RobotCatalog.WearFactor(r.Task.Value.Kind) * hours
                                           / r.Attribute(RobotAttribute.Durability);
                        break;
                }
                if (airTemperatureAt != null)
                    ApplyWeather(r, hours, airTemperatureAt(r.Tile), isShelter != null && isShelter(r.Tile));
                if (flaringStar && r.Activity != RobotActivity.Maintenance)
                    r.Condition -= RobotSpecs.FlareWearPerHour * hours * (r.Has(RobotModule.Shielding) ? RobotTech.ShieldingFactor : 1.0)
                                   / r.Attribute(RobotAttribute.Durability);
                r.Condition = Math.Max(0.0, Math.Min(1.0, r.Condition));
            }
        }

        readonly System.Collections.Generic.List<int> destroyed = new System.Collections.Generic.List<int>();

        /// <summary>Robots whose battery ran out outside their operating range in the last <see cref="Apply"/> long enough to destroy them.</summary>
        public System.Collections.Generic.IReadOnlyList<int> DestroyedRobotIds => destroyed;

        /// <summary>The heater or cooler of a robot outside its range runs from its battery; shelter removes the draw (KI-43).</summary>
        void ApplyWeather(Robot r, double hours, double airC, bool sheltered)
        {
            bool insulated = r.Has(RobotModule.ThermalInsulation);
            double heater = sheltered ? 0.0 : RobotThermal.HeaterW(airC, insulated, WindFactorAt != null ? WindFactorAt(r.Tile) : 1.0);
            r.Sheltered = sheltered;
            r.HeaterW = heater;
            // the battery follows its surroundings; the heater (while the battery has energy) holds it at the limit it would pass first
            r.BatteryTempC = RobotThermal.BatteryStep(r.BatteryTempC, RobotThermal.BatteryTarget(airC, insulated, sheltered, heater > 0.0 && r.Battery.UsableWh > 1e-6), hours);
            if (r.Activity != RobotActivity.Charging) r.WarmingUp = false;
            if (heater <= 0.0)
            {
                r.ExposedEmptyHours = 0.0;
                r.ThermalHoursLeft = double.PositiveInfinity;
                r.ThermalWarned = false;
                return;
            }
            r.Battery.Draw(heater, hours);
            if (r.Battery.UsableWh <= 1e-6) r.ExposedEmptyHours += hours; else r.ExposedEmptyHours = 0.0;
            r.ThermalHoursLeft = r.Battery.UsableWh / heater;
            if (r.ExposedEmptyHours >= RobotThermal.DestroyAfterHours - 1e-12) destroyed.Add(r.Id);
        }

        /// <summary>Drive along the route: draw drive power and cross tiles at their terrain's pace.</summary>
        void Drive(Robot r, double hours, Movement movement)
        {
            double cargoKg = CargoKgOf != null ? CargoKgOf(r) : 0.0;
            double want = (cargoKg > 0.0 ? movement.DrivePowerWithCargoW(cargoKg) : movement.DrivePowerW) / r.Attribute(RobotAttribute.EnergyEfficiency);
            double got = r.Battery.Draw(want, hours);
            if (cargoKg > 0.0)
                HaulDriveWh += got * hours;
            double moved = (want > 0.0 ? hours * got / want : hours) * r.Attribute(RobotAttribute.Speed);
            double climbWh = 0.0;
            while (moved > 0.0 && r.Route.Count > 0)
            {
                double hop = movement.RoutesFor(r).HopHoursAt(r.Route[0]);
                double left = (1.0 - r.HopProgress) * hop;
                if (moved < left - 1e-12)
                {
                    r.HopProgress += moved / hop;
                    break;
                }
                moved -= left;
                // climbing: potential energy m g dh over the drive efficiency, uphill only, no regeneration (KI-42)
                if (movement.HeightM != null)
                    climbWh += Logistics.HaulPhysics.ClimbEffortShare * Logistics.HaulPhysics.ClimbEnergyWh(Logistics.HaulPhysics.DefaultRobotMassKg, cargoKg, movement.GravityMs2,
                                                                   movement.HeightM[r.Route[0]] - movement.HeightM[r.Tile], r.Attribute(RobotAttribute.EnergyEfficiency));
                r.Tile = r.Route[0];
                r.Route.RemoveAt(0);
                r.HopProgress = 0.0;
            }
            if (climbWh > 0.0 && hours > 0.0)
            {
                r.Battery.Draw(climbWh / hours, hours);
                ClimbDriveWh += climbWh;
            }
            if (r.Route.Count == 0 && r.ManualTarget.HasValue && r.Tile == r.ManualTarget.Value)
                r.ManualTarget = null;
        }

        /// <summary>Energy robots spent climbing so far, Wh (not saved; a readout).</summary>
        public double ClimbDriveWh { get; private set; }

        /// <summary>
        /// Repairs are paid in materials (director, 2026-11-14): a new battery takes the cells' materials
        /// (<see cref="Resources.BuildCosts.BatteryCells"/>), minor battery ageing a share of them, and basic maintenance iron.
        /// </summary>
        static void Repair(Robot r, double hours, double speed, Resources.MaterialStock materials)
        {
            if (materials == null)
                return;
            if (r.Battery.WornOut)
            {
                Resources.MaterialBill cells = Resources.BuildCosts.BatteryCells(r.Battery.NominalWh);
                if (cells.CoveredBy(materials))
                {
                    Take(materials, cells);
                    r.Battery.Replace();
                }
            }
            // Minor battery ageing is put right while it is here (spec v2 §7), paid like a share of a new battery.
            if (!r.Battery.WornOut && r.Battery.Health < 1.0)
            {
                double health = Math.Min(1.0 - r.Battery.Health, RobotSpecs.MinorBatteryRepairPerHour * hours);
                Resources.MaterialBill cost = Resources.BuildCosts.BatteryCells(r.Battery.NominalWh).Times(health);
                if (health > 0.0 && cost.CoveredBy(materials) && r.Battery.RepairMinor(health) > 0.0)
                    Take(materials, cost);
            }
            double want = Math.Min(1.0 - r.Condition, RobotSpecs.RepairPerHour * speed * hours);
            if (want <= 0.0)
                return;
            // Basic maintenance uses iron; without it, repairs go slower (spec v2 §8).
            double ironNeed = want * RobotSpecs.RepairIronKg;
            double iron = materials.Take(Resources.Material.Iron, ironNeed);
            double share = ironNeed > 0.0 ? iron / ironNeed : 1.0;
            want *= RobotSpecs.RepairWithoutIronRate + (1.0 - RobotSpecs.RepairWithoutIronRate) * share;
            r.Condition += want;
            if (r.Condition >= 1.0 - 1e-12)
                r.CriticallyDamaged = false;   // a full repair also fixes critical damage
        }

        static void Take(Resources.MaterialStock stock, Resources.MaterialBill bill)
        {
            foreach (var (m, kg) in bill.Items())
                stock.Take(m, kg);
        }

        static int Used(Dictionary<(int, int), int> used, (int, int) key) => used.TryGetValue(key, out int n) ? n : 0;

        /// <summary>
        /// A facility with a free place: one that repairs, or a charger. With movement, the nearest one it can reach
        /// (then the most powerful); without, the most powerful.
        /// </summary>
        static bool HasChargingFacility(IReadOnlyList<RobotFacility> facilities)
        {
            foreach (RobotFacility f in facilities)
                if (f.Capability.Charges) return true;
            return false;
        }

        static RobotFacility? Free(Robot r, IReadOnlyList<RobotFacility> facilities, Dictionary<(int, int), int> used, bool repair, Movement movement)
        {
            RobotFacility? best = null;
            double bestHours = double.PositiveInfinity;
            foreach (RobotFacility f in facilities)
            {
                if ((repair && !f.Capability.Repairs) || (!repair && !f.Capability.Charges) || !f.Capability.Allows(r.Kind))
                    continue;
                if (Used(used, (f.Tile, f.Slot)) >= f.Capability.Slots)
                    continue;
                double h = movement != null ? movement.RoutesFor(r).HoursBetween(r.Tile, f.Tile) : 0.0;
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
                List<int> route = movement.RoutesFor(r).Route(r.Tile, f.Tile);
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

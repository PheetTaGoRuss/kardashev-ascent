using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Robot batteries, charging, wear and repair (phase 2). Values from physics-verification.md, robot battery section.</summary>
    public class RobotUpkeepTests
    {
        // ---- Battery (physics-verification.md table) ----

        [Test]
        public void BatteryCapacity_IsPerseverances()
        {
            Assert.AreEqual(2408.0, RobotSpecs.BatteryCapacityWh);
            Assert.AreEqual(2408.0, new RobotBattery().CapacityWh);
        }

        [TestCase(0.0, 1.0)]
        [TestCase(1500.0, 0.9)]
        [TestCase(3000.0, 0.8)]
        [TestCase(6000.0, 0.6)]
        public void Health_FallsLinearlyWithEquivalentFullCycles(double cycles, double health)
        {
            Assert.AreEqual(health, RobotBattery.HealthAfter(cycles), 1e-9);
        }

        [Test]
        public void Capacity_FollowsHealth_AndCyclesCountDeliveredEnergy()
        {
            var b = new RobotBattery();
            b.Restore(1500.0 * 2408.0, 2408.0);
            Assert.AreEqual(1500.0, b.EquivalentFullCycles, 1e-9);
            Assert.AreEqual(2167.2, b.CapacityWh, 1e-6);
            Assert.AreEqual(2167.2, b.StoredWh, 1e-6, "stored energy is capped at the faded capacity");

            var fresh = new RobotBattery();
            for (int i = 0; i < 10; i++)
            {
                fresh.Charge(1e9, 10.0);
                while (fresh.StoredWh > 1e-6) fresh.Draw(2408.0, 1.0);
            }
            Assert.Greater(fresh.EquivalentFullCycles, 9.0, "each cycle delivers the charge × √RTE");
            Assert.Less(fresh.EquivalentFullCycles, 10.0 + 1e-9);
        }

        [Test]
        public void HealthIsSeparateFromCharge_AndReplacementRestoresIt()
        {
            var b = new RobotBattery();
            b.Restore(3500.0 * 2408.0, 500.0);
            Assert.IsTrue(b.WornOut);
            Assert.AreEqual(500.0, b.StoredWh, 1e-9, "health doesn't change the charge left");
            b.Replace();
            Assert.AreEqual(1.0, b.Health);
            Assert.AreEqual(2408.0, b.CapacityWh, 1e-9);
            Assert.AreEqual(500.0, b.StoredWh, 1e-9);
        }

        [Test]
        public void BatteryReplacement_TakesTheCellsMaterials()
        {
            // LiFePO4 cathode, 2.05 kg per kWh: lithium 6.94/157.76 and iron 55.845/157.76 of it by mass.
            MaterialBill cells = BuildCosts.BatteryCells(RobotSpecs.BatteryCapacityWh);
            double cathode = 2.05 * RobotSpecs.BatteryCapacityWh / 1000.0;
            Assert.AreEqual(cathode * 6.94 / 157.76, cells[Material.Lithium], 1e-9);
            Assert.AreEqual(cathode * 55.845 / 157.76, cells[Material.Iron], 1e-9);
            Assert.AreEqual(cathode, cells.TotalKg, 1e-9, "the rest (phosphate, oxygen) stands in as rock");
        }

        // ---- Charging hysteresis and facilities ----

        static MaterialStock StockOf(params (Material M, double Kg)[] items)
        {
            var s = new MaterialStock();
            foreach (var (m, kg) in items) s.Add(m, kg);
            return s;
        }

        static Workforce WithRobots(int n, double charge)
        {
            var w = new Workforce();
            for (int i = 0; i < n; i++)
            {
                Robot r = w.AddRobot(RobotKind.General, 0);
                r.Battery.Restore(0.0, charge * RobotSpecs.BatteryCapacityWh);
            }
            return w;
        }

        static readonly List<RobotFacility> LanderOnly = new List<RobotFacility> { new RobotFacility(0, -1, ChargingCapability.Lander) };

        [Test]
        public void Below20_GoesToCharge_StaysUntil80_ThenWorks()
        {
            Workforce w = WithRobots(1, 0.19);
            Robot r = w.Robots[0];
            Assert.IsTrue(r.NeedsCharge);
            Assert.IsFalse(r.Available);
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 0.25);
            Assert.AreEqual(RobotState.Charging, r.State);
            Assert.Greater(up.DockRequestW, 0.0, "at the lander");
            Assert.AreEqual(0.0, up.CriticalRequestW);
            Assert.AreEqual(0.0, up.TopUpRequestW);

            int steps = 0;
            while (r.Activity == RobotActivity.Charging && steps++ < 1000)
            {
                up.Plan(w, LanderOnly, 0.25);
                up.Apply(w, 0.25, up.DockRequestW, up.CriticalRequestW, up.TopUpRequestW, false, null);
            }
            Assert.AreEqual(RobotActivity.None, r.Activity);
            Assert.GreaterOrEqual(r.Battery.StateOfCharge, RobotSpecs.ResumeWorkFraction - 1e-9);
            Assert.IsTrue(r.Available);
        }

        [Test]
        public void Above20_DoesNotGoToCharge()
        {
            Workforce w = WithRobots(1, 0.5);
            new RobotUpkeep().Plan(w, LanderOnly, 0.25);
            Assert.AreEqual(RobotActivity.None, w.Robots[0].Activity);
        }

        [Test]
        public void ChargingPower_IsTheFacilitysPerRobot_AndSlotsLimitHowMany()
        {
            Workforce w = WithRobots(3, 0.1);
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 0.25);
            int charging = 0;
            foreach (Robot r in w.Robots) if (r.Activity == RobotActivity.Charging) charging++;
            Assert.AreEqual(ChargingCapability.Lander.Slots, charging);
            Assert.AreEqual(ChargingCapability.Lander.Slots * ChargingCapability.Lander.PowerW, up.DockRequestW, 1e-6);
            Assert.IsTrue(w.Robots[2].NeedsCharge, "the third waits for a free charger");
        }

        [Test]
        public void Above20_WithNoPowerAtTheCharger_GoesBackToWork()
        {
            Workforce w = WithRobots(1, 0.1);
            Robot r = w.Robots[0];
            var yard = new List<RobotFacility> { new RobotFacility(0, 3, ChargingCapability.BatteryYard) };
            var up = new RobotUpkeep();
            up.Plan(w, yard, 0.25);
            up.Apply(w, 0.25, 0.0, up.CriticalRequestW, 0.0, false, null);   // below 20 %: served
            Assert.Greater(r.Battery.StateOfCharge, RobotSpecs.ReturnToChargeFraction);

            up.Plan(w, yard, 0.25);
            Assert.AreEqual(RobotActivity.Charging, r.Activity, "it got power last step: it stays");
            Assert.Greater(up.TopUpRequestW, 0.0);
            up.Apply(w, 0.25, 0.0, 0.0, 0.0, false, null);                    // no spare power for a top-up
            up.Plan(w, yard, 0.25);
            Assert.AreEqual(RobotActivity.None, r.Activity, "nothing came: back to work above 20 %");
            Assert.IsTrue(r.Available);
        }

        [Test]
        public void Above20_WithSparePower_StaysUntil80()
        {
            Workforce w = WithRobots(1, 0.1);
            Robot r = w.Robots[0];
            var yard = new List<RobotFacility> { new RobotFacility(0, 3, ChargingCapability.BatteryYard) };
            var up = new RobotUpkeep();
            for (int i = 0; i < 40 && (i == 0 || r.Activity == RobotActivity.Charging); i++)
            {
                up.Plan(w, yard, 0.25);
                up.Apply(w, 0.25, 0.0, up.CriticalRequestW, up.TopUpRequestW, false, null);
            }
            up.Plan(w, yard, 0.25);
            Assert.GreaterOrEqual(r.Battery.StateOfCharge, RobotSpecs.ResumeWorkFraction - 1e-9);
        }

        [Test]
        public void Chargers_PreferTheMostPowerful()
        {
            Workforce w = WithRobots(1, 0.1);
            var both = new List<RobotFacility>(LanderOnly) { new RobotFacility(0, 3, ChargingCapability.BatteryYard) };
            new RobotUpkeep().Plan(w, both, 0.25);
            Assert.AreEqual((0, 3), w.Robots[0].Facility);
        }

        [Test]
        public void ChargingShare_FollowsWhatTheGridGave()
        {
            Workforce w = WithRobots(1, 0.1);
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 1.0);
            double before = w.Robots[0].Battery.StoredWh;
            up.Apply(w, 1.0, up.DockRequestW / 2.0, 0.0, 0.0, false, null);
            Assert.AreEqual(up.DockRequestW / 2.0, up.ChargingW, 1e-6);
            Assert.AreEqual(before + up.ChargingW * Math.Sqrt(Battery.LithiumIonRoundTrip), w.Robots[0].Battery.StoredWh, 1e-6);
        }

        [Test]
        public void FacilityGone_SendsTheRobotBack()
        {
            Workforce w = WithRobots(1, 0.1);
            var yard = new List<RobotFacility> { new RobotFacility(0, 3, ChargingCapability.BatteryYard) };
            var up = new RobotUpkeep();
            up.Plan(w, yard, 0.25);
            Assert.AreEqual(RobotActivity.Charging, w.Robots[0].Activity);
            up.Plan(w, new List<RobotFacility>(), 0.25);
            Assert.AreEqual(RobotActivity.None, w.Robots[0].Activity);
            Assert.IsNull(w.Robots[0].Facility);
        }

        // ---- Energy use and wear ----

        [Test]
        public void IdleAndWorkingDraw_FromTheRobotsOwnBattery_WorkWears()
        {
            Workforce w = WithRobots(2, 1.0);
            w.Assign(new List<WorkTask> { new WorkTask(TaskKind.Build, 1, 1) });
            Robot busy = w.Robots[0], idle = w.Robots[1];
            Assert.IsNotNull(busy.Task, "the first robot takes the one task");
            Assert.IsNull(idle.Task);
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 1.0);
            up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, null);
            // Plus the onboard AI (stage E): a level-1 robot, connected (no network in this test counts as connected).
            double ai = RobotSpecs.OnboardAIW(1, false);
            Assert.AreEqual(2408.0 - (RobotSpecs.IdlePowerW + ai) / Math.Sqrt(Battery.LithiumIonRoundTrip), idle.Battery.StoredWh, 1e-6);
            Assert.AreEqual(2408.0 - (RobotSpecs.BuildPowerW + ai) / Math.Sqrt(Battery.LithiumIonRoundTrip), busy.Battery.StoredWh, 1e-6);
            Assert.AreEqual(1.0, idle.Condition, 1e-12);
            Assert.AreEqual(1.0 - RobotSpecs.WearPerWorkingHour * RobotCatalog.WearFactor(TaskKind.Build), busy.Condition, 1e-12);
        }

        [Test]
        public void FlaringStar_WearsEveryRobot()
        {
            Workforce w = WithRobots(1, 1.0);
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 2.0);
            up.Apply(w, 2.0, 0.0, 0.0, 0.0, true, null);
            Assert.AreEqual(1.0 - 2.0 * RobotSpecs.FlareWearPerHour, w.Robots[0].Condition, 1e-12);
        }

        [Test]
        public void ConditionZero_ShutsDown_NoDraw_NoWork()
        {
            Workforce w = WithRobots(1, 1.0);
            Robot r = w.Robots[0];
            r.Condition = 0.0;
            Assert.AreEqual(RobotState.Shutdown, r.State);
            Assert.IsFalse(r.Available);
            w.Assign(new List<WorkTask> { new WorkTask(TaskKind.Build, 1, 1) });
            Assert.IsNull(r.Task);
            var up = new RobotUpkeep();
            up.Plan(w, new List<RobotFacility>(), 1.0);   // nowhere to be repaired
            up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, null);
            Assert.AreEqual(2408.0, r.Battery.StoredWh, 1e-9);
        }

        [Test]
        public void LowCondition_Maintenance_RepairsWithIron_ThenBackToWork()
        {
            Workforce w = WithRobots(1, 1.0);
            Robot r = w.Robots[0];
            r.Condition = 0.0;
            MaterialStock stock = StockOf((Material.Iron, 1e6));
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 1.0);
            Assert.AreEqual(RobotState.Maintenance, r.State);
            up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, null, stock);
            Assert.AreEqual(RobotSpecs.RepairPerHour, r.Condition, 1e-12);
            Assert.AreEqual(1e6 - RobotSpecs.RepairPerHour * RobotSpecs.RepairIronKg, stock[Material.Iron], 1e-6);
            for (int i = 0; i < 20 && r.Activity == RobotActivity.Maintenance; i++)
            {
                up.Plan(w, LanderOnly, 1.0);
                up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, null, stock);
            }
            up.Plan(w, LanderOnly, 1.0);
            Assert.AreEqual(1.0, r.Condition, 1e-9);
            Assert.IsTrue(r.Available);
        }

        [Test]
        public void Repair_WithoutIron_GoesSlower()
        {
            Workforce w = WithRobots(1, 1.0);
            Robot r = w.Robots[0];
            r.Condition = 0.1;
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 1.0);
            up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, null, new MaterialStock());
            Assert.AreEqual(0.1 + RobotSpecs.RepairPerHour * RobotSpecs.RepairWithoutIronRate, r.Condition, 1e-12);
            Assert.AreEqual(RobotState.Maintenance, r.State);
        }

        [Test]
        public void WornOutBattery_IsReplacedInMaintenance()
        {
            Workforce w = WithRobots(1, 1.0);
            Robot r = w.Robots[0];
            r.Battery.Restore(3200.0 * 2408.0, 1000.0);
            Assert.IsTrue(r.NeedsMaintenance);
            MaterialStock stock = StockOf((Material.Lithium, 10.0), (Material.Iron, 10.0), (Material.Rock, 10.0));
            MaterialBill cells = BuildCosts.BatteryCells(r.Battery.NominalWh);
            var up = new RobotUpkeep();
            up.Plan(w, LanderOnly, 0.25);
            up.Apply(w, 0.25, 0.0, 0.0, 0.0, false, null, stock);
            Assert.AreEqual(1.0, r.Battery.Health);
            Assert.AreEqual(10.0 - cells[Material.Lithium], stock[Material.Lithium], 1e-9);
            Assert.AreEqual(10.0 - cells[Material.Iron] - RobotSpecs.RepairIronKg * 0.0, stock[Material.Iron], 1e-6);
            up.Plan(w, LanderOnly, 0.25);
            Assert.IsTrue(r.Available, "condition was fine: back to work");
        }

        // ---- Grid priority (design D6) ----

        [Test]
        public void Grid_PriorityLoad_ComesAfterTheColony_BeforeTheBattery()
        {
            var grid = new PowerGrid(new Battery(1000.0, 1.0, 10.0, 0.0), 100.0);
            GridStep s = grid.Step(150.0, 1.0, 0.0, 30.0);
            Assert.AreEqual(100.0, s.ServedW, 1e-9);
            Assert.AreEqual(30.0, s.PriorityServedW, 1e-9);
            Assert.AreEqual(20.0, s.ChargeW, 1e-9, "the battery gets what is left");
        }

        [Test]
        public void Grid_PriorityLoad_CanUseTheBattery_ButNeverCausesABrownout()
        {
            var grid = new PowerGrid(new Battery(1000.0, 1.0, 10.0, 1.0), 100.0);
            GridStep s = grid.Step(100.0, 1.0, 0.0, 30.0);
            Assert.AreEqual(30.0, s.PriorityServedW, 1e-9);
            Assert.AreEqual(30.0, s.DischargeW, 1e-9);
            Assert.IsFalse(s.IsBrownout);

            var empty = new PowerGrid(new Battery(1000.0, 1.0, 10.0, 0.0), 100.0);
            GridStep t = empty.Step(110.0, 1.0, 0.0, 30.0);
            Assert.AreEqual(100.0, t.ServedW, 1e-9, "the colony first");
            Assert.AreEqual(10.0, t.PriorityServedW, 1e-9);
            Assert.IsFalse(t.IsBrownout);
        }

        [Test]
        public void Grid_LanderDock_ChargesFromTheRtgs_EvenInABrownout()
        {
            var grid = new PowerGrid(new Battery(1000.0, 1.0, 10.0, 0.0), 100_000.0, rtgUnits: 4);
            double rtg = grid.RtgPowerW(0.0);
            GridStep s = grid.Step(0.0, 1.0, 0.0, 0.0, 1000.0);
            Assert.IsTrue(s.IsBrownout, "the colony gets what is left");
            Assert.AreEqual(rtg, s.DockServedW, 1e-9, "the dock keeps the RTGs for the robot");
            Assert.AreEqual(0.0, s.ServedW, 1e-9);
        }

        // ---- Saving ----

        [Test]
        public void CaptureRestore_KeepsBatteryConditionAndActivity()
        {
            Workforce w = WithRobots(1, 0.1);
            Robot r = w.Robots[0];
            r.Battery.Restore(100.0 * 2408.0, 300.0);
            r.Condition = 0.7;
            new RobotUpkeep().Plan(w, LanderOnly, 0.25);
            var copy = new Workforce();
            Assert.IsTrue(copy.Restore(w.Capture()));
            Robot c = copy.Robots[0];
            Assert.AreEqual(300.0, c.Battery.StoredWh, 1e-9);
            Assert.AreEqual(100.0, c.Battery.EquivalentFullCycles, 1e-9);
            Assert.AreEqual(0.7, c.Condition, 1e-12);
            Assert.AreEqual(RobotActivity.Charging, c.Activity);
            Assert.AreEqual((0, -1), c.Facility);
        }

        [Test]
        public void Restore_WithoutUpkeep_GivesAChargedNewRobot()
        {
            Workforce w = WithRobots(1, 0.1);
            WorkforceState s = w.Capture();
            s.Robots[0].HasUpkeep = false;   // as written by save version 7
            var copy = new Workforce();
            Assert.IsTrue(copy.Restore(s));
            Assert.AreEqual(1.0, copy.Robots[0].Battery.StateOfCharge, 1e-12);
            Assert.AreEqual(1.0, copy.Robots[0].Condition);
            Assert.AreEqual(RobotActivity.None, copy.Robots[0].Activity);
        }
    }
}

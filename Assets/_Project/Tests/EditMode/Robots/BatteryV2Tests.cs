using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>
    /// Stage D (spec v2 §7, §8, §11): battery ageing by depth of discharge, 0 % events, minor repair, cold capacity,
    /// task wear, charging station and access, group maintenance rules, repair materials.
    /// Values from physics-verification.md, robot battery section 2.
    /// </summary>
    public class BatteryV2Tests
    {
        // ---- physics table ----

        [TestCase(1.0, 6.666666666666667e-05)]
        [TestCase(0.6, 3.0983866769659334e-05)]
        [TestCase(0.25, 8.333333333333334e-06)]
        public void CycleDamage_WoehlerForm(double depth, double damage)
        {
            Assert.AreEqual(damage, RobotBattery.CycleDamage(depth), 1e-9);
        }

        [Test]
        public void FullCycles_MatchTheFirstModel()
        {
            Assert.AreEqual(0.9, RobotBattery.HealthAfter(1500.0), 1e-9);
            Assert.AreEqual(1500.0 * RobotBattery.CycleDamage(1.0), 0.1, 1e-9);
        }

        [TestCase(25.0, 1.0)]
        [TestCase(40.0, 1.0)]
        [TestCase(7.5, 0.925)]
        [TestCase(-10.0, 0.85)]
        [TestCase(-15.0, 0.75)]
        [TestCase(-20.0, 0.65)]
        [TestCase(-55.0, 0.65)]
        public void ColdCapacity_Bressan2025(double t, double factor)
        {
            Assert.AreEqual(factor, RobotBattery.ColdCapacityFactor(t), 1e-9);
        }

        // ---- behaviour ----

        static double CycleTo(RobotBattery b, double low)
        {
            double before = b.Damage;
            while (b.StateOfCharge > low + 1e-9) b.Draw(100.0, 0.25 * Math.Min(1.0, (b.StateOfCharge - low) * b.CapacityWh / 25.0 + 0.01));
            for (int i = 0; i < 200 && b.StateOfCharge < 0.999; i++) b.Charge(5000.0, 0.25);
            return b.Damage - before;
        }

        [Test]
        public void DeepCycles_AgeMoreThanTheSameEnergyInShallowOnes()
        {
            var deep = new RobotBattery();
            var shallow = new RobotBattery();
            double d = CycleTo(deep, 0.2);                       // one 80 % cycle
            double s = CycleTo(shallow, 0.6) + CycleTo(shallow, 0.6);   // two 40 % cycles
            Assert.Greater(d, s);
            Assert.AreEqual(RobotBattery.CycleDamage(0.8), d, 2e-6);
        }

        [Test]
        public void RunningToZero_AddsDamage_OncePerDischarge()
        {
            var b = new RobotBattery();
            for (int i = 0; i < 40; i++) b.Draw(1000.0, 1.0);
            Assert.AreEqual(1, b.ZeroDischarges);
            double before = b.Damage;
            b.Charge(2000.0, 1.0);
            Assert.Greater(b.Damage, before, "the deep cycle closes at the charge");
            for (int i = 0; i < 40; i++) b.Draw(1000.0, 1.0);
            Assert.AreEqual(2, b.ZeroDischarges);
        }

        [Test]
        public void Cold_LocksCapacity_WithoutAgeing_AndGivesItBack()
        {
            var b = new RobotBattery();
            b.ColdFactor = RobotBattery.ColdCapacityFactor(-20.0);
            Assert.AreEqual(0.35 * b.CapacityWh, b.LockedWh, 1e-6);
            Assert.AreEqual(1.0, b.StateOfCharge, 1e-9, "the usable part is full");
            double delivered = 0.0;
            for (int i = 0; i < 40; i++) delivered += b.Draw(500.0, 1.0);
            Assert.AreEqual(0.0, b.UsableWh, 1e-6);
            Assert.AreEqual(0.35 * b.CapacityWh, b.StoredWh, 1e-6, "the locked energy is still in the cell");
            b.ColdFactor = 1.0;
            Assert.AreEqual(0.35, b.StateOfCharge, 1e-6, "warm again: it is usable");
        }

        [Test]
        public void MinorAgeing_CanBeRepaired_SevereCanNot()
        {
            var b = new RobotBattery();
            b.Restore(0.0, 2408.0, 0.05, -1, -1, 0);
            Assert.AreEqual(0.05, b.RepairMinor(0.1), 1e-12);
            Assert.AreEqual(1.0, b.Health, 1e-12);
            Assert.AreEqual(2408.0, b.CapacityWh, 1e-9);
            var worn = new RobotBattery();
            worn.Restore(0.0, 1000.0, 0.15, -1, -1, 0);
            Assert.AreEqual(0.0, worn.RepairMinor(0.1), "below 90 %: not minor");
        }

        [Test]
        public void TaskWear_HeavyWorkWearsMore()
        {
            Assert.Greater(RobotCatalog.WearFactor(TaskKind.Mine), RobotCatalog.WearFactor(TaskKind.Manufacture));
            Assert.Less(RobotCatalog.WearFactor(TaskKind.Survey), RobotCatalog.WearFactor(TaskKind.Build));
        }

        // ---- charging access, group rules, repairs ----

        [Test]
        public void ChargingAccess_RestrictedFacilitiesTurnOtherKindsAway()
        {
            var w = new Workforce();
            Robot g = w.AddRobot(RobotKind.General, 0);
            Robot m = w.AddRobot(RobotKind.Mining, 0);
            g.Battery.Restore(0.0, 0.1 * 2408.0);
            m.Battery.Restore(0.0, 0.1 * 2408.0);
            var minersOnly = new ChargingCapability(4, 2000.0, false, new[] { RobotKind.Mining });
            var up = new RobotUpkeep();
            up.Plan(w, new List<RobotFacility> { new RobotFacility(0, 7, minersOnly) }, 0.25);
            Assert.AreEqual(RobotActivity.Charging, m.Activity);
            Assert.AreEqual(RobotActivity.None, g.Activity, "not allowed there");
            Assert.IsTrue(ChargingCapability.Station.Allows(RobotKind.General));
        }

        [Test]
        public void GroupMaintenanceRule_SetsWhenRobotsGoForRepair()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            r.Condition = 0.4;
            var lander = new List<RobotFacility> { new RobotFacility(0, -1, ChargingCapability.Lander) };
            var up = new RobotUpkeep();
            up.Plan(w, lander, 0.25);
            Assert.AreEqual(RobotActivity.None, r.Activity, "default rule: below 30 %");
            Assert.IsTrue(w.SetGroupMaintenance(r.GroupId, 0.5));
            Assert.IsFalse(w.SetGroupMaintenance(r.GroupId, 0.95));
            up.Plan(w, lander, 0.25);
            Assert.AreEqual(RobotActivity.Maintenance, r.Activity);
        }

        [Test]
        public void Repairs_UseIron_OrGoSlowerWithout()
        {
            double Repaired(double iron)
            {
                var w = new Workforce();
                Robot r = w.AddRobot(RobotKind.General, 0);
                r.Condition = 0.1;
                var stock = new MaterialStock();
                stock.Add(Material.Iron, iron);
                var lander = new List<RobotFacility> { new RobotFacility(0, -1, ChargingCapability.Lander) };
                var up = new RobotUpkeep();
                up.Plan(w, lander, 1.0);
                up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, null, stock);
                return r.Condition - 0.1;
            }
            double with = Repaired(1000.0), without = Repaired(0.0);
            Assert.AreEqual(RobotSpecs.RepairPerHour, with, 1e-12);
            Assert.AreEqual(RobotSpecs.RepairPerHour * RobotSpecs.RepairWithoutIronRate, without, 1e-12);
        }

        // ---- colony ----

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSolarParams Planet(double airC) => new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, airC);

        [Test]
        public void ColdPlanet_RobotsHaveLessUsableBattery_InsulationHelps()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Planet(-20.0));
            Robot r = sim.Workforce.Robots[0];
            Robot insulated = sim.Workforce.AddRobot(RobotKind.General, sim.BaseTileId);
            insulated.Modules = RobotModule.ThermalInsulation;
            sim.Tick(0.25);
            Assert.AreEqual(0.65, r.Battery.ColdFactor, 1e-9);
            Assert.AreEqual(RobotBattery.ColdCapacityFactor(RobotTech.InsulatedBatteryC), insulated.Battery.ColdFactor, 1e-9);
            Assert.Greater(insulated.Battery.ColdFactor, r.Battery.ColdFactor);
        }

        [Test]
        public void ChargingStation_IsAFacility_AndSaved()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Planet(25.0));
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlaceChargingStation(t, 6));
            Assert.AreEqual(BuildingType.ChargingStation, sim.GetBuilding(t, 6));
            bool found = false;
            foreach (RobotFacility f in sim.RobotFacilities())
                found |= f.Tile == t && f.Slot == 6 && f.Capability.PowerW == ChargingCapability.Station.PowerW;
            Assert.IsTrue(found);
            SimulationState state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var b = new PlanetSimulation(Sphere, Surface, Planet(25.0));
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(BuildingType.ChargingStation, b.GetBuilding(t, 6));
        }

        [Test]
        public void Save_KeepsAgeingAndRules_OldSavesConvert()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            r.Battery.Restore(10.0 * 2408.0, 1500.0, 0.03, 0.9, 0.4, 2);
            w.SetGroupMaintenance(r.GroupId, 0.45);
            WorkforceState s = w.Capture();
            var copy = new Workforce();
            Assert.IsTrue(copy.Restore(s));
            Robot c = copy.Robots[0];
            Assert.AreEqual(0.03, c.Battery.Damage, 1e-12);
            Assert.AreEqual(2, c.Battery.ZeroDischarges);
            Assert.AreEqual(0.4, c.Battery.CycleLow, 1e-12);
            Assert.AreEqual(0.45, copy.GetGroup(c.GroupId).MaintenanceBelow, 1e-12);

            s.Robots[0].HasBatteryAgeing = false;   // as written by version 14
            s.Groups[0].MaintenanceBelow = 0.0;
            var old = new Workforce();
            Assert.IsTrue(old.Restore(s));
            Assert.AreEqual(RobotBattery.HealthAfter(10.0), old.Robots[0].Battery.Health, 1e-12, "full cycles, as before");
            Assert.AreEqual(RobotSpecs.MaintenanceBelowCondition, old.GetGroup(old.Robots[0].GroupId).MaintenanceBelow);
        }
    }
}

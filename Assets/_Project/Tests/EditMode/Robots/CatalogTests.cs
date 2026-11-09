using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Stage A (spec v2): attributes, the compatibility table, productivity, condition penalties, shutdown kinds.</summary>
    public class CatalogTests
    {
        // ---- Compatibility from data ----

        [Test]
        public void General_DoesEveryBasicTask_AtRate1()
        {
            foreach (TaskKind t in (TaskKind[])Enum.GetValues(typeof(TaskKind)))
            {
                Assert.IsTrue(RobotCatalog.CanDo(RobotKind.General, t, 0), t.ToString());
                Assert.AreEqual(1.0, RobotCatalog.Efficiency(RobotKind.General, t, 0), 1e-12, t.ToString());
            }
        }

        [Test]
        public void Specialists_StartWithTheirOwnRoleOnly_AndAreBetterAtIt()
        {
            Assert.AreEqual(2.0, RobotCatalog.Efficiency(RobotKind.Mining, TaskKind.Mine, 0));
            Assert.AreEqual(2.0, RobotCatalog.Efficiency(RobotKind.Construction, TaskKind.Build, 0));
            Assert.AreEqual(2.0, RobotCatalog.Efficiency(RobotKind.Research, TaskKind.Survey, 0));
            Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Mining, TaskKind.Salvage, 0));
            Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Research, TaskKind.Build, 0));
            foreach (TaskKind t in (TaskKind[])Enum.GetValues(typeof(TaskKind)))
                Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Transport, t, 10), "Transport waits for logistics");
        }

        [Test]
        public void SecondaryCapabilities_UnlockByMilestone_AtALowerRate()
        {
            Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Mining, TaskKind.Salvage, 3));
            Capability? c = RobotCatalog.Find(RobotKind.Mining, TaskKind.Salvage, 4);
            Assert.IsTrue(c.HasValue);
            Assert.IsTrue(c.Value.Secondary);
            Assert.AreEqual(0.5 * RobotCatalog.Profile(RobotKind.Mining)[RobotAttribute.Construction],
                            RobotCatalog.Efficiency(RobotKind.Mining, TaskKind.Salvage, 4), 1e-12);
            Assert.Less(RobotCatalog.Efficiency(RobotKind.Mining, TaskKind.Salvage, 4), RobotCatalog.Efficiency(RobotKind.General, TaskKind.Salvage, 4));
        }

        [Test]
        public void EveryRowNamesARealPair_AndTaskAttributesAreSet()
        {
            foreach (Capability c in RobotCatalog.Capabilities)
            {
                Assert.Greater(c.Factor, 0.0);
                Assert.AreEqual(c.Secondary, c.UnlockMilestone > 0, $"{c.Robot} {c.Task}: secondary rows are the unlocked ones");
            }
            Assert.AreEqual(RobotAttribute.MiningPower, RobotCatalog.TaskAttribute(TaskKind.Mine));
            Assert.AreEqual(RobotAttribute.SensorQuality, RobotCatalog.TaskAttribute(TaskKind.Survey));
            Assert.AreEqual(RobotAttribute.Construction, RobotCatalog.TaskAttribute(TaskKind.Build));
        }

        [Test]
        public void Workforce_UsesSecondaryCapabilitiesOnceUnlocked()
        {
            var w = new Workforce();
            Robot m = w.AddRobot(RobotKind.Mining, 0);
            var salvage = new List<WorkTask> { new WorkTask(TaskKind.Salvage, 1, 1) };
            w.Assign(salvage);
            Assert.IsNull(m.Task);
            w.Milestones = 4;
            w.Assign(salvage);
            Assert.AreEqual(TaskKind.Salvage, m.Task.Value.Kind);
        }

        // ---- Condition and productivity ----

        [TestCase(1.0, 1.0)]
        [TestCase(0.7, 1.0)]
        [TestCase(0.35, 0.75)]
        [TestCase(0.2, 0.8 * (0.5 + 0.5 * 0.2 / 0.7))]
        [TestCase(0.05, 0.8 * 0.6 * (0.5 + 0.5 * 0.05 / 0.7))]
        [TestCase(0.0, 0.8 * 0.6 * 0.5)]
        public void ConditionFactor_GradualThenThresholds(double condition, double factor)
        {
            Assert.AreEqual(factor, RobotCatalog.ConditionFactor(condition), 1e-12);
        }

        [Test]
        public void Productivity_CombinesKindUpgradesAndCondition()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.Mining, 0);
            Assert.AreEqual(2.0, RobotCatalog.Productivity(r, TaskKind.Mine, 0), 1e-12);
            r.Modules = RobotModule.FastDrive;
            Assert.AreEqual(2.0 * RobotTech.FastDriveFactor, RobotCatalog.Productivity(r, TaskKind.Mine, 0), 1e-12);
            r.Condition = 0.35;
            Assert.AreEqual(2.0 * RobotTech.FastDriveFactor * 0.75, RobotCatalog.Productivity(r, TaskKind.Mine, 0), 1e-12);
            Assert.AreEqual(0.0, RobotCatalog.Productivity(r, TaskKind.Survey, 0), "it can't survey");
        }

        [Test]
        public void Attributes_ScaleEnergyUseAndWear()
        {
            var w = new Workforce();
            Robot miner = w.AddRobot(RobotKind.Mining, 0);
            w.Assign(new List<WorkTask> { new WorkTask(TaskKind.Mine, 0, 1) });
            var up = new RobotUpkeep();
            up.Plan(w, new List<RobotFacility>(), 1.0);
            up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, null);
            double durability = RobotCatalog.Profile(RobotKind.Mining)[RobotAttribute.Durability];
            Assert.AreEqual(1.0 - RobotSpecs.WearPerWorkingHour * RobotCatalog.WearFactor(TaskKind.Mine) / durability, miner.Condition, 1e-12,
                            "tougher robots wear slower; mining is heavy work");
            Assert.AreEqual(1.25, durability);
            Assert.AreEqual(0.8, miner.Attribute(RobotAttribute.Speed), 1e-12);
            miner.Modules = RobotModule.FastDrive;
            Assert.AreEqual(0.8 * RobotTech.FastDriveFactor, miner.Attribute(RobotAttribute.Speed), 1e-12, "faster drive raises speed");
        }

        // ---- Shutdown kinds ----

        [Test]
        public void ShutdownKinds_AreSeparate()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            Assert.AreEqual(ShutdownKind.None, r.Shutdown);
            r.Battery.Restore(0.0, 0.0);
            Assert.AreEqual(ShutdownKind.Energy, r.Shutdown);
            r.Battery.Restore(0.0, 1000.0);
            r.Condition = 0.0;
            Assert.AreEqual(ShutdownKind.Mechanical, r.Shutdown);
            r.Condition = 0.9;
            w.DamageCritically(r.Id);
            Assert.AreEqual(ShutdownKind.CriticalDamage, r.Shutdown);
            Assert.IsTrue(r.NeedsMaintenance);
            Assert.AreEqual(RobotState.Shutdown, r.State);
        }

        static readonly List<RobotFacility> Lander = new List<RobotFacility> { new RobotFacility(0, -1, ChargingCapability.Lander) };

        sealed class Parts : IPartsStore
        {
            public double StockWh => 1e12;
            public double Take(double wh) => wh;
        }

        [Test]
        public void EnergyShutdown_GoesToCharge_NotToRepair()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            r.Battery.Restore(0.0, 0.0);
            var up = new RobotUpkeep();
            up.Plan(w, Lander, 0.25);
            Assert.AreEqual(RobotActivity.Charging, r.Activity);
            Assert.AreEqual(ShutdownKind.None, r.Shutdown, "plugged in");
        }

        [Test]
        public void CriticalDamage_RepairedAtTheLander_ThenBackToWork()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            r.Condition = 0.95;
            w.DamageCritically(r.Id);
            var up = new RobotUpkeep();
            for (int i = 0; i < 10; i++)
            {
                up.Plan(w, Lander, 1.0);
                up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, new Parts());
            }
            up.Plan(w, Lander, 1.0);
            Assert.IsFalse(r.CriticallyDamaged);
            Assert.AreEqual(ShutdownKind.None, r.Shutdown);
            Assert.IsTrue(r.Available);
        }

        [Test]
        public void Save_KeepsCriticalDamage()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            w.DamageCritically(r.Id);
            var copy = new Workforce();
            Assert.IsTrue(copy.Restore(w.Capture()));
            Assert.IsTrue(copy.Robots[0].CriticallyDamaged);
        }
    }
}

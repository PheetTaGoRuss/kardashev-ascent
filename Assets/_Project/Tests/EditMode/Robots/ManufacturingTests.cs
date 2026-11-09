using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Robot Workshop, manufacturing, substitutes and upgrades (phase 3b).</summary>
    public class ManufacturingTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static StubPlanetSimulation WithWorkshop()
        {
            var sim = new StubPlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(sim.PlaceWorkshop(sim.BaseTileId, 5));
            Assert.AreEqual(1, sim.WorkshopCount, "built from the lander's parts at once");
            return sim;
        }

        static void Stock(StubPlanetSimulation sim, double kg)
        {
            foreach (Material m in Materials.All) sim.Materials.Add(m, kg);
        }

        /// <summary>Tick until <paramref name="done"/> (robots stop to charge, so work takes longer than its hours).</summary>
        static void RunUntil(StubPlanetSimulation sim, Func<bool> done, double maxHours = 96.0)
        {
            for (double h = 0.0; h < maxHours && !done(); h += 0.25)
                sim.Tick(0.25);
            Assert.IsTrue(done(), "did not finish in time");
        }

        static void Unlock(StubPlanetSimulation sim, int milestones) =>
            typeof(StubPlanetSimulation).GetProperty("MilestonesReached").SetValue(sim, milestones);

        [Test]
        public void Workshop_IsABuilding_SalvagedLikeOthers()
        {
            var sim = WithWorkshop();
            Assert.AreEqual(BuildingType.Workshop, sim.GetBuilding(sim.BaseTileId, 5));
            double stock = sim.BuildStockWh;
            Assert.IsTrue(sim.RemoveBuilding(sim.BaseTileId, 5));
            Assert.AreEqual(stock + BuildCosts.SalvageShare * RobotTech.WorkshopPartsWh, sim.BuildStockWh, 1e-3);
        }

        [Test]
        public void OrderRobot_NeedsAWorkshopAndMaterials()
        {
            var sim = new StubPlanetSimulation(Sphere, Surface, Earthlike);
            StringAssert.Contains("Workshop", sim.OrderRobot(RobotKind.General));
            sim.PlaceWorkshop(sim.BaseTileId, 5);
            StringAssert.StartsWith("Missing", sim.OrderRobot(RobotKind.General));
            Assert.AreEqual(0, sim.Jobs.Count);
        }

        [Test]
        public void NewRobot_TakesItsInputs_ThenTheWork_ThenJoinsTheWorkforce()
        {
            var sim = WithWorkshop();
            Stock(sim, 1000.0);
            double parts = sim.BuildStockWh;
            Assert.IsNull(sim.OrderRobot(RobotKind.General));
            Assert.AreEqual(1000.0 - 400.0, sim.Materials[Material.Iron], 1e-9);
            Assert.AreEqual(1000.0 - 40.0, sim.Materials[Material.Copper], 1e-9, "copper while there is enough");
            Assert.AreEqual(1000.0 - 2.0, sim.Materials[Material.Lithium], 1e-9);
            Assert.AreEqual(parts - 50_000.0, sim.BuildStockWh, 1e-6);
            Assert.AreEqual(TaskKind.Manufacture, sim.Workforce.Robots[0].Task.Value.Kind);
            sim.Tick(23.0);
            Assert.AreEqual(1, sim.Workforce.Robots.Count, "24 hours of work");
            RunUntil(sim, () => sim.Workforce.Robots.Count == 2);
            Assert.AreEqual(0, sim.Jobs.Count);
            Assert.AreEqual(RobotSpecs.BatteryCapacityWh, sim.Workforce.Robots[1].Battery.NominalWh, 1e-9);
        }

        [Test]
        public void WorkshopJobs_GoBeforeMining_AndLetBuildingGoFirst()
        {
            var sim = WithWorkshop();
            Stock(sim, 1000.0);
            Assert.IsTrue(sim.OrderMining(sim.BaseTileId, Material.Rock));
            Assert.AreEqual(TaskKind.Mine, sim.Workforce.Robots[0].Task.Value.Kind);
            Assert.IsNull(sim.OrderRobot(RobotKind.General));
            sim.Tick(1.0);   // the mining batch ends; ties put the Workshop before mining
            Assert.AreEqual(TaskKind.Manufacture, sim.Workforce.Robots[0].Task.Value.Kind);
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            sim.PlaceBattery(sim.BaseTileId, 0);
            sim.Workforce.Resume(sim.Workforce.Robots[0].GroupId);
            sim.Tick(1.0);   // one round of Workshop work, then the site
            Assert.IsFalse(sim.IsUnderConstruction(sim.BaseTileId, 0));
        }

        [Test]
        public void ManufactureCostsMoreThanARepair()
        {
            Assert.Greater(RobotTech.Robot(RobotKind.General, false, false).PartsWh, RobotSpecs.RepairPartsWh);
        }

        [Test]
        public void SpecialisedRobots_UnlockByMilestone_TransportNever()
        {
            var sim = WithWorkshop();
            Stock(sim, 5000.0);
            StringAssert.Contains("milestone 1", sim.OrderRobot(RobotKind.Mining));
            Unlock(sim, 1);
            Assert.IsNull(sim.OrderRobot(RobotKind.Mining));
            StringAssert.Contains("milestone 2", sim.OrderRobot(RobotKind.Maintenance));
            Unlock(sim, 10);
            StringAssert.Contains("not available", sim.OrderRobot(RobotKind.Transport));
        }

        [Test]
        public void WithoutCopperOrLithium_SubstitutesAreUsed_NaIonHoldsLess()
        {
            var sim = WithWorkshop();
            Unlock(sim, 2);
            sim.Materials.Add(Material.Iron, 400.0);
            sim.Materials.Add(Material.Rock, 200.0);
            sim.Materials.Add(Material.Aluminium, 40.0);
            sim.Materials.Add(Material.Sodium, 6.0);
            Assert.IsNull(sim.OrderRobot(RobotKind.General));
            Assert.IsTrue(sim.Jobs[0].AluminiumWiring);
            Assert.IsTrue(sim.Jobs[0].SodiumBattery);
            RunUntil(sim, () => sim.Workforce.Robots.Count == 2);
            Robot made = sim.Workforce.Robots[1];
            Assert.IsTrue(made.SodiumBattery);
            Assert.AreEqual(2408.0 * 160.0 / 250.0, made.Battery.NominalWh, 1e-9);
        }

        [Test]
        public void Substitutes_NeedTheirUnlock()
        {
            var sim = WithWorkshop();
            sim.Materials.Add(Material.Iron, 400.0);
            sim.Materials.Add(Material.Rock, 200.0);
            sim.Materials.Add(Material.Aluminium, 40.0);
            sim.Materials.Add(Material.Lithium, 2.0);
            StringAssert.Contains("copper", sim.OrderRobot(RobotKind.General), "aluminium wiring is locked at milestone 0");
        }

        [Test]
        public void Refining_TurnsRockIntoAluminium()
        {
            var sim = WithWorkshop();
            Unlock(sim, 1);
            sim.Materials.Add(Material.Rock, 100.0);
            Assert.IsNull(sim.OrderRefine(Material.Aluminium));
            Assert.AreEqual(100.0 - 60.0, sim.Materials[Material.Rock], 1e-9);
            RunUntil(sim, () => sim.Jobs.Count == 0);
            Assert.AreEqual(10.0, sim.Materials[Material.Aluminium], 1e-9);
            StringAssert.Contains("milestone 2", sim.OrderRefine(Material.Sodium));
        }

        [Test]
        public void CancelJob_GivesEverythingBack()
        {
            var sim = WithWorkshop();
            Stock(sim, 1000.0);
            double parts = sim.BuildStockWh;
            sim.OrderRobot(RobotKind.General);
            Assert.IsTrue(sim.CancelJob(0));
            Assert.AreEqual(1000.0, sim.Materials[Material.Iron], 1e-9);
            Assert.AreEqual(parts, sim.BuildStockWh, 1e-6);
            Assert.IsFalse(sim.CancelJob(0));
        }

        [Test]
        public void Upgrades_BigBatteryFastDriveShielding()
        {
            var sim = WithWorkshop();
            Unlock(sim, 3);
            Stock(sim, 1000.0);
            Robot r = sim.Workforce.Robots[0];
            Assert.IsNull(sim.OrderUpgrade(r.Id, RobotModule.BigBattery));
            StringAssert.Contains("Already", sim.OrderUpgrade(r.Id, RobotModule.BigBattery));
            RunUntil(sim, () => r.Has(RobotModule.BigBattery));
            Assert.AreEqual(2408.0 * 1.5, r.Battery.NominalWh, 1e-9);
            StringAssert.Contains("already", sim.OrderUpgrade(r.Id, RobotModule.BigBattery));

            Assert.IsNull(sim.OrderUpgrade(r.Id, RobotModule.FastDrive));
            RunUntil(sim, () => r.Has(RobotModule.FastDrive));
            r.Battery.Restore(r.Battery.DeliveredWh, 1e9);   // charged, so the next hour is all work
            sim.Tick(0.0);
            Assert.IsTrue(sim.OrderMining(sim.BaseTileId, Material.Rock));
            double rock = sim.Materials[Material.Rock];
            sim.Tick(1.0);
            Assert.AreEqual(rock + RobotSpecs.MiningKgPerHour, sim.Materials[Material.Rock], 1e-6, "1.5 h of work in an hour: one batch");
        }

        [Test]
        public void Shielding_HalvesFlareWear()
        {
            var w = new Workforce();
            Robot a = w.AddRobot(RobotKind.General, 0), b = w.AddRobot(RobotKind.General, 0);
            b.Modules = RobotModule.Shielding;
            var up = new RobotUpkeep();
            up.Plan(w, new System.Collections.Generic.List<RobotFacility>(), 10.0);
            up.Apply(w, 10.0, 0.0, 0.0, 0.0, true, null);
            Assert.AreEqual(1.0 - 10.0 * RobotSpecs.FlareWearPerHour, a.Condition, 1e-12);
            Assert.AreEqual(1.0 - 10.0 * RobotSpecs.FlareWearPerHour * RobotTech.ShieldingFactor, b.Condition, 1e-12);
        }

        [Test]
        public void MaintenanceRobot_SpeedsUpRepairs()
        {
            var w = new Workforce();
            Robot hurt = w.AddRobot(RobotKind.General, 0);
            hurt.Condition = 0.0;
            w.AddRobot(RobotKind.Maintenance, 0);
            var up = new RobotUpkeep();
            var lander = new System.Collections.Generic.List<RobotFacility> { new RobotFacility(0, -1, ChargingCapability.Lander) };
            up.Plan(w, lander, 1.0);
            var parts = new BigParts();
            up.Apply(w, 1.0, 0.0, 0.0, 0.0, false, parts);
            Assert.AreEqual(RobotSpecs.RepairPerHour * RobotSpecs.SpecialistSpeed, hurt.Condition, 1e-12);
        }

        sealed class BigParts : IPartsStore
        {
            public double StockWh => 1e12;
            public double Take(double wh) => wh;
        }

        [Test]
        public void GroupCapacity_RisesWithMilestones()
        {
            Assert.AreEqual(Workforce.DefaultGroupCapacity, RobotTech.GroupCapacity(0));
            Assert.AreEqual(Workforce.DefaultGroupCapacity + 2, RobotTech.GroupCapacity(2));
            Assert.AreEqual(Workforce.DefaultGroupCapacity + 6, RobotTech.GroupCapacity(10));
        }

        [Test]
        public void Save_KeepsWorkshopJobsAndRobotUpgrades()
        {
            var a = WithWorkshop();
            Unlock(a, 3);
            Stock(a, 2000.0);
            a.OrderUpgrade(a.Workforce.Robots[0].Id, RobotModule.Shielding);
            RunUntil(a, () => a.Workforce.Robots[0].Has(RobotModule.Shielding));
            a.OrderRobot(RobotKind.Mining);
            a.Tick(5.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(17, state.Version);

            var b = new StubPlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(1, b.WorkshopCount);
            Assert.AreEqual(1, b.Jobs.Count);
            Assert.AreEqual(a.Jobs[0].DoneHours, b.Jobs[0].DoneHours, 1e-9);
            Assert.IsTrue(b.Workforce.Robots[0].Has(RobotModule.Shielding));
            RunUntil(b, () => b.Workforce.Robots.Count == 2);
            Assert.AreEqual(RobotKind.Mining, b.Workforce.Robots[1].Kind);
        }

        [Test]
        public void SaveTypesAreSerializable()
        {
            Assert.IsTrue(typeof(JobRecord).IsSerializable);
        }
    }
}

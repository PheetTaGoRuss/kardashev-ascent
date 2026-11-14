using KardashevAscent.Simulation.Energy;
using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Robots in the colony simulation: sites need a robot (design D2), salvage orders, saves version 7.</summary>
    public class RobotWorkTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        /// <summary>A colony with one robot (these tests follow what one robot does).</summary>
        static PlanetSimulation Create()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Workforce.Reset(sim.BaseTileId, 1);
            return sim;
        }

        static RobotGroup GroupOf(PlanetSimulation sim) => sim.Workforce.GetGroup(sim.Workforce.Robots[0].GroupId);

        [Test]
        public void NewColony_HasThreeGeneralRobotsAtTheBase()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.AreEqual(3, Workforce.StarterRobots, "director, 2026-11-14");
            Assert.AreEqual(Workforce.StarterRobots, sim.Workforce.Robots.Count);
            foreach (Robot r in sim.Workforce.Robots)
            {
                Assert.AreEqual(RobotKind.General, r.Kind);
                Assert.AreEqual(sim.BaseTileId, r.Tile);
            }
        }

        [Test]
        public void IdleRobot_BuildsAtOnceFromStock()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            var events = new List<(int, int)>();
            sim.BuildingCompleted += (a, b) => events.Add((a, b));
            Assert.IsTrue(sim.PlacePanel(t, 0, 0.3, Math.PI));
            Assert.IsFalse(sim.IsUnderConstruction(t, 0), "the idle robot built it with parts in stock");
            Assert.IsTrue(sim.PlaceBattery(t, 1));
            Assert.IsFalse(sim.IsUnderConstruction(t, 1));
            Assert.IsNull(sim.Workforce.Robots[0].Task, "free again");
            CollectionAssert.IsEmpty(events, "a building finished by the command that placed it raises no event");
        }

        [Test]
        public void PausedRobots_SitesWaitAndStockGrows_ResumeFinishesThem()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            RobotGroup g = GroupOf(sim);
            sim.Workforce.Pause(g.Id);
            double stock = sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron];
            Assert.IsTrue(sim.PlacePanel(t, 0, 0.3, Math.PI));
            Assert.IsTrue(sim.IsUnderConstruction(t, 0), "no robot works on it");
            Assert.AreEqual(stock, sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron], "nothing was taken from stock");
            Assert.AreEqual(0.0, sim.ConstructionProgress(t, 0));

            sim.Tick(1.0);
            Assert.IsTrue(sim.IsUnderConstruction(t, 0));

            var events = new List<(int, int)>();
            sim.BuildingCompleted += (a, b) => events.Add((a, b));
            sim.Workforce.Resume(g.Id);
            sim.RefreshWork();
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            CollectionAssert.AreEqual(new[] { (t, 0) }, events);
        }

        [Test]
        public void OneRobot_FinishesSeveralSitesInOrderInOneStep()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Workforce.Pause(GroupOf(sim).Id);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Battery).Times(3).Items()) sim.Materials.Add(m, kg);
            for (int s = 0; s < 3; s++) Assert.IsTrue(sim.PlaceBattery(t, 20 * s));   // two-slot footprints, kept apart
            var events = new List<(int, int)>();
            sim.BuildingCompleted += (a, b) => events.Add((a, b));
            sim.Workforce.Resume(GroupOf(sim).Id);
            sim.Tick(0.25);
            CollectionAssert.AreEqual(new[] { (t, 0), (t, 20), (t, 40) }, events, "the freed robot takes the next site at once");
            Assert.AreEqual(3 * sim.BatteryUnitWh, sim.BatteryCapacityWh - PowerGridLanderCapacity());
        }

        static double PowerGridLanderCapacity() => KardashevAscent.Simulation.Energy.PowerGrid.Lander().Battery.CapacityWh;

        [Test]
        public void SalvageOrder_WaitsForARobot_TheBuildingKeepsWorking()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlaceBattery(t, 0));
            double capacity = sim.BatteryCapacityWh;
            RobotGroup g = GroupOf(sim);
            sim.Workforce.Pause(g.Id);
            Assert.IsTrue(sim.OrderSalvage(t, 0));
            Assert.IsTrue(sim.IsSalvageOrdered(t, 0));
            Assert.AreEqual(BuildingType.Battery, sim.GetBuilding(t, 0), "still standing");
            Assert.AreEqual(capacity, sim.BatteryCapacityWh, "still working");
            Assert.IsFalse(sim.OrderSalvage(t, 0), "already ordered");

            var salvaged = new List<(int, int)>();
            sim.BuildingSalvaged += (a, b) => salvaged.Add((a, b));
            double stock = sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron];
            sim.Workforce.Resume(g.Id);
            sim.RefreshWork();
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(t, 0));
            Assert.IsFalse(sim.IsSalvageOrdered(t, 0));
            CollectionAssert.AreEqual(new[] { (t, 0) }, salvaged);
            Assert.AreEqual(stock + BuildCosts.SalvageShare * sim.BuildBill(BuildingType.Battery)[KardashevAscent.Simulation.Resources.Material.Iron],
                            sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron], 1e-6);
        }

        [Test]
        public void SalvageOrder_OnASite_CancelsItAtOnce()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Workforce.Pause(GroupOf(sim).Id);
            sim.PlacePanel(t, 0, 0.3, Math.PI);
            Assert.IsTrue(sim.OrderSalvage(t, 0));
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(t, 0));
            Assert.IsFalse(sim.OrderSalvage(t, 0), "empty slot");
        }

        [Test]
        public void CancelSalvage_KeepsTheBuilding()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.PlaceBattery(t, 0);
            sim.Workforce.Pause(GroupOf(sim).Id);
            sim.OrderSalvage(t, 0);
            Assert.IsTrue(sim.CancelSalvage(t, 0));
            Assert.IsFalse(sim.CancelSalvage(t, 0));
            sim.Workforce.Resume(GroupOf(sim).Id);
            sim.Tick(1.0);
            Assert.AreEqual(BuildingType.Battery, sim.GetBuilding(t, 0));
        }

        [Test]
        public void CriticalSalvage_PullsTheRobotOffASite()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.PlaceBattery(t, 0);
            // Use up the stock so the next site has to wait for materials with the robot on it.
            sim.Materials.Clear();
            sim.PlacePanel(t, 1, 0.3, Math.PI);
            Robot r = sim.Workforce.Robots[0];
            Assert.AreEqual(TaskKind.Build, r.Task.Value.Kind, "the robot waits at the site for parts");
            sim.Workforce.SetGroupPriority(r.GroupId, TaskKind.Salvage, TaskPriority.Critical);
            sim.OrderSalvage(t, 0);
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(t, 0), "the critical order interrupted the build");
            Assert.AreEqual(TaskKind.Build, r.Task.Value.Kind, "and the robot went back to the site");
        }

        [Test]
        public void Save_RoundTripsRobotsAndOrders()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.PlaceBattery(t, 0);
            a.Workforce.AddRobot(RobotKind.General, t);
            RobotGroup crew = a.Workforce.CreateGroup("Crew");
            a.Workforce.MoveRobot(a.Workforce.Robots[1].Id, crew.Id);
            a.Workforce.Pause(crew.Id);
            a.Workforce.Pause(a.Workforce.GetGroup(a.Workforce.Robots[0].GroupId).Id);
            a.OrderSalvage(t, 0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);

            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(2, b.Workforce.Robots.Count);
            Assert.AreEqual("Crew", b.Workforce.GetGroup(b.Workforce.Robots[1].GroupId).Name);
            Assert.IsTrue(b.IsSalvageOrdered(t, 0));
            Assert.AreEqual(BuildingType.Battery, b.GetBuilding(t, 0));
        }

        [Test]
        public void Save_Version6_LoadsWithTheStarterRobots_AndItsSitesKeepGoing()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.Materials.Clear();
            a.PlacePanel(t, 0, 0.3, Math.PI);
            SimulationState state = a.CaptureState();
            state.Version = 6;
            state.Workforce = null;
            state.SalvageOrders = null;

            var b = Create();
            b.Workforce.AddRobot(RobotKind.General, 0);
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(Workforce.StarterRobots, b.Workforce.Robots.Count, "a save from before robots gets the lander's crew");
            Assert.AreEqual(b.BaseTileId, b.Workforce.Robots[0].Tile);
            Assert.IsTrue(b.IsUnderConstruction(t, 0));
            Assert.AreEqual(TaskKind.Build, b.Workforce.Robots[0].Task.Value.Kind, "the starting robot took the site");
        }

        [Test]
        public void RemoveBuilding_DropsItsSalvageOrder()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.PlaceBattery(t, 0);
            sim.Workforce.Pause(GroupOf(sim).Id);
            sim.OrderSalvage(t, 0);
            Assert.IsTrue(sim.RemoveBuilding(t, 0));
            Assert.IsFalse(sim.IsSalvageOrdered(t, 0));
        }

        // ---- Phase 2: batteries, charging, wear ----

        [Test]
        public void OverTenDays_TheRobotChargesAtTheLander_AndNeverRunsFlat()
        {
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            bool charged = false;
            double lowest = 1.0;
            for (int h = 0; h < 240; h++)
            {
                sim.Tick(1.0);
                charged |= r.State == RobotState.Charging;
                lowest = Math.Min(lowest, r.Battery.StateOfCharge);
            }
            Assert.IsTrue(charged, "idle draw empties the battery to 20 % within days");
            Assert.Greater(lowest, 0.15, "it goes to charge at 20 %");
        }

        [Test]
        public void RobotCharging_CountsAsUsedPower()
        {
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            r.Battery.Restore(0.0, 0.1 * RobotSpecs.BatteryCapacityWh);
            sim.Tick(0.25);
            Assert.AreEqual(RobotState.Charging, r.State);
            Assert.Greater(sim.RobotChargingW, 0.0);
            Assert.Greater(r.Battery.StateOfCharge, 0.1);
        }

        [Test]
        public void NoPanels_EmptyBattery_TheRobotStillRecovers_AndCanBuild()
        {
            var sim = Create();
            sim.Grid.Battery.SetStored(0.0);
            Robot r = sim.Workforce.Robots[0];
            r.Battery.Restore(0.0, 0.15 * RobotSpecs.BatteryCapacityWh);
            double highest = 0.0;
            for (int h = 0; h < 12; h++)
            {
                sim.Tick(1.0);
                highest = System.Math.Max(highest, r.Battery.StateOfCharge);
            }
            Assert.IsTrue(sim.IsBrownout, "the colony has nothing to spare");
            Assert.GreaterOrEqual(highest, RobotSpecs.ResumeWorkFraction - 0.01, "the lander's RTGs charged it to 80 %");
            Assert.IsTrue(r.Available);
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, 0, 0.3, Math.PI));
            Assert.IsFalse(sim.IsUnderConstruction(sim.BaseTileId, 0));
        }

        [Test]
        public void BatteryYards_BecomeChargers_OnceFinished()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.AreEqual(1, sim.RobotFacilities().Count, "the lander");
            sim.PlaceBattery(t, 0);
            var list = sim.RobotFacilities();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual((t, 0), (list[1].Tile, list[1].Slot));
            sim.Workforce.Pause(GroupOf(sim).Id);
            sim.PlaceBattery(t, 1);
            Assert.AreEqual(2, sim.RobotFacilities().Count, "a site doesn't charge anything");
        }

        [Test]
        public void FlaringStar_IsAnMDwarf()
        {
            var sim = Create();
            Assert.IsFalse(sim.IsFlaringStar, "0 K means the Sun");
            sim.StarTemperatureK = 2900.0;
            Assert.IsTrue(sim.IsFlaringStar);
            sim.StarTemperatureK = 5772.0;
            Assert.IsFalse(sim.IsFlaringStar);
        }

        [Test]
        public void Save_KeepsBatteryAndCondition_Version7LoadsCharged()
        {
            var a = Create();
            Robot r = a.Workforce.Robots[0];
            r.Battery.Restore(50.0 * RobotSpecs.BatteryCapacityWh, 1000.0);
            r.Condition = 0.6;
            SimulationState state = a.CaptureState();

            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(1000.0, b.Workforce.Robots[0].Battery.StoredWh, 1e-9);
            Assert.AreEqual(0.6, b.Workforce.Robots[0].Condition, 1e-12);

            state.Version = 7;
            foreach (var rr in state.Workforce.Robots) rr.HasUpkeep = false;
            var c = Create();
            Assert.IsTrue(c.RestoreState(state));
            Assert.AreEqual(1.0, c.Workforce.Robots[0].Battery.StateOfCharge, 1e-12);
            Assert.AreEqual(1.0, c.Workforce.Robots[0].Condition);
        }
    }
}

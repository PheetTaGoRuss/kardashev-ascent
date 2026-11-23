using System;
using System.Linq;
using KardashevAscent.Simulation.Circuits;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>Inspecting, repairing and equipping the grid are robot tasks: a robot walks there, works, and the result lands.</summary>
    public class ElectricalWorkTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Chain(out int a, out int b)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePowerLine(b, 0, a));
            sim.Tick(1.0);
            return sim;
        }

        static void RunUntil(PlanetSimulation sim, Func<bool> done, double maxHours = 96.0)
        {
            for (double h = 0; h < maxHours && !done(); h += 0.5) sim.Tick(0.5);
        }

        [Test]
        public void ARobotInspectsALine_AndTheReadingBecomesKnown()
        {
            var sim = Chain(out int a, out _);
            Assert.IsFalse(sim.LineReadout(a).Measured);
            Assert.IsTrue(sim.OrderLineInspection(a));
            Assert.IsFalse(sim.OrderLineInspection(a), "one robot is already on it");
            Assert.AreEqual(1, sim.ElectricalOrders().Count);
            RunUntil(sim, () => sim.LineReadout(a).Measured);
            Assert.IsTrue(sim.LineReadout(a).Measured, "the inspection finished");
            Assert.AreEqual(0, sim.ElectricalOrders().Count);
        }

        [Test]
        public void ARobotRepairsAFailedLine_AndTheGridIsWholeAgain()
        {
            var sim = Chain(out _, out int b);
            sim.SetCableState(b, 0, 0.0, false);
            sim.Tick(0.1);
            Assert.IsFalse(sim.IsPowered(b));
            sim.Materials.Add(Material.Copper, 500.0); sim.Materials.Add(Material.Iron, 500.0); sim.Materials.Add(Material.Rock, 500.0);
            Assert.IsNull(sim.OrderLineRepair(b, 0));
            Assert.AreEqual("A robot is already on it.", sim.OrderLineRepair(b, 0));
            double copper = sim.Materials[Material.Copper];
            RunUntil(sim, () => sim.IsPowered(b));
            Assert.IsTrue(sim.IsPowered(b));
            Assert.AreEqual(0, sim.FailedLines().Count());
            Assert.Less(sim.Materials[Material.Copper], copper, "the repair took its copper when it finished");
        }

        [Test]
        public void ARepairWithoutTheMaterialsIsRefusedAtOrderTime()
        {
            var sim = Chain(out _, out int b);
            sim.SetCableState(b, 0, 0.0, false);
            sim.Materials.TryTake(Material.Copper, sim.Materials[Material.Copper]);
            StringAssert.Contains("Missing", sim.OrderLineRepair(b, 0));
            Assert.AreEqual(0, sim.ElectricalOrders().Count);
        }

        [Test]
        public void IfTheStockIsGoneWhenTheRobotFinishes_TheOrderIsDroppedWithAReason()
        {
            var sim = Chain(out _, out int b);
            sim.SetCableState(b, 0, 0.0, false);
            sim.Materials.Add(Material.Copper, 500.0); sim.Materials.Add(Material.Iron, 500.0); sim.Materials.Add(Material.Rock, 500.0);
            Assert.IsNull(sim.OrderLineRepair(b, 0));
            foreach (Material m in new[] { Material.Copper, Material.Iron, Material.Rock }) sim.Materials.TryTake(m, sim.Materials[m]);
            RunUntil(sim, () => sim.ElectricalOrders().Count == 0);
            Assert.AreEqual(0, sim.ElectricalOrders().Count);
            StringAssert.Contains("Missing", sim.LastElectricalProblem);
            Assert.IsFalse(sim.IsPowered(b));
        }

        [Test]
        public void ARobotInstallsACircuitInAStation()
        {
            var sim = Chain(out _, out _);
            int slot = 20;
            Assert.IsTrue(sim.PlaceChargingStation(sim.BaseTileId, slot));
            sim.Tick(2.0);
            var d = new CircuitBlueprint { Name = "Enable" };
            var src = d.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 10.0);
            var lamp = d.Place(CircuitComponentType.Lamp, 2, 0, 12.0, 6.0);
            d.Connect(src.Id, 1, lamp.Id, 0); d.Connect(lamp.Id, 1, src.Id, 0);
            d.Ports.Add(new CircuitPort { Name = "enable", Component = lamp.Id, Pin = 0 });
            int idx = sim.SaveCircuitBlueprint(d);
            Assert.IsNull(sim.OrderCircuitInstall(sim.BaseTileId, slot, idx));
            Assert.IsNull(sim.StationCircuit(sim.BaseTileId, slot), "not installed until the robot has worked");
            RunUntil(sim, () => sim.StationCircuit(sim.BaseTileId, slot) != null);
            Assert.IsNotNull(sim.StationCircuit(sim.BaseTileId, slot));
        }

        [Test]
        public void OrdersAreRefusedWhenTheyMakeNoSense()
        {
            var sim = Chain(out _, out _);
            Assert.IsFalse(sim.OrderLineInspection(sim.BaseTileId));
            StringAssert.Contains("no charging station", sim.OrderCircuitInstall(sim.BaseTileId, 5, 0));
            StringAssert.Contains("needs no repair", sim.OrderLineRepair(sim.BaseTileId, 0));
        }

        [Test]
        public void PendingOrdersSurviveSaveAndLoad()
        {
            var sim = Chain(out int a, out _);
            Assert.IsTrue(sim.OrderLineInspection(a));
            var state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(1, loaded.ElectricalOrders().Count);
            Assert.AreEqual(a, loaded.ElectricalOrders()[0].Tile);
        }

        [Test]
        public void ASaveFromVersion45HasNoElectricalOrders()
        {
            var sim = Chain(out int a, out _);
            sim.OrderLineInspection(a);
            var state = sim.CaptureState();
            state.Version = 45;
            state.ElectricalOrders.Clear();
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(0, loaded.ElectricalOrders().Count);
        }
    }
}

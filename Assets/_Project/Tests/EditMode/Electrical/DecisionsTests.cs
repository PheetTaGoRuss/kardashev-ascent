using System;
using System.Linq;
using KardashevAscent.Simulation.Circuits;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>The decisions of 2026-11-23: one AC frequency and ladder, converter catalogue, meter price, circuit supply on the grid.</summary>
    public class DecisionsTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void TheLadderIsOneSetOfNumbersAndAcIs50HzOnly()
        {
            Assert.AreEqual(50.0, VoltageLadder.AcFrequencyHz);
            CollectionAssert.AreEqual(new[] { 24, 400, 33_000, 500_000 }, VoltageLadder.DcClasses);
            CollectionAssert.AreEqual(new[] { 400, 33_000, 500_000 }, VoltageLadder.AcClasses);
            Assert.IsTrue(VoltageLadder.IsDcOnly(24));
            Assert.IsFalse(VoltageLadder.IsDcOnly(400));
            foreach (int ac in VoltageLadder.AcClasses) CollectionAssert.Contains(VoltageLadder.DcClasses, ac, "the AC ladder matches the DC one");
        }

        [Test]
        public void EveryCatalogueConverterObeysItsKindsRules()
        {
            var all = new[]
            {
                VoltageLadder.Converters.PowerTransformer(true), VoltageLadder.Converters.PowerTransformer(false),
                VoltageLadder.Converters.DistributionTransformer(true), VoltageLadder.Converters.DistributionTransformer(false),
                VoltageLadder.Converters.Inverter400(), VoltageLadder.Converters.Rectifier400(),
                VoltageLadder.Converters.Inverter33k(), VoltageLadder.Converters.Rectifier33k(),
                VoltageLadder.Converters.Inverter500k(), VoltageLadder.Converters.Rectifier500k(),
                VoltageLadder.Converters.DcDc(VoltageLadder.Dc400, VoltageLadder.Dc33k),
            };
            foreach (var c in all) Assert.IsNull(c.Problem(), c.Name);
            Assert.AreEqual(0.995, VoltageLadder.Converters.PowerTransformer(true).Efficiency);
            Assert.AreEqual(0.985, VoltageLadder.Converters.DistributionTransformer(true).Efficiency);
            Assert.AreEqual(0.97, VoltageLadder.Converters.Inverter400().Efficiency);
            Assert.AreEqual(0.99, VoltageLadder.Converters.Inverter500k().Efficiency);
        }

        [Test]
        public void ThePowerTransformerJoinsThe33kVAnd500kVAcNetworks()
        {
            var g = new ElectricalGraph();
            var lo = g.AddNode(1, NetworkType.Ac, VoltageLadder.Ac33k, VoltageLadder.AcFrequencyHz);
            var hi = g.AddNode(2, NetworkType.Ac, VoltageLadder.Ac500k, VoltageLadder.AcFrequencyHz);
            Assert.IsTrue(ConverterInstaller.Install(g, VoltageLadder.Converters.PowerTransformer(true), lo, hi).Ok);
            Assert.AreEqual(2, g.Networks().Count);
        }

        [Test]
        public void AnAirlessWorldHasNoCoronaAndMarsIsInThePaschenRegion()
        {
            Assert.AreEqual(CableEnvironment.Regime.Vacuum, CableEnvironment.RegimeOf(CableEnvironment.RelativeAirDensity(0.0, -170.0)));
            Assert.AreEqual(1.0, CableEnvironment.VoltageDerate(0.0, 500e-6, out bool fallback), 1e-12);
            Assert.IsFalse(fallback);
            Assert.AreEqual(CableEnvironment.Regime.Paschen, CableEnvironment.RegimeOf(CableEnvironment.RelativeAirDensity(660.0, -60.0)), "Mars");
        }

        // ---- the meter and the circuit on the grid ----
        static PlanetSimulation Wired(out int a)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            sim.Tick(1.0);
            return sim;
        }

        [Test]
        public void AMeterCostsTwoCopperAndOneIron()
        {
            var sim = Wired(out int a);
            Assert.AreEqual(2.0, PlanetSimulation.MeterPrice[Material.Copper]);
            Assert.AreEqual(1.0, PlanetSimulation.MeterPrice[Material.Iron]);
            double cu = sim.Materials[Material.Copper], fe = sim.Materials[Material.Iron];
            Assert.IsTrue(sim.InstallLineMeter(a));
            Assert.AreEqual(cu - 2.0, sim.Materials[Material.Copper], 1e-9);
            Assert.AreEqual(fe - 1.0, sim.Materials[Material.Iron], 1e-9);
            Assert.IsFalse(sim.InstallLineMeter(a), "one meter per line, and no second charge");
            Assert.AreEqual(cu - 2.0, sim.Materials[Material.Copper], 1e-9);
        }

        [Test]
        public void WithoutTheMaterialsThereIsNoMeter()
        {
            var sim = Wired(out int a);
            sim.Materials.TryTake(Material.Copper, sim.Materials[Material.Copper]);
            Assert.IsFalse(sim.InstallLineMeter(a));
            StringAssert.Contains("Missing", sim.OrderLineMeter(a));
        }

        [Test]
        public void ARobotInstallsTheMeterAndCancelTakesTheJobBack()
        {
            var sim = Wired(out int a);
            Assert.IsNull(sim.OrderLineMeter(a));
            Assert.AreEqual("A robot is already on it.", sim.OrderLineMeter(a));
            Assert.IsTrue(sim.CancelElectricalOrder(ElectricalAction.InstallMeter, a, 0));
            Assert.AreEqual(0, sim.ElectricalOrders().Count);
            Assert.IsNull(sim.OrderLineMeter(a));
            for (double h = 0; h < 96 && !sim.HasLineMeter(a); h += 0.5) sim.Tick(0.5);
            Assert.IsTrue(sim.HasLineMeter(a));
        }

        [Test]
        public void ACircuitSupplyCountsAsTheStationsLoadOnTheGrid()
        {
            var sim = Wired(out _);
            Assert.IsTrue(sim.PlaceChargingStation(sim.BaseTileId, 20));
            sim.WireAllBuildings();   // a station off the grid has no supply to draw
            sim.Tick(2.0);
            Assert.AreEqual(0.0, sim.CircuitLoadW);
            var d = new CircuitBlueprint { Name = "Lamp" };
            var src = d.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 10.0);
            var lamp = d.Place(CircuitComponentType.Lamp, 2, 0, 12.0, 24.0);
            d.Connect(src.Id, 1, lamp.Id, 0); d.Connect(lamp.Id, 1, src.Id, 0);
            d.Ports.Add(new CircuitPort { Name = "enable", Component = lamp.Id, Pin = 0 });
            Assert.IsNull(sim.InstallStationCircuit(sim.BaseTileId, 20, sim.SaveCircuitBlueprint(d)));
            sim.Tick(0.5);
            Assert.AreEqual(24.0, sim.CircuitLoadW, 1.0, "the 24 W lamp is drawn from the grid");
            sim.RemoveStationCircuit(sim.BaseTileId, 20);
            sim.Tick(0.5);
            Assert.AreEqual(0.0, sim.CircuitLoadW);
        }

        [Test]
        public void ACircuitBatteryChargesWhenCurrentIsDrivenIntoItAtTheRoundTripEfficiency()
        {
            var d = new CircuitBlueprint { Name = "Charge" };
            var psu = d.Place(CircuitComponentType.DcSupply, 0, 0, 14.0, 0.01, 10.0);
            var bat = d.Place(CircuitComponentType.Battery, 2, 0, 12.0, 0.5, 100.0);
            d.Connect(psu.Id, 1, bat.Id, 1); d.Connect(bat.Id, 0, psu.Id, 0);
            var inst = new CircuitInstance { Design = d };
            inst.ResetState();
            inst.StateOf(bat.Id).EnergyWh = 10.0;
            var res = CircuitSolver.Solve(d, inst.State);
            double into = res.Of(bat.Id).PowerW;
            Assert.Greater(into, 0.0, "current runs into the battery: it is charging");
            double accepted = Math.Min(into, 100.0 * CircuitSolver.BatteryCRatePerHour);
            CircuitSolver.Advance(d, inst.State, res, 0.5);
            Assert.AreEqual(10.0 + accepted * Math.Sqrt(0.90) * 0.5, inst.StateOf(bat.Id).EnergyWh, 1e-6);
            CircuitSolver.Advance(d, inst.State, res, 1000.0);
            Assert.AreEqual(100.0, inst.StateOf(bat.Id).EnergyWh, 1e-9, "never above its capacity");
        }
    }
}

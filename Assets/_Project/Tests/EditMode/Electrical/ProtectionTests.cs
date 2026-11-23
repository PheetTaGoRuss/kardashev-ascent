using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>Fuses and breakers on lines: they open a line that overheats before its insulation is spent.</summary>
    public class ProtectionTests
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
            for (int slot = 1; slot < 6; slot++) sim.PlacePanel(b, slot, 0.35, Math.PI);
            sim.WireAllBuildings();
            sim.Tick(12.0);
            return sim;
        }

        [Test]
        public void AFuseCostsOneCopperAndABreakerMore()
        {
            Assert.AreEqual(1.0, PlanetSimulation.ProtectionPrice(ProtectionKind.Fuse)[Material.Copper]);
            Assert.AreEqual(5.0, PlanetSimulation.ProtectionPrice(ProtectionKind.Breaker)[Material.Copper]);
            Assert.AreEqual(2.0, PlanetSimulation.ProtectionPrice(ProtectionKind.Breaker)[Material.Iron]);
            Assert.AreEqual(0.0, PlanetSimulation.ProtectionPrice(ProtectionKind.None)[Material.Copper]);
        }

        [Test]
        public void FittingTakesThePriceAndIsRefusedWhenShortOrRepeated()
        {
            var sim = Chain(out int a, out _);
            double cu = sim.Materials[Material.Copper];
            Assert.IsTrue(sim.InstallLineProtection(a, ProtectionKind.Fuse));
            Assert.AreEqual(ProtectionKind.Fuse, sim.LineProtectionOf(a));
            Assert.AreEqual(cu - 1.0, sim.Materials[Material.Copper], 1e-9);
            Assert.IsFalse(sim.InstallLineProtection(a, ProtectionKind.Fuse), "already fitted, no second charge");
            sim.Materials.TryTake(Material.Copper, sim.Materials[Material.Copper]);
            Assert.IsFalse(sim.InstallLineProtection(a, ProtectionKind.Breaker));
            StringAssert.Contains("Missing", sim.OrderLineProtection(a, ProtectionKind.Breaker));
        }

        [Test]
        public void AnOverheatedProtectedLineOpensAndKeepsItsCondition()
        {
            var sim = Chain(out int a, out int b);
            Assert.IsTrue(sim.InstallLineProtection(b, ProtectionKind.Breaker));
            sim.SetLineTemperatureForTests(b, a, 150.0);
            sim.Tick(0.01);
            Assert.IsFalse(sim.IsPowered(b), "the breaker tripped");
            Assert.AreEqual(1.0, sim.LineCondition(b, 0), 1e-5, "the insulation was not spent (only the ordinary wear of half a day)");
            Assert.AreEqual(1, sim.FailedLines().Count());
        }

        [Test]
        public void AnOverheatedUnprotectedLineAgesInstead()
        {
            var sim = Chain(out int a, out int b);
            sim.SetLineTemperatureForTests(b, a, 150.0);
            sim.Tick(0.01);
            Assert.IsTrue(sim.IsPowered(b), "nothing opened it");
            Assert.Less(sim.LineCondition(b, 0), 1.0, "but its insulation aged");
        }

        [Test]
        public void ResettingABreakerIsFreeAndReplacingAFuseCostsCopper()
        {
            var sim = Chain(out int a, out int b);
            sim.InstallLineProtection(b, ProtectionKind.Breaker);
            sim.SetLineTemperatureForTests(b, a, 150.0);
            sim.Tick(0.01);
            Assert.AreEqual(0.0, sim.RepairBill(b, 0)[Material.Copper], 1e-12, "a breaker only needs a reset");
            Assert.IsNull(sim.RepairLine(b, 0));
            sim.Tick(0.1);
            Assert.IsTrue(sim.IsPowered(b));

            var fused = Chain(out int a2, out int b2);
            fused.InstallLineProtection(b2, ProtectionKind.Fuse);
            fused.SetLineTemperatureForTests(b2, a2, 150.0);
            fused.Tick(0.01);
            Assert.AreEqual(1.0, fused.RepairBill(b2, 0)[Material.Copper], 1e-12, "a new fuse is one copper");
        }

        [Test]
        public void ProtectionSurvivesSaveAndLoad()
        {
            var sim = Chain(out int a, out _);
            sim.InstallLineProtection(a, ProtectionKind.Breaker);
            var state = sim.CaptureState();
            Assert.AreEqual(1, state.LineProtections.Count);
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(ProtectionKind.Breaker, loaded.LineProtectionOf(a));
            state.Version = 46;
            state.LineProtections.Clear();
            var old = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(old.RestoreState(state));
            Assert.AreEqual(ProtectionKind.None, old.LineProtectionOf(a));
        }

        [Test]
        public void ARobotFitsTheProtection()
        {
            var sim = Chain(out int a, out _);
            Assert.IsNull(sim.OrderLineProtection(a, ProtectionKind.Fuse));
            for (double h = 0; h < 96 && sim.LineProtectionOf(a) == ProtectionKind.None; h += 0.5) sim.Tick(0.5);
            Assert.AreEqual(ProtectionKind.Fuse, sim.LineProtectionOf(a));
        }
    }
}

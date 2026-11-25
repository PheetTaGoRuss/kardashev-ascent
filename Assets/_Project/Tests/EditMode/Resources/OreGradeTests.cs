using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Resources
{
    /// <summary>Deposit tonnage, ore grade, tailings and the falling grade (B1 to B3).</summary>
    public class OreGradeTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0), Kardashev.EarthRadiusM) { WeatherEnabled = false };

        [Test]
        public void TypicalTonnages_AreTheCitedSizesTimesTheTileShare()
        {
            Assert.AreEqual(3_000_000_000.0 * 1e-4, DepositMap.ReserveKgAtGrade1(Material.Copper), 1.0, "3 Mt of copper (Singer et al. 2008) times 1e-4");
            Assert.AreEqual(500_000_000.0 * 1e-4, DepositMap.ReserveKgAtGrade1(Material.Lithium), 1.0);
            Assert.AreEqual(200_000_000.0 * 1e-4, DepositMap.ReserveKgAtGrade1(Material.RareEarth), 1.0);
            Assert.Greater(DepositMap.ReserveKgAtGrade1(Material.Iron), DepositMap.ReserveKgAtGrade1(Material.Copper));
        }

        [Test]
        public void OreGrade_IsAKgOfProductPerKgOfRock_AndFallsAsADepositIsMined()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Deposits.MarkSurveyed(t);
            double full = sim.Deposits.EffectiveGrade(t, Material.Iron);
            Assert.AreEqual(sim.Deposits.GradeOf(t, Material.Iron), full, 1e-12, "rich ore first: full grade at the start");
            sim.Deposits.Extract(t, Material.Iron, sim.Deposits.ReserveKg(t, Material.Iron) * 0.5);
            Assert.AreEqual(full * 0.75, sim.Deposits.EffectiveGrade(t, Material.Iron), 1e-9, "half mined: three quarters of the grade");
            Assert.AreEqual(0.60 * sim.Deposits.GradeOf(t, Material.Iron), sim.Deposits.OreGrade(t, Material.Iron), 1e-12);
            Assert.AreEqual(sim.Deposits.GradeOf(t, Material.Rock), sim.Deposits.EffectiveGrade(t, Material.Rock), 1e-12, "rock is the product itself");
        }

        [Test]
        public void Mining_KeepsMass_TheNonProductRockBecomesTailings()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Deposits.MarkSurveyed(t);
            Assert.IsTrue(sim.OrderMining(t, Material.Iron));
            double before = sim.Materials[Material.Iron];
            sim.Tick(4.0);
            double product = sim.Materials[Material.Iron] - before;
            Assert.Greater(product, 0.0);
            Assert.Greater(sim.TailingsKg, 0.0, "iron ore at 60 % leaves 40 % as tailings");
            Assert.AreEqual(sim.RockMovedKg, product + sim.TailingsKg, 1e-6, "rock moved = product + tailings");
        }

        [Test]
        public void TheReserveIsARangeUntilTheAssay_AndTheRateFallsWithDepletion()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Deposits.MarkSurveyed(t);
            var r = sim.ReserveEstimate(t, Material.Iron);
            Assert.IsFalse(r.IsExact, "tonnage is a range before the ore assay");
            double rate0 = sim.MiningRateEstimate(t, Material.Iron).Value;
            sim.Deposits.Extract(t, Material.Iron, sim.Deposits.ReserveKg(t, Material.Iron) * 0.9);
            Assert.Less(sim.MiningRateEstimate(t, Material.Iron).Value, rate0);
            Assert.AreEqual(0.1, sim.DepositShareLeft(t, Material.Iron), 1e-9);
        }

        [Test]
        public void AnExhaustedDeposit_WritesALogEntry()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Deposits.MarkSurveyed(t);
            sim.Deposits.Extract(t, Material.Iron, sim.Deposits.ReserveKg(t, Material.Iron) - 5.0);
            sim.OrderMining(t, Material.Iron);
            sim.Tick(1.0);
            Assert.IsTrue(sim.OperationsLog.Any(o => o.Quantity == "Deposit exhausted"));
        }

        [Test]
        public void TheMiningCounters_AreSaved_AndAnOldSaveStartsAtZero()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Deposits.MarkSurveyed(t);
            sim.OrderMining(t, Material.Iron);
            sim.Tick(3.0);
            var again = Create(); again.RestoreState(sim.CaptureState());
            Assert.AreEqual(sim.TailingsKg, again.TailingsKg, 1e-9);
            Assert.AreEqual(sim.RockMovedKg, again.RockMovedKg, 1e-9);
            var old = sim.CaptureState(); old.Version = 64;
            var third = Create(); third.RestoreState(old);
            Assert.AreEqual(0.0, third.TailingsKg);
        }
    }
}

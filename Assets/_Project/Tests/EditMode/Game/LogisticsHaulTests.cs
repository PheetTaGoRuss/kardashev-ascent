using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Hauls (MO-03, MO-04): demand makes them, robots carry them, mass is conserved, the energy is counted.</summary>
    public class LogisticsHaulTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Make() => new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, ChemistryEnabled = false };   // this test counts mass without the environment

        static int LandNeighbour(PlanetSimulation sim, int skip = 0) =>
            Sphere.Neighbors[sim.BaseTileId].Where(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n)).Skip(skip).First();

        static double Everything(PlanetSimulation sim) => Materials.All.Sum(m => sim.TotalKg(m));

        [Test]
        public void ADepotTarget_MakesHaulsUntilItIsMet_ThenStops()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            Assert.IsTrue(sim.SetStockTarget(tile, Material.Iron, 300.0));
            Assert.AreEqual(300.0, sim.StockTarget(tile, Material.Iron), 1e-12);
            for (int i = 0; i < 300 && sim.KgAt(tile, Material.Iron) < 300.0 - 1e-6; i++) sim.Tick(1.0);
            Assert.AreEqual(300.0, sim.KgAt(tile, Material.Iron), 1e-6, "the target is met");
            for (int i = 0; i < 12; i++) sim.Tick(1.0);
            Assert.IsEmpty(sim.HaulOrders(), "no more hauls once it is met");
            double after = sim.KgAt(tile, Material.Iron);
            for (int i = 0; i < 48; i++) sim.Tick(1.0);
            Assert.AreEqual(after, sim.KgAt(tile, Material.Iron), 1e-9, "and it stays put");
        }

        [Test]
        public void AHaul_NeverCreatesOrLosesMass()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Rock, 800.0);
            double before = Everything(sim);
            for (int i = 0; i < 400; i++)
            {
                sim.Tick(1.0);
                Assert.LessOrEqual(Everything(sim), before + 1e-6, "no mass appears");
            }
            Assert.Greater(sim.KgAt(tile, Material.Rock), 0.0);
            Assert.GreaterOrEqual(Everything(sim), before - 5.0, "only a little goes to repairs");
            // the places add up to the total
            Assert.AreEqual(Everything(sim), sim.Holdings().Sum(h => h.Kg), 1e-6);
        }

        [Test]
        public void HaulingCostsEnergy_AndIsCountedPerKgAndKm()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Iron, 200.0);
            for (int i = 0; i < 300 && sim.KgAt(tile, Material.Iron) < 200.0 - 1e-6; i++) sim.Tick(1.0);
            var totals = sim.HaulTotals;
            Assert.AreEqual(200.0, totals.KgMoved, 1e-6);
            Assert.Greater(totals.KgKm, 0.0);
            Assert.Greater(totals.Wh, 0.0, "driving loaded cost energy");
        }

        [Test]
        public void TheCargoStaysWithTheRobot_WhileItIsOnTheWay()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Iron, 300.0);
            bool sawCargo = false;
            for (int i = 0; i < 300 && !sawCargo; i++)
            {
                sim.Tick(0.25);
                sawCargo = sim.Holdings().Any(h => h.Kind == KardashevAscent.Simulation.Logistics.PlaceKind.Cargo && h.Kg > 0.0);
            }
            Assert.IsTrue(sawCargo, "a robot carried the iron: it was in a cargo place, not in a depot");
            var loaded = sim.HaulOrders().First(o => o.Loaded);
            Assert.GreaterOrEqual(loaded.RobotId, 0);
        }

        [Test]
        public void ARobotThatIsGone_ReturnsItsLoadToTheDepotItCameFrom()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Iron, 300.0);
            double iron = Everything(sim);
            for (int i = 0; i < 300 && !sim.HaulOrders().Any(o => o.Loaded); i++) sim.Tick(0.25);
            var order = sim.HaulOrders().First(o => o.Loaded);
            int robot = order.RobotId;
            var state = sim.CaptureState();
            state.Workforce.Robots.RemoveAll(r => r.Id == robot);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            other.Tick(0.25);
            Assert.AreEqual(iron, Everything(other), 1e-6, "nothing was lost with the robot");
            Assert.IsFalse(other.HaulOrders().Any(o => o.Id == order.Id && o.Loaded), "the haul was dropped");
        }

        /// <summary>A far order that no robot can start does not count as on the way (decided 2026-11-27): the planner supplies the site from a nearer depot, and the stale order is kept, then cut when the delivery arrives.</summary>
        [Test]
        public void AStaleFarOrderNoRobotCanStart_DoesNotStarveASiteThatANearDepotCanSupply()
        {
            var sim = Make();
            int near = LandNeighbour(sim);
            int far = Enumerable.Range(0, Sphere.TileCount).Where(t => !sim.GetTile(t).IsWater && t != sim.BaseTileId).OrderByDescending(t => Sphere.Centers[t].X * Sphere.Centers[sim.BaseTileId].X * -1.0 + Sphere.Centers[t].Y * Sphere.Centers[sim.BaseTileId].Y * -1.0 + Sphere.Centers[t].Z * Sphere.Centers[sim.BaseTileId].Z * -1.0).First();
            sim.PutMaterials(far, Material.Iron, 300.0);
            sim.SetStockTarget(near, Material.Iron, 300.0);
            var state = sim.CaptureState();
            state.HaulOrders.Add(new HaulOrderRecord { Id = 900, FromTile = far, ToTile = near, Material = Material.Iron, Kg = 300.0, Stage = 0, RobotId = -1 });
            var other = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, ChemistryEnabled = false };
            Assert.IsTrue(other.RestoreState(state));
            for (int i = 0; i < 400 && other.KgAt(near, Material.Iron) < 300.0 - 1e-6; i++) other.Tick(1.0);
            Assert.AreEqual(300.0, other.KgAt(near, Material.Iron), 1e-6, "the nearer depot supplied it within 400 hours");
        }

        [Test]
        public void SaveAndLoadInTheMiddleOfAHaul_FinishesIt()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Iron, 300.0);
            for (int i = 0; i < 300 && !sim.HaulOrders().Any(o => o.Loaded); i++) sim.Tick(0.25);
            Assert.IsTrue(sim.HaulOrders().Any(o => o.Loaded));
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(sim.CaptureState()));
            Assert.AreEqual(sim.StockTarget(tile, Material.Iron), other.StockTarget(tile, Material.Iron), 1e-12);
            Assert.AreEqual(sim.HaulOrders().Count, other.HaulOrders().Count);
            for (int i = 0; i < 400 && other.KgAt(tile, Material.Iron) < 300.0 - 1e-6; i++) other.Tick(1.0);
            Assert.AreEqual(300.0, other.KgAt(tile, Material.Iron), 1e-6);
        }

        [Test]
        public void Determinism_TwoRunsGiveTheSameHauls()
        {
            string Run()
            {
                var sim = Make();
                int tile = LandNeighbour(sim);
                sim.SetStockTarget(tile, Material.Iron, 400.0);
                sim.SetStockTarget(LandNeighbour(sim, 1), Material.Rock, 300.0);
                for (int i = 0; i < 200; i++) sim.Tick(1.0);
                var s = sim.CaptureState();
                return string.Join(";", s.Inventories.Select(r => $"{r.Kind}{r.Id}:" + string.Join(",", r.Stock.Select(m => $"{m.Material}={m.Kg:R}"))))
                       + "|" + string.Join(",", s.HaulOrders.Select(o => $"{o.Id}:{o.Stage}:{o.Kg:R}")) + "|" + s.HaulWh.ToString("R");
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void WithLogisticsOff_NoHaulsAreMade()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Iron, 300.0);
            for (int i = 0; i < 100; i++) sim.Tick(1.0);
            Assert.IsEmpty(sim.HaulOrders());
            Assert.AreEqual(0.0, sim.HaulTotals.KgMoved, 0.0);
        }

        [Test]
        public void ATransportRobot_CarriesMoreThanAGeneralOne()
        {
            Assert.Greater(RobotCatalog.Profile(RobotKind.Transport)[RobotAttribute.TransportCapacity],
                           RobotCatalog.Profile(RobotKind.General)[RobotAttribute.TransportCapacity]);
            Assert.AreEqual(1.0, RobotCatalog.Efficiency(RobotKind.Transport, TaskKind.Haul, 0) / RobotCatalog.Profile(RobotKind.Transport)[RobotAttribute.Speed], 1e-12);
            Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Research, TaskKind.Haul, 10), "researchers do not haul");
        }
    }
}

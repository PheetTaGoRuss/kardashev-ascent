using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Weather-aware planning (P2): a forecast storm on the route holds a haul; go anyway releases it; the low temperature the leg budget uses is the pessimistic forecast.</summary>
    public class ForecastPlanningTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Earth => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);

        static PlanetSimulation Colony(int seed = 0)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earth, Kardashev.EarthRadiusM) { ConstructionTimeEnabled = false, LogisticsEnabled = true, Clouds = CloudModel.EarthClimatology, TerrainSeed = 7 + seed * 101 };
            sim.Workforce.Reset(sim.BaseTileId, 0);
            sim.Debug.UnlockAll = true; sim.Debug.FreeBuilding = true;
            for (int sl = 0, placed = 0; sl < sim.SlotCount(sim.BaseTileId) && placed < 40; sl++) if (sim.PlacePanel(sim.BaseTileId, sl, 0.35, Math.PI)) placed++;
            for (int sl = 0; sl < sim.SlotCount(sim.BaseTileId); sl++) if (sim.PlaceChargingStation(sim.BaseTileId, sl)) break;
            sim.Debug.FreeBuilding = false;
            sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.Transport, sim.BaseTileId);
            return sim;
        }

        static int NearTarget(PlanetSimulation sim) =>
            Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && t != sim.BaseTileId && sim.Routes.HoursBetween(sim.BaseTileId, t) > 1.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 5.0)
                .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).First();

        [Test]
        public void AStormOverTheBase_HoldsAHaulWithItsReason_AndGoAnywayReleasesIt()
        {
            var sim = Colony();
            for (int i = 0; i < 24 * 365 * 4 && !sim.StormAt(sim.BaseTileId); i++) sim.Tick(1.0);
            Assert.IsTrue(sim.StormAt(sim.BaseTileId), "a storm comes");
            int target = NearTarget(sim);
            sim.SetStockTarget(target, Material.Iron, 300.0);
            sim.Tick(0.05); sim.Tick(0.05);
            var order = sim.HaulOrders().FirstOrDefault(o => o.To == target);
            Assert.AreNotEqual(0, order.Id, "a haul was planned");
            Assert.IsTrue(sim.HaulWaitsForStorm(order.Id), "blocked: " + sim.HaulBlockedReason(order.Id) + " storm now " + sim.StormAt(sim.BaseTileId) + " from " + order.From + " base " + sim.BaseTileId + " fc " + sim.StormForecastOnRoute(order.From, order.To, 8.0));
            StringAssert.Contains("storm is forecast", sim.HaulBlockedReason(order.Id));
            var why = sim.HaulWhyOf(order.Id);
            Assert.AreEqual((int)PlanetSimulation.HaulWhyKind.StormForecast, why.Kind);
            Assert.IsTrue(sim.GoAnywayHaul(order.Id));
            sim.Tick(0.05);
            Assert.IsFalse(sim.HaulWaitsForStorm(order.Id), "the weather wait is overridden");
            Assert.IsTrue(sim.HaulGoesAnyway(order.Id));
            // and it is saved
            var again = Colony();
            again.RestoreState(sim.CaptureState());
            Assert.IsTrue(again.HaulGoesAnyway(order.Id));
        }

        [Test]
        public void AHaulOnACalmDay_IsNotHeld()
        {
            var sim = Colony(1);
            for (int i = 0; i < 24 * 365 * 2 && (sim.StormAt(sim.BaseTileId) || sim.StormForecastOnRoute(sim.BaseTileId, NearTarget(sim), 12.0).HasValue); i++) sim.Tick(1.0);
            int target = NearTarget(sim);
            sim.SetStockTarget(target, Material.Iron, 300.0);
            sim.Tick(0.05); sim.Tick(0.05);
            var order = sim.HaulOrders().First(o => o.To == target);
            Assert.IsFalse(sim.HaulWaitsForStorm(order.Id));
        }

        [Test]
        public void P3_AdviceIsWorkedAtTheWinterColdCapacity_AndLeavesTheNormalPlannerAlone()
        {
            double wantLat = 60.0 * Math.PI / 180.0;
            int baseTile = Enumerable.Range(0, Sphere.TileCount).Where(t => !Surface.Terrain[t].ToString().Contains("Ocean")).OrderBy(t => Math.Abs(Sphere.Latitude(t) - wantLat)).First();
            var sim = new PlanetSimulation(Sphere, Surface, Earth, Kardashev.EarthRadiusM, baseTile) { ConstructionTimeEnabled = false, LogisticsEnabled = true, Clouds = CloudModel.EarthClimatology };
            sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.Transport, sim.BaseTileId);
            for (int h = 0; h < 24 * 365 && Math.Abs(((sim.SolarLongitudeDegAt(sim.TimeHours) - 90.0 + 540.0) % 360.0) - 180.0) > 6.0; h++) sim.Tick(1.0);   // midsummer (Ls 90 in the north)
            double before = sim.LegBudgetWh;
            Assert.Less(sim.WinterColdShare(), 1.0, "the coming winter at 60 degrees is cold enough to cut the battery");
            sim.DepotSuggestions(3);
            Assert.AreEqual(before, sim.LegBudgetWh, 1e-9, "the winter cap is only for the advice");
        }

        [Test]
        public void P3_DustSeasonOnAThinAirWorld_CutsTheExpectedSolarShare()
        {
            var mars = new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);
            var sim = new PlanetSimulation(Sphere, Surface, mars, Kardashev.EarthRadiusM);
            double s = sim.ExpectedSolarShareInDust(), g = sim.ExpectedSolarShareInDust(true);
            Assert.Less(s, 1.0); Assert.Less(g, s); Assert.Less(g, 0.01);
            Assert.AreEqual(1.0, Colony().ExpectedSolarShareInDust());
        }
    }
}

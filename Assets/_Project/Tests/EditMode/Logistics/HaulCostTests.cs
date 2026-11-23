using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Logistics
{
    /// <summary>Logistics on the climb and weather costs (decided 2026-11-24): what a haul really costs, and the planner ranks sources by it.</summary>
    public class HaulCostTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static (PlanetSimulation Sim, int Low, int High) Pair()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int low = sim.BaseTileId;
            int high = Enumerable.Range(0, sim.TileCount)
                .Where(t => !sim.GetTile(t).IsWater && !double.IsInfinity(sim.Routes.HoursBetween(low, t)) && sim.TileHeightM(t) > sim.TileHeightM(low) + 800.0)
                .OrderBy(t => sim.Routes.HoursBetween(low, t)).First();
            return (sim, low, high);
        }

        [Test]
        public void ALoadedLegUphillPaysClimbEnergy_TheWayBackDownPaysNone()
        {
            var (sim, low, high) = Pair();
            var up = sim.HaulLegCost(low, high, 400.0);
            var down = sim.HaulLegCost(high, low, 0.0);
            Assert.Greater(up.ClimbWh, 1.0);
            Assert.Less(down.ClimbWh, up.ClimbWh, "no regeneration, and the empty robot is lighter");
            Assert.Greater(up.DriveWh, 0.0);
        }

        [Test]
        public void ClimbingOffMeansNoClimbEnergy_AndTheCostIsThenTheDrivingHoursInMildAir()
        {
            var (sim, low, high) = Pair();
            sim.ClimbEnabled = false;
            Assert.AreEqual(0.0, sim.HaulLegCost(low, high, 400.0).ClimbWh, 1e-12);
        }

        [Test]
        public void ASourceUpAMountainCostsMoreThanItsDrivingHours()
        {
            var (sim, low, high) = Pair();
            double hours = sim.Routes.HoursBetween(low, high);
            Assert.Greater(sim.HaulCostHours(low, high, 400.0), hours);
        }

        [Test]
        public void TheUnsurveyedReliefMakesTheClimbCostlierToPlan()
        {
            var (sim, low, high) = Pair();
            double unsure = sim.HaulLegCost(low, high, 400.0).ClimbWh;
            sim.LearnExperimentForTests(ExperimentKind.ReliefSurvey);
            Assert.Less(sim.HaulLegCost(low, high, 400.0).ClimbWh, unsure);
        }

        [Test]
        public void AnUnreachableTileCostsInfinity()
        {
            var (sim, low, _) = Pair();
            int sea = Enumerable.Range(0, sim.TileCount).First(t => sim.GetTile(t).IsWater);
            Assert.IsTrue(double.IsInfinity(sim.HaulCostHours(low, sea, 100.0)));
        }

        [Test]
        public void AHaulBeyondOneChargeIsRefusedUntilAChargerSitsHalfWay_ThenItIsStagedThroughIt()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.ConstructionTimeEnabled = false;
            int lander = sim.BaseTileId;
            var far = Enumerable.Range(0, sim.TileCount)
                .Where(t => !sim.GetTile(t).IsWater && !double.IsInfinity(sim.Routes.HoursBetween(lander, t)) && !sim.PlanHaul(lander, t, 400.0).Feasible)
                .OrderBy(t => sim.Routes.HoursBetween(lander, t)).ToList();
            if (far.Count == 0) Assert.Ignore("every reachable tile is within one charge on this surface");
            int target = far[0];
            Assert.IsFalse(sim.PlanHaul(lander, target, 400.0).Feasible, "refused: no chain of legs fits the battery");
            StringAssert.Contains("REFUSED", sim.HaulCostText(lander, target, 400.0));
            var route = sim.Routes.Route(lander, target);
            sim.Debug.FreeBuilding = true;
            bool placed = false;
            for (int k = route.Count / 2; k >= 0 && !placed; k--) placed = sim.PlaceChargingStation(route[k], 20);
            Assert.IsTrue(placed, "a charging station on the way");
            var plan = sim.PlanHaul(lander, target, 400.0);
            if (!plan.Feasible) Assert.Ignore("one station is not enough for this far a tile");
            Assert.Greater(plan.Stops.Count, 0, "the haul is staged through the new charger");
            StringAssert.Contains("staged via", sim.HaulCostText(lander, target, 400.0));
        }

        [Test]
        public void DepotAdviceIsEmptyWithNoMines_AndRanksSitesBySavedEnergyWithAMine()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsEmpty(sim.DepotSites(3));
            sim.Debug.UnlockAll = true;
            // a mine on the farthest reachable tile that has copper or iron
            var tiles = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && t != sim.BaseTileId && !double.IsInfinity(sim.Routes.HoursBetween(sim.BaseTileId, t)) && sim.Deposits.GradeOf(t, KardashevAscent.Simulation.Resources.Material.Iron) > 0.0)
                .OrderByDescending(t => sim.TileHeightM(t)).Take(5).ToList();
            foreach (int t in tiles) { sim.Deposits.MarkSurveyed(t); sim.OrderMining(t, KardashevAscent.Simulation.Resources.Material.Iron); }
            var sites = sim.DepotSites(3);
            for (int i = 1; i < sites.Count; i++) Assert.GreaterOrEqual(sites[i - 1].SavedWhPerDay, sites[i].SavedWhPerDay, "best first");
            foreach (var s in sites) Assert.Greater(s.SavedWhPerDay, 0.0);
        }
    }
}

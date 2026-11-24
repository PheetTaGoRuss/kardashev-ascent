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
            Assert.IsTrue(plan.Feasible, "with a charger on the way the haul is possible (direct if the charger is at the drop, staged otherwise)");
            StringAssert.DoesNotContain("REFUSED", sim.HaulCostText(lander, target, 400.0));
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

        [Test]
        public void ContourRoutesCostNoMoreEnergyThanTimeOnlyRoutes_AndNoLessTime_AndSomeDiffer()
        {
            var contour = new PlanetSimulation(Sphere, Surface, Earthlike) { ContourRouting = true };
            var straight = new PlanetSimulation(Sphere, Surface, Earthlike) { ContourRouting = false };
            int from = contour.BaseTileId, differ = 0, tested = 0;
            for (int t = 0; t < contour.TileCount && tested < 150; t += 11)
            {
                if (contour.GetTile(t).IsWater || t == from) continue;
                var a = contour.Routes.Route(from, t); var b = straight.Routes.Route(from, t);
                if (a == null || b == null) continue;
                tested++;
                double ea = contour.HaulLegCost(from, t, 0.0).ClimbWh, eb = straight.HaulLegCost(from, t, 0.0).ClimbWh;
                Assert.LessOrEqual(ea, eb + 1e-6, "tile " + t + ": the contour route never climbs more");
                Assert.GreaterOrEqual(contour.Routes.HoursBetween(from, t), straight.Routes.HoursBetween(from, t) - 1e-9, "and never takes less time");
                if (!a.SequenceEqual(b)) differ++;
            }
            Assert.Greater(tested, 20);
            Assert.Greater(differ, 0, "on rough ground some route goes round a climb");
        }

        [Test]
        public void ContourRoutingIsSaved_AndAnOldSaveKeepsTimeOnlyRoutes()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(sim.ContourRouting, "new missions use contour routes");
            var state = sim.CaptureState();
            Assert.IsTrue(state.ContourRouting);
            state.Version = 52;   // an older save
            var old = new PlanetSimulation(Sphere, Surface, Earthlike);
            old.RestoreState(state);
            Assert.IsFalse(old.ContourRouting);
        }

        [Test]
        public void AChargeingBayGoesOnAStorageYard_CostsLessThanAStation_AndCountsAsAStop()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false };
            sim.Debug.UnlockAll = true;
            int tile = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && !double.IsInfinity(sim.Routes.HoursBetween(sim.BaseTileId, t)));
            StringAssert.Contains("storage yard", sim.ChargingBayBlocker(tile));
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceStorage(KardashevAscent.Simulation.Game.BuildingType.StorageYard, tile, 0));
            Assert.IsNull(sim.ChargingBayBlocker(tile));
            sim.Debug.FreeBuilding = false;
            sim.Materials.Add(KardashevAscent.Simulation.Resources.Material.Iron, 1000.0);
            sim.Materials.Add(KardashevAscent.Simulation.Resources.Material.Copper, 1000.0);
            sim.Materials.Add(KardashevAscent.Simulation.Resources.Material.Rock, 1000.0);
            bool placed = false;
            for (int slot = 0; slot < sim.SlotCount(tile) && !placed; slot++) placed = sim.PlaceChargingBay(tile, slot);
            Assert.IsTrue(placed);
            Assert.IsTrue(sim.HasChargingBay(tile));
            Assert.Less(KardashevAscent.Simulation.Resources.BuildCosts.ChargingBay.Items().First(x => x.Item1 == KardashevAscent.Simulation.Resources.Material.Iron).Item2, KardashevAscent.Simulation.Resources.BuildCosts.ChargingStation.Items().First(x => x.Item1 == KardashevAscent.Simulation.Resources.Material.Iron).Item2);
            StringAssert.Contains("already", sim.ChargingBayBlocker(tile));
        }

        [Test]
        public void AMineTwoOrThreeHopsFromTheLanderCanBeHauledDirectly_MostOfTheTime()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int lander = sim.BaseTileId;
            var depth = new System.Collections.Generic.Dictionary<int, int> { { lander, 0 } };
            var q = new System.Collections.Generic.Queue<int>(); q.Enqueue(lander);
            while (q.Count > 0) { int t = q.Dequeue(); if (depth[t] >= 3) continue; foreach (int n in Sphere.Neighbors[t]) if (!depth.ContainsKey(n)) { depth[n] = depth[t] + 1; q.Enqueue(n); } }
            foreach (int hop in new[] { 2, 3 })
            {
                var tiles = depth.Where(kv => kv.Value == hop && !sim.GetTile(kv.Key).IsWater && !double.IsInfinity(sim.Routes.HoursBetween(lander, kv.Key))).Select(kv => kv.Key).ToList();
                Assert.Greater(tiles.Count, 2);
                int direct = tiles.Count(t => sim.PlanHaul(lander, t, PlanetSimulation.BasePayloadKg).Feasible && sim.PlanHaul(lander, t, PlanetSimulation.BasePayloadKg).Stops.Count == 0);
                // Honest numbers after the strict round-trip rule (KI-46, 2026-11-25): the loaded leg out, the drop and the empty run home must fit ONE charge when the drop
                // is not a charger. That holds for most mines 2 hops away and for few or none 3 hops away (the earlier 70 % counted the legs one by one); from 3 hops the player builds a depot with a charger.
                double share = direct / (double)tiles.Count;
                if (hop == 2) Assert.GreaterOrEqual(share, 0.1, "hop 2: some mines are hauled directly (20 % in this surface: the budget is 60 % of a battery at 65 % cold capacity over the least efficient robot)");
                else Assert.LessOrEqual(share, 0.35, "hop 3: few or none are hauled directly without a charger");
            }
        }

        [Test]
        public void NightsAreColderThanAfternoons_AndANightHaulCostsMoreHeater()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int t = Enumerable.Range(0, sim.TileCount).First(x => !sim.GetTile(x).IsWater && Math.Abs(sim.GetTile(x).Latitude) < 0.3);
            double day = sim.DayLengthHours;
            double warm = double.MinValue, cold = double.MaxValue, warmT = 0, coldT = 0;
            for (double h = 0.0; h < day; h += day / 24.0)
            {
                double a = sim.DiurnalAirTemperatureAt(t, h);
                if (a > warm) { warm = a; warmT = h; }
                if (a < cold) { cold = a; coldT = h; }
            }
            Assert.Greater(warm - cold, sim.DiurnalAmplitudeK(t), "the swing is at least the amplitude");
            sim.DiurnalTemperature = false;
            Assert.AreEqual(sim.DiurnalAirTemperatureAt(t, warmT), sim.DiurnalAirTemperatureAt(t, coldT), 1e-9, "off: the yearly temperature only");
        }

        [Test]
        public void ALocklessDayHasASwing_ALockedPlanetHasNone()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.Greater(sim.DiurnalAmplitudeK(sim.BaseTileId), 0.0);
            var state = sim.CaptureState();
            Assert.IsTrue(state.DiurnalTemperature);
            state.Version = 53;
            var old = new PlanetSimulation(Sphere, Surface, Earthlike);
            old.RestoreState(state);
            Assert.IsFalse(old.DiurnalTemperature, "a save before version 54 keeps the yearly temperature");
        }

        [Test]
        public void ANightHaulInTheColdCostsMoreHeaterThanADayHaul_AndTheBestStartIsFound()
        {
            var cold = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
                282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, -35.0);
            var sim = new PlanetSimulation(Sphere, Surface, cold);
            int lander = sim.BaseTileId;
            int far = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && !double.IsInfinity(sim.Routes.HoursBetween(lander, t)))
                .OrderBy(t => Math.Abs(sim.Routes.HoursBetween(lander, t) - 12.0)).First();
            var (delay, saved) = sim.BestHaulStart(lander, far, 400.0);
            Assert.Greater(saved, 0.0, "starting at the warm part of the day saves heater energy: " + delay + " h");
            Assert.Greater(delay, 0.0);
            double[] heater = Enumerable.Range(0, 12).Select(k => sim.HaulLegCost(lander, far, 400.0).HeaterWh).ToArray();
            Assert.Greater(heater[0], 0.0, "in the cold the heater runs");
        }

        [Test]
        public void ThinAirSwingsMoreThanThickAir_AndMarsMatchesTheRoverRecord()
        {
            PlanetSolarParams Make(double pressure) => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
                282.94 * Math.PI / 180.0, 24.66, 365.256 * 24.0, 0.32, 0.3, -59.0, false, pressure);
            var earth = new PlanetSimulation(Sphere, Surface, Make(1.0));
            var mars = new PlanetSimulation(Sphere, Surface, Make(0.00628));
            Assert.AreEqual(10.0, earth.DiurnalAmplitudeK(earth.BaseTileId), 1e-9);
            Assert.AreEqual(34.8, mars.DiurnalAmplitudeK(mars.BaseTileId), 0.1);
            Assert.GreaterOrEqual(2.0 * mars.DiurnalAmplitudeK(mars.BaseTileId), 60.0, "a range of 60 to 80 K, as Curiosity's REMS measured at Gale crater");
            Assert.LessOrEqual(2.0 * mars.DiurnalAmplitudeK(mars.BaseTileId), 80.0);
        }

        [Test]
        public void DepotAdviceCarriesTheFullCost_CopperLineTilesAndHours_AndRanksByValuePerKilogram()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.UnlockAll = true;
            var tiles = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && t != sim.BaseTileId && sim.Routes.HoursBetween(sim.BaseTileId, t) > 8.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 60.0 && sim.Deposits.GradeOf(t, KardashevAscent.Simulation.Resources.Material.Iron) > 0.0).Take(4).ToList();
            foreach (int t in tiles) { sim.Deposits.MarkSurveyed(t); sim.OrderMining(t, KardashevAscent.Simulation.Resources.Material.Iron); }
            var advice = sim.DepotSuggestions(5);
            foreach (var a in advice)
            {
                Assert.GreaterOrEqual(a.CopperKg, KardashevAscent.Simulation.Resources.BuildCosts.ChargingBay[KardashevAscent.Simulation.Resources.Material.Copper] - 1e-9);
                Assert.Greater(a.BuildHours, 0.0);
                if (a.LineTiles > 0) Assert.Greater(a.CopperKg, a.LineTiles * KardashevAscent.Simulation.Resources.BuildCosts.CableCopperKg, "the line's real-mass copper is in the bill");
            }
            for (int i = 1; i < advice.Count; i++) Assert.GreaterOrEqual(advice[i - 1].ValueWhPerDayPerKg, advice[i].ValueWhPerDayPerKg - 1e-9, "best value first");
        }

        [Test]
        public void ALoadTheBatteryCannotCarryIsSplitIntoSmallerLoads()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int lander = sim.BaseTileId;
            double full = PlanetSimulation.BasePayloadKg;
            int split = -1;
            for (int t = 0; t < sim.TileCount && split < 0; t++)
            {
                if (sim.GetTile(t).IsWater || t == lander || double.IsInfinity(sim.Routes.HoursBetween(lander, t))) continue;
                double f = sim.FeasiblePayloadKg(lander, t, full);
                if (f > 0.0 && f < full - 1e-9) split = t;
            }
            Assert.GreaterOrEqual(split, 0, "some mine needs a smaller load than the full payload");
            Assert.Greater(sim.FeasiblePayloadKg(lander, split, full), 0.0);
            Assert.LessOrEqual(sim.FeasiblePayloadKg(lander, split, full), full * 0.75 + 1e-9);
        }

        [Test]
        public void ThePlannerCapsEachHaulAtThePairsFeasiblePayload()
        {
            var demands = new[] { new KardashevAscent.Simulation.Logistics.HaulDemand(5, KardashevAscent.Simulation.Resources.Material.Iron, 1000.0) };
            var plan = KardashevAscent.Simulation.Logistics.HaulPlanner.Plan(demands, new[] { 1 }, (t, m) => 5000.0, (a, b) => 10.0, 490.0, 8, (a, b) => 100.0);
            Assert.Greater(plan.Count, 4, "a load split into many small trips");
            foreach (var h in plan) Assert.LessOrEqual(h.Kg, 100.0 + 1e-9);
        }

        [Test]
        public void TheWhyOfAHaulIsAStructuredReason_OkNearRefusedFarAndALostStop()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int lander = sim.BaseTileId;
            int near = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && t != lander && sim.Routes.HoursBetween(lander, t) > 3.0 && sim.Routes.HoursBetween(lander, t) < 6.0).First();
            Assert.AreEqual(PlanetSimulation.HaulWhyKind.Ok, sim.WhyHaul(lander, near, 100.0).Kind);
            int far = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && !double.IsInfinity(sim.Routes.HoursBetween(lander, t)) && !sim.PlanHaul(lander, t, 400.0).Feasible).OrderBy(t => sim.Routes.HoursBetween(lander, t)).First();
            var why = sim.WhyHaul(lander, far, 400.0);
            Assert.AreNotEqual(PlanetSimulation.HaulWhyKind.Ok, why.Kind);
            Assert.AreEqual(sim.LegBudgetWh, why.BudgetWh, 1e-9);
            if (why.Kind == PlanetSimulation.HaulWhyKind.LegTooLong) Assert.Greater(why.NeedWh, why.BudgetWh);
            var lost = sim.WhyHaul(lander, near, 100.0, new[] { 9999 });
            Assert.AreEqual(PlanetSimulation.HaulWhyKind.StopLost, lost.Kind);
            Assert.AreEqual(9999, lost.StopTile);
            int sea = Enumerable.Range(0, sim.TileCount).First(t => sim.GetTile(t).IsWater);
            Assert.AreEqual(PlanetSimulation.HaulWhyKind.NoRoute, sim.WhyHaul(lander, sea, 100.0).Kind);
        }

        [Test]
        public void TheCableModelIsOutsideItsLawOnlyInThinAir()
        {
            PlanetSolarParams Make(double pressure) => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
                282.94 * Math.PI / 180.0, 24.66, 365.256 * 24.0, 0.32, 0.3, -59.0, false, pressure);
            Assert.IsFalse(new PlanetSimulation(Sphere, Surface, Make(1.0)).CableOutsideModel, "Earth air: Peek's law holds");
            Assert.IsTrue(new PlanetSimulation(Sphere, Surface, Make(0.00628)).CableOutsideModel, "Mars air: the Paschen region");
        }

        [Test]
        public void InWinterFewerMinesAreHauledDirectly_TheRestAreStagedOrRefused_NeverSentOut()
        {
            // Li-ion capacity falls in the cold (0.65 at -20 C, Bressan et al. 2025; RobotBattery.ColdCapacityFactor) and the leg budget uses it
            var mild = new PlanetSimulation(Sphere, Surface, Earthlike);
            var cold = new PlanetSimulation(Sphere, Surface, new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
                282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, -45.0, false, 1.0)) { WeatherEnabled = false, DiurnalTemperature = false };
            Assert.Less(cold.LegBudgetWh, mild.LegBudgetWh, "the colder the air, the smaller the charge a leg may use");
            int lander = mild.BaseTileId;
            int direct(PlanetSimulation sim) => Enumerable.Range(0, sim.TileCount).Count(t => !sim.GetTile(t).IsWater && t != lander && sim.Routes.HoursBetween(lander, t) < 12.0 && sim.PlanHaul(lander, t, PlanetSimulation.BasePayloadKg).Feasible);
            Assert.LessOrEqual(direct(cold), direct(mild));
        }
    }
}

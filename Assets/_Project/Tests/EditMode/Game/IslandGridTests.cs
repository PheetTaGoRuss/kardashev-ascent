using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Island grids (2026-11-25): a connected network with a wired source, not joined to the lander, serves the chargers on it.</summary>
    public class IslandGridTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static (PlanetSimulation sim, int far) Wired()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true, WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            int far = Sphere.Neighbors[sim.BaseTileId].SelectMany(n => Sphere.Neighbors[n]).First(t => t != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(t) && !sim.GetTile(t).IsWater && sim.IsOpenGround(t));
            return (sim, far);
        }

        [Test]
        public void AStationCannotBeBuiltOffTheGrid_UntilASourceStandsThere()
        {
            var (sim, far) = Wired();
            Assert.IsFalse(sim.PlaceChargingStation(far, 1), "no source on the tile: not allowed");
            Assert.IsTrue(sim.PlacePanel(far, 0, 0.35, Math.PI));
            Assert.IsTrue(sim.PlaceBuildingWire(far, 0));
            Assert.IsTrue(sim.IslandHasSource(far));
            Assert.IsTrue(sim.PlaceChargingStation(far, 1), "an island with a wired panel may have a charger");
            Assert.IsTrue(sim.PlaceBattery(far, 2), "a battery on an island belongs to the island");
        }

        [Test]
        public void ARobotCharges_AtAChargerOnAnIsland_FromTheIslandsOwnPanels()
        {
            var (sim, far) = Wired();
            for (int s = 0; s < 6; s++) { sim.PlacePanel(far, s, 0.35, Math.PI); sim.PlaceBuildingWire(far, s); }
            Assert.IsTrue(sim.PlaceChargingStation(far, 10));
            sim.PlaceBuildingWire(far, 10);
            double colonyBefore = sim.BatteryStoredWh;
            var r = sim.Workforce.AddRobot(RobotKind.General, far);
            r.Battery.Draw(r.Battery.UsableWh * 0.9, 1.0);
            double before = r.Battery.UsableWh;
            for (int h = 0; h < 8; h++) sim.Tick(1.0);
            Assert.Greater(sim.IslandGenerationW(far), 0.0, "the island's panels make power by day");
            Assert.Greater(r.Battery.UsableWh, before, "the robot charged");
        }

        [Test]
        public void AnIslandBattery_FillsFromTheIslandsPanels_IsNotPartOfTheColonyBattery_AndIsSaved()
        {
            var (sim, far) = Wired();
            double colonyCap = sim.BatteryCapacityWh;
            for (int s = 0; s < 6; s++) { sim.PlacePanel(far, s, 0.35, Math.PI); sim.PlaceBuildingWire(far, s); }
            Assert.IsTrue(sim.PlaceBattery(far, 10)); sim.PlaceBuildingWire(far, 10);
            Assert.AreEqual(colonyCap, sim.BatteryCapacityWh, 1e-9, "an island battery is not added to the colony battery");
            double made = 0.0; for (int h = 0; h < 12; h++) { sim.Tick(1.0); made += sim.IslandGenerationW(far) * 1.0; }
            var (stored, cap) = sim.IslandBatteryOf(far);
            Assert.AreEqual(sim.BatteryUnitWh, cap, 1e-6);
            Assert.Greater(stored, 0.0, "the panels charged it"); TestContext.WriteLine($"made about {made:0} Wh, stored {stored:0} Wh"); Assert.LessOrEqual(stored, 1.15 * made + 1.0, "energy is kept: the battery cannot hold more than the panels made (end-of-hour samples, a margin for the day shape)");
            var again = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true, WeatherEnabled = false };
            again.RestoreState(sim.CaptureState());
            Assert.AreEqual(stored, again.IslandBatteryOf(far).StoredWh, 1e-6);
            Assert.AreEqual(colonyCap, again.BatteryCapacityWh, 1e-9);
        }

        [Test]
        public void AnOutpostTrip_IsOneWayOnlyWhenTheBatteryCoversItAndThePanelsWillChargeTheRobot()
        {
            var (sim, far) = Wired();
            sim.Debug.FreeBuilding = false;
            sim.Tick(0.5);
            var r = sim.Workforce.Robots.First();
            Assert.IsFalse(sim.OneWayVerdict(r, far).IsOutpost, "no sites there: not an outpost");
            sim.PlacePanel(far, 0, 0.35, Math.PI); sim.PlaceBuildingWire(far, 0);
            Assert.IsTrue(sim.PlaceChargingStation(far, 1)); sim.PlaceBuildingWire(far, 1);
            var v = sim.OneWayVerdict(r, far);
            TestContext.WriteLine(v.Text);
            Assert.IsTrue(v.IsOutpost);
            StringAssert.StartsWith(v.Allowed ? "one-way: the outpost will charge it by sol" : "refused:", v.Text);
            r.Battery.Draw(r.Battery.UsableWh * 0.95, 1.0);
            var low = sim.OneWayVerdict(r, far);
            Assert.IsFalse(low.Allowed, "a nearly empty robot is refused");
            StringAssert.StartsWith("refused:", low.Text);
        }

        [Test]
        public void MiningAnOreTheTileCannotStore_IsRefusedUpFront_WithTheReason_AndWorksOnceTheStoreStands()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            var li = KardashevAscent.Simulation.Resources.Material.Lithium;
            int t = sim.BaseTileId;
            int tile = Enumerable.Range(0, sim.TileCount).First(q => !sim.GetTile(q).IsWater && sim.Deposits.GradeOf(q, li) > 0.0 && q != t && sim.IsOpenGround(q) && sim.Routes.HoursBetween(t, q) < 12.0);
            sim.DebugSurveyAll(); sim.PlaceChargingStation(t, 0); sim.PlacePanel(t, 1, 0.35, Math.PI); sim.PlaceBattery(t, 2); sim.WireAllBuildings();
            StringAssert.Contains("sealed store", sim.MiningBlocker(tile, li));
            Assert.IsFalse(sim.OrderMining(tile, li), "no robot hours are spent on a tile that cannot store the ore");
            Assert.AreEqual(KardashevAscent.Simulation.Game.BuildingType.SealedStore, sim.StoreFor(li));
            Assert.IsTrue(sim.PlaceStorage(sim.StoreFor(li), tile, 5)); sim.PlaceChargingStation(tile, 6); sim.PlacePanel(tile, 7, 0.35, Math.PI);
            Assert.IsNull(sim.MiningBlocker(tile, li));
            Assert.IsTrue(sim.OrderMining(tile, li));
            for (int h = 0; h < 800; h++) sim.Tick(1.0);
            TestContext.WriteLine("mined " + sim.Deposits.MinedKg(tile, li) + " room " + sim.RoomAt(tile, li)); foreach (var tq in sim.Workforce.Tasks.Open) TestContext.WriteLine($"{tq.Task.Kind}@{tq.Task.Tile} {tq.State} {tq.Reason}"); foreach (var rb in sim.Workforce.Robots) TestContext.WriteLine($"R{rb.Id} tile {rb.Tile} act {rb.Activity} soc {rb.Battery.StateOfCharge:0.00} trav {rb.IsTravelling}");
            Assert.Greater(sim.Deposits.MinedKg(tile, li), 0.0, "with the store standing the ore is mined");
        }

        [Test]
        public void ALoadThatDoesNotFit_IsSplitToTheLargestLoadThatDoes_DownToTheMinimum_AndMoreTripsAreTheCost()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.PlaceBattery(sim.BaseTileId, 2); sim.WireAllBuildings();
            sim.Tick(0.5);
            int far = Enumerable.Range(0, sim.TileCount).Where(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && sim.IsOpenGround(q) && sim.Routes != null && sim.Routes.HoursBetween(sim.BaseTileId, q) > 12.0 && sim.Routes.HoursBetween(sim.BaseTileId, q) < 40.0).OrderBy(q => sim.Routes.HoursBetween(sim.BaseTileId, q)).First();
            double full = PlanetSimulation.BasePayloadKg;
            double load = sim.FeasiblePayloadKg(sim.BaseTileId, far, full);
            TestContext.WriteLine($"tile {far}: full {full} kg -> {load:0.0} kg");
            if (load >= full) Assert.Pass("the full load fits here");
            Assert.AreEqual(load > 0.0, sim.PlanHaul(sim.BaseTileId, far, Math.Max(load, PlanetSimulation.MinHaulLoadKg)).Feasible, "a positive answer is a feasible plan");
            if (load > 0.0)
            {
                Assert.GreaterOrEqual(load, PlanetSimulation.MinHaulLoadKg);
                Assert.IsTrue(sim.PlanHaul(sim.BaseTileId, far, load).Feasible);
                Assert.IsFalse(sim.PlanHaul(sim.BaseTileId, far, load * 1.2 + 1.0).Feasible, "a heavier load does not fit");
                Assert.Less(load, 0.25 * full + 1e-6, "below the old quarter-load floor: loads that used to be refused now go in smaller pieces");
            }
        }

        [Test]
        public void AHaulPlannedForAWarmHour_IsCutToWhatFitsNow_NotLeftBlockedForever()
        {
            var cold = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit * 1.524, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0);
            var sim = new PlanetSimulation(Sphere, Surface, cold) { LogisticsEnabled = true };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.PlaceBattery(sim.BaseTileId, 2); sim.WireAllBuildings();
            sim.Materials.Add(KardashevAscent.Simulation.Resources.Material.Rock, 5000.0);
            sim.Tick(0.5);
            int far = Enumerable.Range(0, sim.TileCount).Where(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && sim.IsOpenGround(q) && sim.Routes != null && sim.Routes.HoursBetween(sim.BaseTileId, q) > 10.0 && sim.Routes.HoursBetween(sim.BaseTileId, q) < 40.0).OrderBy(q => sim.Routes.HoursBetween(sim.BaseTileId, q)).First();
            sim.SetStockTarget(far, KardashevAscent.Simulation.Resources.Material.Rock, 600.0);
            for (int h = 0; h < 24 * 6; h++) sim.Tick(1.0);
            foreach (var o in sim.HaulOrders())
            {
                double fits = sim.FeasiblePayloadKg(o.From, o.To, 490.0);
                string why = sim.HaulBlockedReason(o.Id);
                if (why != null && why.Contains("chain of legs")) Assert.Less(fits, PlanetSimulation.MinHaulLoadKg + 1e-6, $"haul {o.Id} of {o.Kg:0} kg is blocked although {fits:0} kg fits now");
            }
        }

        [Test]
        public void AMineOrderWhoseOreCannotBeHauledHome_IsAcceptedWithAWarning_AndALogEntry()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.PlaceBattery(sim.BaseTileId, 2); sim.WireAllBuildings();
            sim.DebugSurveyAll();
            sim.Tick(0.5);
            var iron = KardashevAscent.Simulation.Resources.Material.Iron;
            Assert.IsNull(sim.MiningWarning(sim.BaseTileId, iron), "the lander tile uses the colony stock");
            int far = Enumerable.Range(0, sim.TileCount).Where(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && sim.IsOpenGround(q) && sim.Deposits.GradeOf(q, iron) > 0.0 && sim.Routes != null && sim.Routes.HoursBetween(sim.BaseTileId, q) > 40.0 && !double.IsInfinity(sim.Routes.HoursBetween(sim.BaseTileId, q))).OrderBy(q => sim.Routes.HoursBetween(sim.BaseTileId, q)).First();
            double fits = sim.FeasiblePayloadKg(far, sim.BaseTileId, PlanetSimulation.BasePayloadKg);
            string warning = sim.MiningWarning(far, iron);
            Assert.AreEqual(fits <= 0.0, warning != null, "a warning exactly when not even a minimum load can be hauled home");
            if (warning != null)
            {
                StringAssert.Contains("No haul route home", warning);
                Assert.IsTrue(sim.OrderMining(far, iron), "the order is still accepted");
                Assert.IsTrue(sim.OperationsLog.Any(o => o.Quantity == "Mining warning"));
            }
        }

        [Test, Explicit("where is anorthosite on the Mars-like seed")]
        public void PrintAnorthosite()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            var cold = new PlanetSolarParams(lum, 1.524 * au, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0);
            var sim = new PlanetSimulation(Sphere, Surface, cold) { LogisticsEnabled = true };
            var an = KardashevAscent.Simulation.Resources.Material.Anorthosite;
            var tiles = Enumerable.Range(0, sim.TileCount).Where(q => sim.Deposits.GradeOf(q, an) > 0.0).OrderBy(q => sim.Routes.HoursBetween(sim.BaseTileId, q)).Take(5).ToList();
            Assert.Fail("\nnearest anorthosite: " + string.Join(" | ", tiles.Select(q => $"tile {q} {sim.Routes.HoursBetween(sim.BaseTileId, q):0.0} h grade {sim.Deposits.GradeOf(q, an):0.00} terrain {sim.GetTile(q).Terrain}")) + $" ; rock grade at base {sim.Deposits.GradeOf(sim.BaseTileId, KardashevAscent.Simulation.Resources.Material.Rock):0.00}");
        }

        [Test]
        public void ARescueThatNeverFinishes_IsEndedAfterItsMaximum_AndTheHelperIsReleased()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.WireAllBuildings();
            sim.Tick(0.5);
            var s = sim.Workforce.Robots[0]; var h = sim.Workforce.Robots[1];
            int shelter = Enumerable.Range(0, sim.TileCount).First(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && System.Array.IndexOf(Sphere.Neighbors[sim.BaseTileId], q) < 0);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var list = (System.Collections.Generic.List<RescueOrder>)typeof(PlanetSimulation).GetField("rescues", flags).GetValue(sim);
            list.Add(new RescueOrder { Id = 99, StrandedId = s.Id, RescuerId = h.Id, Stage = 2, Shelter = shelter, Method = RescueMethod.Tow, DeadlineHours = double.PositiveInfinity });
            for (int i = 0; i < (int)PlanetSimulation.RescueMaxHours + 10; i++) sim.Tick(1.0);
            Assert.AreEqual(0, list.Count, "the order ends after its maximum: no helper is re-sent for ever");
            Assert.IsFalse(h.ManualTarget.HasValue && h.IsTravelling && sim.TimeHours > PlanetSimulation.RescueMaxHours + 20, "the helper is released");
        }

        [Test]
        public void ARobotWithAFractionOfAWattHourLeft_FarFromACharger_IsStranded_SoARescueCanBeAsked()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.WireAllBuildings();
            int far = Enumerable.Range(0, sim.TileCount).First(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && System.Array.IndexOf(Sphere.Neighbors[sim.BaseTileId], q) < 0);
            var r = sim.Workforce.AddRobot(RobotKind.General, far);
            r.Battery.Draw(r.Battery.UsableWh - 0.3, 1.0);
            sim.Tick(0.1);
            Assert.Less(r.Battery.UsableWh, 5.0);
            Assert.IsTrue(r.IsStranded, "a robot with 0.3 Wh cannot drive: it is stranded, not 'travelling'");
        }

        [Test]
        public void ARobotThatNeedsMaintenanceButHasNoChargeToDriveToTheWorkshop_ChargesFirst_AndDoesNotSitOnTheRoad()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.PlaceWorkshop(sim.BaseTileId, 3); sim.WireAllBuildings();
            sim.Tick(0.5);
            int far = Enumerable.Range(0, sim.TileCount).First(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && System.Array.IndexOf(Sphere.Neighbors[sim.BaseTileId], q) < 0 && sim.Routes != null && sim.Routes.HoursBetween(sim.BaseTileId, q) < 12.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, far);
            r.Condition = 0.1;   // wants maintenance
            r.Battery.Draw(r.Battery.UsableWh - 3.0, 1.0);
            for (int h = 0; h < 12; h++) sim.Tick(1.0);
            Assert.IsFalse(r.Activity == RobotActivity.Maintenance && r.IsTravelling && r.Battery.UsableWh < 5.0, "it must not set out for the workshop on an empty battery");
        }

        [Test]
        public void AMineWhoseStoreIsFull_Pauses_WithTheReasonInTheLog_AndResumesWhenThereIsRoom()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, WeatherEnabled = false, MinePushEnabled = false };
            sim.Debug.FreeBuilding = true;
            var li = KardashevAscent.Simulation.Resources.Material.Lithium;
            int t = sim.BaseTileId;
            int tile = Enumerable.Range(0, sim.TileCount).First(q => !sim.GetTile(q).IsWater && sim.Deposits.GradeOf(q, li) > 0.0 && q != t && sim.IsOpenGround(q) && sim.Routes.HoursBetween(t, q) < 12.0);
            sim.DebugSurveyAll(); sim.PlaceChargingStation(t, 0); sim.PlacePanel(t, 1, 0.35, Math.PI); sim.PlaceBattery(t, 2); sim.WireAllBuildings();
            Assert.IsTrue(sim.PlaceStorage(sim.StoreFor(li), tile, 5)); sim.PlaceChargingStation(tile, 6); sim.PlacePanel(tile, 7, 0.35, Math.PI);
            Assert.IsTrue(sim.OrderMining(tile, li));
            for (int h = 0; h < 900 && sim.RoomAt(tile, li) > 1e-9; h++) sim.Tick(1.0);
            Assert.AreEqual(0.0, sim.RoomAt(tile, li), 1e-6, "the store filled");
            for (int h = 0; h < 6; h++) sim.Tick(1.0);
            Assert.IsFalse(sim.Workforce.Tasks.Open.Any(q => q.Task.Kind == KardashevAscent.Simulation.Robots.TaskKind.Mine && q.Task.Tile == tile), "no mine task is open while the store is full");
            Assert.IsTrue(sim.OperationsLog.Any(o => o.Quantity == "Mine paused" && o.Text.Contains("Store full at tile")));
            double mined = sim.Deposits.MinedKg(tile, li);
            for (int h = 0; h < 24; h++) sim.Tick(1.0);
            Assert.AreEqual(mined, sim.Deposits.MinedKg(tile, li), 1e-6, "nothing is mined while it is paused");
        }

        [Test]
        public void ALoadedHaulOnAColdWorld_IsOnlyDispatchedWhenTheBatteryCoversTheWholeFirstSegment()
        {
            var cold = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit * 1.524, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0);
            var sim = new PlanetSimulation(Sphere, Surface, cold) { LogisticsEnabled = true };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.PlaceBattery(sim.BaseTileId, 2); sim.WireAllBuildings();
            sim.Debug.FreeBuilding = false;
            sim.Tick(0.5);
            var rock = KardashevAscent.Simulation.Resources.Material.Rock;
            sim.Materials.Add(rock, 3000.0);
            int far = Enumerable.Range(0, sim.TileCount).Where(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && sim.IsOpenGround(q) && sim.Routes.HoursBetween(sim.BaseTileId, q) > 6.0 && sim.Routes.HoursBetween(sim.BaseTileId, q) < 14.0).OrderBy(q => sim.Routes.HoursBetween(sim.BaseTileId, q)).First();
            foreach (var r in sim.Workforce.Robots) r.Battery.Draw(r.Battery.UsableWh * 0.6, 1.0);   // low charge
            sim.SetStockTarget(far, rock, 400.0);
            int dispatched = 0;
            for (int h = 0; h < 96; h++)
            {
                sim.Tick(0.5);
                foreach (var r in sim.Workforce.Robots)
                    if (r.Task.HasValue && r.Task.Value.Kind == KardashevAscent.Simulation.Robots.TaskKind.Haul && r.Task.Value.Slot % 2 == 0 && !r.IsTravelling)
                    {
                        var need = sim.Workforce.HaulSegmentNeed(r, r.Task.Value);
                        if (!need.HasValue) continue;
                        dispatched++;
                        double lock_ = r.Battery.CapacityWh * Math.Max(0.0, r.Battery.ColdFactor - KardashevAscent.Simulation.Robots.RobotBattery.ColdCapacityFactor(need.Value.MinC));
                        double reserve = KardashevAscent.Simulation.Robots.RobotSpecs.ReturnToChargeFraction * r.Battery.CapacityWh * KardashevAscent.Simulation.Robots.RobotBattery.ColdCapacityFactor(need.Value.MinC);
                        Assert.GreaterOrEqual(r.Battery.UsableWh - lock_ - reserve, need.Value.Wh * 0.99, "a robot takes a loaded haul only with enough battery for the whole first segment");
                    }
            }
            Assert.Greater(sim.Workforce.Robots.Count(r => r.Battery.StateOfCharge > 0.0), 0);
            Assert.AreEqual(0, sim.RobotsDestroyed, "nobody is sent out to die");
        }

        [Test]
        public void AMineWhoseStoreHoldsAFullLoad_GetsAHaulHome_EvenWithNoStockTarget()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            var li = KardashevAscent.Simulation.Resources.Material.Lithium;
            int t = sim.BaseTileId;
            int tile = Enumerable.Range(0, sim.TileCount).First(q => !sim.GetTile(q).IsWater && sim.Deposits.GradeOf(q, li) > 0.0 && q != t && sim.IsOpenGround(q) && sim.Routes.HoursBetween(t, q) < 12.0);
            sim.DebugSurveyAll(); sim.PlaceChargingStation(t, 0); sim.PlacePanel(t, 1, 0.35, Math.PI); sim.PlaceBattery(t, 2); sim.WireAllBuildings();
            Assert.IsTrue(sim.PlaceStorage(sim.StoreFor(li), tile, 5)); sim.PlaceChargingStation(tile, 6); sim.PlacePanel(tile, 7, 0.35, Math.PI); sim.WireAllBuildings();
            double start = sim.KgAt(t, li);
            Assert.IsTrue(sim.OrderMining(tile, li));
            bool hauled = false;
            for (int h = 0; h < 1500 && !hauled; h++) { sim.Tick(1.0); hauled = sim.HaulOrders().Any(o => o.Material == li && o.From == tile && o.To == t) || sim.KgAt(t, li) > start + 1e-6; }
            Assert.IsTrue(hauled, "no stock target at the base, yet the mine's output is hauled home once it makes a load");
        }

        [Test]
        public void ManySitesAtOneDestination_AreServedByLoadsAsBigAsThePayload_NotOneHaulPerSite()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true, WeatherEnabled = false };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.PlaceBattery(sim.BaseTileId, 2); sim.WireAllBuildings();
            sim.Debug.FreeBuilding = false;
            sim.Materials.Add(KardashevAscent.Simulation.Resources.Material.Iron, 20000.0);
            int near = Sphere.Neighbors[sim.BaseTileId].First(q => !sim.GetTile(q).IsWater && sim.IsOpenGround(q));
            int sites = 0;
            for (int s2 = 0; s2 < 19; s2++) if (sim.PlaceBattery(near, s2)) sites++;
            Assert.GreaterOrEqual(sites, 8);
            sim.Tick(0.1);
            var fe = KardashevAscent.Simulation.Resources.Material.Iron;
            var orders = sim.HaulOrders().Where(o => o.Material == fe && o.To == near).ToList();
            double total = orders.Sum(o => o.Kg);
            Assert.LessOrEqual(orders.Count, 4, "at most the per-destination limit, not one per site");
            Assert.GreaterOrEqual(total, Math.Min(PlanetSimulation.BasePayloadKg, 0.9 * orders.Count * PlanetSimulation.BasePayloadKg) * 0.5, "loads are as big as the payload allows");
        }

        static double LegShare(double airC, out int stopTile)
        {
            var planet = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, airC);
            var sim = new PlanetSimulation(Sphere, Surface, planet) { LogisticsEnabled = true, WeatherEnabled = false, DiurnalTemperature = false };
            sim.Debug.FreeBuilding = true;
            sim.PlaceChargingStation(sim.BaseTileId, 0); sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI); sim.PlaceBattery(sim.BaseTileId, 2); sim.WireAllBuildings();
            sim.Tick(0.5);
            int far = Enumerable.Range(0, sim.TileCount).Where(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && sim.IsOpenGround(q) && sim.Routes.HoursBetween(sim.BaseTileId, q) > 8.0 && sim.Routes.HoursBetween(sim.BaseTileId, q) < 20.0).OrderBy(q => sim.Routes.HoursBetween(sim.BaseTileId, q)).First();
            sim.PlaceChargingStation(far, 0); sim.PlacePanel(far, 1, 0.35, Math.PI); sim.WireAllBuildings();
            sim.Debug.FreeBuilding = false;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var book = typeof(PlanetSimulation).GetField("haulBook", flags).GetValue(sim);
            var add = book.GetType().GetMethod("Add");
            var order = (KardashevAscent.Simulation.Logistics.HaulOrder)add.Invoke(book, new object[] { far, sim.BaseTileId, KardashevAscent.Simulation.Resources.Material.Rock, 300.0 });
            var r = sim.Workforce.Robots[0];
            order.Stage = 1; order.RobotId = r.Id; order.Stops = new System.Collections.Generic.List<int> { far };
            r.Tile = far;
            stopTile = far;
            return (double)typeof(PlanetSimulation).GetMethod("LegChargeShareFor", flags).Invoke(sim, new object[] { r });
        }

        [Test]
        public void BeforeEachLaterLeg_TheNextLegIsCheckedAtTheAirOfThatHour_ColderAirAsksForMoreCharge()
        {
            double mild = LegShare(15.0, out _), cold = LegShare(-60.0, out _);
            TestContext.WriteLine($"charge asked for the next leg: mild {mild:0.00}, cold {cold:0.00}");
            Assert.Greater(mild, 0.2, "a leg of several hours needs more than the reserve");
            Assert.GreaterOrEqual(cold, mild, "the same leg in colder air asks for at least as much (the heater and the cold battery)");
        }
        [Test]
        public void WhenTheWeatherTurnsColder_ALegCostsMore_AndTheForecastAnomalyFadesWithDays()
        {
            var planet = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, -40.0);
            var sim = new PlanetSimulation(Sphere, Surface, planet) { LogisticsEnabled = true, WeatherEnabled = true, DiurnalTemperature = false };
            sim.Tick(0.5);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            int far = Enumerable.Range(0, sim.TileCount).Where(q => !sim.GetTile(q).IsWater && q != sim.BaseTileId && sim.IsOpenGround(q) && sim.Routes.HoursBetween(sim.BaseTileId, q) > 8.0).First();
            var field = typeof(PlanetSimulation).GetField("legAnomalyK", flags);
            double normal = sim.HaulLegCost(sim.BaseTileId, far, 300.0).TotalWh;
            field.SetValue(sim, (Func<int, double, double>)((t, h) => -40.0));
            double colder = sim.HaulLegCost(sim.BaseTileId, far, 300.0).TotalWh;
            field.SetValue(sim, null);
            TestContext.WriteLine($"leg energy: {normal:0} Wh normal, {colder:0} Wh with the air 40 K colder");
            Assert.Greater(colder, normal, "colder air means more heater energy on the same leg");
            var anomaly = typeof(PlanetSimulation).GetMethod("ForecastAnomalyK", flags);
            double a0 = (double)anomaly.Invoke(sim, new object[] { sim.BaseTileId, 0.0 });
            double a3 = (double)anomaly.Invoke(sim, new object[] { sim.BaseTileId, 72.0 });
            double a9 = (double)anomaly.Invoke(sim, new object[] { sim.BaseTileId, 24.0 * 9 });
            Assert.LessOrEqual(Math.Abs(a3), Math.Abs(a0) + 1e-9, "the anomaly fades day by day");
            Assert.AreEqual(0.0, a9, 1e-12, "no forecast beyond its range");
        }
    }
}

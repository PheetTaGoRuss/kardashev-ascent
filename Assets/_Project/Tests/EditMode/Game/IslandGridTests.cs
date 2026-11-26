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
    }
}

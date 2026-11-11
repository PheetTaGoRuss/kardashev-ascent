using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Driving between tiles (spec §10, design D9): routes, terrain pace, gravity, range, charging trips.</summary>
    public class MovementTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static StubPlanetSimulation Create() => new StubPlanetSimulation(Sphere, Surface, Earthlike);

        /// <summary>A land neighbour of the base, and one two hops away (reachable by land).</summary>
        static int LandNeighbour(StubPlanetSimulation sim, int from, int not = -1)
        {
            foreach (int n in Sphere.Neighbors[from])
                if (!sim.GetTile(n).IsWater && n != not && n != sim.BaseTileId) return n;
            return -1;
        }

        [Test]
        public void HopHours_ByTerrain_WaterImpassable()
        {
            Assert.AreEqual(RouteFinder.FlatHopHours, RouteFinder.HopHours(TerrainType.Lowland));
            Assert.Greater(RouteFinder.HopHours(TerrainType.Mountain), RouteFinder.HopHours(TerrainType.Highland));
            Assert.IsTrue(double.IsInfinity(RouteFinder.HopHours(TerrainType.DeepOcean)));
            Assert.IsTrue(double.IsInfinity(RouteFinder.HopHours(TerrainType.ShallowOcean)));
        }

        [TestCase(9.80665, 1.0)]
        [TestCase(12.1, 1.2339)]
        [TestCase(8.0, 0.8158)]
        public void DrivePower_ScalesWithGravity(double g, double factor)
        {
            var routes = new RouteFinder(Sphere, Surface, g);
            Assert.AreEqual(RouteFinder.BaseDrivePowerW * factor, routes.DrivePowerW, 0.01 * RouteFinder.BaseDrivePowerW);
        }

        [Test]
        public void Routes_AvoidWater_AndAddUpTerrain()
        {
            var routes = new RouteFinder(Sphere, Surface);
            var sim = Create();
            int b = sim.BaseTileId;
            int n = LandNeighbour(sim, b);
            Assert.AreEqual(RouteFinder.HopHours(Surface.Terrain[n]), routes.HoursBetween(b, n), 1e-9);
            CollectionAssert.AreEqual(new[] { n }, routes.Route(b, n));
            double[] all = routes.HoursFrom(b);
            for (int t = 0; t < Sphere.TileCount; t++)
                if (Terrains.IsWater(Surface.Terrain[t]))
                    Assert.IsTrue(double.IsInfinity(all[t]), "never onto water");
            List<int> route = null;
            for (int t = 0; t < Sphere.TileCount && route == null; t++)
                if (!double.IsInfinity(all[t]) && all[t] > 3 * RouteFinder.FlatHopHours) route = routes.Route(b, t);
            Assert.IsNotNull(route);
            double sum = 0.0;
            foreach (int t in route) { Assert.IsFalse(Terrains.IsWater(Surface.Terrain[t])); sum += RouteFinder.HopHours(Surface.Terrain[t]); }
            Assert.AreEqual(all[route[route.Count - 1]], sum, 1e-9);
        }

        [Test]
        public void Survey_OnAnotherTile_TheRobotDrivesThereFirst()
        {
            var sim = Create();
            int n = LandNeighbour(sim, sim.BaseTileId);
            Robot r = sim.Workforce.Robots[0];
            Assert.IsTrue(sim.OrderSurvey(n));
            Assert.IsTrue(r.IsTravelling);
            Assert.AreEqual(n, r.Destination);
            double hop = RouteFinder.HopHours(Surface.Terrain[n]);
            sim.Tick(hop - 0.25);
            Assert.AreEqual(sim.BaseTileId, r.Tile, "still on the way");
            Assert.AreEqual(0.0, sim.SurveyProgress(n), "no work while driving");
            sim.Tick(0.25);
            Assert.AreEqual(n, r.Tile);
            Assert.IsFalse(r.IsTravelling);
            sim.Tick(RobotSpecs.SurveyWorkHours + 0.25);
            PuzzleSolving.SolveSurvey(sim, n);
            Assert.IsTrue(sim.Deposits.IsSurveyed(n), "the cores are in, and the right estimate surveys the tile");
        }

        [Test]
        public void Driving_DrawsDrivePower()
        {
            var sim = Create();
            int n = LandNeighbour(sim, sim.BaseTileId);
            Robot r = sim.Workforce.Robots[0];
            double before = r.Battery.StoredWh;
            sim.OrderSurvey(n);
            sim.Tick(1.0);
            Assert.AreEqual(before - sim.Routes.DrivePowerW / Math.Sqrt(KardashevAscent.Simulation.Energy.Battery.LithiumIonRoundTrip),
                            r.Battery.StoredWh, 1e-6);
        }

        [Test]
        public void OutOfRange_TasksAreNotTaken()
        {
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            double[] hours = sim.Routes.HoursFrom(sim.BaseTileId);
            int far = -1;
            for (int t = 0; t < sim.TileCount; t++)
                if (!double.IsInfinity(hours[t]) && sim.Routes.DrivePowerW * 2.0 * hours[t] > 0.8 * RobotSpecs.BatteryCapacityWh) { far = t; break; }
            Assert.GreaterOrEqual(far, 0, "a tile too far for one charge");
            Assert.IsTrue(sim.OrderSurvey(far));
            Assert.IsNull(r.Task, "it couldn't get there and back with 20 % left");
        }

        [Test]
        public void LowBattery_FarFromTheLander_DrivesBackToCharge()
        {
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            int n = LandNeighbour(sim, sim.BaseTileId);
            sim.SendRobot(r.Id, n);
            for (int i = 0; i < 40 && r.Tile != n; i++) sim.Tick(0.25);
            Assert.AreEqual(n, r.Tile);
            r.Battery.Restore(r.Battery.DeliveredWh, 0.21 * r.Battery.CapacityWh);
            sim.Tick(0.25);
            Assert.AreEqual(RobotActivity.Charging, r.Activity, "20 % plus the drive home");
            Assert.AreEqual(sim.BaseTileId, r.Destination);
            for (int i = 0; i < 40 && r.IsTravelling; i++) sim.Tick(0.25);
            Assert.AreEqual(sim.BaseTileId, r.Tile);
            Assert.AreEqual(RobotState.Charging, r.State);
        }

        [Test]
        public void EmptyBattery_AwayFromChargers_IsStranded()
        {
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            int n = LandNeighbour(sim, sim.BaseTileId);
            sim.SendRobot(r.Id, n);
            for (int i = 0; i < 40 && r.Tile != n; i++) sim.Tick(0.25);
            r.Battery.Restore(r.Battery.DeliveredWh, 0.0);
            sim.Tick(0.25);
            Assert.IsTrue(r.IsStranded);
            Assert.AreEqual(RobotState.Shutdown, r.State);
            sim.Tick(5.0);
            Assert.AreEqual(n, r.Tile, "it can't move: rescue is a later feature");
        }

        [Test]
        public void SendRobot_Manual_ThenBackToWork()
        {
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            int n = LandNeighbour(sim, sim.BaseTileId);
            Assert.IsTrue(sim.SendRobot(r.Id, n));
            Assert.AreEqual(n, r.ManualTarget);
            Assert.IsFalse(r.Available, "no tasks on the way");
            for (int i = 0; i < 40 && r.ManualTarget.HasValue; i++) sim.Tick(0.25);
            Assert.AreEqual(n, r.Tile);
            Assert.IsNull(r.ManualTarget);
            Assert.IsTrue(r.Available);
            Assert.IsFalse(sim.SendRobot(r.Id, n), "already there");
        }

        [Test]
        public void SitesOnAnotherTile_NeedTheRobotThere()
        {
            var sim = Create();
            int n = LandNeighbour(sim, sim.BaseTileId);
            Assert.IsTrue(sim.PlaceBattery(n, 0));
            Assert.IsTrue(sim.IsUnderConstruction(n, 0), "the robot has to drive there first");
            for (int i = 0; i < 40 && sim.IsUnderConstruction(n, 0); i++) sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(n, 0));
        }

        [Test]
        public void MovementOff_RobotsWorkFromWhereTheyAre()
        {
            var sim = Create();
            sim.MovementEnabled = false;
            int n = LandNeighbour(sim, sim.BaseTileId);
            Assert.IsTrue(sim.PlaceBattery(n, 0));
            Assert.IsFalse(sim.IsUnderConstruction(n, 0));
        }

        [Test]
        public void Save_KeepsRouteAndTarget()
        {
            var a = Create();
            Robot r = a.Workforce.Robots[0];
            int n = LandNeighbour(a, a.BaseTileId);
            int m = LandNeighbour(a, n, a.BaseTileId);
            a.SendRobot(r.Id, m >= 0 ? m : n);
            a.Tick(1.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(21, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Robot c = b.Workforce.Robots[0];
            Assert.AreEqual(r.Tile, c.Tile);
            Assert.AreEqual(r.ManualTarget, c.ManualTarget);
            Assert.AreEqual(r.HopsLeft, c.HopsLeft);
        }
    }
}

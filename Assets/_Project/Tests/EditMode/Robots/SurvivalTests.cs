using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Hibernation and cold or heat soak (S1): an empty robot cools with tau = m cp / G and is destroyed only past its storage limits.</summary>
    public class SurvivalTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSimulation Colony(double airC) => new PlanetSimulation(Sphere, Surface, new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, airC, false, 1.0), Kardashev.EarthRadiusM) { WeatherEnabled = false };

        [Test]
        public void TheTimeConstantIsMassTimesSpecificHeatOverTheLossConductance()
        {
            double c = RobotThermal.BodyHeatCapacityJPerK;
            TestContext.WriteLine($"heat capacity {c / 1000.0:0} kJ/K, tau {RobotThermal.BodyTauHours():0.0} h");
            Assert.AreEqual(642.0 * 0.58 * 1000.0, c, 25_000.0, "642 kg at about 0.58 kJ/kg K");
            Assert.AreEqual(c / 2.0 / 3600.0, RobotThermal.BodyTauHours(), 1e-9, "G = 2 W/K");
            Assert.That(RobotThermal.BodyTauHours(), Is.InRange(40.0, 70.0), "tens of hours");
            Assert.AreEqual(-40.0, RobotThermal.StorageMinC); Assert.AreEqual(60.0, RobotThermal.StorageMaxC);
        }

        [Test]
        public void HibernationHoursFollowTheExponential_AndNeverReachALimitTheAirDoesNotPass()
        {
            double tau = RobotThermal.BodyTauHours();
            Assert.AreEqual(tau * Math.Log((15.0 + 150.0) / (-40.0 + 150.0)), RobotThermal.HibernationHoursUntilLimit(15.0, -150.0, tau), 1e-9);
            Assert.IsTrue(double.IsInfinity(RobotThermal.HibernationHoursUntilLimit(15.0, -30.0, tau)), "air inside the storage range: it never gets there");
            Assert.AreEqual(0.0, RobotThermal.HibernationHoursUntilLimit(-45.0, -80.0, tau), "already past");
        }

        [Test]
        public void AnEmptyRobotInTheCold_LivesForHours_ThenColdSoaksToDestruction_WithTheCauseRecorded()
        {
            var sim = Colony(-150.0);
            int tile = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater && !Sphere.Neighbors[sim.BaseTileId].Contains(t));
            var r = sim.Workforce.AddRobot(RobotKind.General, tile);
            r.Battery.Draw(1e12, 1.0);
            sim.Tick(10.0);
            Assert.AreEqual(0, sim.RobotsDestroyed, "ten hours in: still alive (not an hour)");
            Assert.IsTrue(r.Hibernating);
            Assert.Less(r.BodyTempC, 15.0); Assert.Greater(r.BodyTempC, -40.0);
            sim.Tick(30.0);
            Assert.AreEqual(1, sim.RobotsDestroyed);
            StringAssert.Contains("cold-soaked", sim.OperationsLog.Last(o => o.Quantity == "Robot destroyed").Text);
        }

        [Test]
        public void TheBodyTemperatureIsSaved()
        {
            var sim = Colony(-150.0);
            int tile = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater && !Sphere.Neighbors[sim.BaseTileId].Contains(t));
            var r = sim.Workforce.AddRobot(RobotKind.General, tile);
            r.Battery.Draw(1e12, 1.0);
            sim.Tick(8.0);
            double body = r.BodyTempC;
            var again = Colony(-150.0); again.RestoreState(sim.CaptureState());
            Assert.AreEqual(body, again.Workforce.GetRobot(r.Id).BodyTempC, 1e-9);
        }
    }
}

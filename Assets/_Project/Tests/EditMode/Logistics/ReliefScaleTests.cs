using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Logistics
{
    /// <summary>KI-42 (decided 2026-11-23): the terrain's relief in metres and the climb term.</summary>
    public class ReliefScaleTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void EarthTwinsUseEarthsReliefAndMarsUsesMola()
        {
            Assert.AreEqual((-11000.0, 8800.0), ReliefScale.For("viridia", 9.8));
            Assert.AreEqual((-8200.0, 21200.0), ReliefScale.For("mars", 3.71));
            Assert.AreEqual((-11000.0, 8800.0), ReliefScale.For("earth", 9.80665));
        }

        [Test]
        public void OtherWorldsScaleEarthsReliefInverselyWithGravity_AnAssumption()
        {
            var moonish = ReliefScale.For("some-planet", 9.80665 / 2.0);   // half Earth's gravity: twice the relief
            Assert.AreEqual(-22000.0, moonish.MinM, 1e-6);
            Assert.AreEqual(17600.0, moonish.MaxM, 1e-6);
            var heavy = ReliefScale.For("some-planet", 9.80665 * 2.0);
            Assert.AreEqual(-5500.0, heavy.MinM, 1e-6);
            Assert.AreEqual(4400.0, heavy.MaxM, 1e-6);
            Assert.AreEqual((-11000.0, 8800.0), ReliefScale.For("some-planet", 0.0), "no gravity known: Earth's relief");
        }

        [Test]
        public void ElevationMapsLinearlyOntoTheTwoEnds()
        {
            var earth = ReliefScale.For("earth", 9.80665);
            Assert.AreEqual(0.0, ReliefScale.HeightM(0.0, earth), 1e-9);
            Assert.AreEqual(8800.0, ReliefScale.HeightM(1.0, earth), 1e-9);
            Assert.AreEqual(4400.0, ReliefScale.HeightM(0.5, earth), 1e-9);
            Assert.AreEqual(-11000.0, ReliefScale.HeightM(-1.0, earth), 1e-9);
            Assert.AreEqual(8800.0, ReliefScale.HeightM(3.0, earth), 1e-9, "clamped");
        }

        [Test]
        public void TheSimulationHasMetreHeights_ZeroOnWater_AndTheClimbIsOnByDefault()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(sim.ClimbEnabled);
            int water = Enumerable.Range(0, sim.TileCount).First(t => sim.GetTile(t).IsWater);
            Assert.AreEqual(0.0, sim.TileHeightM(water));
            double max = Enumerable.Range(0, sim.TileCount).Max(t => sim.TileHeightM(t));
            Assert.Greater(max, 100.0);
            Assert.LessOrEqual(max, 8800.0 + 1e-6);
        }

        [Test]
        public void ARobotDrivingUphillSpendsClimbingEnergy_AndNoneWhenTheClimbIsOff()
        {
            double Climbed(bool climb)
            {
                var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { ClimbEnabled = climb, ConstructionTimeEnabled = false };
                sim.Debug.FreeBuilding = true;
                Assert.IsTrue(sim.PlaceChargingStation(sim.BaseTileId, 20));
                sim.Debug.FreeBuilding = false;
                sim.Debug.FullBattery = true;
                Robot r = sim.Workforce.Robots[0];
                // a land tile reachable by road whose height is above the lander's
                int target = Enumerable.Range(0, sim.TileCount)
                    .Where(t => !sim.GetTile(t).IsWater && !double.IsInfinity(sim.Routes.HoursBetween(sim.BaseTileId, t)) && sim.TileHeightM(t) > sim.TileHeightM(sim.BaseTileId) + 500.0)
                    .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).First();
                Assert.IsTrue(sim.Workforce.SendTo(r.Id, target));
                for (int i = 0; i < 200 && r.Tile != target; i++) sim.Tick(0.5);
                return sim.Upkeep.ClimbDriveWh;
            }
            Assert.AreEqual(0.0, Climbed(false), 1e-12);
            Assert.Greater(Climbed(true), 1.0);
        }

        [Test]
        public void TheClimbEnergyIsTheShareTimesMgh()
        {
            double full = KardashevAscent.Simulation.Logistics.HaulPhysics.ClimbEnergyWh(1025.0, 0.0, 9.80665, 100.0, 1.0);
            Assert.AreEqual(1025.0 * 9.80665 * 100.0 / 3600.0, full, 1e-9);
            Assert.AreEqual(279.2, full, 0.1);
        }
    }
}

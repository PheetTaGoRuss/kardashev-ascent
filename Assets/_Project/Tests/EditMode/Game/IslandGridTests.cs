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
            Assert.IsFalse(sim.PlaceBattery(far, 2), "a battery belongs to the lander grid");
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
    }
}

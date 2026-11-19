using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The out-of-range message for work a robot could never reach on one charge (balance pass 2026-11-20).</summary>
    public class WorkReachTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation WithCharger()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceChargingStation(sim.BaseTileId, 0));
            sim.Tick(1.0);
            return sim;
        }

        [Test]
        public void WithNoCharger_ThereIsNothingToWarnAbout()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            for (int t = 0; t < sim.TileCount; t += 50) Assert.IsNull(sim.WorkReachProblem(t));
        }

        [Test]
        public void TheChargerTile_AndItsNeighbours_AreInRange()
        {
            var sim = WithCharger();
            Assert.IsNull(sim.WorkReachProblem(sim.BaseTileId));
            foreach (int nb in Sphere.Neighbors[sim.BaseTileId])
                if (!sim.GetTile(nb).IsWater) Assert.IsNull(sim.WorkReachProblem(nb), "next door");
        }

        [Test]
        public void ADistantLandTile_IsOutOfRange_AndTheMessageSaysWhatToDo()
        {
            var sim = WithCharger();
            int far = -1;
            for (int t = 0; t < sim.TileCount; t++)
                if (!sim.GetTile(t).IsWater && sim.WorkReachProblem(t) != null && sim.WorkReachProblem(t).StartsWith("Out of range")) { far = t; break; }
            Assert.GreaterOrEqual(far, 0, "some tile is beyond one charge");
            StringAssert.Contains("charging station", sim.WorkReachProblem(far));
        }

        [Test]
        public void ABuiltCharger_NearTheFarTile_BringsItInRange()
        {
            var sim = WithCharger();
            int far = -1;
            for (int t = 0; t < sim.TileCount && far < 0; t++)
                if (!sim.GetTile(t).IsWater && sim.WorkReachProblem(t) != null && sim.WorkReachProblem(t).StartsWith("Out of range")) far = t;
            Assert.GreaterOrEqual(far, 0);
            Assert.IsTrue(sim.PlaceChargingStation(far, 0), "a forward charger on the far tile (free building)");
            sim.Tick(1.0);
            Assert.IsNull(sim.WorkReachProblem(far));
        }
    }
}

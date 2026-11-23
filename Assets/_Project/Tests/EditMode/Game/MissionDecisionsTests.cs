using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Decisions of 2026-11-23: no piles on water, wetland or ice; any General robot can survey.</summary>
    public class MissionDecisionsTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * System.Math.PI / 180.0,
            282.94 * System.Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void NoPileOnWaterOrWetland_AndOrdinaryGroundIsFine()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int water = Enumerable.Range(0, sim.TileCount).First(t => sim.GetTile(t).IsWater);
            Assert.IsFalse(sim.CanPileOn(water, Material.Iron));
            int land = Enumerable.Range(0, sim.TileCount).First(t => sim.GetTile(t).Terrain == TerrainType.Lowland);
            Assert.IsTrue(sim.CanPileOn(land, Material.Iron));
            var wet = Enumerable.Range(0, sim.TileCount).Where(t => sim.GetTile(t).Terrain == TerrainType.Wetland).ToList();
            if (wet.Count > 0) Assert.IsFalse(sim.CanPileOn(wet[0], Material.Iron));
        }

        [Test]
        public void IceTilesOnlyPileWaterIce()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            var ice = Enumerable.Range(0, sim.TileCount).Where(t => sim.GetTile(t).Terrain == TerrainType.Ice).ToList();
            if (ice.Count == 0) Assert.Pass("no glacier on this planet");
            Assert.IsTrue(sim.CanPileOn(ice[0], Material.Water));
            Assert.IsFalse(sim.CanPileOn(ice[0], Material.Iron));
        }

        [Test]
        public void AnyGeneralRobotCanSurvey_AsWellAsMeasureAndExperiment()
        {
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.General, TaskKind.Survey, 0));
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.General, TaskKind.Measure, 0));
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.General, TaskKind.Experiment, 0));
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.Research, TaskKind.Survey, 0), "research robots stay as specialists");
        }
    }
}

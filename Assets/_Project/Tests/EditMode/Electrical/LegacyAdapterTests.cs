using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>W-11: the graph built from the legacy wire book agrees with the old powered-tile search on every scenario.</summary>
    public class LegacyAdapterTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * System.Math.PI / 180.0,
            282.94 * System.Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Wired()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            return sim;
        }

        static int Land(PlanetSimulation sim, int from, params int[] avoid) =>
            Sphere.Neighbors[from].First(n => !sim.GetTile(n).IsWater && !avoid.Contains(n));

        static void AssertSameAsOldSearch(PlanetSimulation sim)
        {
            var graphTiles = sim.LanderNetworkTiles().ToHashSet();
            for (int t = 0; t < Sphere.TileCount; t++)
                Assert.AreEqual(sim.IsPowered(t), graphTiles.Contains(t) || t == sim.BaseTileId, "tile " + t);
        }

        [Test]
        public void NoLinesMeansOnlyTheLanderNetwork()
        {
            var sim = Wired();
            AssertSameAsOldSearch(sim);
            Assert.AreEqual(1, sim.ElectricalNetworkCount);
        }

        [Test]
        public void AChainFromTheLanderMatches()
        {
            var sim = Wired();
            int a = Land(sim, sim.BaseTileId);
            int b = Land(sim, a, sim.BaseTileId);
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePowerLine(b, 0, a));
            AssertSameAsOldSearch(sim);
            Assert.AreEqual(1, sim.ElectricalNetworkCount);
        }

        [Test]
        public void AnIslandIsAnotherNetworkAndNotOnTheMainGrid()
        {
            var sim = Wired();
            int a = Land(sim, sim.BaseTileId);
            int far = Land(sim, a, sim.BaseTileId);
            far = Land(sim, far, a, sim.BaseTileId);
            int far2 = Land(sim, far, a, sim.BaseTileId);
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePowerLine(far2, 0, far));
            AssertSameAsOldSearch(sim);
            Assert.AreEqual(2, sim.ElectricalNetworkCount);
            Assert.IsTrue(sim.IsOnElectricalIsland(far));
            Assert.IsFalse(sim.IsOnElectricalIsland(a));
            Assert.IsFalse(sim.IsPowered(far));
        }

        [Test]
        public void AHighVoltageLineJoinsPlainLines()
        {
            var sim = Wired();
            sim.Debug.FreeBuilding = true;
            int a = Land(sim, sim.BaseTileId);
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            AssertSameAsOldSearch(sim);
        }

        [Test]
        public void TheAdapterIsDeterministic()
        {
            var sim = Wired();
            int a = Land(sim, sim.BaseTileId);
            int b = Land(sim, a, sim.BaseTileId);
            sim.PlacePowerLine(a, 0, sim.BaseTileId);
            sim.PlacePowerLine(b, 0, a);
            CollectionAssert.AreEqual(sim.LanderNetworkTiles().ToArray(), sim.LanderNetworkTiles().ToArray());
        }
    }
}

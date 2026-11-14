using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    /// <summary>Rivers (decided 2026-11-14): springs on high land run downhill to the sea; small rules on top.</summary>
    public class RiverTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(24);
        static readonly PlanetSurface Wet = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 25.0, false, true, 0.62));
        static readonly PlanetSurface Dry = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, -8.0, false, false, 0.45));

        [Test]
        public void AWetPlanet_HasRivers_ADryOneNone()
        {
            Assert.Greater(RiverMap.Generate(Sphere, Wet, 7).RiverTileCount, 20, "an Earth-like planet has rivers");
            Assert.AreEqual(0, RiverMap.Generate(Sphere, Dry, 7).RiverTileCount, "no sea to run to");
        }

        [Test]
        public void EveryRiver_RunsDownhill_ToTheSea()
        {
            RiverMap rivers = RiverMap.Generate(Sphere, Wet, 7);
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                if (!rivers.IsRiver(t)) continue;
                Assert.IsFalse(Terrains.IsWater(Wet.Terrain[t]), "rivers are on land");
                int cur = t;
                for (int guard = 0; guard < Sphere.TileCount; guard++)
                {
                    int next = rivers.Downstream(cur);
                    Assert.GreaterOrEqual(next, 0, $"tile {cur} has somewhere to go");
                    CollectionAssert.Contains(Sphere.Neighbors[cur], next, "one tile at a time");
                    if (Terrains.IsWater(Wet.Terrain[next])) break;   // reached the sea
                    Assert.Less(Wet.Elevation[next], Wet.Elevation[cur], "downhill");
                    Assert.GreaterOrEqual(rivers.Flow(next), rivers.Flow(cur), "rivers grow as they join");
                    cur = next;
                }
            }
        }

        [Test]
        public void SamePlanet_SameRivers()
        {
            RiverMap a = RiverMap.Generate(Sphere, Wet, 7), b = RiverMap.Generate(Sphere, Wet, 7);
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                Assert.AreEqual(a.Flow(t), b.Flow(t));
                Assert.AreEqual(a.Downstream(t), b.Downstream(t));
            }
        }

        [Test]
        public void RiverTiles_HaveWater_AndAreSlowerToCross()
        {
            RiverMap rivers = RiverMap.Generate(Sphere, Wet, 7);
            var map = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7, rivers);
            var plain = new RouteFinder(Sphere, Wet);
            var withRivers = new RouteFinder(Sphere, Wet, rivers: rivers);
            int checkedTiles = 0;
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                if (!rivers.IsRiver(t)) continue;
                Assert.Greater(map.GradeOf(t, Material.Water), 0.0, "pump the river");
                Assert.AreEqual(plain.HopHoursAt(t) * RouteFinder.RiverCrossingFactor, withRivers.HopHoursAt(t), 1e-9);
                checkedTiles++;
            }
            Assert.Greater(checkedTiles, 0);
        }
    }
}

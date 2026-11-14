using System;
using System.Linq;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    /// <summary>Forest, wetland, beach and volcanic rock (decided 2026-11-13).</summary>
    public class RichTerrainTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(24);

        static int Count(PlanetSurface s, TerrainType t) => s.Terrain.Count(x => x == t);

        [Test]
        public void Landing_NeedsFlatOpenGround()
        {
            // Director 2026-11-14: no landing on sea, wetland or ground the colony can't start on.
            Assert.IsNotNull(Terrains.LandingProblem(TerrainType.ShallowOcean));
            Assert.IsNotNull(Terrains.LandingProblem(TerrainType.DeepOcean));
            Assert.IsNotNull(Terrains.LandingProblem(TerrainType.Wetland));
            Assert.IsNotNull(Terrains.LandingProblem(TerrainType.Mountain));
            Assert.IsNotNull(Terrains.LandingProblem(TerrainType.Ice));
            foreach (TerrainType t in new[] { TerrainType.Lowland, TerrainType.Desert, TerrainType.DryBasin, TerrainType.Beach, TerrainType.Forest, TerrainType.Tundra })
                Assert.IsNull(Terrains.LandingProblem(t), t.ToString());
            foreach (TerrainType t in System.Enum.GetValues(typeof(TerrainType)))
                Assert.AreEqual(Terrains.BaseSiteRank(t) > 0, Terrains.LandingProblem(t) == null, t.ToString());
        }

        [Test]
        public void WetPlanet_GetsAllFourNewTerrains()
        {
            PlanetSurface s = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 15.0, false));
            Assert.Greater(Count(s, TerrainType.Forest), 0, "forest");
            Assert.Greater(Count(s, TerrainType.Wetland), 0, "wetland");
            Assert.Greater(Count(s, TerrainType.Beach), 0, "beach");
            Assert.Greater(Count(s, TerrainType.VolcanicRock), 0, "volcanic rock");
            Assert.Greater(Count(s, TerrainType.Lowland), 0, "some plain lowland is left for the landing");
        }

        [Test]
        public void DryPlanet_HasNoForestWetlandOrBeach_ButMayHaveVolcanicRock()
        {
            PlanetSurface s = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 25.0, false, liquidWater: false));
            Assert.AreEqual(0, Count(s, TerrainType.Forest));
            Assert.AreEqual(0, Count(s, TerrainType.Wetland));
            Assert.AreEqual(0, Count(s, TerrainType.Beach));
        }

        [Test]
        public void Generation_IsDeterministic()
        {
            var a = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 15.0, false));
            var b = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 15.0, false));
            CollectionAssert.AreEqual(a.Terrain, b.Terrain);
        }

        [Test]
        public void Beach_SitsOnTheShore_AndWetlandIsNearTheSea()
        {
            PlanetSurface s = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 15.0, false));
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                if (s.Terrain[t] != TerrainType.Beach && s.Terrain[t] != TerrainType.Wetland) continue;
                bool near = false;
                foreach (int n in Sphere.Neighbors[t])
                {
                    near |= Terrains.IsWater(s.Terrain[n]);
                    foreach (int m in Sphere.Neighbors[n]) near |= Terrains.IsWater(s.Terrain[m]);
                }
                Assert.IsTrue(near, $"{s.Terrain[t]} tile {t} is within two tiles of the sea");
            }
        }

        [Test]
        public void Rules_Buildable_Floating_Albedo_Movement()
        {
            Assert.IsFalse(Terrains.IsBuildableLand(TerrainType.Wetland), "too soft for land buildings");
            Assert.IsFalse(Terrains.IsBuildableLand(TerrainType.ShallowOcean));
            foreach (TerrainType t in new[] { TerrainType.Forest, TerrainType.Beach, TerrainType.VolcanicRock, TerrainType.Lowland })
                Assert.IsTrue(Terrains.IsBuildableLand(t), t.ToString());
            Assert.IsTrue(Terrains.AllowsFloating(TerrainType.ShallowOcean));
            Assert.IsTrue(Terrains.AllowsFloating(TerrainType.Wetland));
            Assert.IsFalse(Terrains.AllowsFloating(TerrainType.DeepOcean));
            Assert.Less(Terrains.Albedo(TerrainType.VolcanicRock), Terrains.Albedo(TerrainType.Lowland), "dark rock reflects less");
            Assert.Greater(Terrains.Albedo(TerrainType.Beach), Terrains.Albedo(TerrainType.Lowland), "sand reflects more");
            Assert.Greater(RouteFinder.HopHours(TerrainType.Wetland), RouteFinder.HopHours(TerrainType.Forest));
            Assert.Greater(RouteFinder.HopHours(TerrainType.Forest), RouteFinder.FlatHopHours);
            Assert.IsFalse(double.IsInfinity(RouteFinder.HopHours(TerrainType.Wetland)), "robots can wade through a wetland");
            Assert.AreEqual("Volcanic rock", Terrains.Name(TerrainType.VolcanicRock));
            Assert.AreEqual("Wetland", Terrains.Name(TerrainType.Wetland));
        }
    }
}

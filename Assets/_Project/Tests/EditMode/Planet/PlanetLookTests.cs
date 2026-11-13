using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    /// <summary>Each planet's surface from its data: own continents, ice from temperature, eyeballs when locked.</summary>
    public class PlanetLookTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);

        [Test]
        public void TemperatePlanet_KeepsTheEditorDefaults()
        {
            // Viridia (25 °C, seed 7) must look exactly as before, so its base tile and saves don't change.
            SurfaceSettings s = SurfaceSettings.ForClimate(7, 25.0, false);
            var before = new SurfaceSettings(7);
            Assert.AreEqual(before.Seed, s.Seed);
            Assert.AreEqual(before.OceanFraction, s.OceanFraction);
            Assert.AreEqual(before.IceLatitudeDeg, s.IceLatitudeDeg);
            Assert.AreEqual(before.NoiseScale, s.NoiseScale);
            Assert.IsFalse(s.IceAroundSubStellar);
            CollectionAssert.AreEqual(PlanetSurface.Generate(Sphere, before).Terrain, PlanetSurface.Generate(Sphere, s).Terrain);
        }

        [TestCase(0.0, 70.0)]
        [TestCase(-8.1, 57.85)]
        [TestCase(-40.0, 10.0)]
        [TestCase(-67.2, 10.0)]
        [TestCase(45.0, 90.0)]
        public void IceLine_FollowsAirTemperature(double airC, double iceLatitude)
        {
            Assert.AreEqual(iceLatitude, SurfaceSettings.ForClimate(1, airC, false).IceLatitudeDeg, 1e-9);
        }

        [TestCase(25.0, 0.62)]
        [TestCase(60.0, 0.37)]
        [TestCase(200.0, 0.10)]
        public void HotPlanets_DryOut(double airC, double ocean)
        {
            Assert.AreEqual(ocean, SurfaceSettings.ForClimate(1, airC, false).OceanFraction, 1e-9);
        }

        [TestCase(-55.1, 35.0)]
        [TestCase(-23.4, 56.6)]
        [TestCase(150.0, 180.0)]
        public void TidallyLocked_IsAnEyeball(double airC, double openRadius)
        {
            SurfaceSettings s = SurfaceSettings.ForClimate(1, airC, true);
            Assert.IsTrue(s.IceAroundSubStellar);
            Assert.AreEqual(openRadius, s.IceLatitudeDeg, 1e-9);
        }

        static bool IsCold(TerrainType t) => t == TerrainType.Ice || t == TerrainType.FrozenSea || t == TerrainType.Tundra;

        [Test]
        public void Eyeball_ColdOutsideTheCircle_GlacierOnlyFarOut()
        {
            var settings = new SurfaceSettings(3, 0.62, 50.0, 1.6, true);
            PlanetSurface surface = PlanetSurface.Generate(Sphere, settings);
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                var c = Sphere.Centers[t];
                double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, c.Z / c.Length()))) * 180.0 / Math.PI;
                TerrainType terrain = surface.Terrain[t];
                // The edge moves by ±6° of noise and up to 8° inland on high ground.
                if (angle < 50.0 - 14.5)
                    Assert.IsFalse(IsCold(terrain), $"tile {t} at {angle:0.0}°: {terrain}");
                else if (angle > 50.0 + 6.5)
                    Assert.IsTrue(IsCold(terrain), $"tile {t} at {angle:0.0}°: {terrain}");
                if (terrain == TerrainType.Ice)
                    Assert.Greater(angle, 50.0 + 45.0 - 14.5, "glacier only well past the edge");
            }
        }

        static int Count(PlanetSurface s, params TerrainType[] types)
        {
            int n = 0;
            foreach (TerrainType t in s.Terrain) if (Array.IndexOf(types, t) >= 0) n++;
            return n;
        }

        [Test]
        public void ProximaLook_BaseIsOpenLowlandUnderTheStar_AndNotAllIce()
        {
            PlanetRecord r = PlanetRecordTests.ProximaForOtherTests();
            r.look.terrainSeed = 1697376206;
            PlanetSurface surface = PlanetSurface.Generate(Sphere, r.ToSurfaceSettings());
            var sim = new PlanetSimulation(Sphere, surface, r);
            Assert.AreEqual(TerrainType.Lowland, surface.Terrain[sim.BaseTileId]);
            Assert.IsTrue(sim.IsDayAtBase);
            Assert.Greater(Count(surface, TerrainType.Tundra), 0, "cold bare ground");
            Assert.Greater(Count(surface, TerrainType.FrozenSea), 0, "frozen seas");
            Assert.Greater(Count(surface, TerrainType.ShallowOcean, TerrainType.DeepOcean), 0, "open sea in the eye");
            Assert.Less(Count(surface, TerrainType.Ice), Sphere.TileCount * 0.75, "not one sheet of ice");
        }

        [Test]
        public void DryWorld_HasNoWaterAnywhere()
        {
            PlanetSurface s = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(5, -8.0, false, false, 0.45));
            Assert.AreEqual(0, Count(s, TerrainType.DeepOcean, TerrainType.ShallowOcean, TerrainType.FrozenSea));
            Assert.Greater(Count(s, TerrainType.DryBasin), Sphere.TileCount / 4);
            Assert.Greater(Count(s, TerrainType.Desert), 0);
            Assert.AreEqual(0, Count(s, TerrainType.Lowland), "dry lowland is desert");
        }

        [Test]
        public void WaterWorld_IsMostlySea()
        {
            PlanetSurface s = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(5, 15.0, false, true, 0.85));
            int sea = Count(s, TerrainType.DeepOcean, TerrainType.ShallowOcean, TerrainType.FrozenSea);
            Assert.AreEqual(0.85, (double)sea / Sphere.TileCount, 0.02);
        }

        [Test]
        public void HotPlanet_LowlandsAreDesertBesideItsSeas()
        {
            PlanetSurface s = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(5, 60.0, false));
            Assert.Greater(Count(s, TerrainType.ShallowOcean, TerrainType.DeepOcean), 0);
            Assert.Greater(Count(s, TerrainType.Desert), 0);
            Assert.AreEqual(0, Count(s, TerrainType.Lowland));
            Assert.AreEqual(0, Count(s, TerrainType.Ice, TerrainType.FrozenSea, TerrainType.Tundra), "no cold at 60 °C");
        }

        [Test]
        public void Terrains_WaterAlbedoAndNames()
        {
            Assert.IsTrue(Terrains.IsWater(TerrainType.ShallowOcean));
            Assert.IsFalse(Terrains.IsWater(TerrainType.FrozenSea), "frozen seas take buildings");
            Assert.IsFalse(Terrains.IsWater(TerrainType.DryBasin));
            Assert.AreEqual(0.8, Terrains.Albedo(TerrainType.Ice));
            Assert.Greater(Terrains.Albedo(TerrainType.Desert), Terrains.Albedo(TerrainType.Lowland));
            Assert.AreEqual(2, Terrains.BaseSiteRank(TerrainType.Desert));
            Assert.AreEqual(1, Terrains.BaseSiteRank(TerrainType.Tundra));
            Assert.AreEqual(0, Terrains.BaseSiteRank(TerrainType.Mountain));
            foreach (TerrainType t in (TerrainType[])Enum.GetValues(typeof(TerrainType)))
                Assert.AreNotEqual(t.ToString(), "", Terrains.Name(t));
            Assert.AreEqual("Frozen sea", Terrains.Name(TerrainType.FrozenSea));
        }

        [Test]
        public void DifferentSeeds_DifferentContinents()
        {
            var a = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 15.0, false)).Terrain;
            var b = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(543934398, 15.0, false)).Terrain;
            int same = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] == b[i]) same++;
            Assert.Less(same, a.Length * 0.8);
        }
    }
}

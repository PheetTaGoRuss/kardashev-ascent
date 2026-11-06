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
        [TestCase(45.0, 80.0)]
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

        [Test]
        public void Eyeball_IceOutsideTheCircleOnly()
        {
            var settings = new SurfaceSettings(3, 0.62, 50.0, 1.6, true);
            PlanetSurface surface = PlanetSurface.Generate(Sphere, settings);
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                var c = Sphere.Centers[t];
                double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, c.Z / c.Length()))) * 180.0 / Math.PI;
                // The edge moves by ±6° of noise and up to 8° inland on high ground.
                if (angle < 50.0 - 14.5)
                    Assert.AreNotEqual(TerrainType.Ice, surface.Terrain[t], $"tile {t} at {angle:0.0}°");
                else if (angle > 50.0 + 6.5)
                    Assert.AreEqual(TerrainType.Ice, surface.Terrain[t], $"tile {t} at {angle:0.0}°");
            }
        }

        [Test]
        public void ProximaLook_BaseIsOpenLowlandUnderTheStar()
        {
            PlanetRecord r = PlanetRecordTests.ProximaForOtherTests();
            r.look.terrainSeed = 1697376206;
            PlanetSurface surface = PlanetSurface.Generate(Sphere, r.ToSurfaceSettings());
            var sim = new StubPlanetSimulation(Sphere, surface, r);
            Assert.AreEqual(TerrainType.Lowland, surface.Terrain[sim.BaseTileId]);
            Assert.IsTrue(sim.IsDayAtBase);
            int ice = 0;
            foreach (TerrainType t in surface.Terrain) if (t == TerrainType.Ice) ice++;
            Assert.Greater(ice, Sphere.TileCount / 2, "a cold eyeball is mostly ice");
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

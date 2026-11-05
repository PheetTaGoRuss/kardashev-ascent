using System;
using System.Linq;
using System.Numerics;
using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    public class HexSphereTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(20)]
        public void TileCount_Is10NSquaredPlus2_With12Pentagons(int frequency)
        {
            var sphere = HexSphere.Create(frequency);
            Assert.AreEqual(HexSphere.TileCountFor(frequency), sphere.TileCount);
            Assert.AreEqual(12, Enumerable.Range(0, sphere.TileCount).Count(sphere.IsPentagon));
        }

        [TestCase(4)]
        [TestCase(20)]
        public void EveryTile_HasMatchingCornersAndNeighbours(int frequency)
        {
            var sphere = HexSphere.Create(frequency);
            for (int t = 0; t < sphere.TileCount; t++)
            {
                int n = sphere.Neighbors[t].Length;
                Assert.That(n == 5 || n == 6, $"tile {t} has {n} neighbours");
                Assert.AreEqual(n, sphere.Corners[t].Length, $"tile {t} corners");
                Assert.AreEqual(1f, sphere.Centers[t].Length(), 1e-5f, $"tile {t} centre is not on the unit sphere");
                foreach (Vector3 corner in sphere.Corners[t])
                    Assert.AreEqual(1f, corner.Length(), 1e-5f);
            }
        }

        [Test]
        public void Neighbours_AreSymmetricAndClose()
        {
            var sphere = HexSphere.Create(10);
            for (int t = 0; t < sphere.TileCount; t++)
            foreach (int other in sphere.Neighbors[t])
            {
                Assert.Contains(t, sphere.Neighbors[other], $"{other} does not list {t} back");
                float angle = (float)Math.Acos(Vector3.Dot(sphere.Centers[t], sphere.Centers[other]));
                Assert.Less(angle, 2.5f * MathF.PI / (5f * sphere.Frequency), $"{t}–{other} are too far apart");
            }
        }

        [Test]
        public void Corners_GoAroundTheTileInOrder()
        {
            var sphere = HexSphere.Create(8);
            for (int t = 0; t < sphere.TileCount; t++)
            {
                Vector3 c = sphere.Centers[t];
                Vector3[] corners = sphere.Corners[t];
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 a = corners[i] - c, b = corners[(i + 1) % corners.Length] - c;
                    Assert.Greater(Vector3.Dot(Vector3.Cross(a, b), c), 0f, $"tile {t} corner {i} is out of order");
                }
            }
        }

        [Test]
        public void Create_RejectsZeroFrequency()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HexSphere.Create(0));
        }

        [Test]
        public void Surface_HasRequestedOceanShareAndIsDeterministic()
        {
            var sphere = HexSphere.Create(20);
            var settings = new SurfaceSettings(seed: 42, oceanFraction: 0.6);
            var a = PlanetSurface.Generate(sphere, settings);
            var b = PlanetSurface.Generate(sphere, settings);

            CollectionAssert.AreEqual(a.Elevation, b.Elevation);
            double ocean = a.Elevation.Count(e => e < 0f) / (double)sphere.TileCount;
            Assert.AreEqual(0.6, ocean, 0.01);
            Assert.That(a.Terrain, Has.Some.EqualTo(TerrainType.Ice));
            Assert.That(a.Terrain, Has.Some.EqualTo(TerrainType.Lowland));
        }

        [Test]
        public void Surface_DifferentSeedsGiveDifferentPlanets()
        {
            var sphere = HexSphere.Create(10);
            var a = PlanetSurface.Generate(sphere, new SurfaceSettings(1));
            var b = PlanetSurface.Generate(sphere, new SurfaceSettings(2));
            CollectionAssert.AreNotEqual(a.Elevation, b.Elevation);
        }
    }
}

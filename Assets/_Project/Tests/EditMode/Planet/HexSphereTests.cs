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

        [TestCase(3, 20000)]
        [TestCase(24, 20000)]
        [TestCase(48, 5000)]
        public void FindTile_MatchesFullScan_FromAnyStart(int frequency, int lookups)
        {
            var sphere = HexSphere.Create(frequency);
            var random = new Random(1234);
            int previous = 0;
            for (int i = 0; i < lookups; i++)
            {
                var dir = new Vector3((float)Gaussian(random), (float)Gaussian(random), (float)Gaussian(random));
                int start = i % 3 == 0 ? random.Next(sphere.TileCount) : previous;
                int found = sphere.FindTile(dir * 3.7f, start);

                float bestDot = float.NegativeInfinity;
                for (int t = 0; t < sphere.TileCount; t++)
                    bestDot = Math.Max(bestDot, Vector3.Dot(sphere.Centers[t], dir));
                Assert.AreEqual(bestDot, Vector3.Dot(sphere.Centers[found], dir), 1e-6f,
                                $"direction {dir} from tile {start} found {found}, not the closest tile");
                previous = found;
            }
        }

        [Test]
        public void FindTile_TileCentreFindsItself()
        {
            var sphere = HexSphere.Create(16);
            for (int t = 0; t < sphere.TileCount; t++)
                Assert.AreEqual(t, sphere.FindTile(sphere.Centers[t], (t * 7919) % sphere.TileCount));
        }

        static double Gaussian(Random r) =>
            Math.Sqrt(-2.0 * Math.Log(1.0 - r.NextDouble())) * Math.Cos(2.0 * Math.PI * r.NextDouble());

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

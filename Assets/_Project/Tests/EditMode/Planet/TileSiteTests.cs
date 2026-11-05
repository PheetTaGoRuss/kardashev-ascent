using System;
using System.Numerics;
using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    public class TileSiteTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);

        static int Pentagon()
        {
            for (int t = 0; t < Sphere.TileCount; t++)
                if (Sphere.IsPentagon(t))
                    return t;
            throw new InvalidOperationException();
        }

        [TestCase(0, 1)]
        [TestCase(1, 7)]
        [TestCase(3, 37)]
        [TestCase(4, 61)]
        public void SlotCount(int rings, int expected)
        {
            Assert.AreEqual(expected, TileSite.SlotCountFor(rings));
            Assert.AreEqual(expected, TileSite.Create(Sphere, 100, rings).SlotCount);
        }

        [Test]
        public void EverySlot_FindsItself_AndLiesInsideItsTile()
        {
            foreach (int tile in new[] { 100, Pentagon(), Sphere.TileCount - 1 })
            {
                TileSite site = TileSite.Create(Sphere, tile);
                for (int s = 0; s < site.SlotCount; s++)
                {
                    Vector3 d = site.SlotDirections[s];
                    Assert.AreEqual(s, site.FindSlot(d), $"tile {tile} slot {s}");
                    Assert.AreEqual(tile, Sphere.FindTile(d, tile), $"slot {s} of tile {tile} lies inside the tile");
                }
            }
        }

        [Test]
        public void SlotZero_IsTheCentre_AndNeighboursAreOneSpacingApart()
        {
            TileSite site = TileSite.Create(Sphere, 100);
            Assert.Greater(Vector3.Dot(site.SlotDirections[0], site.Centre), 0.999999f);
            for (int s = 1; s <= 6; s++)
            {
                double angle = Math.Acos(Math.Min(1.0, Vector3.Dot(site.SlotDirections[0], site.SlotDirections[s])));
                Assert.AreEqual(site.Spacing, angle, site.Spacing * 0.01, "first ring one spacing out");
            }
        }

        [Test]
        public void PointsBetweenSlots_GoToTheNearest_AndOutsideTheGridIsMinusOne()
        {
            TileSite site = TileSite.Create(Sphere, 100);
            Vector2 a = site.SlotOffset(0), b = site.SlotOffset(1);
            Vector2 nearA = a + (b - a) * 0.3f;
            Assert.AreEqual(0, site.FindSlot(site.DirectionAt(nearA.X, nearA.Y)));
            Vector2 nearB = a + (b - a) * 0.7f;
            Assert.AreEqual(1, site.FindSlot(site.DirectionAt(nearB.X, nearB.Y)));

            double far = site.Spacing * (site.Rings + 2);
            Assert.AreEqual(-1, site.FindSlot(site.DirectionAt(far, 0)));
            Assert.AreEqual(-1, site.FindSlot(-site.Centre), "opposite side of the planet");
        }
    }
}

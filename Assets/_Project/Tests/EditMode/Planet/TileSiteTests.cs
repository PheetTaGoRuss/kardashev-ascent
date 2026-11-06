using System;
using System.Numerics;
using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    public class TileSiteTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(24);

        static int Pentagon()
        {
            for (int t = 0; t < Sphere.TileCount; t++)
                if (Sphere.IsPentagon(t))
                    return t;
            throw new InvalidOperationException();
        }

        static double Area(Vector2[] polygon)
        {
            double area = 0.0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                area += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            return area / 2.0;
        }

        /// <summary>Signed distance from p to the nearest edge of a counter-clockwise polygon (+ inside).</summary>
        static double DistanceInside(Vector2 p, Vector2[] polygon)
        {
            double best = double.MaxValue;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                Vector2 ab = b - a;
                double cross = (double)ab.X * (p.Y - a.Y) - (double)ab.Y * (p.X - a.X);
                best = Math.Min(best, cross / ab.Length());
            }
            return best;
        }

        static int[] SomeTiles() => new[] { 0, 100, 1805, 3000, Pentagon(), Sphere.TileCount - 1 };

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(4)]
        public void HexagonalTile_Has3RRPlus1Slots(int rings)
        {
            foreach (int tile in new[] { 100, 1805, 3000 })
            {
                Assert.IsFalse(Sphere.IsPentagon(tile));
                Assert.AreEqual(TileSite.HexagonSlotCount(rings), TileSite.Create(Sphere, tile, rings).SlotCount, $"tile {tile}");
            }
        }

        [Test]
        public void EverySlotIsAWholeRegularHexagon()
        {
            foreach (int tile in SomeTiles())
            {
                TileSite site = TileSite.Create(Sphere, tile);
                Assert.Greater(site.SlotCount, 30, $"tile {tile}");
                for (int s = 0; s < site.SlotCount; s++)
                    Assert.AreEqual(1.0, site.SlotAreaShare(s), 1e-4, $"tile {tile} slot {s}");
            }
        }

        [Test]
        public void Slots_StayInside_WithABorderToTheEdge()
        {
            foreach (int tile in SomeTiles())
            {
                TileSite site = TileSite.Create(Sphere, tile);
                double closest = double.MaxValue, covered = 0.0;
                for (int s = 0; s < site.SlotCount; s++)
                {
                    covered += Area(site.SlotOutline(s));
                    foreach (Vector2 p in site.SlotOutline(s))
                        closest = Math.Min(closest, DistanceInside(p, site.TileOutline));
                }
                // On the reference hexagon the nearest slot corner is ¾·(1/√3) ≈ 0.72 spacing... measured on the real
                // (slightly uneven) tile it stays a clear border, never touching the edge.
                Assert.Greater(closest, 0.4 * site.Spacing, $"tile {tile}: a border all round");
                Assert.Less(covered / Area(site.TileOutline), 0.95, $"tile {tile}: the border is left empty");
            }
        }

        [Test]
        public void EverySlot_FindsItself()
        {
            foreach (int tile in SomeTiles())
            {
                TileSite site = TileSite.Create(Sphere, tile);
                for (int s = 0; s < site.SlotCount; s++)
                {
                    Vector3 d = site.SlotDirections[s];
                    Assert.AreEqual(s, site.FindSlot(d), $"tile {tile} slot {s}");
                    // FindTile picks the nearest tile centre, which near a corner can be the neighbour.
                    int nearest = Sphere.FindTile(d, tile);
                    Assert.That(nearest == tile || Array.IndexOf(Sphere.Neighbors[tile], nearest) >= 0,
                                $"slot {s} of tile {tile} is on or next to its tile");
                }
            }
        }

        [Test]
        public void SlotZero_IsTheCentre_AndNeighboursAreOneSpacingApart()
        {
            TileSite site = TileSite.Create(Sphere, 1805);
            Assert.Greater(Vector3.Dot(site.SlotDirections[0], site.Centre), 0.999999f);
            for (int s = 1; s <= 6; s++)
            {
                double angle = Math.Acos(Math.Min(1.0, Vector3.Dot(site.SlotDirections[0], site.SlotDirections[s])));
                Assert.AreEqual(site.Spacing, angle, site.Spacing * 0.12, "first ring about one spacing out (tiles are not quite regular)");
            }
        }

        [Test]
        public void TheBorderAndOutsideHaveNoSlot()
        {
            TileSite site = TileSite.Create(Sphere, 1805);
            Vector2 corner = site.TileOutline[0];
            Assert.AreEqual(-1, site.FindSlot(site.DirectionAt(corner.X * 0.97, corner.Y * 0.97)), "in the border near a corner");
            Assert.AreEqual(-1, site.FindSlot(site.DirectionAt(corner.X * 1.05, corner.Y * 1.05)), "just past a corner");
            Assert.AreEqual(0, site.FindSlot(site.Centre), "the centre");
            Assert.AreEqual(-1, site.FindSlot(-site.Centre), "opposite side of the planet");
        }
    }
}

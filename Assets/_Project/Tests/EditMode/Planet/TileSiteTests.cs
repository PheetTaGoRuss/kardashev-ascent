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
        public void Slots_FillTheWholeTile()
        {
            foreach (int tile in SomeTiles())
            {
                TileSite site = TileSite.Create(Sphere, tile);
                double tileArea = Area(site.TileOutline), covered = 0.0;
                for (int s = 0; s < site.SlotCount; s++)
                    covered += Area(site.SlotOutline(s));
                Assert.AreEqual(1.0, covered / tileArea, 0.005, $"tile {tile} ({site.SlotCount} slots) is covered edge to edge");
            }
        }

        [Test]
        public void EverySlotIsWhole_NoHalvesOrCornerPieces()
        {
            foreach (int tile in SomeTiles())
            {
                TileSite site = TileSite.Create(Sphere, tile);
                // Hexagons: the outer ring grows by under 20 %. Pentagons reach further into their corners.
                double most = Sphere.IsPentagon(tile) ? 2.2 : 1.2;
                for (int s = 0; s < site.SlotCount; s++)
                {
                    double share = site.SlotAreaShare(s);
                    Assert.That(share, Is.InRange(0.97, most), $"tile {tile} slot {s}: a whole slot, at most stretched a little");
                }
            }
        }

        [Test]
        public void InnerSlots_StayRegular_OnlyTheOuterRingStretches()
        {
            TileSite site = TileSite.Create(Sphere, 1805);
            int inner = TileSite.HexagonSlotCount(site.Rings - 2);       // rings 0..R−2
            for (int s = 0; s < inner; s++)
                Assert.AreEqual(1.0, site.SlotAreaShare(s), 0.02, $"slot {s}");
        }

        [Test]
        public void EverySlot_FindsItself_AndLiesInsideItsTile()
        {
            foreach (int tile in SomeTiles())
            {
                TileSite site = TileSite.Create(Sphere, tile);
                for (int s = 0; s < site.SlotCount; s++)
                {
                    Vector3 d = site.SlotDirections[s];
                    // FindSlot also checks the point is inside the tile's polygon.
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
        public void PointsOutsideTheTile_HaveNoSlot()
        {
            TileSite site = TileSite.Create(Sphere, 1805);
            Vector2 corner = site.TileOutline[0];
            Assert.AreEqual(-1, site.FindSlot(site.DirectionAt(corner.X * 1.05, corner.Y * 1.05)), "just past a corner");
            Assert.GreaterOrEqual(site.FindSlot(site.DirectionAt(corner.X * 0.97, corner.Y * 0.97)), 0, "just inside a corner");
            Assert.AreEqual(-1, site.FindSlot(-site.Centre), "opposite side of the planet");
        }
    }
}

using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class FootprintMapTests
    {
        [Test]
        public void Occupy_PointsEveryCoveredSlotAtTheAnchor()
        {
            var map = new FootprintMap();
            map.Occupy(7, new List<int> { 4, 5, 9 });
            Assert.AreEqual(4, map.AnchorOrSelf(7, 5));
            Assert.AreEqual(4, map.AnchorOrSelf(7, 9));
            Assert.AreEqual(4, map.AnchorOrSelf(7, 4), "the anchor is its own anchor");
            Assert.AreEqual(3, map.AnchorOrSelf(7, 3), "an uncovered slot answers itself");
            Assert.AreEqual(3, map.AnchorOrSelf(8, 5) == 5 ? 3 : 0, "another tile is unaffected");
            Assert.IsTrue(map.TryGetFootprint(7, 4, out List<int> f));
            CollectionAssert.AreEqual(new[] { 4, 5, 9 }, f);
        }

        [Test]
        public void Vacate_FreesAllItsSlots()
        {
            var map = new FootprintMap();
            map.Occupy(1, new List<int> { 0, 1 });
            map.Occupy(1, new List<int> { 5 });
            map.Vacate(1, 0);
            Assert.AreEqual(1, map.AnchorOrSelf(1, 1));
            Assert.IsFalse(map.TryGetFootprint(1, 0, out _));
            Assert.AreEqual(1, map.BuildingCount, "the other building stays");
            map.Vacate(1, 0);   // vacating twice is harmless
        }
    }
}

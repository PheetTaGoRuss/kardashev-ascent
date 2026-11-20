using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Logistics
{
    /// <summary>The week of flows behind the Flowsheet view (MO-15, U8).</summary>
    public class FlowLedgerTests
    {
        [Test]
        public void FlowsInTheSameDayAndKey_AreMerged()
        {
            var l = new FlowLedger();
            l.Add(5.0, FlowStage.Mined, -1, Material.Iron, 10.0);
            l.Add(20.0, FlowStage.Mined, -1, Material.Iron, 15.0);
            l.Add(20.0, FlowStage.Mined, -1, Material.Rock, 1.0);
            Assert.AreEqual(2, l.Entries.Count);
            Assert.AreEqual(25.0, l.Last(23.0, 7).First(e => e.Material == Material.Iron).Kg, 1e-12);
        }

        [Test]
        public void OnlyTheLastWeekIsKept_AndTheWindowCanBeShorter()
        {
            var l = new FlowLedger();
            l.Add(1.0, FlowStage.Hauled, -1, Material.Iron, 1.0);          // day 0
            l.Add(24.0 * 3 + 1.0, FlowStage.Hauled, -1, Material.Iron, 2.0);   // day 3
            l.Add(24.0 * 9 + 1.0, FlowStage.Hauled, -1, Material.Iron, 4.0);   // day 9
            Assert.AreEqual(6.0, l.Last(24.0 * 9 + 2.0, 7).Sum(e => e.Kg), 1e-12, "day 0 is more than a week old");
            Assert.AreEqual(4.0, l.Last(24.0 * 9 + 2.0, 1).Sum(e => e.Kg), 1e-12, "a one-day window");
            Assert.LessOrEqual(l.Entries.Count, 2);
        }

        [Test]
        public void RestoreKeepsWhatWasSaved()
        {
            var l = new FlowLedger();
            l.Add(3.0, FlowStage.PlantIn, 2, Material.Alumina, 38.6);
            var copy = new FlowLedger();
            copy.Restore(l.Entries.ToList());
            Assert.AreEqual(38.6, copy.Last(10.0, 7).Single().Kg, 1e-12);
            Assert.AreEqual(2, copy.Last(10.0, 7).Single().Key);
        }
    }
}

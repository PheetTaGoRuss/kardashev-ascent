using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The bookkeeping behind saved angle layouts, on its own (no simulation needed).</summary>
    public class AnglePresetBookTests
    {
        const double Deg = System.Math.PI / 180.0;

        [Test]
        public void Add_CleansNames_WrapsFacing_ClampsTilt_AndCountsIds()
        {
            var book = new AnglePresetBook();
            AnglePreset a = book.Add("  Noon  ", "note", 5, 2, 19.8, 0.0, 120.0 * Deg, -90.0 * Deg, 24.0);
            Assert.AreEqual(1, a.Id);
            Assert.AreEqual("Noon", a.Name);
            Assert.AreEqual(90.0, a.TiltDeg, 1e-9);
            Assert.AreEqual(270.0, a.FacingDeg, 1e-9);
            AnglePreset b = book.Add("", "", 5, 2, 19.8, 0.0, 0.0, 0.0, 25.0);
            Assert.AreEqual(2, b.Id);
            Assert.AreEqual("Slot 3", b.Name, "no name: the slot");
            Assert.AreEqual(2, book.All.Count);
        }

        [Test]
        public void UpdateAndDelete()
        {
            var book = new AnglePresetBook();
            AnglePreset p = book.Add("A", "", 1, 0, 0, 0, 0, 0, 0);
            Assert.IsTrue(book.Update(p.Id, "B", "n"));
            Assert.AreEqual("B", book.Find(p.Id).Name);
            Assert.IsFalse(book.Update(99, "x", ""));
            Assert.IsTrue(book.Delete(p.Id));
            Assert.IsFalse(book.Delete(p.Id));
            Assert.IsNull(book.Find(p.Id));
        }

        [Test]
        public void CaptureAndRestore_RoundTrip_IdsKeepCountingUp_NullMeansEmpty()
        {
            var book = new AnglePresetBook();
            book.Add("A", "a", 1, 0, 1, 2, 0.1, 0.2, 3);
            book.Add("B", "b", 2, 1, 4, 5, 0.3, 0.4, 6);
            book.Delete(2);
            var saved = new List<AnglePreset>();
            book.Capture(saved, out int next);
            Assert.AreEqual(1, saved.Count);
            Assert.AreEqual(3, next);

            var other = new AnglePresetBook();
            other.Restore(saved, next);
            Assert.AreEqual("A", other.All[0].Name);
            Assert.AreEqual(3, other.Add("C", "", 1, 0, 0, 0, 0, 0, 0).Id, "ids never repeat after a load");
            other.Restore(null, 0);
            Assert.AreEqual(0, other.All.Count);
        }
    }
}

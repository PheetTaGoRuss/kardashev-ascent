using System;
using System.Linq;
using KardashevAscent.Simulation.Knowledge;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Knowledge
{
    /// <summary>The knowledge store (K1): conflicts, freshness, sync and known-at, events, history cap, determinism, performance.</summary>
    public class KnowledgeBaseTests
    {
        static readonly Subject Planet = Subject.Planet;
        static Observation Obs(double value, double unc, Confidence c, SourceKind src, double observed, double known, int id = 0, Quantity q = Quantity.AirPressure) =>
            new Observation(Planet, q, value, unc, c, new Source(src, id), observed, known);

        [Test]
        public void TheShownRecordIsTheMostRecentOfTheHighestConfidence_AndBothAreKept()
        {
            var kb = new KnowledgeBase();
            kb.Add(Obs(90_000, 1000, Confidence.High, SourceKind.MeasuredByColony, 10, 10), 10);
            kb.Add(Obs(95_000, 500, Confidence.Low, SourceKind.Robot, 20, 20, 3), 20);
            Assert.AreEqual(90_000, kb.Shown(Planet, Quantity.AirPressure).Value.Value, "a later low-confidence record does not replace a high one");
            kb.Add(Obs(91_000, 800, Confidence.High, SourceKind.MeasuredByColony, 30, 30), 30);
            Assert.AreEqual(91_000, kb.Shown(Planet, Quantity.AirPressure).Value.Value, "the later of two high ones");
            Assert.AreEqual(3, kb.History(Planet, Quantity.AirPressure).Count, "all are kept");
            Assert.AreEqual(1, kb.Conflicts(Planet, Quantity.AirPressure).Count, "the 95 kPa record disagrees beyond both ranges");
        }

        [Test]
        public void AnUnsyncedRecordIsNeverShown_UntilItsRobotSyncs_AndAnUnrecoveredOneIsDropped()
        {
            var kb = new KnowledgeBase();
            kb.Add(Obs(100.0, 1.0, Confidence.Medium, SourceKind.Robot, 5, -1, 7, Quantity.WindSpeed), 5);
            Assert.IsFalse(kb.Shown(Planet, Quantity.WindSpeed).HasValue);
            Assert.AreEqual(1, kb.Unsynced(7).Count);
            Assert.AreEqual(1, kb.Sync(7, 50));
            var shown = kb.Shown(Planet, Quantity.WindSpeed).Value;
            Assert.AreEqual(50, shown.KnownAt); Assert.AreEqual(5, shown.ObservedAt, "known-at is not observed-at");
            kb.Add(Obs(1.0, 0.1, Confidence.Low, SourceKind.Robot, 60, -1, 9, Quantity.WindSpeed), 60);
            Assert.AreEqual(1, kb.DropUnsynced(9));
            Assert.AreEqual(2 - 1, kb.TotalRecords());
        }

        [Test]
        public void Events_FirstMeasuredOverAnEstimate_RangeNarrowed_Discrepancy()
        {
            var kb = new KnowledgeBase();
            kb.Add(Obs(78_000, 49_000, Confidence.Low, SourceKind.Derived, 0, 0), 0);   // estimated 29 to 127 kPa
            Assert.AreEqual(KnowledgeEventKind.FirstMeasured, kb.TakeEvents().Single().Kind, "the first record of anything");
            kb.Add(Obs(93_900, 1_500, Confidence.High, SourceKind.MeasuredByColony, 100, 100), 100);
            var e = kb.TakeEvents().Single();
            Assert.AreEqual(KnowledgeEventKind.FirstMeasured, e.Kind);
            Assert.AreEqual(78_000, e.OldValue); Assert.AreEqual(49_000, e.OldUncertainty);
            kb.Add(Obs(93_700, 500, Confidence.High, SourceKind.MeasuredByColony, 200, 200), 200);
            Assert.AreEqual(KnowledgeEventKind.RangeNarrowed, kb.TakeEvents().Single().Kind, "the range shrank by two thirds");
            kb.Add(Obs(70_000, 500, Confidence.High, SourceKind.Robot, 300, 300, 2), 300);
            Assert.AreEqual(KnowledgeEventKind.Discrepancy, kb.TakeEvents().Single().Kind, "far outside both ranges");
            kb.Add(Obs(70_100, 200, Confidence.High, SourceKind.Robot, 310, 310, 2), 310);
            Assert.AreEqual(KnowledgeEventKind.RangeNarrowed, kb.TakeEvents().Single().Kind, "agreeing with the shown value, narrower: no discrepancy");
        }

        [Test]
        public void Freshness_AValueGoesStaleAfterItsQuantitysTime_AndIsReportedOnce()
        {
            var kb = new KnowledgeBase();
            kb.Add(Obs(5.0, 0.5, Confidence.High, SourceKind.MeasuredByColony, 0, 0, 0, Quantity.HubWindSpeed), 0);
            Assert.IsFalse(KnowledgeBase.IsStale(kb.Shown(Planet, Quantity.HubWindSpeed).Value, 24.0 * 10));
            Assert.IsTrue(KnowledgeBase.IsStale(kb.Shown(Planet, Quantity.HubWindSpeed).Value, 24.0 * 40));
            Assert.AreEqual(0, kb.NewlyStale(24.0 * 10).Count);
            Assert.AreEqual(1, kb.NewlyStale(24.0 * 40).Count);
            Assert.AreEqual(0, kb.NewlyStale(24.0 * 41).Count, "once");
            Assert.AreEqual("3 d ago", Quantities.Age(72.0));
        }

        [Test]
        public void TheHistoryIsDailyForAYear_ThenWeekly()
        {
            var kb = new KnowledgeBase();
            double now = 0;
            for (int h = 0; h < 24 * 365 * 2; h += 6)   // a record every 6 hours for two years
            {
                now = h;
                kb.Add(Obs(100.0 + (h % 7), 1.0, Confidence.Medium, SourceKind.MeasuredByColony, h, h, 0, Quantity.AirTemperature), now);
            }
            int n = kb.History(Planet, Quantity.AirTemperature).Count;
            TestContext.WriteLine($"two years of a record every 6 h: {2 * 365 * 4} written, {n} kept");
            Assert.Less(n, 365 + 53 + 96 + 4, "about a year of days, a year of weeks, and the latest few");
            Assert.Greater(n, 300);
        }

        [Test]
        public void SaveRoundTrip_AndDeterminism()
        {
            var a = new KnowledgeBase(); var b = new KnowledgeBase();
            foreach (var kb in new[] { a, b })
            {
                for (int i = 0; i < 50; i++) kb.Add(new Observation(Subject.Tile(i % 5), (Quantity)(i % 4), i, 0.5, Confidence.Medium, new Source(SourceKind.Robot, i % 3), i, i % 7 == 0 ? -1 : i), i);
            }
            Assert.AreEqual(a.All().Count, b.All().Count);
            CollectionAssert.AreEqual(a.All().Select(o => o.Value), b.All().Select(o => o.Value));
            var c = new KnowledgeBase(); c.Restore(a.All());
            Assert.AreEqual(a.TotalRecords(), c.TotalRecords());
            Assert.AreEqual(a.Shown(Subject.Tile(1), (Quantity)1)?.Value, c.Shown(Subject.Tile(1), (Quantity)1)?.Value);
        }

        [Test]
        public void Performance_ARecordCostsMicroseconds_AndTwoYearsOfWeatherIsBounded()
        {
            var kb = new KnowledgeBase();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int writes = 0;
            // 3 sites, 4 quantities, one record a day for two years
            for (int d = 0; d < 730; d++)
                for (int site = 0; site < 3; site++)
                    for (int q = 0; q < 4; q++) { kb.Add(new Observation(Subject.Tile(site), (Quantity)(q + 4), 10.0 + d % 5, 0.5, Confidence.Medium, new Source(SourceKind.MeasuredByColony), d * 24.0, d * 24.0), d * 24.0); writes++; }
            sw.Stop();
            double perWriteUs = sw.Elapsed.TotalMilliseconds * 1000.0 / writes;
            TestContext.WriteLine($"{writes} writes in {sw.Elapsed.TotalMilliseconds:0} ms = {perWriteUs:0.0} us each; {kb.TotalRecords()} records held");
            Assert.Less(perWriteUs, 200.0);
            Assert.AreEqual(writes, 730 * 12);
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10_000; i++) kb.Shown(Subject.Tile(1), Quantity.AirTemperature);
            TestContext.WriteLine($"10000 reads of the shown record: {sw2.Elapsed.TotalMilliseconds:0.0} ms");
        }
    }
}

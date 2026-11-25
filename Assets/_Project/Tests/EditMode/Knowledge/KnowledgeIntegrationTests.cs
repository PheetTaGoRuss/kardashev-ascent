using System;
using System.Linq;
using System.Reflection;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Knowledge;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Knowledge
{
    /// <summary>The knowledge store in the simulation (K2, K5, K6): writers, reports, sync and wrecks, save, migration, growth.</summary>
    public class KnowledgeIntegrationTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Earth => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);
        static PlanetSimulation Make() => new PlanetSimulation(Sphere, Surface, Earth, Kardashev.EarthRadiusM) { Clouds = CloudModel.EarthClimatology };
        const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

        static int FarLand(PlanetSimulation sim) => Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater);

        static void Hold(PlanetSimulation sim, Robot r, int tile)
        {
            var held = new HeldFinding { RobotId = r.Id, Kind = FindingKind.SurveyRockOnly, Tile = tile };
            sim.heldFindings.Add(held);
            typeof(PlanetSimulation).GetMethod("KnowHeldFinding", NonPublic).Invoke(sim, new object[] { held });
        }

        [Test]
        public void TheLandingWritesDerivedEstimates_AndAnExperimentThenMeasuresOverThem_WithAFirstMeasuredReport()
        {
            var sim = Make();
            sim.Tick(25.0);
            var est = sim.KnowledgeStore.Shown(Subject.Planet, Quantity.AirPressure).Value;
            Assert.AreEqual(SourceKind.Derived, est.Source.Kind);
            Assert.Greater(est.Uncertainty, 0.0, "a range, not a number");
            Assert.IsFalse(sim.OperationsLog.Any(o => o.Quantity == "Measured"), "the first estimates are not news");
            sim.LearnExperimentForTests(ExperimentKind.AirPressure);
            typeof(PlanetSimulation).GetMethod("OnExperimentFinished", NonPublic).Invoke(sim, new object[] { ExperimentKind.AirPressure });
            sim.Tick(25.0);
            Assert.AreEqual(SourceKind.MeasuredByColony, sim.KnowledgeStore.Shown(Subject.Planet, Quantity.AirPressure).Value.Source.Kind, "an experiment done makes it a measurement");
            sim.AnswerFitPuzzle(ExperimentKind.AirPressure, sim.PuzzleFor(ExperimentKind.AirPressure).Best);   // the fit narrows the range
            sim.Tick(25.0);
            var now = sim.KnowledgeStore.Shown(Subject.Planet, Quantity.AirPressure).Value;
            Assert.Less(now.Uncertainty, est.Uncertainty);
            var report = sim.OperationsLog.Last(o => o.Quantity == "Measured");
            StringAssert.Contains("Air pressure", report.Text);
            StringAssert.Contains("was estimated", report.Text);
        }

        [Test]
        public void WeatherAtTheLander_IsOneRecordADay_AndNeverADiscrepancy()
        {
            var sim = Make();
            for (int d = 0; d < 30; d++) sim.Tick(24.0);
            var hist = sim.KnowledgeStore.History(Subject.Tile(sim.BaseTileId), Quantity.AirTemperature);
            Assert.GreaterOrEqual(hist.Count, 28); Assert.LessOrEqual(hist.Count, 31);
            Assert.IsFalse(sim.OperationsLog.Any(o => o.Quantity == "Discrepancy"));
        }

        [Test]
        public void ADiscrepancy_IsReported_BothRecordsKept_AndTheMostRecentHighConfidenceIsShown()
        {
            var sim = Make();
            sim.Tick(25.0);
            var kb = sim.KnowledgeStore;
            double t = sim.TimeHours;
            kb.Add(new Observation(Subject.Planet, Quantity.SurfaceGravity, 9.8, 0.05, Confidence.High, new Source(SourceKind.MeasuredByColony), t, t), t);
            kb.TakeEvents();
            kb.Add(new Observation(Subject.Planet, Quantity.SurfaceGravity, 11.0, 0.05, Confidence.High, new Source(SourceKind.Robot, 2), t + 1, t + 1), t + 1);
            Assert.AreEqual(KnowledgeEventKind.Discrepancy, kb.PendingEvents.Single().Kind);
            Assert.AreEqual(11.0, kb.Shown(Subject.Planet, Quantity.SurfaceGravity).Value.Value);
            Assert.AreEqual(1, kb.Conflicts(Subject.Planet, Quantity.SurfaceGravity).Count);
        }

        [Test]
        public void ACutOffRobotsFinding_IsAnUnsyncedRecord_AColdWreckKeepsItForRecovery_AndLosingTheElectronicsDropsIt()
        {
            var sim = Make();
            int tile = FarLand(sim);
            var cold = sim.Workforce.AddRobot(RobotKind.General, tile);
            Hold(sim, cold, tile);
            Assert.IsFalse(sim.KnowledgeStore.Shown(Subject.Tile(tile), Quantity.SurveyCompleted).HasValue, "not known yet");
            Assert.AreEqual(1, sim.KnowledgeStore.Unsynced(cold.Id).Count);
            Wreck w = sim.DestroyForTests(cold.Id, -40.0);
            Assert.AreEqual(1, sim.KnowledgeStore.Unsynced(cold.Id).Count, "the electronics survived the cold: the record waits in the wreck");
            Assert.AreEqual(1, w.Findings.Count);
            // empty the pile (as a recovery does) and the wreck is cleared: the findings are read back and synchronised
            sim.InventoriesForTests.Remove(PlaceId.Wreck(w.Id));
            sim.LogisticsEnabled = true;
            sim.Tick(1.0);
            Assert.IsTrue(sim.Wrecks.All(x => x.Recovered));
            Assert.IsTrue(sim.KnowledgeStore.Shown(Subject.Tile(tile), Quantity.SurveyCompleted).HasValue, "recovered and synchronised");
            var hot = sim.Workforce.AddRobot(RobotKind.General, tile);
            Hold(sim, hot, tile);
            sim.DestroyForTests(hot.Id, 500.0);
            Assert.AreEqual(0, sim.KnowledgeStore.Unsynced(hot.Id).Count, "dropped with the electronics");
        }

        [Test]
        public void SaveRoundTrip_KeepsTheRecords_AndAnOlderSaveDerivesItsFirstOnes()
        {
            var sim = Make();
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            for (int d = 0; d < 5; d++) sim.Tick(24.0);
            int n = sim.KnowledgeStore.TotalRecords();
            Assert.Greater(n, 5);
            var state = sim.CaptureState();
            var again = Make(); again.RestoreState(state);
            Assert.AreEqual(n, again.KnowledgeStore.TotalRecords());
            Assert.AreEqual(sim.KnowledgeStore.Shown(Subject.Planet, Quantity.SurfaceGravity).Value.Value, again.KnowledgeStore.Shown(Subject.Planet, Quantity.SurfaceGravity).Value.Value);
            state.Version = 60; state.Observations.Clear();
            var old = Make(); old.RestoreState(state);
            Assert.AreEqual(0, old.KnowledgeStore.TotalRecords());
            old.Tick(25.0);
            Assert.IsTrue(old.KnowledgeStore.Shown(Subject.Planet, Quantity.AirPressure).HasValue, "derived from the state it has");
        }

        [Test]
        public void Determinism_TwoRunsGiveTheSameRecords()
        {
            string Run() { var s = Make(); for (int d = 0; d < 20; d++) s.Tick(24.0); return string.Join(",", s.KnowledgeStore.All().Select(o => $"{o.Subject}{o.Quantity}{o.Value:R}")); }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TwoYears_RecordGrowthAndPerTickCost_Reported()
        {
            var sim = Make();
            int tile = FarLand(sim);
            for (int i = 0; i < 3; i++) sim.Workforce.AddRobot(RobotKind.General, tile);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int h = 0; h < 24 * 365 * 2; h++) sim.Tick(1.0);
            sw.Stop();
            var off = Make(); off.KnowledgeEnabled = false;
            for (int i = 0; i < 3; i++) off.Workforce.AddRobot(RobotKind.General, tile);
            var swOff = System.Diagnostics.Stopwatch.StartNew();
            for (int h = 0; h < 24 * 365 * 2; h++) off.Tick(1.0);
            swOff.Stop();
            TestContext.WriteLine($"2 years: {sim.KnowledgeStore.TotalRecords()} records held ({sim.KnowledgeStore.Count} counted), {sim.KnowledgeStore.Keys().Count} series; with knowledge {sw.Elapsed.TotalMilliseconds:0} ms, without {swOff.Elapsed.TotalMilliseconds:0} ms: {(sw.Elapsed.TotalMilliseconds - swOff.Elapsed.TotalMilliseconds) / (24.0 * 365 * 2):0.000} ms per tick extra");
            Assert.Less(sim.KnowledgeStore.TotalRecords(), 20_000);
        }

        [Test]
        public void AStaleValue_IsReportedOnce_WhenTheDayTurns()
        {
            var sim = Make();
            sim.Tick(30.0);
            double t = sim.TimeHours;
            sim.KnowledgeStore.Add(new Observation(Subject.Tile(sim.BaseTileId), Quantity.HubWindSpeed, 6.1, 0.5, Confidence.High, new Source(SourceKind.MeasuredByColony), t - 24.0 * 40.0, t), t);   // observed 40 days ago, known now
            sim.Tick(26.0);
            Assert.AreEqual(1, sim.OperationsLog.Count(o => o.Quantity == "Value stale"));
            sim.Tick(48.0);
            Assert.AreEqual(1, sim.OperationsLog.Count(o => o.Quantity == "Value stale"), "once");
        }
    }
}

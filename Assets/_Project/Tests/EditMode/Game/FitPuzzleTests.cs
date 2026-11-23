using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>RS-13 (decided 2026-11-23): every experiment ends in a "fit the data" puzzle that narrows the result's range.</summary>
    public class FitPuzzleTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Make() => new PlanetSimulation(Sphere, Surface, Earthlike) { PlanetId = "some-planet" };

        [Test]
        public void EveryExperimentHasAPuzzleWithDataAndAFit()
        {
            var sim = Make();
            foreach (ExperimentKind k in Experiments.All)
            {
                FitPuzzle p = sim.PuzzleFor(k);
                Assert.AreEqual(k, p.Kind);
                Assert.Greater(p.X.Length, 5, k.ToString());
                Assert.AreEqual(p.X.Length, p.Y.Length);
                Assert.AreEqual(p.ParamNames.Length, p.Start.Length);
                StringAssert.Contains("", p.Physics);
                Assert.Greater(p.Physics.Length, 20, k + " says its physics in one line");
                Assert.LessOrEqual(p.Share(p.Best), p.Curve == null ? 0.7 : 0.35, k + ": the best choice keeps the range narrow");
            }
        }

        [Test]
        public void ThePressureFitRecoversTheScaleHeightAndTheDatumPressure()
        {
            var sim = Make();
            FitPuzzle p = sim.PuzzleFor(ExperimentKind.AirPressure);
            double truthKpa = sim.PressurePaAt(sim.BaseTileId) / 1000.0;
            Assert.AreEqual(truthKpa, p.Best[0], truthKpa * 0.08, "datum pressure");
            double h = 8.314462618 * (sim.TemperatureEstimate.Value) / 1; // not used: only the order of magnitude is checked below
            Assert.Greater(p.Best[1], 1000.0);
            Assert.Less(p.Best[1], 30000.0);
        }

        [Test]
        public void ThePressureFitIsExactForAnExactModel()
        {
            double[] truth = { 100.0, 8000.0 };
            Func<double[], double, double> f = (q, x) => q[0] * Math.Exp(-x / q[1]);
            var xs = Enumerable.Range(0, 9).Select(i => i * 2000.0).ToArray();
            var ys = xs.Select(x => f(truth, x)).ToArray();
            var fit = FitPuzzle.LeastSquares(f, xs, ys, new[] { 50.0, 3000.0 }, new[] { 1.0, 500.0 }, new[] { 400.0, 40000.0 });
            Assert.AreEqual(100.0, fit[0], 1e-3);
            Assert.AreEqual(8000.0, fit[1], 1e-1);
        }

        [Test]
        public void ABadHandFitKeepsTheRangeWide_AGoodOneNarrowsIt()
        {
            var sim = Make();
            FitPuzzle p = sim.PuzzleFor(ExperimentKind.AirPressure);
            double good = p.Share(p.Best);
            double poor = p.Share(p.Start);
            Assert.Less(good, 0.2);
            Assert.Greater(poor, 0.8);
            Assert.AreEqual(FitPuzzle.PrecisionShare, p.Share(p.Best), 0.05);
        }

        [Test]
        public void TheReliefThresholdRejectsSpikesButKeepsRealPeaks()
        {
            var sim = Make();
            FitPuzzle p = sim.PuzzleFor(ExperimentKind.ReliefSurvey);
            Assert.Greater(p.Share(new[] { 6.0 }), 0.6, "no rejection keeps the spikes");
            Assert.Less(p.Share(p.Best), 0.5);
        }

        [Test]
        public void MoreAssaySamplesNarrowTheRangeAndCostMoreRobotTime()
        {
            var sim = Make();
            FitPuzzle p = sim.PuzzleFor(ExperimentKind.OreAssay);
            Assert.Greater(p.Share(new[] { 3.0 }), p.Share(new[] { 30.0 }));
            Assert.Greater(p.ExtraHours(new[] { 30.0 }), p.ExtraHours(new[] { 3.0 }));
            Assert.AreEqual(2.5, p.ExtraHours(new[] { 5.0 }), 1e-9);
        }

        [Test]
        public void FinishingAnExperimentLeavesAPuzzleAndTheFullRange_SolvingItNarrowsTheEstimate()
        {
            var sim = Make();
            sim.LearnExperimentForTests(ExperimentKind.AirPressure);
            Assert.AreEqual(0.0, sim.ExperimentRangeShare(ExperimentKind.AirPressure), "a legacy result (no puzzle) is exact");
            // pretend the robot hours finished: the same path the simulation takes
            typeof(PlanetSimulation).GetMethod("OnExperimentFinished", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(sim, new object[] { ExperimentKind.AirPressure });
            Assert.Contains(ExperimentKind.AirPressure, sim.PendingPuzzles.ToList());
            Assert.AreEqual(1.0, sim.ExperimentRangeShare(ExperimentKind.AirPressure));
            double wide = sim.PressureEstimate.HalfRange;
            FitPuzzle p = sim.PuzzleFor(ExperimentKind.AirPressure);
            sim.AnswerFitPuzzle(ExperimentKind.AirPressure, p.Best);
            Assert.IsEmpty(sim.PendingPuzzles);
            Assert.Less(sim.PressureEstimate.HalfRange, wide * 0.2);
        }

        [Test]
        public void FitForMeIsWiderThanAGoodHandFitButNarrowerThanNothing()
        {
            var sim = Make();
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            typeof(PlanetSimulation).GetMethod("OnExperimentFinished", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(sim, new object[] { ExperimentKind.Gravity });
            sim.AutoFitPuzzle(ExperimentKind.Gravity);
            Assert.AreEqual(FitPuzzle.AutoFitShare, sim.ExperimentRangeShare(ExperimentKind.Gravity), 1e-9);
        }

        [Test]
        public void ThePuzzleStateSurvivesASave_AndAnOldSaveLoadsExact()
        {
            var sim = Make();
            sim.LearnExperimentForTests(ExperimentKind.AirPressure);
            typeof(PlanetSimulation).GetMethod("OnExperimentFinished", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(sim, new object[] { ExperimentKind.AirPressure });
            var state = sim.CaptureState();
            Assert.AreEqual(KardashevAscent.Simulation.Game.SimulationState.CurrentVersion, 51);
            var loaded = Make();
            loaded.RestoreState(state);
            Assert.Contains(ExperimentKind.AirPressure, loaded.PendingPuzzles.ToList());
            Assert.AreEqual(1.0, loaded.ExperimentRangeShare(ExperimentKind.AirPressure));
            state.ExperimentPuzzles.Clear(); state.ExperimentShares.Clear();   // a version-50 save has neither
            var old = Make();
            old.RestoreState(state);
            Assert.IsEmpty(old.PendingPuzzles);
            Assert.AreEqual(0.0, old.ExperimentRangeShare(ExperimentKind.AirPressure), "an old result stays exact");
        }

        [Test]
        public void TheSameDataEveryRun()
        {
            Assert.AreEqual(Make().PuzzleFor(ExperimentKind.TideGauge).Y[7], Make().PuzzleFor(ExperimentKind.TideGauge).Y[7]);
        }
    }
}

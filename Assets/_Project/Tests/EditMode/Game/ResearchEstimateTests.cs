using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Research tied to the physics (RS-14, decided 2026-11-23): estimates with ranges until measured, then exact.</summary>
    public class ResearchEstimateTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Make() => new PlanetSimulation(Sphere, Surface, Earthlike) { PlanetId = "some-planet" };

        [Test]
        public void AnEstimateHasARangeAroundButNotOnTheTruth_AndIsExactOnceMeasured()
        {
            var sim = Make();
            var e = sim.PressureEstimate;
            double truth = sim.PressurePaAt(sim.BaseTileId);
            Assert.IsFalse(e.IsExact);
            Assert.GreaterOrEqual(truth, e.Low);
            Assert.LessOrEqual(truth, e.High);
            Assert.AreNotEqual(truth, e.Value, "the centre must not give the answer away");
            sim.LearnExperimentForTests(ExperimentKind.AirPressure);
            Assert.IsTrue(sim.PressureEstimate.IsExact);
            Assert.AreEqual(truth, sim.PressureEstimate.Value, 1e-9);
        }

        [Test]
        public void TheEstimatesAreTheSameEveryTimeAndEveryRun()
        {
            Assert.AreEqual(Make().HighestPointEstimate.Value, Make().HighestPointEstimate.Value);
            Assert.AreEqual(Make().TemperatureEstimate.Value, Make().TemperatureEstimate.Value);
        }

        [Test]
        public void EveryQuantityHasItsOwnMeasurement()
        {
            var sim = Make();
            Assert.IsFalse(sim.TemperatureEstimate.IsExact);
            sim.LearnExperimentForTests(ExperimentKind.SurfaceTemperature);
            Assert.IsTrue(sim.TemperatureEstimate.IsExact);

            Assert.IsFalse(sim.HighestPointEstimate.IsExact);
            sim.LearnExperimentForTests(ExperimentKind.ReliefSurvey);
            Assert.IsTrue(sim.HighestPointEstimate.IsExact);
            Assert.AreEqual(sim.Relief.MaxM, sim.HighestPointEstimate.Value, 1e-9);

            int tile = Enumerable.Range(0, sim.TileCount).First(t => !sim.GetTile(t).IsWater);
            sim.LearnExperimentForTests(ExperimentKind.OreAssay);
            Assert.IsTrue(sim.GradeEstimate(tile, Material.Iron).IsExact);

            Assert.IsFalse(sim.TideRangeEstimate.IsExact && sim.TideRangeEstimate.Value > 0.0);
            sim.LearnExperimentForTests(ExperimentKind.TideGauge);
            Assert.IsTrue(sim.TideRangeEstimate.IsExact);
        }

        [Test]
        public void TheNewExperimentsAreOnTheTreeAndNeedWhatTheyShould()
        {
            Assert.Contains(ExperimentKind.ReliefSurvey, Experiments.All);
            Assert.Contains(ExperimentKind.OreAssay, Experiments.All);
            Assert.AreEqual(ExperimentKind.Gravity, Experiments.Needs(ExperimentKind.ReliefSurvey));
            Assert.IsNull(Experiments.Needs(ExperimentKind.OreAssay));
            Assert.AreEqual(8, (int)ExperimentKind.BasinSurvey + 1, "saved integers: new members are appended after the old ones");
            Assert.AreEqual(8, (int)ExperimentKind.ReliefSurvey);
            Assert.AreEqual(9, (int)ExperimentKind.OreAssay);
        }

        [Test]
        public void CablesAreRatedLowerUntilTheAirIsMeasured_NeverBelowHalf()
        {
            var sim = Make();
            double share = sim.CableDesignShare;
            Assert.LessOrEqual(share, 1.0);
            Assert.GreaterOrEqual(share, PlanetSimulation.MinimumCableShare);
            sim.LearnExperimentForTests(ExperimentKind.AirPressure);
            Assert.AreEqual(1.0, sim.CableDesignShare);
        }

        [Test]
        public void TheReliefSurveyAndAssayRunAsExperimentsAndSayWhatTheyFound()
        {
            var sim = Make();
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            sim.LearnExperimentForTests(ExperimentKind.ReliefSurvey);
            StringAssert.Contains("km", sim.ExperimentResult(ExperimentKind.ReliefSurvey));
            sim.LearnExperimentForTests(ExperimentKind.OreAssay);
            StringAssert.Contains("true grade", sim.ExperimentResult(ExperimentKind.OreAssay));
        }

        [Test]
        public void AnOldSaveWithoutTheNewExperimentsLoads_AndKeepsWhatItKnew()
        {
            var sim = Make();
            sim.LearnExperimentForTests(ExperimentKind.AirPressure);
            var state = sim.CaptureState();
            var loaded = Make();
            loaded.RestoreState(state);
            Assert.IsTrue(loaded.IsExperimentKnown(ExperimentKind.AirPressure));
            Assert.IsFalse(loaded.IsExperimentKnown(ExperimentKind.ReliefSurvey));
        }
    }
}

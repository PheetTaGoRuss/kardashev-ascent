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

        [Test]
        public void ARobotBelowItsChargerKeepsAMoreCautiousClimbReserveUntilTheReliefIsSurveyed()
        {
            double Reserve(bool surveyed)
            {
                var sim = Make();
                sim.ConstructionTimeEnabled = false;
                sim.Debug.FreeBuilding = true;
                Assert.IsTrue(sim.PlaceChargingStation(sim.BaseTileId, 20));
                var tiles = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && sim.TileHeightM(t) < sim.TileHeightM(sim.BaseTileId) - 300.0).ToList();
                if (tiles.Count == 0) Assert.Ignore("no tile below the lander on this surface");
                int low = tiles.OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).First(t => !double.IsInfinity(sim.Routes.HoursBetween(sim.BaseTileId, t)));
                var r = sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.General, low);
                if (surveyed) sim.LearnExperimentForTests(ExperimentKind.ReliefSurvey);
                sim.Tick(0.01);
                return r.ReserveFraction;
            }
            Assert.Greater(Reserve(false), Reserve(true), "the climb back is reserved at the high end of the relief range until it is measured");
        }

        [Test]
        public void TheMineRateIsARangeUntilTheAssay()
        {
            var sim = Make();
            int tile = Enumerable.Range(0, sim.TileCount).First(t => !sim.GetTile(t).IsWater && sim.Deposits.GradeOf(t, Material.Iron) > 0.0);
            var r = sim.MiningRateEstimate(tile, Material.Iron);
            Assert.IsFalse(r.IsExact);
            double truth = KardashevAscent.Simulation.Robots.RobotSpecs.MiningKgPerHour * sim.Deposits.GradeOf(tile, Material.Iron);
            Assert.GreaterOrEqual(truth, r.Low - 1e-9);
            Assert.LessOrEqual(truth, r.High + 1e-9);
            sim.LearnExperimentForTests(ExperimentKind.OreAssay);
            Assert.AreEqual(truth, sim.MiningRateEstimate(tile, Material.Iron).Value, 1e-9);
        }

        [Test]
        public void RockIsNeverARange()
        {
            var sim = Make();
            int tile = Enumerable.Range(0, sim.TileCount).First(t => !sim.GetTile(t).IsWater);
            Assert.IsTrue(sim.GradeEstimate(tile, Material.Rock).IsExact);
            Assert.IsTrue(sim.MiningRateEstimate(tile, Material.Rock).IsExact);
        }

        [Test]
        public void TheSimulationHasWeather_ClimatologyPlusTheDaysAnomaly_AndAnOldSaveHasNone()
        {
            var sim = Make();
            Assert.IsTrue(sim.HasWeather);
            var w = sim.WeatherAt(sim.BaseTileId);
            Assert.Greater(w.WindMs, 0.0);
            Assert.Greater(w.PressurePa, 0.0);
            var state = sim.CaptureState();
            Assert.IsTrue(state.WeatherOn);
            state.Version = 55;
            var old = Make();
            old.RestoreState(state);
            Assert.IsFalse(old.HasWeather, "a save before version 56 keeps the old, weatherless balance");
            Assert.AreEqual(0.0, old.WeatherAt(old.BaseTileId).WindMs);
        }

        [Test]
        public void AnAirlessWorldHasNoWeatherInTheSimulation()
        {
            var p = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 0.4, 5.0, 24.0, 8760.0, 0.0, 0.3, -50.0, false, 0.0);
            var sim = new PlanetSimulation(Sphere, Surface, p);
            Assert.IsFalse(sim.HasWeather);
        }
    }
}

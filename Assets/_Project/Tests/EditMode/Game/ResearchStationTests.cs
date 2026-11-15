using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// The Research Station (director 2026-11-14): measuring and experiments happen there, the experiments' physics,
    /// what the colony knows before landing, and how knowledge gates buildings and sets safety margins.
    /// </summary>
    public class ResearchStationTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false, PuzzleChance = 0.0 };

        static int FreeSlot(PlanetSimulation sim, BuildingType type, int tile)
        {
            for (int s = 0; s < sim.SlotCount(tile); s++)
                if (sim.CanPlace(type, tile, s)) return s;
            return -1;
        }

        // ---- physics ----

        // Steam-table boiling points of water (IAPWS-IF97, as in standard steam tables): kPa → °C.
        [TestCase(1.0, 6.97)]
        [TestCase(10.0, 45.81)]
        [TestCase(50.0, 81.32)]
        [TestCase(101.325, 100.0)]
        [TestCase(200.0, 120.21)]
        [TestCase(1000.0, 179.88)]
        public void BoilingPoint_MatchesSteamTables(double kPa, double expectedC) =>
            Assert.AreEqual(expectedC, Experiments.WaterBoilingPointC(kPa * 1000.0), 0.3);

        [Test]
        public void BelowTheTriplePoint_WaterCantBeLiquid()
        {
            Assert.IsNaN(Experiments.WaterBoilingPointC(600.0), "Mars-like air (about 0.6 kPa)");
            Assert.IsNaN(Experiments.WaterBoilingPointC(0.0), "no air");
            Assert.IsFalse(Experiments.WaterStaysLiquid(600.0, 10.0, 20.0));
            Assert.IsTrue(Experiments.WaterStaysLiquid(Experiments.StandardPressurePa, 5.0, 30.0));
            Assert.IsFalse(Experiments.WaterStaysLiquid(Experiments.StandardPressurePa, -5.0, 30.0), "freezes in winter");
        }

        [Test]
        public void Pendulum_PeriodAndGravity()
        {
            double t = Experiments.PendulumPeriodS(1.0, 9.80665);
            Assert.AreEqual(2.006, t, 0.001, "a 1 m pendulum on Earth swings in about 2 s");
            Assert.AreEqual(9.80665, Experiments.GravityFromPeriod(1.0, t), 1e-9);
            Assert.AreEqual(t * Math.Sqrt(9.80665 / 3.71), Experiments.PendulumPeriodS(1.0, 3.71), 1e-9, "slower in Mars gravity");
        }

        [TestCase(15.0, OrbitalBrief.RobotVerdict.Works)]
        [TestCase(-30.0, OrbitalBrief.RobotVerdict.NeedsHeatingOrCooling)]
        [TestCase(60.0, OrbitalBrief.RobotVerdict.NeedsHeatingOrCooling)]
        [TestCase(-100.0, OrbitalBrief.RobotVerdict.CannotSurvive)]
        [TestCase(120.0, OrbitalBrief.RobotVerdict.CannotSurvive)]
        public void RobotsBrief_FromTheMeanTemperature(double c, OrbitalBrief.RobotVerdict expected) =>
            Assert.AreEqual(expected, OrbitalBrief.Robots(c));

        // ---- the station ----

        [Test]
        public void Measuring_NeedsAResearchStation()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsFalse(sim.HasResearchStation);
            Assert.IsNotNull(sim.MeasureBlocker(SolarParameter.Spectrum, t));
            Assert.IsFalse(sim.OrderMeasure(SolarParameter.Spectrum, t), "no station yet");
            Assert.IsTrue(sim.PlaceResearchStation(t, FreeSlot(sim, BuildingType.ResearchStation, t)));
            Assert.IsTrue(sim.HasResearchStation);
            Assert.IsNull(sim.MeasureBlocker(SolarParameter.Spectrum, t));
            Assert.IsTrue(sim.OrderMeasure(SolarParameter.Spectrum, t));
            for (int i = 0; i < 40 && !sim.Parameters.IsMeasured(SolarParameter.Spectrum); i++)
                sim.Tick(1.0);
            Assert.IsTrue(sim.Parameters.IsMeasured(SolarParameter.Spectrum), "a robot measured it at the station");
        }

        [Test]
        public void Latitude_IsMeasuredOnlyWhereAStationStands()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.AddFinishedResearchStationForTests(t);
            int other = -1;
            for (int i = 0; i < sim.TileCount && other < 0; i++)
                if (i != t && !sim.GetTile(i).IsWater) other = i;
            Assert.IsNotNull(sim.MeasureBlocker(SolarParameter.Latitude, other));
            Assert.IsFalse(sim.OrderMeasure(SolarParameter.Latitude, other));
            Assert.IsTrue(sim.OrderMeasure(SolarParameter.Latitude, t));
        }

        [Test]
        public void AStationUnderConstruction_DoesntMeasureYet()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);   // construction takes time
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlaceResearchStation(t, FreeSlot(sim, BuildingType.ResearchStation, t)));
            Assert.IsFalse(sim.HasResearchStation, "still a building site");
            Assert.IsFalse(sim.OrderMeasure(SolarParameter.Dust, t));
        }

        [Test]
        public void Experiments_RunAtTheStation_InOrder()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsNotNull(sim.ExperimentBlocker(ExperimentKind.AirPressure), "no station");
            Assert.IsFalse(sim.OrderExperiment(ExperimentKind.AirPressure));
            sim.AddFinishedResearchStationForTests(t);
            Assert.IsNotNull(sim.ExperimentBlocker(ExperimentKind.BoilingPoint), "air pressure first");
            Assert.IsFalse(sim.OrderExperiment(ExperimentKind.BoilingPoint));
            Assert.IsTrue(sim.OrderExperiment(ExperimentKind.AirPressure));
            Assert.IsFalse(sim.OrderExperiment(ExperimentKind.AirPressure), "already running");
            ExperimentKind? done = null;
            sim.ExperimentDone += k => done = k;
            for (int i = 0; i < 40 && !sim.IsExperimentKnown(ExperimentKind.AirPressure); i++)
                sim.Tick(1.0);
            Assert.IsTrue(sim.IsExperimentKnown(ExperimentKind.AirPressure));
            Assert.AreEqual(ExperimentKind.AirPressure, done);
            StringAssert.Contains("kPa", sim.ExperimentResult(ExperimentKind.AirPressure));
            Assert.IsNull(sim.ExperimentBlocker(ExperimentKind.BoilingPoint));
            Assert.IsTrue(sim.OrderExperiment(ExperimentKind.BoilingPoint));
            for (int i = 0; i < 60 && !sim.IsExperimentKnown(ExperimentKind.BoilingPoint); i++)
                sim.Tick(1.0);
            StringAssert.Contains("boils at", sim.ExperimentResult(ExperimentKind.BoilingPoint));
        }

        [Test]
        public void Experiments_TakeTheirHours()
        {
            var sim = Create();
            sim.AddFinishedResearchStationForTests(sim.BaseTileId);
            Assert.IsTrue(sim.OrderExperiment(ExperimentKind.SurfaceTemperature));
            sim.Tick(Experiments.WorkHours(ExperimentKind.SurfaceTemperature) / 4.0);
            Assert.IsFalse(sim.IsExperimentKnown(ExperimentKind.SurfaceTemperature), "a day's log, not a minute's");
            Assert.Greater(sim.ExperimentProgress(ExperimentKind.SurfaceTemperature), 0.0);
            Assert.IsTrue(sim.CancelExperiment(ExperimentKind.SurfaceTemperature));
            Assert.AreEqual(0.0, sim.ExperimentProgress(ExperimentKind.SurfaceTemperature));
        }

        [Test]
        public void ExperimentResults_ComeFromThePlanet()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.AreEqual(Experiments.StandardPressurePa, sim.PressurePaAt(t), Experiments.StandardPressurePa * 0.2, "Earth-like air");
            var (lo, hi) = sim.SurfaceTemperatureRange(t);
            Assert.LessOrEqual(lo, hi);
            Assert.AreEqual(PlanetSimulation.StandardGravityMs2, sim.SurfaceGravityMs2, 1e-9);
            Assert.IsNull(sim.ExperimentResult(ExperimentKind.Gravity), "not done yet");
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            StringAssert.Contains("2.006 s", sim.ExperimentResult(ExperimentKind.Gravity));
        }

        // ---- knowledge gates buildings and sets margins ----

        [Test]
        public void FloatingSolar_NeedsToKnowWaterStaysLiquid()
        {
            var sim = Create();
            int sea = -1;
            for (int i = 0; i < sim.TileCount && sea < 0; i++)
                if (Terrains.AllowsFloating(sim.GetTile(i).Terrain)) sea = i;
            Assume.That(sea >= 0, "this map has shallow sea");
            Assert.IsNotNull(sim.KnowledgeBlocker(BuildingType.FloatingSolar));
            Assert.IsFalse(sim.CanPlace(BuildingType.FloatingSolar, sea, 0));
            sim.LearnExperimentForTests(ExperimentKind.BoilingPoint);
            Assert.IsFalse(sim.CanPlace(BuildingType.FloatingSolar, sea, 0), "still needs the surface temperature");
            sim.LearnExperimentForTests(ExperimentKind.SurfaceTemperature);
            Assert.IsTrue(sim.KnownWaterStaysLiquid.GetValueOrDefault(), "Earth-like: water stays liquid");
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.FloatingSolar));
            Assert.IsTrue(sim.CanPlace(BuildingType.FloatingSolar, sea, 0));
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.SolarPanel), "ordinary panels need no experiment");
        }

        [Test]
        public void UnknownValues_CutSolarOutput_UntilMeasured()
        {
            var sim = Create();
            double share = sim.SolarDesignShare;
            Assert.Less(share, 1.0, "unknown values: designed for the cautious end");
            Assert.GreaterOrEqual(share, PlanetSimulation.MinimumDesignShare);
            foreach (SolarParameter p in (SolarParameter[])Enum.GetValues(typeof(SolarParameter)))
                sim.Parameters.MarkMeasured(p);
            sim.Notebook.RecordLatitude(sim.BaseTileId, sim.GetTile(sim.BaseTileId).Latitude * 180.0 / Math.PI, "test");
            Assert.AreEqual(1.0, sim.SolarDesignShare, 1e-9, "everything known: full output");
        }

        [Test]
        public void LiveOutput_IsTheDesignShareOfThePhysics()
        {
            var a = Create();
            var b = Create();
            b.SafetyMarginsEnabled = false;
            int t = a.BaseTileId;
            double tilt = Math.Abs(a.GetTile(t).Latitude);
            Assert.IsTrue(a.PlacePanel(t, 0, tilt, Math.PI));
            Assert.IsTrue(b.PlacePanel(t, 0, tilt, Math.PI));
            a.Tick(3.0);
            b.Tick(3.0);
            Assume.That(b.GenerationW - b.RtgW > 0.0, "sun up");
            Assert.AreEqual((b.GenerationW - b.RtgW) * a.SolarDesignShare, a.GenerationW - a.RtgW, 1e-6 * (b.GenerationW + 1.0));
        }

        [Test]
        public void Batteries_KeepAMargin_UntilTheSurfaceTemperatureIsKnown()
        {
            var sim = Create();
            Assert.AreEqual(PlanetSimulation.BatteryUnknownTemperatureShare, sim.BatteryUsableShare);
            sim.LearnExperimentForTests(ExperimentKind.SurfaceTemperature);
            Assert.AreEqual(1.0, sim.BatteryUsableShare);

            var cell = new KardashevAscent.Simulation.Energy.Battery(1000.0, startFraction: 0.0) { UsableShare = 0.8 };
            for (int i = 0; i < 100; i++) cell.Charge(1000.0, 1.0);
            Assert.AreEqual(800.0, cell.StoredWh, 1e-6, "charged only to the margin");
        }

        // ---- save ----

        [Test]
        public void Save_KeepsStationsKnowledgeAndOrders()
        {
            var a = Create();
            int t = a.BaseTileId;
            Assert.IsTrue(a.PlaceResearchStation(t, FreeSlot(a, BuildingType.ResearchStation, t)));
            a.LearnExperimentForTests(ExperimentKind.AirPressure);
            a.Workforce.Pause(a.Workforce.Robots[0].GroupId);
            Assert.IsTrue(a.OrderExperiment(ExperimentKind.Gravity));
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsTrue(b.HasResearchStation);
            Assert.IsTrue(b.IsExperimentKnown(ExperimentKind.AirPressure));
            Assert.IsTrue(b.IsExperimentOrdered(ExperimentKind.Gravity));
            Assert.IsFalse(b.IsExperimentKnown(ExperimentKind.Gravity));
        }

        [Test]
        public void AVersion28Save_LoadsWithNoStationAndNothingLearned()
        {
            var a = Create();
            SimulationState state = a.CaptureState();
            state.Version = 28;
            state.KnownExperiments = null;      // fields a version-28 save doesn't have
            state.ExperimentOrders = null;
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsFalse(b.HasResearchStation);
            foreach (ExperimentKind k in Experiments.All)
                Assert.IsFalse(b.IsExperimentKnown(k));
        }

        [Test]
        public void ResearchStation_IsInTheCatalog_AndTheLanderCanAffordIt()
        {
            BuildingDefinition d = BuildingCatalog.Get(BuildingType.ResearchStation);
            Assert.IsNotNull(d);
            Assert.AreEqual("Research Station", d.Name);
            Assert.IsNotNull(d.FixedBill);
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.Research, TaskKind.Experiment, 0));
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.General, TaskKind.Experiment, 0));
            Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Mining, TaskKind.Experiment, 0));
        }
    }
}

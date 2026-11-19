using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Free-flow tidal turbines (director 2026-11-19): where they stand, how the tide drives them, save and load.</summary>
    public class TidalEnergyTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        /// <summary>A planet whose colony has already run the tide gauge (turbines are allowed).</summary>
        static PlanetSimulation Create()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.LearnExperimentForTests(ExperimentKind.TideGauge);
            return sim;
        }

        static PlanetSimulation CreateUnknowing() => new PlanetSimulation(Sphere, Surface, Earthlike);

        /// <summary>The shallow-sea tile where the tide is strongest, and one that is far from land (weakest).</summary>
        static void FindCoasts(PlanetSimulation sim, out int best, out int open)
        {
            best = -1; open = -1;
            double most = -1.0, least = double.MaxValue;
            for (int t = 0; t < sim.TileCount; t++)
            {
                if (sim.GetTile(t).Terrain != TerrainType.ShallowOcean) continue;
                double range = sim.TidalRangeAtM(t);
                if (range > most) { most = range; best = t; }
                if (range < least) { least = range; open = t; }
            }
        }

        [Test]
        public void TheWorldHasAGeneratedMoon_ThatNeverChanges()
        {
            PlanetSimulation a = Create(), b = Create();
            Assert.IsTrue(a.Moon.Exists);
            Assert.AreEqual(a.Moon.MassRatio, b.Moon.MassRatio);
            Assert.AreEqual(a.Moon.DistanceRadii, b.Moon.DistanceRadii);
            Assert.Greater(a.TidePeriodHours, 1.0);
            Assert.Less(a.TidePeriodHours, 200.0);
        }

        [Test]
        public void Tide_IsStrongerWhereLandFunnelsIt_AndZeroOnLand()
        {
            PlanetSimulation sim = Create();
            FindCoasts(sim, out int best, out int open);
            Assert.GreaterOrEqual(best, 0, "the test planet has shallow sea");
            Assert.Greater(sim.TidalRangeAtM(best), sim.TidalRangeAtM(open));
            for (int t = 0; t < sim.TileCount; t++)
                if (!sim.GetTile(t).IsWater) { Assert.AreEqual(0.0, sim.TidalRangeAtM(t)); break; }
        }

        [Test]
        public void ATurbine_OnlyStandsOnShallowSea()
        {
            PlanetSimulation sim = Create();
            Assert.IsFalse(sim.PlaceTidalTurbine(sim.BaseTileId, 0), "not on land");
            FindCoasts(sim, out int best, out _);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceTidalTurbine(best, 0));
            Assert.AreEqual(BuildingType.TidalTurbine, sim.GetBuilding(best, 0));
            Assert.IsFalse(sim.PlaceTidalTurbine(best, 0), "the slot is taken");
        }

        [Test]
        public void ATurbineOnAStrongCoast_MakesPowerThatRisesAndFallsWithTheTide()
        {
            PlanetSimulation sim = Create();
            FindCoasts(sim, out int best, out _);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceTidalTurbine(best, 0));
            Assert.Greater(sim.PeakStreamAtMs(best), Tides.CutInMs, "a good site clears the cut-in speed");
            double min = double.MaxValue, max = 0.0;
            for (int i = 0; i < 200; i++)
            {
                sim.Tick(sim.TidePeriodHours / 50.0);
                double p = sim.TidalPowerW();
                min = Math.Min(min, p);
                max = Math.Max(max, p);
            }
            Assert.Greater(max, 0.0, "the turbine makes power at the peak");
            Assert.AreEqual(0.0, min, 1e-9, "and none at slack water");
            Assert.LessOrEqual(max, Tides.TurbinePowerW(Tides.RatedMs) * 1.0001);
        }

        [Test]
        public void ATurbineInOpenSea_MakesNothing()
        {
            PlanetSimulation sim = Create();
            FindCoasts(sim, out int best, out int open);
            if (sim.PeakStreamAtMs(open) >= Tides.CutInMs) Assert.Ignore("this test planet has no weak shallow-sea tile");
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceTidalTurbine(open, 0));
            for (int i = 0; i < 100; i++)
            {
                sim.Tick(sim.TidePeriodHours / 25.0);
                Assert.AreEqual(0.0, sim.TidalPowerW(), 1e-9);
            }
        }

        [Test]
        public void ASite_MakesNothingUntilFinished_AndItsPowerIsInTheColonyGeneration()
        {
            PlanetSimulation sim = Create();
            FindCoasts(sim, out int best, out _);
            Assert.IsTrue(sim.PlaceTidalTurbine(best, 0));
            Assert.IsTrue(sim.IsUnderConstruction(best, 0));
            sim.Tick(sim.TidePeriodHours * 0.25);
            Assert.AreEqual(0.0, sim.TidalPowerW(), "a construction site makes nothing");

            PlanetSimulation free = Create();
            free.Debug.FreeBuilding = true;
            Assert.IsTrue(free.PlaceTidalTurbine(best, 0));
            PlanetSimulation none = Create();
            double with = 0.0, without = 0.0;
            for (int i = 0; i < 40; i++)
            {
                free.Tick(free.TidePeriodHours / 20.0);
                none.Tick(none.TidePeriodHours / 20.0);
                with = Math.Max(with, free.GenerationW);
                without = Math.Max(without, none.GenerationW);
            }
            Assert.Greater(with, without, "the grid sees the tidal power");
        }

        [Test]
        public void WithoutTheTideGauge_NoTurbineCanBeBuilt()
        {
            PlanetSimulation sim = CreateUnknowing();
            FindCoasts(sim, out int best, out _);
            sim.Debug.FreeBuilding = true;
            StringAssert.Contains("tide gauge", sim.KnowledgeBlocker(BuildingType.TidalTurbine));
            Assert.IsFalse(sim.PlaceTidalTurbine(best, 0));
            sim.LearnExperimentForTests(ExperimentKind.TideGauge);
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.TidalTurbine));
            Assert.IsTrue(sim.PlaceTidalTurbine(best, 0));
        }

        [Test]
        public void TheDebugUnlock_SkipsTheTideGauge()
        {
            PlanetSimulation sim = CreateUnknowing();
            sim.Debug.UnlockAll = true;
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.TidalTurbine));
        }

        [Test]
        public void TheTideGauge_IsAnExperiment_ThatNeedsGravityFirst_AndReportsTheTide()
        {
            Assert.Contains(ExperimentKind.TideGauge, Experiments.All);
            Assert.AreEqual(ExperimentKind.Gravity, Experiments.Needs(ExperimentKind.TideGauge));
            PlanetSimulation sim = CreateUnknowing();
            Assert.IsNull(sim.ExperimentResult(ExperimentKind.TideGauge), "nothing is known yet");
            int t = sim.BaseTileId;
            foreach (var (m, kg) in sim.BuildBill(BuildingType.ResearchStation).Items()) sim.Materials.Add(m, kg);
            int slot = Enumerable.Range(0, sim.SlotCount(t)).First(s => sim.CanPlace(BuildingType.ResearchStation, t, s));
            Assert.IsTrue(sim.PlaceResearchStation(t, slot));
            for (int i = 0; i < 50 && !sim.HasResearchStation; i++) sim.Tick(0.5);
            sim.Deposits.MarkSurveyed(t);
            StringAssert.Contains("gravity", sim.ExperimentBlocker(ExperimentKind.TideGauge));
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            Assert.IsNull(sim.ExperimentBlocker(ExperimentKind.TideGauge));
            Assert.IsTrue(sim.OrderExperiment(ExperimentKind.TideGauge));
            for (int i = 0; i < 400 && !sim.IsExperimentKnown(ExperimentKind.TideGauge); i++) sim.Tick(0.5);
            Assert.IsTrue(sim.IsExperimentKnown(ExperimentKind.TideGauge));
            StringAssert.Contains("tide", sim.ExperimentResult(ExperimentKind.TideGauge));
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.TidalTurbine), "the experiment opens turbines");
            // and a save keeps it
            PlanetSimulation other = CreateUnknowing();
            Assert.IsTrue(other.RestoreState(sim.CaptureState()));
            Assert.IsTrue(other.IsExperimentKnown(ExperimentKind.TideGauge));
        }

        [Test]
        public void SaveAndLoad_KeepsTheTurbine()
        {
            PlanetSimulation sim = Create();
            FindCoasts(sim, out int best, out _);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceTidalTurbine(best, 0));
            SimulationState state = sim.CaptureState();
            PlanetSimulation other = Create();
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(BuildingType.TidalTurbine, other.GetBuilding(best, 0));
            other.Tick(other.TidePeriodHours * 0.25);
            sim.Tick(sim.TidePeriodHours * 0.25);
            Assert.AreEqual(sim.TidalPowerW(), other.TidalPowerW(), 1e-6);
        }

        [Test]
        public void ARemovedTurbine_GivesNoMorePowerAndFreesTheSlot()
        {
            PlanetSimulation sim = Create();
            FindCoasts(sim, out int best, out _);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceTidalTurbine(best, 0));
            Assert.IsTrue(sim.RemoveBuilding(best, 0));
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(best, 0));
            sim.Tick(sim.TidePeriodHours * 0.25);
            Assert.AreEqual(0.0, sim.TidalPowerW());
        }
    }
}

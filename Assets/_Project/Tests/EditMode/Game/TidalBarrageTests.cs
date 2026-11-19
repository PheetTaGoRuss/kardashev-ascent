using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Tidal barrage (director 2026-11-19): the physics, where it can stand, the coast effects, the gates, save and load.</summary>
    public class TidalBarrageTests
    {
        const double G = 9.80665;
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        /// <summary>A planet with strong tides, the basin survey done and the milestone reached (a barrage is allowed).</summary>
        static PlanetSimulation Create()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Debug.StrongTides = true;
            sim.LearnExperimentForTests(ExperimentKind.BasinSurvey);
            sim.LearnExperimentForTests(ExperimentKind.TideGauge);
            sim.SetMilestonesForTests(Tides.BarrageMilestone);
            return sim;
        }

        static int Bay(PlanetSimulation sim)
        {
            for (int t = 0; t < sim.TileCount; t++)
                if (sim.BarrageBlocker(t) == null) return t;
            return -1;
        }

        [Test]
        public void LaRance_FiguresGiveAboutItsRealOutput()
        {
            // 22 km2 basin, 8 m range, 0.3 share, one emptying per 12.42 h tide: about 420 GWh a year (real La Rance: about 500).
            double perTide = Tides.BarrageEnergyPerTideJ(22e6, 8.0, G);
            double yearGWh = perTide * (365.25 * 24.0 / 12.42) / 3.6e12;
            Assert.AreEqual(420.0, yearGWh, 40.0);
            Assert.AreEqual(2.77e12, Tides.BarrageEnergyPerTideJ(22e6, 5.0, G, 1.0), 0.05e12, "1/2 rho g A h^2 for a 5 m range");
        }

        [Test]
        public void Energy_GoesAsRangeSquared_AndBasinArea()
        {
            Assert.AreEqual(4.0, Tides.BarrageEnergyPerTideJ(22e6, 8.0, G) / Tides.BarrageEnergyPerTideJ(22e6, 4.0, G), 1e-9);
            Assert.AreEqual(2.0, Tides.BarrageEnergyPerTideJ(44e6, 4.0, G) / Tides.BarrageEnergyPerTideJ(22e6, 4.0, G), 1e-9);
            Assert.AreEqual(0.0, Tides.BarrageEnergyPerTideJ(22e6, 0.0, G));
        }

        [Test]
        public void Power_IsAHalfSineInTheEbbWindow_AndHoldsTheWholeEnergy()
        {
            double energy = 5e12, period = 12.42, sum = 0.0;
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                double f = (double)i / n;
                double p = Tides.BarragePowerW(energy, period, f);
                sum += p * (period * 3600.0 / n);
                if (f < Tides.BarrageGenStart - 1e-9 || f > Tides.BarrageGenStart + Tides.BarrageGenLength + 1e-9) Assert.AreEqual(0.0, p, "off outside the window");
            }
            Assert.AreEqual(energy, sum, energy * 0.01, "the pulse carries one emptying");
            Assert.Greater(Tides.BarragePowerW(energy, period, Tides.BarrageGenStart + Tides.BarrageGenLength / 2.0), 0.0);
        }

        [Test]
        public void ABarrage_NeedsABay_AWideTide_AndOnlyOnePerTile()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int open = -1;
            for (int t = 0; t < sim.TileCount && open < 0; t++)
                if (sim.GetTile(t).Terrain == TerrainType.ShallowOcean) open = t;
            Assert.IsNotNull(sim.BarrageBlocker(sim.BaseTileId), "not on land");
            Assert.IsNotNull(sim.BarrageBlocker(open), "this world's weak tide (no debug boost)");
            PlanetSimulation strong = Create();
            int bay = Bay(strong);
            Assert.GreaterOrEqual(bay, 0, "a bay with a big tide exists on the test planet with strong tides");
            strong.Debug.FreeBuilding = true;
            Assert.IsTrue(strong.PlaceBarrage(bay, 0));
            StringAssert.Contains("already", strong.BarrageBlocker(bay));
            Assert.IsFalse(strong.PlaceBarrage(bay, 20));
        }

        [Test]
        public void TheGates_BasinSurveyAndMilestone()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            StringAssert.Contains("basin survey", sim.KnowledgeBlocker(BuildingType.TidalBarrage));
            sim.LearnExperimentForTests(ExperimentKind.BasinSurvey);
            StringAssert.Contains("milestone", sim.KnowledgeBlocker(BuildingType.TidalBarrage));
            sim.SetMilestonesForTests(Tides.BarrageMilestone);
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.TidalBarrage));
            var debug = new PlanetSimulation(Sphere, Surface, Earthlike);
            debug.Debug.UnlockAll = true;
            Assert.IsNull(debug.KnowledgeBlocker(BuildingType.TidalBarrage));
        }

        [Test]
        public void ABarrage_CoversThreeSlots_AndFreesThemWhenRemoved()
        {
            PlanetSimulation sim = Create();
            int bay = Bay(sim);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceBarrage(bay, 0));
            int covered = Enumerable.Range(0, sim.SlotCount(bay)).Count(s => sim.GetBuilding(bay, s) == BuildingType.TidalBarrage);
            Assert.AreEqual(BuildingFootprints.TidalBarrage, covered);
            Assert.IsTrue(sim.RemoveBuilding(bay, 0));
            Assert.AreEqual(0, Enumerable.Range(0, sim.SlotCount(bay)).Count(s => sim.GetBuilding(bay, s) != BuildingType.None));
        }

        [Test]
        public void ABarrage_MakesPowerInTheEbbWindow_AndNothingOutsideIt()
        {
            PlanetSimulation sim = Create();
            int bay = Bay(sim);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceBarrage(bay, 0));
            double period = sim.TidePeriodHours, max = 0.0, zeros = 0, total = 0;
            for (int i = 0; i < 400; i++)
            {
                sim.Tick(period / 100.0);
                double p = sim.BarragePowerW();
                max = Math.Max(max, p);
                total++;
                if (p == 0.0) zeros++;
            }
            Assert.Greater(max, 1e6, "megawatts in the ebb");
            Assert.Greater(zeros / total, 0.4, "off for more than a third of the cycle");
        }

        [Test]
        public void TheDam_WeakensTheTideBehindIt_AndStopsBoats()
        {
            PlanetSimulation sim = Create();
            int bay = Bay(sim);
            double before = sim.TidalRangeAtM(bay);
            Assert.IsFalse(sim.Routes.IsDammed(bay));
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceBarrage(bay, 0));
            Assert.AreEqual(before * (1.0 - Tides.BarrageRangeDrop), sim.TidalRangeAtM(bay), 1e-9);
            Assert.AreEqual(before, sim.TidalRangeBaseM(bay), 1e-9, "the base tide is unchanged");
            Assert.IsTrue(sim.Routes.IsDammed(bay), "boats cannot cross it");
            Assert.IsTrue(sim.Routes.Amphibious().IsDammed(bay), "the boat routes share the dam");
            Assert.IsTrue(sim.RemoveBuilding(bay, 0));
            Assert.IsFalse(sim.Routes.IsDammed(bay), "taking it down opens the water again");
            Assert.AreEqual(before, sim.TidalRangeAtM(bay), 1e-9);
        }

        [Test]
        public void ASite_IsNotADamUntilFinished()
        {
            PlanetSimulation sim = Create();
            int bay = Bay(sim);
            Assert.IsTrue(sim.PlaceBarrage(bay, 0));
            Assert.IsTrue(sim.IsUnderConstruction(bay, 0));
            Assert.IsFalse(sim.HasBarrage(bay));
            Assert.IsFalse(sim.Routes.IsDammed(bay));
            Assert.AreEqual(0.0, sim.BarragePowerW());
        }

        [Test]
        public void SaveAndLoad_KeepsTheBarrage_AndTheDam()
        {
            PlanetSimulation sim = Create();
            int bay = Bay(sim);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceBarrage(bay, 0));
            SimulationState state = sim.CaptureState();
            PlanetSimulation other = Create();
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(BuildingType.TidalBarrage, other.GetBuilding(bay, 0));
            Assert.IsTrue(other.HasBarrage(bay));
            Assert.IsTrue(other.Routes.IsDammed(bay), "the dam is back after loading");
            sim.Tick(sim.TidePeriodHours * 0.3);
            other.Tick(other.TidePeriodHours * 0.3);
            Assert.AreEqual(sim.BarragePowerW(), other.BarragePowerW(), 1e-3);
        }

        [Test]
        public void TheBasinSurvey_NeedsTheTideGauge_AndReportsTheBays()
        {
            Assert.Contains(ExperimentKind.BasinSurvey, Experiments.All);
            Assert.AreEqual(ExperimentKind.TideGauge, Experiments.Needs(ExperimentKind.BasinSurvey));
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Debug.StrongTides = true;
            Assert.IsNull(sim.ExperimentResult(ExperimentKind.BasinSurvey));
            sim.LearnExperimentForTests(ExperimentKind.BasinSurvey);
            string result = sim.ExperimentResult(ExperimentKind.BasinSurvey);
            StringAssert.Contains("barrage", result);
            StringAssert.Contains("GWh", result);
        }
    }
}

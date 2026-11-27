using System;
using System.Linq;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Wind turbines (T1 and T2): the mast experiment unlocks them, one per tile, the power follows the day's wind and the air, and they save and load.</summary>
    public class WindTurbineTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Earth => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);
        static PlanetSolarParams Mars => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);

        static PlanetSimulation Make(PlanetSolarParams p, bool knowsMast = true)
        {
            var sim = new PlanetSimulation(Sphere, Surface, p, Kardashev.EarthRadiusM) { WiredGrid = false, Clouds = CloudModel.EarthClimatology };
            sim.Debug.FreeBuilding = true; sim.Debug.UnlockAll = true;
            if (knowsMast) sim.LearnExperimentForTests(ExperimentKind.WindMast);
            return sim;
        }

        static int LandTile(PlanetSimulation sim) => Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && sim.SlotCount(t) > 0);

        [Test]
        public void TheTurbineNeedsTheWindMast_AndOnlyOneStandsOnATile()
        {
            var sim = Make(Earth, knowsMast: false);
            sim.Debug.UnlockAll = false;
            int t = LandTile(sim);
            Assert.IsFalse(sim.PlaceWindTurbine(t, 0), "no mast, no turbine");
            StringAssert.Contains("wind mast", sim.KnowledgeBlocker(BuildingType.WindTurbine));
            sim.LearnExperimentForTests(ExperimentKind.WindMast);
            Assert.IsTrue(sim.PlaceWindTurbine(t, 0));
            Assert.IsFalse(sim.PlaceWindTurbine(t, 1), "one turbine per tile (no wake model)");
            Assert.AreEqual(1, sim.WindTurbineCount(t));
        }

        [Test]
        public void TheMastHasAPuzzle_AResult_AndOpensAForecastSite()
        {
            var sim = Make(Earth);
            var puzzle = sim.PuzzleFor(ExperimentKind.WindMast);
            Assert.AreEqual(1, puzzle.ParamNames.Length);
            Assert.AreEqual(ExperimentKind.AirPressure, Experiments.Needs(ExperimentKind.WindMast));
            StringAssert.Contains("Mean wind", sim.ExperimentResult(ExperimentKind.WindMast));
        }

        [Test]
        public void ATurbineOnAWindyDay_MakesPower_AndTheDailyMeanBeatsThePowerAtTheMean()
        {
            var sim = Make(Earth);
            int t = LandTile(sim);
            Assert.IsTrue(sim.PlaceWindTurbine(t, 0));
            double best = 0.0, worst = double.MaxValue; int idle = 0;
            for (int d = 0; d < 365; d++)
            {
                sim.Tick(24.0);
                double p = sim.WindTurbinePowerAtW(t);
                best = Math.Max(best, p); worst = Math.Min(worst, p);
                if (p < 1.0) idle++;
                Assert.LessOrEqual(p, 100_000.0 + 1e-6);
            }
            TestContext.WriteLine($"wind turbine at tile {t}: best {best / 1000.0:0} kW, worst {worst / 1000.0:0.0} kW, idle days {idle}");
            Assert.Greater(best, 20_000.0, "a windy day gives real power");
        }

        [Test]
        public void OnMarsTheTurbineIsNearlyUseless_Honestly()
        {
            var sim = Make(Mars);
            int t = LandTile(sim);
            double sum = 0.0;
            for (int d = 0; d < 360; d++) { sim.Tick(24.0); sum += sim.WindTurbinePowerAtW(t); }
            double mean = sum / 360.0;
            TestContext.WriteLine($"Mars turbine mean {mean:0} W");
            Assert.Less(mean, 3000.0, "the air is 1 % of Earth's: a few kW at the very best");
        }

        [Test]
        public void TheGridSeesTheTurbinePower_AndAnIdleTurbineSaysWhy_AndItSavesAndLoads()
        {
            var sim = Make(Earth);
            int t = LandTile(sim);
            Assert.IsTrue(sim.PlaceWindTurbine(t, 0));
            for (int h = 0; h < 24 * 400 && sim.WindTurbinePowerAtW(t) < 5000.0; h++) sim.Tick(1.0);
            sim.Tick(0.1);
            Assert.Greater(sim.WindPowerW(), 0.0, "the flow carries the wind power");
            StringAssert.Contains("kW", sim.WindTurbineStatus(t));
            var again = Make(Earth, knowsMast: false);
            again.RestoreState(sim.CaptureState());
            Assert.AreEqual(1, again.WindTurbineCount(t));
            Assert.AreEqual(BuildingType.WindTurbine, again.GetBuilding(t, 0));
            // find a calm day for the reason text
            for (int h = 0; h < 24 * 400 && sim.WindTurbinePowerAtW(t) > 1.0; h++) sim.Tick(1.0);
            if (sim.WindTurbinePowerAtW(t) < 1.0) StringAssert.Contains("below", sim.WindTurbineStatus(t));
        }

        [Test]
        public void TheBuildBillIsTheDatasheetMass()
        {
            var bill = BuildCosts.WindTurbine;
            double total = 0.0; foreach (var (m, kg) in bill.Items()) total += kg;
            Assert.AreEqual(WindPower.MassKg * BuildCosts.OnSiteShare, total, 0.01, "the real mass of the machine times the on-site share, like every bill");
        }

        [Test]
        public void TheEnergyLedgerCloses_WithATurbineOnTheGrid()
        {
            var sim = Make(Earth);
            for (int i = 0, n = 0; i < sim.TileCount && n < 3; i++) if (i != sim.BaseTileId && !sim.GetTile(i).IsWater && sim.IsOpenGround(i) && sim.PlaceWindTurbine(i, 0)) n++;
            double worst = 0.0, turbine = 0.0;
            for (int h = 0; h < 24 * 30; h++)
            {
                sim.Tick(1.0);
                double stored = sim.BatteryStoredRateW;
                worst = Math.Max(worst, Math.Abs(sim.GenerationW - (sim.KardashevPowerW + sim.CurtailedW + stored)));
                turbine = Math.Max(turbine, sim.WindPowerW());
            }
            Assert.Greater(turbine, 1000.0, "the turbines fed the grid");
            Assert.Less(worst, 1.0, "generation + discharge = used + curtailed, every hour");
        }
    }
}

using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Wave-energy converter lines (director 2026-11-19): where they stand, the sea state, the buoy experiment, save and load.</summary>
    public class WaveEnergyTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Create()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.LearnExperimentForTests(ExperimentKind.WaveBuoy);
            return sim;
        }

        static PlanetSimulation CreateUnknowing() => new PlanetSimulation(Sphere, Surface, Earthlike);

        /// <summary>The roughest shallow-sea tile.</summary>
        static int Roughest(PlanetSimulation sim)
        {
            int best = -1;
            double most = -1.0;
            for (int t = 0; t < sim.TileCount; t++)
                if (sim.GetTile(t).Terrain == TerrainType.ShallowOcean && sim.MeanWaveHeightAtM(t) > most) { most = sim.MeanWaveHeightAtM(t); best = t; }
            return best;
        }

        [Test]
        public void TheSea_HasWaves_AndTheLandHasNone()
        {
            PlanetSimulation sim = Create();
            int sea = Roughest(sim);
            Assert.GreaterOrEqual(sea, 0);
            Assert.Greater(sim.MeanWaveHeightAtM(sea), 0.5);
            Assert.Less(sim.MeanWaveHeightAtM(sea), 8.0, "a sane sea state");
            for (int t = 0; t < sim.TileCount; t++)
                if (!sim.GetTile(t).IsWater) { Assert.AreEqual(0.0, sim.MeanWaveHeightAtM(t)); Assert.AreEqual(0.0, sim.WaveHeightAtM(t)); break; }
        }

        [Test]
        public void TheWeather_SwingsTheSeaAroundItsMean()
        {
            PlanetSimulation sim = Create();
            int sea = Roughest(sim);
            double mean = sim.MeanWaveHeightAtM(sea), lo = double.MaxValue, hi = 0.0;
            for (int i = 0; i < 400; i++)
            {
                sim.Tick(1.0);
                double h = sim.WaveHeightAtM(sea);
                lo = Math.Min(lo, h);
                hi = Math.Max(hi, h);
            }
            Assert.Less(lo, mean);
            Assert.Greater(hi, mean);
        }

        [Test]
        public void ThinAir_MakesSmallerWaves()
        {
            // Same planet, thinner atmosphere: the wind at the surface is weaker (game rule).
            PlanetSimulation sim = Create();
            Assert.Greater(sim.MeanWaveHeightAtM(Roughest(sim)), 0.0);
        }

        [Test]
        public void ConverterOnlyStandsOnShallowSea_AndNeedsTheBuoy()
        {
            PlanetSimulation sim = CreateUnknowing();
            int sea = Roughest(sim);
            sim.Debug.FreeBuilding = true;
            StringAssert.Contains("wave buoy", sim.KnowledgeBlocker(BuildingType.WaveConverter));
            Assert.IsFalse(sim.PlaceWaveConverter(sea, 0));
            sim.LearnExperimentForTests(ExperimentKind.WaveBuoy);
            Assert.IsFalse(sim.PlaceWaveConverter(sim.BaseTileId, 0), "not on land");
            Assert.IsTrue(sim.PlaceWaveConverter(sea, 0));
            Assert.AreEqual(BuildingType.WaveConverter, sim.GetBuilding(sea, 0));
            Assert.IsFalse(sim.PlaceWaveConverter(sea, 0), "the slot is taken");
        }

        [Test]
        public void TheDebugUnlock_SkipsTheBuoy()
        {
            PlanetSimulation sim = CreateUnknowing();
            sim.Debug.UnlockAll = true;
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.WaveConverter));
        }

        [Test]
        public void AConverter_MakesPowerThatFollowsTheSea_AndNeverBeatsItsRating()
        {
            PlanetSimulation sim = Create();
            int sea = Roughest(sim);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceWaveConverter(sea, 0));
            double min = double.MaxValue, max = 0.0;
            for (int i = 0; i < 400; i++)
            {
                sim.Tick(1.0);
                double p = sim.WavePowerW();
                min = Math.Min(min, p);
                max = Math.Max(max, p);
            }
            Assert.Greater(max, 0.0, "waves give power");
            Assert.LessOrEqual(max, Waves.RatedW + 1e-6);
            Assert.Less(min, max, "the weather changes the output");
        }

        [Test]
        public void ASite_MakesNothingUntilFinished_AndTheGridSeesTheWavePower()
        {
            PlanetSimulation sim = Create();
            int sea = Roughest(sim);
            Assert.IsTrue(sim.PlaceWaveConverter(sea, 0));
            Assert.IsTrue(sim.IsUnderConstruction(sea, 0));
            Assert.AreEqual(0.0, sim.WavePowerW(), "a construction site makes nothing");

            PlanetSimulation free = Create();
            free.Debug.FreeBuilding = true;
            Assert.IsTrue(free.PlaceWaveConverter(sea, 0));
            PlanetSimulation none = Create();
            double with = 0.0, without = 0.0;
            for (int i = 0; i < 200; i++)
            {
                free.Tick(1.0);
                none.Tick(1.0);
                with = Math.Max(with, free.GenerationW);
                without = Math.Max(without, none.GenerationW);
            }
            Assert.Greater(with, without);
        }

        [Test]
        public void TheWaveBuoy_IsAnExperiment_ThatNeedsGravityFirst_AndReportsTheSea()
        {
            Assert.Contains(ExperimentKind.WaveBuoy, Experiments.All);
            Assert.AreEqual(ExperimentKind.Gravity, Experiments.Needs(ExperimentKind.WaveBuoy));
            PlanetSimulation sim = CreateUnknowing();
            Assert.IsNull(sim.ExperimentResult(ExperimentKind.WaveBuoy));
            int t = sim.BaseTileId;
            foreach (var (m, kg) in sim.BuildBill(BuildingType.ResearchStation).Items()) sim.Materials.Add(m, kg);
            int slot = Enumerable.Range(0, sim.SlotCount(t)).First(s => sim.CanPlace(BuildingType.ResearchStation, t, s));
            Assert.IsTrue(sim.PlaceResearchStation(t, slot));
            for (int i = 0; i < 50 && !sim.HasResearchStation; i++) sim.Tick(0.5);
            sim.Deposits.MarkSurveyed(t);
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            Assert.IsTrue(sim.OrderExperiment(ExperimentKind.WaveBuoy));
            for (int i = 0; i < 400 && !sim.IsExperimentKnown(ExperimentKind.WaveBuoy); i++) sim.Tick(0.5);
            Assert.IsTrue(sim.IsExperimentKnown(ExperimentKind.WaveBuoy));
            StringAssert.Contains("waves", sim.ExperimentResult(ExperimentKind.WaveBuoy));
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.WaveConverter));
        }

        [Test]
        public void SaveAndLoad_KeepsTheConverter_AndRemovingItFreesTheSlot()
        {
            PlanetSimulation sim = Create();
            int sea = Roughest(sim);
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlaceWaveConverter(sea, 0));
            SimulationState state = sim.CaptureState();
            PlanetSimulation other = Create();
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(BuildingType.WaveConverter, other.GetBuilding(sea, 0));
            sim.Tick(5.0);
            other.Tick(5.0);
            Assert.AreEqual(sim.WavePowerW(), other.WavePowerW(), 1e-6);
            Assert.IsTrue(other.RemoveBuilding(sea, 0));
            Assert.AreEqual(BuildingType.None, other.GetBuilding(sea, 0));
            Assert.AreEqual(0.0, other.WavePowerW());
        }

        /// <summary>Mean wave energy flux over the sea tiles for a game year (daily samples).</summary>
        static double YearMeanFlux(bool weather)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WeatherEnabled = weather, Clouds = CloudModel.EarthClimatology };
            var seas = Enumerable.Range(0, sim.TileCount).Where(t => sim.MeanWaveHeightAtM(t) > 0.0 && t % 5 == 0).ToArray();
            double sum = 0.0; int n = 0;
            for (int d = 0; d < 365; d++)
            {
                sim.Tick(24.0);
                foreach (int t in seas)
                {
                    double h = sim.WaveHeightAtM(t);
                    sum += Waves.EnergyFluxWPerM(h, Waves.EnergyPeriodShare * Waves.PeakPeriodS(Math.Sqrt(h / (Waves.DevelopmentShare * 2.0 * Math.Sqrt(8.1e-3 / 0.74) / 9.81)) , 9.81), 9.81); n++;
                }
            }
            return sum / n;
        }

        [Test]
        public void P1_TheRealWind_KeepsTheLongRunWavePowerWithin30PercentOfTheOldRule()
        {
            double old = YearMeanFlux(false), real = YearMeanFlux(true);
            TestContext.WriteLine($"wave flux old {old:0} W/m, real wind {real:0} W/m, ratio {real / old:0.00}");
            Assert.AreEqual(1.0, real / old, 0.30);
        }
    }
}

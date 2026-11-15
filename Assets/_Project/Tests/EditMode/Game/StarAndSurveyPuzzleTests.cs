using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The star-spectrum (Wien) and ground-survey puzzles (docs/design/solar-measurements.md, stage F).</summary>
    public class StarAndSurveyPuzzleTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike) { PuzzleChance = 1.0 };   // these tests are about the puzzles

        [Test]
        public void Spectrum_PeakBin_GivesTheStarsTemperature_ByWien()
        {
            // A Sun-like star peaks near 501 nm; the spectrometer's 10 nm bins put it at 500 nm → 5796 K.
            ParameterPuzzle p = ParameterPuzzle.Spectrum(0, 5778.0);
            Assert.AreEqual(7, p.WavelengthNm.Length);
            Assert.AreEqual(500.0, p.WavelengthNm[3], "the middle bin is the peak");
            Assert.AreEqual(100.0, p.Brightness[3], 0.01, "the peak reads 100");
            Assert.AreEqual(5778.0, p.Answer, 30.0, "binning to 10 nm moves it by under 1 %");
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, p.Check(5778.0));
            // A red dwarf: TRAPPIST-1 at 2566 K peaks far out at 1130 nm.
            ParameterPuzzle red = ParameterPuzzle.Spectrum(0, 2566.0);
            Assert.AreEqual(2566.0, red.Answer, 15.0);
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, red.Check(5778.0), "a Sun-like guess is far off for a red dwarf");
        }

        [Test]
        public void Survey_CoresAverageToTheDepositGrade()
        {
            var ground = new List<Deposit> { new Deposit(Material.Rock, 1.0), new Deposit(Material.Iron, 0.8), new Deposit(Material.Copper, 1.2) };
            SurveyPuzzle p = SurveyPuzzle.Make(7, ground);
            CollectionAssert.AreEqual(new[] { Material.Iron, Material.Copper }, p.Materials, "rock is always known, so it isn't asked");
            Assert.AreEqual(SurveyPuzzle.Cores, p.Rows.Count);
            Assert.AreEqual(0.8, p.Answer(0), 0.01);
            Assert.AreEqual(1.2, p.Answer(1), 0.01);
            bool someCoreMissedIron = false;
            foreach (SurveyPuzzle.CoreRow r in p.Rows) someCoreMissedIron |= r.Grades[0] == 0.0;
            Assert.IsTrue(someCoreMissedIron, "one core misses the material: it still counts as 0 in the average");
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, p.Check(new[] { 0.82, 1.15 }));
            Assert.AreEqual(LatitudePuzzle.Grade.Rough, p.Check(new[] { 0.8, 1.0 }), "the worst estimate sets the grade");
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, p.Check(new[] { 0.8 }), "one estimate per material");
        }

        [Test]
        public void WithoutAPuzzle_TheRobotWorksTheGradesOut()
        {
            var sim = Create();
            sim.PuzzleChance = 0.0;   // director 2026-11-14: puzzles are occasional
            int t = sim.BaseTileId;
            Assume.That(sim.Deposits.At(t).Count > 1, "the base tile has more than rock");
            Assert.IsTrue(sim.OrderSurvey(t));
            for (int i = 0; i < 10 && !sim.Deposits.IsSurveyed(t); i++)
                sim.Tick(1.0);
            Assert.IsTrue(sim.Deposits.IsSurveyed(t));
            Assert.IsFalse(sim.IsRoughSurvey(t), "worked out exactly");
            Assert.AreEqual(0, sim.SurveyPuzzles.Count);
        }

        [Test]
        public void PuzzleChance_IsOccasional()
        {
            Assert.AreEqual(0.25, PlanetSimulation.DefaultPuzzleChance, "game rule, TBD");
            int puzzles = 0, tiles = 0;
            for (int t = 0; t < Sphere.TileCount && tiles < 120; t += 7)
            {
                var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
                if (sim.Deposits.At(t).Count < 2 || !sim.OrderSurvey(t)) continue;
                sim.MovementEnabled = false;   // work any tile from the base: only the roll matters here
                for (int i = 0; i < 6 && !sim.Deposits.IsSurveyed(t) && sim.SurveyPuzzles.Count == 0; i++) sim.Tick(1.0);
                tiles++;
                if (sim.SurveyPuzzles.Count > 0) puzzles++;
            }
            Assume.That(tiles >= 40, "enough tiles with more than rock");
            double share = puzzles / (double)tiles;
            Assert.Greater(share, 0.05, "some surveys still end in a puzzle");
            Assert.Less(share, 0.5, "most don't");
        }

        [Test]
        public void RobotSurvey_LeavesCores_ThenTheEstimateSurveysTheTile()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assume.That(sim.Deposits.At(t).Count > 1, "the base tile has more than rock");
            Assert.IsTrue(sim.OrderSurvey(t));
            for (int i = 0; i < 10 && sim.SurveyPuzzles.Count == 0; i++)
                sim.Tick(1.0);
            Assert.AreEqual(1, sim.SurveyPuzzles.Count);
            Assert.IsFalse(sim.Deposits.IsSurveyed(t));
            SurveyPuzzle p = sim.SurveyPuzzles[0];

            var wrong = new List<double>();
            for (int m = 0; m < p.Materials.Count; m++) wrong.Add(p.Answer(m) * 3.0);
            sim.AnswerSurveyPuzzle(t, wrong, out LatitudePuzzle.Grade g1);
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, g1);
            Assert.AreEqual(1, sim.SurveyPuzzles[0].Attempts);

            var rough = new List<double>();
            for (int m = 0; m < p.Materials.Count; m++) rough.Add(p.Answer(m) * 1.2);
            sim.AnswerSurveyPuzzle(t, rough, out LatitudePuzzle.Grade g2);
            Assert.AreEqual(LatitudePuzzle.Grade.Rough, g2);
            Assert.IsTrue(sim.Deposits.IsSurveyed(t));
            Assert.IsTrue(sim.IsRoughSurvey(t), "surveyed, but its grades show as ±");
            Assert.AreEqual(0, sim.SurveyPuzzles.Count);
            // The real grade is untouched: mining pays by the deposit, not the guess.
            Assert.AreEqual(sim.Deposits.At(t)[1].Grade, sim.Deposits.GradeOf(t, sim.Deposits.At(t)[1].Material), 1e-12);
        }

        [Test]
        public void Save_KeepsCoresAndRoughSurveys()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.OrderSurvey(t);
            for (int i = 0; i < 10 && a.SurveyPuzzles.Count == 0; i++)
                a.Tick(1.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(1, b.SurveyPuzzles.Count);
            Assert.AreEqual(a.SurveyPuzzles[0].Answer(0), b.SurveyPuzzles[0].Answer(0), 1e-9);
            Assert.IsFalse(b.OrderSurvey(t), "cores still waiting after the load");
        }
    }
}

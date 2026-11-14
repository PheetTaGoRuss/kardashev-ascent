using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Ground reflectance, air temperature and dust puzzles (docs/design/solar-measurements.md, stage E).</summary>
    public class GroundHeatPuzzleTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        [Test]
        public void Albedo_IsReflectedOverIncoming()
        {
            ParameterPuzzle p = ParameterPuzzle.Albedo(0, 1361.0, 0.25);
            Assert.AreEqual(0.25, p.Answer, 0.002);
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, p.Check(0.27), "within 10 %: two-decimal readings");
            Assert.AreEqual(LatitudePuzzle.Grade.Rough, p.Check(0.28));
        }

        [Test]
        public void AirTemperature_IsTheMeanOfTheDay_CheckedInDegrees()
        {
            ParameterPuzzle p = ParameterPuzzle.AirTemperature(0, -8.0);
            Assert.AreEqual(8, p.TemperatureC.Length);
            Assert.AreEqual(-8.0, p.Answer, 0.05, "eight readings 3 h apart average the swing out");
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, p.Check(-7.2));
            Assert.AreEqual(LatitudePuzzle.Grade.Rough, p.Check(-10.5));
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, p.Check(-2.0), "the warmest reading is not the mean");
        }

        [Test]
        public void Dust_IsTheShareTheDustyCellLoses()
        {
            ParameterPuzzle dusty = ParameterPuzzle.Dust(0, 1361.0, 0.04);
            Assert.AreEqual(0.04, dusty.Answer, 0.002);
            ParameterPuzzle clean = ParameterPuzzle.Dust(0, 1361.0, 0.0);
            Assert.AreEqual(0.0, clean.Answer, 1e-9);
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, clean.Check(0.0), "zero is a fine answer: checked in absolute terms");
        }

        [Test]
        public void Measuring_GivesAPuzzle_ThenTheAnswerFixesIt()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { PuzzleChance = 1.0 };   // these tests are about the puzzles
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.OrderMeasure(SolarParameter.AirTemperature, t));
            for (int i = 0; i < 40 && sim.ParameterPuzzles.Count == 0; i++)
                sim.Tick(1.0);
            Assert.AreEqual(1, sim.ParameterPuzzles.Count);
            Assert.AreEqual(25.0, sim.ParameterPuzzles[0].Answer, 0.05, "this planet's air is 25 °C");
            sim.AnswerParameterPuzzle(SolarParameter.AirTemperature, 25.0, out LatitudePuzzle.Grade g);
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, g);
            Assert.IsTrue(sim.Parameters.IsMeasured(SolarParameter.AirTemperature));
        }
    }
}

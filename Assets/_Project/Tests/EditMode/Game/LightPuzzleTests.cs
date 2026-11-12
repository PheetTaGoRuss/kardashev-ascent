using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The light puzzles: haze (Langley), diffuse share and clouds (docs/design/solar-measurements.md, stage D).</summary>
    public class LightPuzzleTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static StubPlanetSimulation Create() => new StubPlanetSimulation(Sphere, Surface, Earthlike);

        [Test]
        public void Langley_GivesBackTheOpticalDepth()
        {
            ParameterPuzzle p = ParameterPuzzle.Haze(0, 1361.0, 0.32, 1.0);
            Assert.AreEqual(4, p.Dni.Length);
            Assert.AreEqual(1.0 / Math.Sin(60.0 * Math.PI / 180.0), p.AirMass[0], 0.01, "m ≈ 1 / sin h high in the sky");
            Assert.AreEqual(0.32, p.Answer, 0.005);
            // One reading is enough by hand: τ = ln(G₀ / DNI) / m.
            Assert.AreEqual(0.32, Math.Log(p.G0 / p.Dni[0]) / p.AirMass[0], 0.005);
        }

        [Test]
        public void Diffuse_GivesBackK()
        {
            ParameterPuzzle p = ParameterPuzzle.Diffuse(0, 1361.0, 0.32, 0.3, 1.0);
            Assert.AreEqual(0.3, p.Answer, 0.003);
        }

        [Test]
        public void Clouds_GiveBackTheSeasonsFactor()
        {
            ParameterPuzzle p = ParameterPuzzle.Clouds(0, 6.0, 0.7);
            Assert.AreEqual(0.7, p.Answer, 0.005, "the logged days average out to the season's factor");
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, p.Check(0.71));
            Assert.AreEqual(LatitudePuzzle.Grade.Rough, p.Check(0.62));
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, p.Check(0.4));
        }

        static void MeasureUntilPuzzle(StubPlanetSimulation sim, SolarParameter p)
        {
            Assert.IsTrue(sim.OrderMeasure(p, sim.BaseTileId));
            for (int i = 0; i < 40 && sim.ParameterPuzzles.Count == 0; i++)
                sim.Tick(1.0);
            Assert.AreEqual(1, sim.ParameterPuzzles.Count, "the readings are in");
        }

        [Test]
        public void PreciseAnswer_RemovesTheSpread()
        {
            var sim = Create();
            MeasureUntilPuzzle(sim, SolarParameter.OpticalDepth);
            Assert.IsFalse(sim.Parameters.IsMeasured(SolarParameter.OpticalDepth), "not until the player works it out");
            Assert.IsFalse(sim.OrderMeasure(SolarParameter.OpticalDepth, sim.BaseTileId), "solve the waiting puzzle first");
            sim.AnswerParameterPuzzle(SolarParameter.OpticalDepth, 1.5, out LatitudePuzzle.Grade wrong);
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, wrong);
            sim.AnswerParameterPuzzle(SolarParameter.OpticalDepth, 0.32, out LatitudePuzzle.Grade right);
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, right);
            Assert.IsTrue(sim.Parameters.IsMeasured(SolarParameter.OpticalDepth));
            Assert.AreEqual(0, sim.ParameterPuzzles.Count);
        }

        [Test]
        public void RoughAnswer_LeavesHalfTheSpread_AndCanBeMeasuredAgain()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            double before = sim.ParameterEffects(t, 0.5, Math.PI).Find(e => e.Parameter == SolarParameter.DiffuseShare).HalfRangeWh;
            MeasureUntilPuzzle(sim, SolarParameter.DiffuseShare);
            sim.AnswerParameterPuzzle(SolarParameter.DiffuseShare, 0.3 * 1.1, out LatitudePuzzle.Grade grade);
            Assert.AreEqual(LatitudePuzzle.Grade.Rough, grade);
            Assert.IsTrue(sim.Parameters.IsRough(SolarParameter.DiffuseShare));
            ParameterEffect after = sim.ParameterEffects(t, 0.5, Math.PI).Find(e => e.Parameter == SolarParameter.DiffuseShare);
            Assert.IsTrue(after.Rough);
            Assert.AreEqual(before * ParameterBook.RoughRangeLeft, after.HalfRangeWh, before * 0.01);
            Assert.IsTrue(sim.OrderMeasure(SolarParameter.DiffuseShare, t), "a rough value can be measured again");
        }

        [Test]
        public void Save_KeepsLightPuzzlesAndRoughValues()
        {
            var a = Create();
            a.Parameters.MarkRough(SolarParameter.Dust);
            MeasureUntilPuzzle(a, SolarParameter.OpticalDepth);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(22, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(1, b.ParameterPuzzles.Count);
            CollectionAssert.AreEqual(a.ParameterPuzzles[0].Dni, b.ParameterPuzzles[0].Dni);
            Assert.IsTrue(b.Parameters.IsRough(SolarParameter.Dust));
        }
    }
}

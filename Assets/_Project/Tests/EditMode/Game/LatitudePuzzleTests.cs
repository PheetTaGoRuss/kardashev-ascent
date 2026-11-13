using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The noon-shadow latitude puzzle (docs/design/solar-measurements.md, stage C).</summary>
    public class LatitudePuzzleTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike);

        [Test]
        public void Readings_GiveBackTheLatitude()
        {
            // 40° N on an equinox: noon sun at 50°, a 2 m rod casts a 1.68 m shadow pointing north.
            LatitudePuzzle p = LatitudePuzzle.Make(0, 40.0, 0.0);
            Assert.IsTrue(p.SunSouth);
            Assert.AreEqual(1.68, p.ShadowM, 1e-9);
            Assert.AreEqual(50.0, p.AltitudeDeg, 0.05);
            Assert.AreEqual(40.0, p.AnswerDeg, 0.05);
            // Southern hemisphere in its summer: the sun stands to the north.
            LatitudePuzzle s = LatitudePuzzle.Make(0, -30.0, -20.0);
            Assert.IsFalse(s.SunSouth);
            Assert.AreEqual(-30.0, s.AnswerDeg, 0.1, "a short shadow read to the centimetre");
        }

        [Test]
        public void Grades_PreciseRoughWrong()
        {
            LatitudePuzzle p = LatitudePuzzle.Make(0, 40.0, 10.0);
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, p.Check(p.AnswerDeg + 0.3));
            Assert.AreEqual(LatitudePuzzle.Grade.Rough, p.Check(p.AnswerDeg - 1.5));
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, p.Check(-p.AnswerDeg));
        }

        [Test]
        public void RobotReadings_ThenTheAnswer_FixTheLatitude()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsFalse(sim.Notebook.KnowsLatitude(t));
            Assert.IsTrue(sim.OrderMeasure(SolarParameter.Latitude, t));
            for (int i = 0; i < 40 && sim.LatitudePuzzles.Count == 0; i++)
                sim.Tick(1.0);
            Assert.AreEqual(1, sim.LatitudePuzzles.Count, "the robot's readings are in");
            Assert.IsFalse(sim.Notebook.KnowsLatitude(t), "not until the player works it out");
            Assert.IsFalse(sim.OrderMeasure(SolarParameter.Latitude, t), "solve the waiting puzzle first");

            LatitudePuzzle p = sim.LatitudePuzzles[0];
            sim.AnswerLatitudePuzzle(t, -p.AnswerDeg - 20.0, out LatitudePuzzle.Grade wrong);
            Assert.AreEqual(LatitudePuzzle.Grade.Wrong, wrong);
            Assert.AreEqual(1, sim.LatitudePuzzles.Count, "still open");
            Assert.AreEqual(1, sim.LatitudePuzzles[0].Attempts);

            sim.AnswerLatitudePuzzle(t, p.AnswerDeg, out LatitudePuzzle.Grade right);
            Assert.AreEqual(LatitudePuzzle.Grade.Precise, right);
            Assert.AreEqual(0, sim.LatitudePuzzles.Count);
            Assert.IsTrue(sim.Notebook.KnowsLatitude(t));
            Assert.IsTrue(sim.IsParameterKnown(SolarParameter.Latitude, t));
        }

        [Test]
        public void Save_KeepsWaitingPuzzles()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.OrderMeasure(SolarParameter.Latitude, t);
            for (int i = 0; i < 40 && a.LatitudePuzzles.Count == 0; i++)
                a.Tick(1.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(1, b.LatitudePuzzles.Count);
            Assert.AreEqual(a.LatitudePuzzles[0].ShadowM, b.LatitudePuzzles[0].ShadowM);
        }
    }
}

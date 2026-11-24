using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>UI information design, stage 7 (decided 2026-11-24): the field log ranks entries act now, watch, info; toasts are for act now only.</summary>
    public class FieldLogTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void WarningsAreActNow_OrdersAndMilestonesWatch_EverythingElseInfo()
        {
            Assert.AreEqual(LogPriority.ActNow, OperationPriority.Of("Robot destroyed"));
            Assert.AreEqual(LogPriority.ActNow, OperationPriority.Of("Cold or heat warning"));
            Assert.AreEqual(LogPriority.ActNow, OperationPriority.Of("Notice"));
            Assert.AreEqual(LogPriority.Watch, OperationPriority.Of("Haul waiting"));
            Assert.AreEqual(LogPriority.Watch, OperationPriority.Of("Milestone reached"));
            Assert.AreEqual(LogPriority.Info, OperationPriority.Of("Rule fired"));
            Assert.AreEqual(LogPriority.Info, OperationPriority.Of("Game saved"));
            Assert.AreEqual(0, (int)LogPriority.Info, "saved integers: Info stays 0 so older records read as info");
        }

        [Test]
        public void NoteEventPutsAnEntryInTheLogWithItsPriority()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.NoteEvent("Colony", "Milestone reached", "Milestone 1 of 10");
            sim.NoteEvent("Colony", "Game saved", "Saved.");
            var log = sim.OperationsLog.ToList();
            Assert.AreEqual(LogPriority.Watch, log.First(r => r.Quantity == "Milestone reached").Priority);
            Assert.AreEqual(LogPriority.Info, log.First(r => r.Quantity == "Game saved").Priority);
        }

        [Test]
        public void AFinishedExperimentIsLoggedWithWhatItFound()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            typeof(PlanetSimulation).GetMethod("OnExperimentFinished", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(sim, new object[] { ExperimentKind.Gravity });
            var rec = sim.OperationsLog.First(r => r.Quantity == "Experiment done");
            StringAssert.Contains("Gravity measured", rec.Text);
            StringAssert.Contains("m/s", rec.Text);
        }

        [Test]
        public void PrioritiesAreSaved_AndARecordFromAnOlderSaveIsInfo()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.NoteEvent("Colony", "Milestone reached", "Milestone 1");
            var state = sim.CaptureState();
            Assert.AreEqual(LogPriority.Watch, state.OperationsLog.First(r => r.Quantity == "Milestone reached").Priority);
            Assert.GreaterOrEqual(SimulationState.CurrentVersion, 55);
            foreach (var r in state.OperationsLog) r.Priority = LogPriority.Info;   // what an older save has: the field is missing, so it reads as 0
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            loaded.RestoreState(state);
            Assert.IsTrue(loaded.OperationsLog.All(r => r.Priority == LogPriority.Info));
        }
    }
}

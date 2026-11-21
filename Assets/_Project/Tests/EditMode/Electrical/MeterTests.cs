using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>Stage 7: a line's readout says whether the colony knows what it carries.</summary>
    public class MeterTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * System.Math.PI / 180.0,
            282.94 * System.Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Wired(out int a)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater);
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            return sim;
        }

        [Test]
        public void AnUnmeteredLineShowsOnlyItsDesignFigures()
        {
            var sim = Wired(out int a);
            var r = sim.LineReadout(a);
            Assert.IsTrue(r.HasLine);
            Assert.IsFalse(r.Measured);
            Assert.AreEqual(ReadingSource.None, r.Source);
            Assert.IsTrue(double.IsNaN(r.FlowW), "the flow is not known without a meter");
            Assert.AreEqual(PlanetSimulation.LineCapacityPlainW, r.CapacityW, 1e-6, "the rating is a design figure");
        }

        [Test]
        public void AMeterMakesTheReadingTrue()
        {
            var sim = Wired(out int a);
            Assert.IsTrue(sim.InstallLineMeter(a));
            Assert.IsTrue(sim.HasLineMeter(a));
            sim.Tick(12.0);
            var r = sim.LineReadout(a);
            Assert.AreEqual(ReadingSource.Meter, r.Source);
            Assert.AreEqual(sim.LineFlowW(a), r.FlowW, 1e-9, "the meter reads the true flow");
            Assert.IsFalse(double.IsNaN(r.FlowW));
            Assert.IsTrue(sim.RemoveLineMeter(a));
            Assert.IsFalse(sim.LineReadout(a).Measured);
        }

        [Test]
        public void AnInspectionIsAReadingThatGoesStale()
        {
            var sim = Wired(out int a);
            Assert.IsTrue(sim.InspectLine(a));
            Assert.AreEqual(ReadingSource.Inspection, sim.LineReadout(a).Source);
            sim.Tick(10.0);
            Assert.AreEqual(ReadingSource.Inspection, sim.LineReadout(a).Source);
            Assert.AreEqual(10.0, sim.LineReadout(a).AgeHours, 0.2);
            sim.Tick(20.0);
            Assert.AreEqual(ReadingSource.None, sim.LineReadout(a).Source, "older than a day");
        }

        [Test]
        public void ThereIsNoMeterOrInspectionWhereThereIsNoLine()
        {
            var sim = Wired(out int a);
            int far = Enumerable.Range(0, sim.TileCount).First(t => !sim.IsPowered(t));
            Assert.IsFalse(sim.InstallLineMeter(far));
            Assert.IsFalse(sim.InspectLine(far));
            Assert.IsFalse(sim.LineReadout(far).HasLine);
            Assert.IsFalse(sim.InstallLineMeter(sim.BaseTileId), "the lander tile has no line toward itself");
        }

        [Test]
        public void MetersAndInspectionsSurviveSaveAndLoad()
        {
            var sim = Wired(out int a);
            sim.InstallLineMeter(a);
            sim.InspectLine(a);
            var state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.IsTrue(loaded.HasLineMeter(a));
            Assert.IsTrue(loaded.LineReadout(a).Measured);
        }

        [Test]
        public void ASaveFromVersion44HasNoMetersAndLoadsAsBefore()
        {
            var sim = Wired(out int a);
            sim.InstallLineMeter(a);
            var state = sim.CaptureState();
            state.Version = 44;
            state.MeteredLines.Clear();
            state.InspectedLines.Clear();
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.IsFalse(loaded.HasLineMeter(a));
            Assert.IsFalse(loaded.LineReadout(a).Measured);
            Assert.IsTrue(loaded.IsPowered(a));
        }
    }
}

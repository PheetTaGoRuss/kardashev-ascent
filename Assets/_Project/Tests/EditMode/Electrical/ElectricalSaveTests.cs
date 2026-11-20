using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>Save version 43: stable electrical connection ids and cable condition, and loading saves from before it.</summary>
    public class ElectricalSaveTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * System.Math.PI / 180.0,
            282.94 * System.Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Fresh() => new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };

        static PlanetSimulation WithTwoLines(out int a, out int b)
        {
            var sim = Fresh();
            sim.Debug.FreeBuilding = true;
            a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater);
            b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(n) && !sim.GetTile(n).IsWater);
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePowerLine(b, 0, a));
            return sim;
        }

        [Test]
        public void TheSaveVersionIs43()
        {
            Assert.AreEqual(43, SimulationState.CurrentVersion);
            Assert.AreEqual(43, Fresh().CaptureState().Version);
        }

        [Test]
        public void IdsAreSavedAndComeBackTheSame()
        {
            var sim = WithTwoLines(out int a, out int b);
            int ida = sim.ElectricalIdOfLine(a, 0), idb = sim.ElectricalIdOfLine(b, 0);
            Assert.Greater(ida, 0);
            Assert.AreNotEqual(ida, idb);
            var state = sim.CaptureState();
            var loaded = Fresh();
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(ida, loaded.ElectricalIdOfLine(a, 0));
            Assert.AreEqual(idb, loaded.ElectricalIdOfLine(b, 0));
            Assert.AreEqual(state.NextElectricalConnectionId, loaded.CaptureState().NextElectricalConnectionId);
        }

        [Test]
        public void ConditionAndOpenStateSurviveSaveAndLoad()
        {
            var sim = WithTwoLines(out int a, out int b);
            Assert.IsTrue(sim.SetCableState(b, 0, 0.4, true));
            var loaded = Fresh();
            Assert.IsTrue(loaded.RestoreState(sim.CaptureState()));
            var rec = loaded.CaptureState().ElectricalLinks.Single(l => l.Tile == b);
            Assert.AreEqual(0.4, rec.Condition, 1e-12);
            Assert.IsTrue(rec.Open);
            Assert.AreEqual(1.0, loaded.CaptureState().ElectricalLinks.Single(l => l.Tile == a).Condition);
        }

        [Test]
        public void ANewLineAfterLoadGetsAnIdNeverUsedBefore()
        {
            var sim = WithTwoLines(out int a, out int b);
            int top = System.Math.Max(sim.ElectricalIdOfLine(a, 0), sim.ElectricalIdOfLine(b, 0));
            var loaded = Fresh();
            loaded.Debug.FreeBuilding = true;
            Assert.IsTrue(loaded.RestoreState(sim.CaptureState()));
            int c = Sphere.Neighbors[b].First(n => n != a && !loaded.GetTile(n).IsWater);
            Assert.IsTrue(loaded.PlacePowerLine(c, 0, b));
            Assert.Greater(loaded.ElectricalIdOfLine(c, 0), top);
        }

        [Test]
        public void ASaveFromVersion42LoadsAndGivesIdsInTileOrder()
        {
            var sim = WithTwoLines(out int a, out int b);
            var state = sim.CaptureState();
            state.Version = 42;
            state.ElectricalLinks.Clear();
            state.NextElectricalConnectionId = 0;
            var loaded = Fresh();
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.IsTrue(loaded.IsPowered(a));
            Assert.IsTrue(loaded.IsPowered(b));
            int ia = loaded.ElectricalIdOfLine(a, 0), ib = loaded.ElectricalIdOfLine(b, 0);
            Assert.Greater(ia, 0);
            Assert.Greater(ib, 0);
            Assert.AreEqual(ia < ib, a < b, "ids are given out in tile order");
        }

        [Test]
        public void ASaveFromBeforeBuildingWiresStillWiresEverything()
        {
            var sim = WithTwoLines(out int a, out int b);
            var state = sim.CaptureState();
            state.Version = 36;
            state.WiredBuildings.Clear();
            state.ElectricalLinks.Clear();
            var loaded = Fresh();
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(0, loaded.UnwiredBuildings().Count());
            Assert.IsTrue(loaded.IsPowered(b));
        }

        [Test]
        public void AnUnwiredGridSaveStaysUnwired()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            var state = sim.CaptureState();
            state.Version = 34;
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.IsFalse(loaded.WiredGrid);
            Assert.IsTrue(loaded.IsPowered(0));
        }

        [Test]
        public void SavingTwiceGivesTheSameElectricalRecords()
        {
            var sim = WithTwoLines(out _, out _);
            var s1 = sim.CaptureState();
            var s2 = sim.CaptureState();
            CollectionAssert.AreEqual(s1.ElectricalLinks.Select(l => (l.Tile, l.Slot, l.ConnectionId)).OrderBy(x => x).ToArray(),
                                      s2.ElectricalLinks.Select(l => (l.Tile, l.Slot, l.ConnectionId)).OrderBy(x => x).ToArray());
        }
    }
}

using System.Linq;
using KardashevAscent.Simulation.Circuits;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Circuits
{
    /// <summary>The first circuit host (gate G4): a charging station charges only while its circuit's enable port is high.</summary>
    public class StationCircuitTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * System.Math.PI / 180.0,
            282.94 * System.Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Colony(out int tile, out int slot)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Debug.FreeBuilding = true;
            tile = sim.BaseTileId; slot = 20;
            Assert.IsTrue(sim.PlaceChargingStation(tile, slot));
            sim.Tick(0.5);
            return sim;
        }

        static bool StationCharges(PlanetSimulation sim, int tile, int slot) =>
            sim.RobotFacilities().Any(f => f.Tile == tile && f.Slot == slot);

        /// <summary>A supply, a switch and a lamp; the enable port is the lamp's first pin, high when the switch is closed.</summary>
        static CircuitBlueprint SwitchedEnable(string name = "Manual enable", bool closed = false, bool battery = false, double wh = 100.0)
        {
            var b = new CircuitBlueprint { Name = name };
            var src = battery ? b.Place(CircuitComponentType.Battery, 0, 0, 12.0, 0.05, wh) : b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 10.0);
            var sw = b.Place(CircuitComponentType.Switch, 2, 0, closed: closed);
            var lamp = b.Place(CircuitComponentType.Lamp, 4, 0, 12.0, 6.0);
            b.Connect(src.Id, 1, sw.Id, 0, 0.1, 1.5); b.Connect(sw.Id, 1, lamp.Id, 0, 0.1, 1.5); b.Connect(lamp.Id, 1, src.Id, 0, 0.1, 1.5);
            b.Ports.Add(new CircuitPort { Name = "enable", Component = lamp.Id, Pin = 0 });
            return b;
        }

        static int SwitchId(PlanetSimulation sim, int tile, int slot) =>
            sim.StationCircuit(tile, slot).Design.Components.First(c => c.Type == CircuitComponentType.Switch).Id;

        [Test]
        public void AStationWithoutACircuitChargesAsBefore()
        {
            var sim = Colony(out int t, out int s);
            Assert.IsTrue(StationCharges(sim, t, s));
            Assert.IsNull(sim.StationCircuit(t, s));
        }

        [Test]
        public void TheCircuitDecidesWhetherTheStationCharges()
        {
            var sim = Colony(out int t, out int s);
            int idx = sim.SaveCircuitBlueprint(SwitchedEnable());
            Assert.IsNull(sim.InstallStationCircuit(t, s, idx));
            Assert.IsFalse(StationCharges(sim, t, s), "switch open: enable is low");
            Assert.IsFalse(sim.StationCircuit(t, s).Enabled);
            Assert.IsTrue(sim.SetCircuitSwitch(t, s, SwitchId(sim, t, s), true));
            Assert.IsTrue(StationCharges(sim, t, s));
            Assert.IsTrue(sim.StationCircuit(t, s).Enabled);
            Assert.IsTrue(sim.SetCircuitSwitch(t, s, SwitchId(sim, t, s), false));
            Assert.IsFalse(StationCharges(sim, t, s));
        }

        [Test]
        public void RemovingTheCircuitGivesTheStationBackItsOldBehaviour()
        {
            var sim = Colony(out int t, out int s);
            sim.InstallStationCircuit(t, s, sim.SaveCircuitBlueprint(SwitchedEnable()));
            Assert.IsFalse(StationCharges(sim, t, s));
            Assert.IsTrue(sim.RemoveStationCircuit(t, s));
            Assert.IsTrue(StationCharges(sim, t, s));
        }

        [Test]
        public void ADesignWithoutAnEnablePortIsRefused()
        {
            var sim = Colony(out int t, out int s);
            var b = SwitchedEnable("No port");
            b.Ports.Clear();
            Assert.IsNotNull(sim.CircuitFitProblem(b));
            string why = sim.InstallStationCircuit(t, s, sim.SaveCircuitBlueprint(b));
            StringAssert.Contains("enable", why);
            Assert.IsNull(sim.StationCircuit(t, s));
        }

        [Test]
        public void ARefusalForTooManyPartsAndForAMissingStation()
        {
            var sim = Colony(out int t, out int s);
            var big = SwitchedEnable("Big");
            for (int i = 0; i < 12; i++) big.Place(CircuitComponentType.Resistor, i, 5, 10.0);
            StringAssert.Contains("holds 12 parts", sim.InstallStationCircuit(t, s, sim.SaveCircuitBlueprint(big)));
            StringAssert.Contains("no charging station", sim.InstallStationCircuit(t, 3, sim.SaveCircuitBlueprint(SwitchedEnable())));
        }

        [Test]
        public void EditingTheSavedBlueprintDoesNotChangeTheInstalledCircuit()
        {
            var sim = Colony(out int t, out int s);
            var design = SwitchedEnable("Editable", closed: true);
            sim.InstallStationCircuit(t, s, sim.SaveCircuitBlueprint(design));
            Assert.IsTrue(StationCharges(sim, t, s));
            design.Components.First(c => c.Type == CircuitComponentType.Switch).Closed = false;
            sim.SaveCircuitBlueprint(design);   // same name: replaces the library copy
            Assert.IsTrue(StationCharges(sim, t, s), "the installed circuit is its own copy");
            Assert.IsTrue(sim.CircuitBlueprints()[0].Components.First(c => c.Type == CircuitComponentType.Switch).Closed == false);
        }

        [Test]
        public void ABatteryRunsDownAndTheStationStopsCharging()
        {
            var sim = Colony(out int t, out int s);
            // 6 W lamp from 12 V is about 0.5 A; a 3 Wh battery lasts about half an hour.
            sim.InstallStationCircuit(t, s, sim.SaveCircuitBlueprint(SwitchedEnable("Battery", closed: true, battery: true, wh: 3.0)));
            Assert.IsTrue(StationCharges(sim, t, s));
            sim.Tick(2.0);
            Assert.IsFalse(StationCharges(sim, t, s), "the battery is empty");
            Assert.IsTrue(sim.StationCircuit(t, s).Result.Has(DiagnosticCode.BatteryEmpty));
        }

        [Test]
        public void LibraryAndInstalledCircuitSurviveSaveAndLoad()
        {
            var sim = Colony(out int t, out int s);
            sim.InstallStationCircuit(t, s, sim.SaveCircuitBlueprint(SwitchedEnable()));
            sim.SetCircuitSwitch(t, s, SwitchId(sim, t, s), true);
            var state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(1, loaded.CircuitBlueprints().Count);
            var view = loaded.StationCircuit(t, s);
            Assert.IsNotNull(view);
            Assert.IsTrue(view.Enabled);
            Assert.IsTrue(StationCharges(loaded, t, s));
        }

        [Test]
        public void ASaveFromVersion43HasNoCircuitsAndLoadsAsBefore()
        {
            var sim = Colony(out int t, out int s);
            var state = sim.CaptureState();
            state.Version = 43;
            state.CircuitLibrary.Clear();
            state.CircuitInstances.Clear();
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(0, loaded.CircuitBlueprints().Count);
            Assert.IsTrue(StationCharges(loaded, t, s));
        }

        [Test]
        public void TheCircuitIsTheSameInTwoIdenticalRuns()
        {
            double Run()
            {
                var sim = Colony(out int t, out int s);
                sim.InstallStationCircuit(t, s, sim.SaveCircuitBlueprint(SwitchedEnable("D", closed: true, battery: true, wh: 5.0)));
                sim.Tick(0.3);
                return sim.StationCircuit(t, s).Result.SourcePowerW + sim.StationCircuit(t, s).Design.Components.Count;
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}

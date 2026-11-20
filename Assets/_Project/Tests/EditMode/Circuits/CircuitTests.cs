using System;
using System.Linq;
using System.Text.Json;
using KardashevAscent.Simulation.Circuits;
using NUnit.Framework;

namespace KardashevAscent.Tests.Circuits
{
    public class CircuitTests
    {
        const double Fat = 1000.0;   // mm2: a wire so thick that it does not matter
        static CircuitBlueprint B() => new CircuitBlueprint();

        static CircuitResult Run(CircuitBlueprint b, out CircuitInstance inst)
        {
            inst = new CircuitInstance { Design = b };
            inst.ResetState();
            return CircuitSolver.Solve(b, inst.State);
        }

        static CircuitResult Run(CircuitBlueprint b) => Run(b, out _);

        static void Wire(CircuitBlueprint b, CircuitPlacement a, int pa, CircuitPlacement c, int pc, double len = 0.01) =>
            b.Connect(a.Id, pa, c.Id, pc, len, Fat);

        // ---- the basics ----
        [Test]
        public void BatteryAndResistor_OhmsLaw()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.Battery, 0, 0, 12.0, 0.2, 100.0);
            var r = b.Place(CircuitComponentType.Resistor, 3, 0, 5.8);
            Wire(b, bat, 1, r, 0); Wire(b, r, 1, bat, 0);
            var res = Run(b);
            Assert.IsTrue(res.Solved);
            double i = 12.0 / (0.2 + 5.8);   // 2 A
            Assert.AreEqual(i, Math.Abs(res.Of(r.Id).CurrentA), 1e-6);
            Assert.AreEqual(i * 5.8, res.Of(r.Id).VoltageV, 1e-4);
            Assert.AreEqual(i * i * 5.8, res.Of(r.Id).PowerW, 1e-4);
            Assert.AreEqual(12.0 * i - i * i * 0.2, res.SourcePowerW, 1e-3);   // power at the battery terminals
        }

        [Test]
        public void BatterySwitchLamp_OpenGivesNothingClosedGivesRatedPower()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.Battery, 0, 0, 12.0, 0.01, 100.0);
            var sw = b.Place(CircuitComponentType.Switch, 2, 0, closed: false);
            var lamp = b.Place(CircuitComponentType.Lamp, 4, 0, 12.0, 24.0);
            Wire(b, bat, 1, sw, 0); Wire(b, sw, 1, lamp, 0); Wire(b, lamp, 1, bat, 0);
            var open = Run(b, out var inst);
            Assert.AreEqual(0.0, Math.Abs(open.Of(lamp.Id).CurrentA), 1e-9);
            Assert.IsTrue(open.Has(DiagnosticCode.OpenCircuit));
            inst.StateOf(sw.Id).Closed = true;
            var closed = CircuitSolver.Solve(b, inst.State);
            Assert.AreEqual(24.0, closed.Of(lamp.Id).PowerW, 0.5);   // about rated: a little below, the battery and wires drop some
            Assert.IsFalse(closed.Has(DiagnosticCode.OpenCircuit));
        }

        [Test]
        public void SeriesResistance_AddsAndVoltageDividesInProportion()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 30.0, 1e-6, 100.0);
            var r1 = b.Place(CircuitComponentType.Resistor, 1, 0, 10.0);
            var r2 = b.Place(CircuitComponentType.Resistor, 2, 0, 20.0);
            Wire(b, bat, 1, r1, 0); Wire(b, r1, 1, r2, 0); Wire(b, r2, 1, bat, 0);
            var res = Run(b);
            Assert.AreEqual(1.0, Math.Abs(res.Of(r1.Id).CurrentA), 1e-5);
            Assert.AreEqual(10.0, res.Of(r1.Id).VoltageV, 1e-3);
            Assert.AreEqual(20.0, res.Of(r2.Id).VoltageV, 1e-3);
        }

        [Test]
        public void ParallelResistance_CurrentsSplitByConductanceAndJunctionConservesCurrent()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 1e-6, 100.0);
            var j1 = b.Place(CircuitComponentType.Junction, 1, 0);
            var j2 = b.Place(CircuitComponentType.Junction, 4, 0);
            var r1 = b.Place(CircuitComponentType.Resistor, 2, 1, 6.0);
            var r2 = b.Place(CircuitComponentType.Resistor, 2, 2, 3.0);
            Wire(b, bat, 1, j1, 0); Wire(b, j1, 0, r1, 0); Wire(b, j1, 0, r2, 0);
            Wire(b, r1, 1, j2, 0); Wire(b, r2, 1, j2, 0); Wire(b, j2, 0, bat, 0);
            var res = Run(b);
            double i1 = Math.Abs(res.Of(r1.Id).CurrentA), i2 = Math.Abs(res.Of(r2.Id).CurrentA);
            Assert.AreEqual(2.0, i1, 1e-4);
            Assert.AreEqual(4.0, i2, 1e-4);
            Assert.AreEqual(i1 + i2, res.Of(bat.Id).CurrentA, 1e-4, "KCL at the junction: what the battery gives is what the branches take");
        }

        [Test]
        public void ThinLongWire_DropsVoltageAndLosesPower()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 1e-6, 100.0);
            var r = b.Place(CircuitComponentType.Resistor, 1, 0, 1.0);
            // 100 m of 0.5 mm2 copper out and back: R each = 1.7241e-8 * 100 / 0.5e-6 = 3.4482 ohm.
            var w1 = b.Connect(bat.Id, 1, r.Id, 0, 100.0, 0.5);
            var w2 = b.Connect(r.Id, 1, bat.Id, 0, 100.0, 0.5);
            var res = Run(b);
            Assert.AreEqual(3.4482, res.WireOf(w1.Id).ResistanceOhm, 1e-3);
            Assert.AreEqual(12.0 / (1.0 + 2 * 3.4482), Math.Abs(res.Of(r.Id).CurrentA), 1e-3);
            Assert.IsTrue(res.Has(DiagnosticCode.ExcessiveVoltageDrop));
            Assert.IsTrue(res.Has(DiagnosticCode.ExcessiveLoss));
            Assert.AreEqual(res.SourcePowerW, res.Of(r.Id).PowerW + res.WireLossW, 1e-3, "energy balance: source = load + wire loss");
        }

        [Test]
        public void WireLengthIsThePhysicalLengthNotTheDrawnDistance()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 1e-6, 100.0);
            var r = b.Place(CircuitComponentType.Resistor, 1, 0, 10.0);
            var w = b.Connect(bat.Id, 1, r.Id, 0, 50.0, 1.5);
            b.Connect(r.Id, 1, bat.Id, 0, 0.1, Fat);
            double before = Run(b).WireOf(w.Id).ResistanceOhm;
            r.X = 15; r.Y = 11;   // move it across the board: the wire is the same wire
            Assert.AreEqual(before, Run(b).WireOf(w.Id).ResistanceOhm, 1e-12);
        }

        // ---- faults ----
        [Test]
        public void ShortCircuit_IsDiagnosedNotInfinite()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 1000.0);
            Wire(b, bat, 1, bat, 0, 0.1);
            var res = Run(b);
            Assert.IsTrue(res.Has(DiagnosticCode.ShortCircuit));
            Assert.IsFalse(double.IsInfinity(res.SourcePowerW) || double.IsNaN(res.SourcePowerW));
        }

        [Test]
        public void SupplyBeyondItsCurrentLimit_IsOvercurrent()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 2.0);
            var r = b.Place(CircuitComponentType.Resistor, 1, 0, 1.0);
            Wire(b, bat, 1, r, 0); Wire(b, r, 1, bat, 0);
            Assert.IsTrue(Run(b).Has(DiagnosticCode.Overcurrent));
        }

        [Test]
        public void UndersizedWire_IsWarned()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 1e-6, 100.0);
            var r = b.Place(CircuitComponentType.Resistor, 1, 0, 0.5);   // 24 A
            b.Connect(bat.Id, 1, r.Id, 0, 0.1, 0.5);   // 0.5 mm2 is good for 4 A
            b.Connect(r.Id, 1, bat.Id, 0, 0.1, Fat);
            var res = Run(b);
            Assert.IsTrue(res.Has(DiagnosticCode.UndersizedWire));
        }

        [Test]
        public void ReversedDiode_BlocksAndIsDiagnosed()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 100.0);
            var d = b.Place(CircuitComponentType.Diode, 1, 0, 0.7);
            var r = b.Place(CircuitComponentType.Resistor, 2, 0, 10.0);
            Wire(b, bat, 1, d, 1); Wire(b, d, 0, r, 0); Wire(b, r, 1, bat, 0);   // anode toward the load: reversed
            var res = Run(b);
            Assert.AreEqual(0.0, Math.Abs(res.Of(r.Id).CurrentA), 1e-6);
            Assert.IsTrue(res.Has(DiagnosticCode.ReversedPolarity));
            var ok = B();
            var bat2 = ok.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 100.0);
            var d2 = ok.Place(CircuitComponentType.Diode, 1, 0, 0.7);
            var r2 = ok.Place(CircuitComponentType.Resistor, 2, 0, 10.0);
            Wire(ok, bat2, 1, d2, 0); Wire(ok, d2, 1, r2, 0); Wire(ok, r2, 1, bat2, 0);
            var good = Run(ok);
            Assert.AreEqual((12.0 - 0.7) / 10.0, Math.Abs(good.Of(r2.Id).CurrentA), 0.02);
            Assert.IsFalse(good.Has(DiagnosticCode.ReversedPolarity));
        }

        [Test]
        public void FuseBlowsAboveItsRatingAndOpensTheCircuit()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 100.0);
            var f = b.Place(CircuitComponentType.Fuse, 1, 0, 2.0);
            var r = b.Place(CircuitComponentType.Resistor, 2, 0, 2.0);   // 6 A
            Wire(b, bat, 1, f, 0); Wire(b, f, 1, r, 0); Wire(b, r, 1, bat, 0);
            var res = Run(b, out var inst);
            Assert.IsTrue(inst.StateOf(f.Id).Blown);
            Assert.IsTrue(res.Has(DiagnosticCode.ComponentFailure));
            Assert.AreEqual(0.0, Math.Abs(res.Of(r.Id).CurrentA), 1e-6);
        }

        [Test]
        public void InvalidConnections_AreReportedAndDoNotCrash()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 10.0);
            var r = b.Place(CircuitComponentType.Resistor, 1, 0, 10.0);
            b.Connect(bat.Id, 1, r.Id, 7, 0.1, 1.5);           // no pin 7
            b.Connect(bat.Id, 1, r.Id, 0, 0.1, 1.5, WireMaterial.Copper, WireKind.Signal);   // a resistor has no signal pin
            b.Connect(bat.Id, 0, r.Id, 1, 0.1, 0.0);           // no cross-section
            var res = Run(b);
            Assert.AreEqual(3, res.Diagnostics.Count(d => d.Code == DiagnosticCode.InvalidConnection));
            Assert.IsTrue(res.Diagnostics.Any(d => d.Code == DiagnosticCode.DisconnectedTerminal));
        }

        [Test]
        public void NoSource_IsDiagnosed()
        {
            var b = B();
            var r = b.Place(CircuitComponentType.Resistor, 0, 0, 10.0);
            Assert.IsTrue(Run(b).Has(DiagnosticCode.NoSource));
        }

        // ---- relay: coil and contacts ----
        [Test]
        public void Relay_CoilCurrentClosesTheContactsThatPowerAnotherLoop()
        {
            var b = B();
            var ctl = b.Place(CircuitComponentType.DcSupply, 0, 0, 5.0, 0.01, 10.0);
            var sw = b.Place(CircuitComponentType.Switch, 1, 0, closed: false);
            var relay = b.Place(CircuitComponentType.Relay, 2, 0, 50.0, 0.05);   // 100 mA at 5 V, pulls in at 50 mA
            Wire(b, ctl, 1, sw, 0); Wire(b, sw, 1, relay, 0); Wire(b, relay, 1, ctl, 0);
            var pwr = b.Place(CircuitComponentType.DcSupply, 0, 5, 12.0, 0.01, 10.0);
            var lamp = b.Place(CircuitComponentType.Lamp, 3, 5, 12.0, 12.0);
            Wire(b, pwr, 1, relay, 2); Wire(b, relay, 3, lamp, 0); Wire(b, lamp, 1, pwr, 0);
            var off = Run(b, out var inst);
            Assert.AreEqual(0.0, Math.Abs(off.Of(lamp.Id).CurrentA), 1e-6);
            inst.StateOf(sw.Id).Closed = true;
            var on = CircuitSolver.Solve(b, inst.State);
            Assert.IsTrue(inst.StateOf(relay.Id).Closed);
            Assert.AreEqual(12.0, on.Of(lamp.Id).PowerW, 0.3);
            // The coil is a real load on the control supply: 5 V / 50 ohm.
            Assert.AreEqual(0.1, Math.Abs(on.Of(relay.Id).CurrentA), 1e-3);
        }

        // ---- battery energy ----
        [Test]
        public void BatteryHasFiniteEnergy_AndEmptyMeansNoVoltage()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.Battery, 0, 0, 12.0, 0.01, 10.0);   // 10 Wh
            var lamp = b.Place(CircuitComponentType.Lamp, 1, 0, 12.0, 12.0);
            Wire(b, bat, 1, lamp, 0); Wire(b, lamp, 1, bat, 0);
            var res = Run(b, out var inst);
            Assert.AreEqual(10.0 / 12.0, res.BatteryHours, 0.02);
            CircuitSolver.Advance(b, inst.State, res, 1.0);
            Assert.AreEqual(0.0, inst.StateOf(bat.Id).EnergyWh, 1e-9);
            var after = CircuitSolver.Solve(b, inst.State);
            Assert.AreEqual(0.0, Math.Abs(after.Of(lamp.Id).CurrentA), 1e-6);
            Assert.IsTrue(after.Has(DiagnosticCode.BatteryEmpty));
        }

        [Test]
        public void TheSolutionIsTheSameEveryTimeAndForAnyWireOrder()
        {
            CircuitBlueprint Make(bool reversed)
            {
                var b = B();
                var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 100.0);
                var r1 = b.Place(CircuitComponentType.Resistor, 1, 0, 6.0);
                var r2 = b.Place(CircuitComponentType.Resistor, 2, 0, 3.0);
                var steps = new Action[] { () => Wire(b, bat, 1, r1, 0), () => Wire(b, bat, 1, r2, 0), () => Wire(b, r1, 1, bat, 0), () => Wire(b, r2, 1, bat, 0) };
                if (reversed) Array.Reverse(steps);
                foreach (var s in steps) s();
                return b;
            }
            double a = Run(Make(false)).SourcePowerW;
            for (int i = 0; i < 50; i++) Assert.AreEqual(a, Run(Make(false)).SourcePowerW);
            Assert.AreEqual(a, Run(Make(true)).SourcePowerW, 1e-9);
        }

        // ---- blueprints and instances ----
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        [Test]
        public void BlueprintSerializesAndComesBackEqual()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.Battery, 0, 0, 12.0, 0.2, 50.0);
            var l = b.Place(CircuitComponentType.Lamp, 2, 0, 12.0, 6.0);
            b.Connect(bat.Id, 1, l.Id, 0, 3.5, 2.5, WireMaterial.Aluminium);
            b.Connect(l.Id, 1, bat.Id, 0, 3.5, 2.5, WireMaterial.Aluminium);
            b.Ports.Add(new CircuitPort { Name = "power", Component = bat.Id, Pin = 1 });
            var copy = JsonSerializer.Deserialize<CircuitBlueprint>(JsonSerializer.Serialize(b, Json), Json);
            Assert.AreEqual(JsonSerializer.Serialize(b, Json), JsonSerializer.Serialize(copy, Json));
            Assert.AreEqual(Run(b).SourcePowerW, Run(copy).SourcePowerW, 1e-12);
            Assert.AreEqual(3.5, copy.Wires[0].LengthM);
            Assert.AreEqual(WireMaterial.Aluminium, copy.Wires[0].Material);
        }

        static CircuitHostSpec Host(int max = 8, params string[] ports) => new CircuitHostSpec { Name = "Charge controller", MaxComponents = max, MaxGridWidth = 16, MaxGridHeight = 12, RequiredPorts = ports.ToList() };

        [Test]
        public void InstallCopiesTheDesign_SoLaterBlueprintEditsDoNotReachIt()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.Battery, 0, 0, 12.0, 0.2, 50.0);
            b.Ports.Add(new CircuitPort { Name = "power", Component = bat.Id, Pin = 1 });
            var inst = CircuitInstance.Install(b, Host(8, "power"), 1, 77, out string why);
            Assert.IsNull(why);
            Assert.AreEqual(77, inst.HostId);
            Assert.AreEqual(b.Version, inst.BlueprintVersion);
            bat.P1 = 24.0;
            b.Place(CircuitComponentType.Resistor, 1, 0, 5.0);
            Assert.AreEqual(12.0, inst.Design.Components[0].P1);
            Assert.AreEqual(1, inst.Design.Components.Count);
            Assert.AreNotEqual(b.Version, inst.BlueprintVersion);
            Assert.AreEqual(50.0, inst.StateOf(bat.Id).EnergyWh, "a fresh battery starts full");
        }

        [Test]
        public void HostRefusesWhatDoesNotFit()
        {
            var b = B();
            for (int i = 0; i < 5; i++) b.Place(CircuitComponentType.Resistor, i, 0, 1.0);
            Assert.IsNull(CircuitInstance.Install(b, Host(4), 1, 1, out string tooMany));
            StringAssert.Contains("holds 4 parts", tooMany);
            Assert.IsNull(CircuitInstance.Install(b, Host(8, "power"), 1, 1, out string noPort));
            StringAssert.Contains("'power'", noPort);
            b.GridWidth = 40;
            Assert.IsNull(CircuitInstance.Install(b, Host(8), 1, 1, out string big));
            StringAssert.Contains("larger", big);
        }

        [Test]
        public void ElectricallySoundButNotFitForTheHost_AreSeparateQuestions()
        {
            var b = B();
            var bat = b.Place(CircuitComponentType.DcSupply, 0, 0, 12.0, 0.01, 100.0);
            var r = b.Place(CircuitComponentType.Resistor, 1, 0, 10.0);
            Wire(b, bat, 1, r, 0); Wire(b, r, 1, bat, 0);
            var res = Run(b);
            Assert.IsFalse(res.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
            Assert.IsNotNull(CircuitCompatibility.Check(b, Host(8, "motor")));
        }
    }
}

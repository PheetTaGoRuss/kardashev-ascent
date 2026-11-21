using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Electrical;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>
    /// AC infrastructure (stage 10). The frequencies and voltage classes below are placeholders for the tests only: the real values are
    /// an open design decision (D2), so no game code uses any.
    /// </summary>
    public class AcTests
    {
        const double F1 = 50.0, F2 = 60.0;   // test values only

        [Test]
        public void TwoAcNodesAtTheSameFrequencyJoin_AndDifferentFrequenciesAreRefused()
        {
            var g = new ElectricalGraph();
            var a = g.AddNode(1, NetworkType.Ac, 3, F1);
            var b = g.AddNode(2, NetworkType.Ac, 3, F1);
            var c = g.AddNode(3, NetworkType.Ac, 3, F2);
            Assert.IsTrue(g.Connect(a, b, 1000).Ok);
            var r = g.Connect(b, c, 1000);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("frequenc", r.Reason);
            Assert.AreEqual(F1, g.Networks()[0].FrequencyHz);
        }

        [Test]
        public void FrequencyDoesNotConcernDcAndAcStillRefusesDc()
        {
            var g = new ElectricalGraph();
            var d1 = g.AddNode(1, NetworkType.Dc, 1);
            var d2 = g.AddNode(2, NetworkType.Dc, 1);
            var a = g.AddNode(3, NetworkType.Ac, 3, F1);
            Assert.IsTrue(g.Connect(d1, d2, 10).Ok);
            StringAssert.Contains("AC and DC", g.Connect(d2, a, 10).Reason);
        }

        [Test]
        public void ATerminalMustMatchTheNodesFrequency()
        {
            var g = new ElectricalGraph();
            var n = g.AddNode(1, NetworkType.Ac, 3, F1);
            var dev = g.AddDevice(1, 0, 1);
            var t60 = g.AddTerminal(dev, NetworkType.Ac, 3, TerminalRole.Load);
            g.Terminal(t60).FrequencyHz = F2;
            Assert.IsFalse(g.Attach(t60, n).Ok);
            var t50 = g.AddTerminal(dev, NetworkType.Ac, 3, TerminalRole.Load);
            g.Terminal(t50).FrequencyHz = F1;
            Assert.IsTrue(g.Attach(t50, n).Ok);
        }

        // ---- converters ----
        [Test]
        public void ATransformerBridgesTwoAcClassesAtOneFrequency_WithoutMergingThem()
        {
            var g = new ElectricalGraph();
            var hi = g.AddNode(1, NetworkType.Ac, 4, F1);
            var lo = g.AddNode(2, NetworkType.Ac, 3, F1);
            var trafo = new ConverterSpec("Transformer", ConverterKind.Transformer, 4, 3, F1, 0.99);
            var r = ConverterInstaller.Install(g, trafo, hi, lo);
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(2, g.Networks().Count);
            Assert.AreEqual(2, g.NetworksOf(new DeviceId(r.Id)).Count);
        }

        [Test]
        public void TheFourKindsConnectTheTypesTheBriefSays()
        {
            Assert.AreEqual((NetworkType.Ac, NetworkType.Ac), (new ConverterSpec("t", ConverterKind.Transformer, 1, 2, F1, 0.9).InputType, new ConverterSpec("t", ConverterKind.Transformer, 1, 2, F1, 0.9).OutputType));
            var rect = new ConverterSpec("r", ConverterKind.Rectifier, 3, 1, F1, 0.9);
            Assert.AreEqual((NetworkType.Ac, NetworkType.Dc), (rect.InputType, rect.OutputType));
            var inv = new ConverterSpec("i", ConverterKind.Inverter, 1, 3, F1, 0.9);
            Assert.AreEqual((NetworkType.Dc, NetworkType.Ac), (inv.InputType, inv.OutputType));
            var dc = new ConverterSpec("d", ConverterKind.DcDc, 1, 2, 0.0, 0.9);
            Assert.AreEqual((NetworkType.Dc, NetworkType.Dc), (dc.InputType, dc.OutputType));
        }

        [Test]
        public void RectifierJoinsAnAcNetworkToADcNetwork()
        {
            var g = new ElectricalGraph();
            var ac = g.AddNode(1, NetworkType.Ac, 3, F1);
            var dc = g.AddNode(2, NetworkType.Dc, 1);
            Assert.IsTrue(ConverterInstaller.Install(g, new ConverterSpec("Rectifier", ConverterKind.Rectifier, 3, 1, F1, 0.97), ac, dc).Ok);
            Assert.AreEqual(2, g.Networks().Count);
        }

        [Test]
        public void ConvertersRefuseWrongNodesAndBadSpecs()
        {
            var g = new ElectricalGraph();
            var ac = g.AddNode(1, NetworkType.Ac, 3, F1);
            var dc = g.AddNode(2, NetworkType.Dc, 1);
            // an inverter wants DC in and AC out: given them the wrong way round the terminals do not fit the nodes
            var inv = new ConverterSpec("Inverter", ConverterKind.Inverter, 1, 3, F1, 0.95);
            Assert.IsFalse(ConverterInstaller.Install(g, inv, ac, dc).Ok);
            Assert.IsTrue(ConverterInstaller.Install(g, inv, dc, ac).Ok);
            StringAssert.Contains("classes must differ", new ConverterSpec("x", ConverterKind.Transformer, 3, 3, F1, 0.9).Problem());
            StringAssert.Contains("frequency", new ConverterSpec("x", ConverterKind.Rectifier, 3, 1, 0.0, 0.9).Problem());
            StringAssert.Contains("Efficiency", new ConverterSpec("x", ConverterKind.DcDc, 1, 2, 0.0, 1.2).Problem());
            // a transformer cannot change the frequency: nodes at two frequencies do not both fit its single frequency
            var a50 = g.AddNode(3, NetworkType.Ac, 4, F1);
            var a60 = g.AddNode(4, NetworkType.Ac, 3, F2);
            Assert.IsFalse(ConverterInstaller.Install(g, new ConverterSpec("T", ConverterKind.Transformer, 4, 3, F1, 0.99), a50, a60).Ok);
        }

        [Test]
        public void ConversionConservesEnergyAndTheLossIsHeat()
        {
            var c = new ConverterSpec("DC-DC", ConverterKind.DcDc, 1, 2, 0.0, 0.96);
            Assert.AreEqual(960.0, c.Output(1000.0), 1e-9);
            Assert.AreEqual(40.0, c.HeatW(1000.0), 1e-9);
            Assert.AreEqual(1000.0, c.Output(1000.0) + c.HeatW(1000.0), 1e-9);
        }

        // ---- RMS steady state ----
        [Test]
        public void ThreePhaseCurrentAndLossFollowTheRmsFormulas()
        {
            double p = 9e6, v = 33e3, r = 2.0;
            double i = AcSteadyState.LineCurrentA(p, v);
            Assert.AreEqual(p / (Math.Sqrt(3.0) * v), i, 1e-9);
            Assert.AreEqual(3.0 * i * i * r, AcSteadyState.LossW(p, v, r), 1e-6);
        }

        [Test]
        public void ThePerPhaseEquivalentGivesTheSameLossFromTheDcSolver()
        {
            double p = 9e6, v = 33e3, r = 2.0;
            var (pp, vp) = AcSteadyState.PerPhase(p, v);
            var res = DcFlow.Solve(2, new List<FlowBranch> { new FlowBranch(0, 1, r, 0.0) }, new[] { 0.0, pp }, 0, vp);
            Assert.IsTrue(res.Converged);
            // The closed form takes the nominal voltage at the point where the power is measured. The solver holds the lander (receiving)
            // end at that voltage, so the sending end is higher and carries a little less current: its loss is a few percent lower.
            double closed = AcSteadyState.LossW(p, v, r);
            Assert.Less(3.0 * res.TotalLossW, closed);
            Assert.AreEqual(closed, 3.0 * res.TotalLossW, 0.05 * closed);
        }

        // ---- subsea ----
        [Test]
        public void ALongAcSeaCableIsRefusedByTheGraphAndADcOneIsNot()
        {
            var g = new ElectricalGraph();
            var a = g.AddNode(1, NetworkType.Ac, 3, F1);
            var b = g.AddNode(2, NetworkType.Ac, 3, F1);
            var r = g.Connect(a, b, 300_000, touchesSea: true);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("direct current", r.Reason);
            Assert.IsTrue(g.Connect(a, b, 80_000, touchesSea: true).Ok);
            var d1 = g.AddNode(3, NetworkType.Dc, 1);
            var d2 = g.AddNode(4, NetworkType.Dc, 1);
            Assert.IsTrue(g.Connect(d1, d2, 300_000, touchesSea: true).Ok);
        }
    }
}

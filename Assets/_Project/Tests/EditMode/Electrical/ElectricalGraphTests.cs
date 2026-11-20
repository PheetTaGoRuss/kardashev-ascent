using System;
using System.Numerics;
using KardashevAscent.Simulation.Electrical;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    public class ElectricalGraphTests
    {
        static NodeId N(ElectricalGraph g, int tile = -1, NetworkType t = NetworkType.Dc, int cls = 1) => g.AddNode(tile, t, cls);
        static void Join(ElectricalGraph g, NodeId a, NodeId b) => Assert.IsTrue(g.Connect(a, b, 1000).Ok);

        [Test]
        public void ChainBranchAndJunctionFormOneNetwork()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g); var c = N(g); var d = N(g);
            Join(g, a, b); Join(g, b, c); Join(g, b, d);
            Assert.AreEqual(1, g.Networks().Count);
            Assert.IsFalse(g.Networks()[0].IsMesh);
            Assert.AreEqual(a, g.Networks()[0].Id);
        }

        [Test]
        public void ALoopMakesAMeshAndOpeningOneCableKeepsItJoined()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g); var c = N(g);
            Join(g, a, b); Join(g, b, c);
            int back = g.Connect(c, a, 1000).Id;
            Assert.IsTrue(g.Networks()[0].IsMesh);
            g.SetOpen(new ConnectionId(back), true);
            Assert.IsFalse(g.Networks()[0].IsMesh);
            Assert.AreEqual(1, g.Networks().Count);
        }

        [Test]
        public void RemovingTheOnlyLinkSplitsAndAddingMerges()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g); var c = N(g);
            Join(g, a, b);
            int link = g.Connect(b, c, 500).Id;
            Assert.AreEqual(1, g.Networks().Count);
            g.RemoveConnection(new ConnectionId(link));
            Assert.AreEqual(2, g.Networks().Count);
            Assert.IsFalse(g.SameNetwork(a, c));
            Join(g, b, c);
            Assert.AreEqual(1, g.Networks().Count);
        }

        [Test]
        public void FailedCableDoesNotJoin()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g);
            int id = g.Connect(a, b, 10).Id;
            g.SetCondition(new ConnectionId(id), 0.0);
            Assert.AreEqual(2, g.Networks().Count);
        }

        [Test]
        public void IndependentIslandsStayApart()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g); var c = N(g); var d = N(g);
            Join(g, a, b); Join(g, c, d);
            Assert.AreEqual(2, g.Networks().Count);
            Assert.IsFalse(g.SameNetwork(a, d));
        }

        [Test]
        public void AcAndDcCannotBeJoinedByACable()
        {
            var g = new ElectricalGraph();
            var dc = N(g, -1, NetworkType.Dc); var ac = N(g, -1, NetworkType.Ac);
            var r = g.Connect(dc, ac, 10);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("AC and DC", r.Reason);
            Assert.AreEqual(0, g.ConnectionCount);
        }

        [Test]
        public void VoltageClassMismatchIsRefused()
        {
            var g = new ElectricalGraph();
            var lo = N(g, -1, NetworkType.Dc, 1); var hi = N(g, -1, NetworkType.Dc, 2);
            var r = g.Connect(lo, hi, 10);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("voltage", r.Reason);
        }

        [Test]
        public void SelfCableAndMissingNodesAreRefused()
        {
            var g = new ElectricalGraph();
            var a = N(g);
            Assert.IsFalse(g.Connect(a, a, 1).Ok);
            Assert.IsFalse(g.Connect(a, new NodeId(99), 1).Ok);
        }

        [Test]
        public void ACableThroughATileDoesNotPowerItsBuilding()
        {
            var g = new ElectricalGraph();
            var left = N(g, 1); var mid = N(g, 2); var right = N(g, 3);
            Join(g, left, mid); Join(g, mid, right);
            // A building on tile 2 with a terminal that is not attached: nothing connects it.
            var dev = g.AddDevice(2, 0, 7);
            var term = g.AddTerminal(dev, NetworkType.Dc, 1, TerminalRole.Load);
            Assert.AreEqual(0, g.NetworksOf(dev).Count);
            Assert.IsTrue(g.Attach(term, mid).Ok);
            Assert.AreEqual(1, g.NetworksOf(dev).Count);
            g.Detach(term);
            Assert.AreEqual(0, g.NetworksOf(dev).Count);
        }

        [Test]
        public void TerminalAttachChecksCompatibility()
        {
            var g = new ElectricalGraph();
            var n = N(g, 1, NetworkType.Dc, 1);
            var dev = g.AddDevice(1, 0, 1);
            var ac = g.AddTerminal(dev, NetworkType.Ac, 1, TerminalRole.Load);
            Assert.IsFalse(g.Attach(ac, n).Ok);
            var hv = g.AddTerminal(dev, NetworkType.Dc, 2, TerminalRole.Load);
            Assert.IsFalse(g.Attach(hv, n).Ok);
        }

        [Test]
        public void AConverterBridgesTwoNetworksWithoutMergingThem()
        {
            var g = new ElectricalGraph();
            var dc1 = N(g, 1, NetworkType.Dc, 1); var dc2 = N(g, 2, NetworkType.Dc, 1);
            var ac1 = N(g, 3, NetworkType.Ac, 2); var ac2 = N(g, 4, NetworkType.Ac, 2);
            Join(g, dc1, dc2); Join(g, ac1, ac2);
            var inverter = g.AddDevice(2, 1, 99);
            var tIn = g.AddTerminal(inverter, NetworkType.Dc, 1, TerminalRole.Load);
            var tOut = g.AddTerminal(inverter, NetworkType.Ac, 2, TerminalRole.Source);
            Assert.IsTrue(g.Attach(tIn, dc2).Ok);
            Assert.IsTrue(g.Attach(tOut, ac1).Ok);
            Assert.AreEqual(2, g.Networks().Count);
            var nets = g.NetworksOf(inverter);
            Assert.AreEqual(2, nets.Count);
            Assert.AreEqual(NetworkType.Dc, nets[0].Type);
            Assert.AreEqual(NetworkType.Ac, nets[1].Type);
        }

        [Test]
        public void NetworksAreIndependentOfInsertionOrder()
        {
            string Describe(int[] order)
            {
                var g = new ElectricalGraph();
                var ids = new NodeId[6];
                for (int i = 0; i < 6; i++) ids[i] = N(g);
                int[][] links = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 3, 4 }, new[] { 4, 5 }, new[] { 5, 3 } };
                foreach (int k in order) Join(g, ids[links[k][0]], ids[links[k][1]]);
                var parts = new System.Collections.Generic.List<string>();
                foreach (var net in g.Networks()) parts.Add(net.Id + ":" + net.Nodes.Count + ":" + net.IsMesh);
                return string.Join("|", parts);
            }
            string expected = Describe(new[] { 0, 1, 2, 3, 4 });
            Assert.AreEqual(expected, Describe(new[] { 4, 3, 2, 1, 0 }));
            Assert.AreEqual(expected, Describe(new[] { 2, 0, 4, 1, 3 }));
            Assert.AreEqual("1:3:False|4:3:True", expected);
        }

        [Test]
        public void NetworksAreCachedUntilTheTopologyChanges()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g);
            Join(g, a, b);
            var first = g.Networks();
            int stamp = g.TopologyStamp;
            Assert.AreSame(first, g.Networks());
            Assert.AreEqual(stamp, g.TopologyStamp);
            N(g);
            Assert.AreNotSame(first, g.Networks());
        }

        [Test]
        public void IdsAreNeverReused()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g);
            g.RemoveNode(b);
            var c = N(g);
            Assert.AreEqual(3, c.Value);
            Assert.AreEqual(4, g.NextNodeValue);
        }

        [Test]
        public void RemovingANodeRemovesItsCablesAndDetachesTerminals()
        {
            var g = new ElectricalGraph();
            var a = N(g, 1); var b = N(g, 2); var c = N(g, 3);
            Join(g, a, b); Join(g, b, c);
            var dev = g.AddDevice(2, 0, 1);
            var t = g.AddTerminal(dev, NetworkType.Dc, 1, TerminalRole.Load);
            g.Attach(t, b);
            g.RemoveNode(b);
            Assert.AreEqual(0, g.ConnectionCount);
            Assert.IsTrue(g.Terminal(t).Node.IsNone);
            Assert.AreEqual(2, g.Networks().Count);
        }

        [Test]
        public void ParallelCablesBetweenTheSameNodesMakeAMesh()
        {
            var g = new ElectricalGraph();
            var a = N(g); var b = N(g);
            Join(g, a, b); Join(g, a, b);
            Assert.IsTrue(g.Networks()[0].IsMesh);
        }

        [Test]
        public void CableLengthIsGreatCircleArcTimesRadiusTimesFactor()
        {
            var x = new Vector3(1, 0, 0);
            var y = new Vector3(0, 1, 0);
            double r = 3_389_500;
            Assert.AreEqual(Math.PI / 2 * r * 1.1, ElectricalGraph.CableLengthM(x, y, r), 1e-3);
            Assert.AreEqual(Math.PI / 2 * r, ElectricalGraph.CableLengthM(x, y, r, 1.0), 1e-3);
            Assert.AreEqual(0.0, ElectricalGraph.CableLengthM(x, x, r), 1e-9);
            // A larger planet makes the same arc longer in proportion.
            Assert.AreEqual(2 * ElectricalGraph.CableLengthM(x, y, r), ElectricalGraph.CableLengthM(x, y, 2 * r), 1e-3);
            // Near-by tiles stay accurate.
            var z = Vector3.Normalize(new Vector3(1, 1e-4f, 0));
            Assert.AreEqual(1e-4 * r * 1.1, ElectricalGraph.CableLengthM(x, z, r), 2.0);
        }
    }
}

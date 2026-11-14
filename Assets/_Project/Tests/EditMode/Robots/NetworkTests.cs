using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>The colony network (workforce v2 stage E): hub, relays, range, brownout, cut-off robots on cached rules.</summary>
    public class NetworkTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike);

        /// <summary>A land tile exactly this many hops from the base (−1 if none).</summary>
        static int LandTileAtHops(PlanetSimulation sim, int hops)
        {
            var dist = new Dictionary<int, int> { [sim.BaseTileId] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(sim.BaseTileId);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                if (dist[t] == hops && !sim.GetTile(t).IsWater) return t;
                if (dist[t] >= hops) continue;
                foreach (int n in Sphere.Neighbors[t])
                    if (!dist.ContainsKey(n)) { dist[n] = dist[t] + 1; queue.Enqueue(n); }
            }
            return -1;
        }

        [Test]
        public void Coverage_ReachesTheRange_FromTheHub()
        {
            var net = new ColonyNetwork(Sphere, 0);
            Assert.AreEqual(0, net.Coverage[0]);
            foreach (int n in Sphere.Neighbors[0])
                Assert.AreEqual(1, net.Coverage[n]);
            int farthest = 0;
            foreach (int hops in net.Coverage.Values) farthest = Math.Max(farthest, hops);
            Assert.AreEqual(ColonyNetwork.RangeTiles, farthest, "coverage stops at the range");
            Assert.Less(net.Coverage.Count, Sphere.TileCount, "the planet is not all covered by one hub");
        }

        [Test]
        public void Brownout_TakesTheWholeNetworkDown_ButCoverageStays()
        {
            var net = new ColonyNetwork(Sphere, 0);
            Assert.IsTrue(net.IsConnected(0));
            net.SetPowered(false);
            Assert.IsFalse(net.IsConnected(0));
            Assert.IsTrue(net.IsCovered(0), "the map still shows what the network covers when powered");
            net.SetPowered(true);
            Assert.IsTrue(net.IsConnected(0));
        }

        [Test]
        public void ARelay_ExtendsCoverage_AndDrawsPower()
        {
            var net = new ColonyNetwork(Sphere, 0);
            int edge = -1;
            foreach (var kv in net.Coverage) if (kv.Value == ColonyNetwork.RangeTiles) { edge = kv.Key; break; }
            Assume.That(edge >= 0);
            int before = net.Coverage.Count;
            double powerBefore = net.PowerW;
            net.AddRelay(edge);
            Assert.AreEqual(0, net.Coverage[edge], "a relay is a node");
            Assert.Greater(net.Coverage.Count, before);
            Assert.AreEqual(powerBefore + ColonyNetwork.RelayPowerW, net.PowerW, 1e-9);
            net.RemoveRelay(edge);
            Assert.AreEqual(before, net.Coverage.Count);
        }

        [Test]
        public void TheLanderIsTheHub_AndRobotsStartConnected()
        {
            var sim = Create();
            Assert.AreEqual(sim.BaseTileId, sim.Network.HubTile);
            sim.Tick(0.25);
            foreach (Robot r in sim.Workforce.Robots)
                Assert.IsTrue(r.Connected);
            Assert.AreEqual(0, sim.Workforce.DisconnectedCount);
        }

        [Test]
        public void ARobotSentOutOfRange_DropsOff_AndKeepsItsCachedRules()
        {
            var sim = Create();
            // A colony that can power itself: without panels the lander's RTGs can't carry 100 kW, the grid browns
            // out within a day and (as designed) takes the network down with it, which is not what this test is about.
            for (int s = 0; s < 40; s += 2) sim.PlacePanel(sim.BaseTileId, s, 0.3, Math.PI);
            sim.Tick(0.25);
            int far = LandTileAtHops(sim, ColonyNetwork.RangeTiles + 3);
            Robot r = sim.Workforce.Robots[0];
            RobotGroup g = sim.Workforce.GetGroup(r.GroupId);
            var events = new List<(int, bool)>();
            sim.Workforce.ConnectionChanged += (rb, on) => events.Add((rb.Id, on));

            Assert.AreEqual(TaskPriority.Normal, sim.Workforce.ProgramPriority(r, TaskKind.Mine));
            sim.Tick(0.25);   // the first tick sets up movement (routes); SendTo needs it
            // Beyond the range and reachable by road: the farthest such tile within a few hops past the edge.
            for (int hops = ColonyNetwork.RangeTiles + 3; hops > ColonyNetwork.RangeTiles && !sim.Workforce.SendTo(r.Id, far); hops--)
                far = LandTileAtHops(sim, hops);
            Assume.That(r.ManualTarget.HasValue, "a reachable land tile beyond the range");
            far = r.ManualTarget.Value;
            // It drives out until it arrives, or turns back for charge (the reach rule): either way it must have
            // crossed the edge of coverage on the way, which is what this test is about.
            for (int i = 0; i < 200 && r.Tile != far && r.Connected; i++) sim.Tick(0.5);
            Assert.IsFalse(r.Connected, "beyond the range: cut off");
            Assert.IsFalse(sim.Network.IsConnected(r.Tile), "it stands outside coverage");
            Assert.Greater(r.HoursOffline, 0.0);
            CollectionAssert.Contains(events, (r.Id, false));

            // The player changes the group's rules while the robot is away: it can't hear, it keeps the cached ones.
            sim.Workforce.SetGroupPriority(g.Id, TaskKind.Mine, TaskPriority.Critical);
            Assert.AreEqual(TaskPriority.Normal, sim.Workforce.ProgramPriority(r, TaskKind.Mine), "cached priority");

            // Back in range: it syncs and picks the new rules up.
            Assert.IsTrue(sim.Workforce.SendTo(r.Id, sim.BaseTileId));
            for (int i = 0; i < 400 && !r.Connected; i++) sim.Tick(0.5);
            Assert.IsFalse(sim.IsBrownout, "the panels keep the grid, and so the hub, up");
            Assert.IsTrue(r.Connected);
            Assert.AreEqual(0.0, r.HoursOffline);
            Assert.AreEqual(TaskPriority.Critical, sim.Workforce.ProgramPriority(r, TaskKind.Mine), "synced on reconnect");
            CollectionAssert.Contains(events, (r.Id, true));
        }

        [Test]
        public void CutOff_UnderDemand_IgnoresTheColonysNeeds()
        {
            var w = new Workforce();
            w.Reset(0);
            Robot r = w.Robots[0];
            w.SetGroupProgram(r.GroupId, OperatingProgram.Demand);
            w.Network = new ColonyNetwork(Sphere, 0);
            w.UpdateConnections(0.0);   // syncs the Demand program
            w.Assign(new List<WorkTask>(), new ColonyNeeds(true, true));
            Assert.AreEqual(TaskPriority.High, w.ProgramPriority(r, TaskKind.Build), "connected: it hears the colony needs builders");
            w.Network.SetPowered(false);
            w.UpdateConnections(1.0);
            Assert.IsFalse(r.Connected);
            Assert.AreEqual(TaskPriority.Normal, w.ProgramPriority(r, TaskKind.Build), "cut off: no news of the colony's needs");
        }

        [Test]
        public void Relay_RelaysOnlyOnceBuilt_AndCanBeRemoved()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            int covered = sim.Network.Coverage.Count;
            Assert.IsTrue(sim.PlaceRelay(t, 5));
            Assert.AreEqual(BuildingType.Relay, sim.GetBuilding(t, 5));
            Assert.IsTrue(sim.IsUnderConstruction(t, 5));
            CollectionAssert.IsEmpty(sim.Network.Relays, "a site relays nothing");
            sim.Workforce.Resume(sim.Workforce.Robots[0].GroupId);
            sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, 5));
            CollectionAssert.AreEquivalent(new[] { t }, sim.Network.Relays);
            Assert.AreEqual(covered, sim.Network.Coverage.Count, "a relay on the hub tile adds no coverage");
            Assert.IsTrue(sim.RemoveBuilding(t, 5));
            CollectionAssert.IsEmpty(sim.Network.Relays);
        }

        [Test]
        public void Save_KeepsRelays_AndEachRobotsConnection()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.PlaceRelay(t, 5);
            a.Tick(0.25);
            Robot r = a.Workforce.Robots[0];
            r.Connected = false;
            r.HoursOffline = 4.5;
            r.CachedPriorities[TaskKind.Mine] = TaskPriority.Low;
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(BuildingType.Relay, b.GetBuilding(t, 5));
            CollectionAssert.AreEquivalent(new[] { t }, b.Network.Relays);
            Robot rb = b.Workforce.Robots[0];
            Assert.IsFalse(rb.Connected);
            Assert.AreEqual(4.5, rb.HoursOffline);
            Assert.AreEqual(TaskPriority.Low, rb.CachedPriorities[TaskKind.Mine]);
        }
    }
}

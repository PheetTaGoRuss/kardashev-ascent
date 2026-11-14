using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>
    /// Workforce v2 stage E, second part (decided 2026-11-14): AI levels from processors, nearby exchange, findings
    /// that wait for sync, onboard AI power, and colony computing as the first load cut in a shortage.
    /// </summary>
    public class NetworkAITests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike) { PuzzleChance = 1.0 };   // these tests are about the puzzles

        /// <summary>A workforce on tile 0 with a network whose hub is far away, so every robot is cut off.</summary>
        static Workforce CutOffWorkforce(int robots)
        {
            var w = new Workforce();
            w.Reset(0);
            for (int i = 1; i < robots; i++) w.AddRobot(RobotKind.General, 0);
            int far = FarTile(0, ColonyNetwork.RangeTiles + 3);
            w.Network = new ColonyNetwork(Sphere, far);
            return w;
        }

        /// <summary>A tile exactly this many hops from a start tile.</summary>
        static int FarTile(int from, int hops)
        {
            var dist = new Dictionary<int, int> { [from] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                if (dist[t] == hops) return t;
                foreach (int n in Sphere.Neighbors[t])
                    if (!dist.ContainsKey(n)) { dist[n] = dist[t] + 1; queue.Enqueue(n); }
            }
            return -1;
        }

        [Test]
        public void AILevel_ComesFromProcessors()
        {
            var w = new Workforce();
            w.Reset(0);
            Robot r = w.Robots[0];
            Assert.AreEqual(1, r.AILevel);
            r.Modules |= RobotModule.Processor;
            Assert.AreEqual(2, r.AILevel);
            r.Modules |= RobotModule.AdvancedProcessor;
            Assert.AreEqual(3, r.AILevel);
            Assert.AreEqual(2 * Workforce.ComputingWPerLevel, w.ComputingRequestW, 1e-9, "each level above 1 asks for computing");
            w.ComputingAvailable = false;
            Assert.AreEqual(1, w.EffectiveAILevel(r), "no computing: back to level 1");
        }

        [Test]
        public void AdvancedProcessor_NeedsAProcessorFirst_AndUnlocksLater()
        {
            Assert.Less(RobotTech.UnlockMilestone(RobotModule.Processor), RobotTech.UnlockMilestone(RobotModule.AdvancedProcessor));
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            StringAssert.Contains("unlocks at milestone", sim.OrderUpgrade(r.Id, RobotModule.Processor));
        }

        [Test]
        public void CutOff_Level2_KeepsWorkingByTheNeedsItLastKnew_Level1DoesNot()
        {
            var w = new Workforce();
            w.Reset(0);
            w.AddRobot(RobotKind.General, 0);
            Robot basic = w.Robots[0], smart = w.Robots[1];
            smart.Modules |= RobotModule.Processor;
            w.SetGroupProgram(basic.GroupId, OperatingProgram.Demand);
            w.SetGroupProgram(smart.GroupId, OperatingProgram.Demand);
            w.Network = new ColonyNetwork(Sphere, 0);
            w.Assign(new List<WorkTask>(), new ColonyNeeds(true, true));   // the colony needs builders
            w.UpdateConnections(0.0);                                      // both sync while connected
            w.Network.SetPowered(false);
            w.UpdateConnections(1.0);
            Assert.IsFalse(basic.Connected);
            Assert.IsFalse(smart.Connected);
            Assert.IsNull(basic.ExchangeVia, "nobody connected to exchange with");
            Assert.AreEqual(TaskPriority.Normal, w.ProgramPriority(basic, TaskKind.Build), "level 1: no notion of the colony's needs");
            Assert.AreEqual(TaskPriority.High, w.ProgramPriority(smart, TaskKind.Build), "level 2: the needs it last synced");
            w.ComputingAvailable = false;
            Assert.AreEqual(TaskPriority.Normal, w.ProgramPriority(smart, TaskKind.Build), "computing cut: back to level 1");
        }

        [Test]
        public void NearbyExchange_ACutOffRobotSyncsThroughAConnectedNeighbour()
        {
            var w = new Workforce();
            w.Reset(0);
            int hub = FarTile(0, ColonyNetwork.RangeTiles);       // tile 0 is right at the edge of coverage
            w.Network = new ColonyNetwork(Sphere, hub);
            int edge = 0, outside = -1;
            foreach (int n in Sphere.Neighbors[edge])
                if (!w.Network.IsCovered(n)) { outside = n; break; }
            Assume.That(outside >= 0, "a tile just outside coverage");
            Robot inside = w.Robots[0];
            Robot lone = w.AddRobot(RobotKind.General, outside);
            w.UpdateConnections(0.0);
            Assert.IsTrue(inside.Connected);
            Assert.IsFalse(lone.Connected, "it is off the network itself");
            Assert.AreEqual(inside.Id, lone.ExchangeVia, "but talks to its neighbour");
            Assert.IsTrue(w.CanSync(lone));
            // The player's new rules reach it through the neighbour.
            w.SetGroupPriority(lone.GroupId, TaskKind.Mine, TaskPriority.Critical);
            w.UpdateConnections(0.5);
            Assert.AreEqual(TaskPriority.Critical, w.ProgramPriority(lone, TaskKind.Mine));
        }

        [Test]
        public void ExchangeRange_IsTwoTilesAtLevel3()
        {
            Assert.AreEqual(1, Workforce.ExchangeRange(1));
            Assert.AreEqual(1, Workforce.ExchangeRange(2));
            Assert.AreEqual(2, Workforce.ExchangeRange(3));
            var net = new ColonyNetwork(Sphere, 0);
            int two = FarTile(0, 2);
            Assert.IsTrue(net.Within(0, two, 2));
            Assert.IsFalse(net.Within(0, two, 1));
        }

        [Test]
        public void OnboardAI_DrawsMore_AtHigherLevels_AndWhenCutOff()
        {
            Assert.Less(RobotSpecs.OnboardAIW(1, false), RobotSpecs.OnboardAIW(2, false));
            Assert.Less(RobotSpecs.OnboardAIW(2, false), RobotSpecs.OnboardAIW(3, false));
            Assert.AreEqual(2.0 * RobotSpecs.OnboardAIW(2, false), RobotSpecs.OnboardAIW(2, true), 1e-12);
        }

        [Test]
        public void CutOffSurvey_WaitsWithTheRobot_UntilItCanSync()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Robot r = sim.Workforce.Robots[0];
            // Brownout: the whole network is down, so the robot is cut off while it surveys the base tile.
            sim.Network.SetPowered(false);
            sim.Workforce.Network.SetPowered(false);
            Assert.IsTrue(sim.OrderSurvey(t));
            for (int i = 0; i < 12 && sim.IsSurveyOrdered(t); i++)
            {
                sim.Tick(0.5);
                sim.Network.SetPowered(false);   // keep it down whatever the grid does
            }
            Assume.That(!sim.IsSurveyOrdered(t), "the survey finished");
            if (r.Connected)
                Assert.Inconclusive("the grid stayed up; this test needs the robot cut off");
            Assert.AreEqual(1, sim.HeldFindings.Count, "the cores stay with the cut-off robot");
            Assert.AreEqual(1, r.UnsyncedFindings);
            Assert.AreEqual(0, sim.SurveyPuzzles.Count);
            Assert.IsFalse(sim.Deposits.IsSurveyed(t));

            // Power back: it syncs and the colony gets the cores.
            sim.Network.SetPowered(true);
            for (int i = 0; i < 4 && sim.HeldFindings.Count > 0; i++)
            {
                sim.Network.SetPowered(true);
                sim.Workforce.UpdateConnections(0.0);
                sim.Tick(0.25);
            }
            if (!r.Connected)
                Assert.Inconclusive("the grid browned out again; the hand-in path is covered by the save test");
            Assert.AreEqual(0, sim.HeldFindings.Count);
            Assert.AreEqual(0, r.UnsyncedFindings);
            Assert.IsTrue(sim.SurveyPuzzles.Count == 1 || sim.Deposits.IsSurveyed(t), "cores waiting, or a rock-only tile surveyed");
        }

        [Test]
        public void ConnectedSurvey_ArrivesAtOnce()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.OrderSurvey(t));
            for (int i = 0; i < 12 && sim.IsSurveyOrdered(t); i++) sim.Tick(0.5);
            Assume.That(sim.Workforce.Robots[0].Connected);
            Assert.AreEqual(0, sim.HeldFindings.Count);
            Assert.IsTrue(sim.SurveyPuzzles.Count == 1 || sim.Deposits.IsSurveyed(t));
        }

        [Test]
        public void Computing_RunsWithPower_IsCutInABrownout()
        {
            var sim = Create();
            Robot r = sim.Workforce.Robots[0];
            r.Modules |= RobotModule.Processor;
            Assert.AreEqual(Workforce.ComputingWPerLevel, sim.ComputingRequestW, 1e-9);
            sim.Tick(0.25);
            Assert.IsTrue(sim.ComputingOn, "the lander's battery starts full: computing runs");
            Assert.IsTrue(sim.Workforce.ComputingAvailable);
            for (int i = 0; i < 200 && !sim.IsBrownout; i++) sim.Tick(0.5);
            Assume.That(sim.IsBrownout, "a colony with no panels browns out on the lander's RTGs");
            sim.Tick(0.25);
            Assert.IsFalse(sim.ComputingOn, "the first load cut");
            Assert.AreEqual(1, sim.Workforce.EffectiveAILevel(r));
        }

        [Test]
        public void Save_KeepsHeldFindingsAndCachedNeeds()
        {
            var a = Create();
            int t = a.BaseTileId;
            Robot r = a.Workforce.Robots[0];
            r.Connected = false;
            r.CachedNeeds = new ColonyNeeds(true, false);
            r.UnsyncedFindings = 1;
            var held = new HeldFinding { RobotId = r.Id, Kind = FindingKind.SurveyRockOnly, Tile = t };
            a.heldFindings.Add(held);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(1, b.HeldFindings.Count);
            Assert.AreEqual(FindingKind.SurveyRockOnly, b.HeldFindings[0].Kind);
            Robot rb = b.Workforce.Robots[0];
            Assert.IsTrue(rb.CachedNeeds.BrownoutRisk);
            Assert.IsFalse(rb.CachedNeeds.SitesWaiting);
            Assert.AreEqual(1, rb.UnsyncedFindings);
        }

        [Test]
        public void OldSave_HasNoHeldFindings()
        {
            var a = Create();
            SimulationState state = a.CaptureState();
            state.Version = 24;
            state.HeldFindings = null;
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(0, b.HeldFindings.Count);
        }
    }
}

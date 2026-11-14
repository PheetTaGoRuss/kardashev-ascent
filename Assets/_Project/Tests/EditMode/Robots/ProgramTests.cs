using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Operating programs, ranked ordering and interruption rules (phase 4).</summary>
    public class ProgramTests
    {
        static Workforce Start()
        {
            var w = new Workforce();
            w.Reset(0, 1);
            return w;
        }

        static WorkTask Build(int tile, int slot, int order = 0) => new WorkTask(TaskKind.Build, tile, slot, order);
        static WorkTask Mine(int tile, int order = 0) => new WorkTask(TaskKind.Mine, tile, 1, order);
        static WorkTask Survey(int tile, int order = 0) => new WorkTask(TaskKind.Survey, tile, -1, order);

        [Test]
        public void Facility_OnlyTasksOnItsTile()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            w.SetGroupProgram(r.GroupId, OperatingProgram.Facility);
            w.SetGroupFacility(r.GroupId, 7);
            w.Assign(new List<WorkTask> { Build(3, 0), Survey(7) });
            Assert.AreEqual(Survey(7), r.Task, "the build on tile 3 is outside its facility");
            w.Assign(new List<WorkTask> { Build(3, 0) });
            Assert.IsNull(r.Task);
            w.SetGroupFacility(r.GroupId, null);
            w.Assign(new List<WorkTask> { Build(3, 0) });
            Assert.AreEqual(Build(3, 0), r.Task, "no facility: anywhere");
        }

        [Test]
        public void Demand_BuildsFirstWhenTheColonyNeedsIt()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            w.SetGroupProgram(r.GroupId, OperatingProgram.Demand);
            w.SetGroupPriority(r.GroupId, TaskKind.Survey, TaskPriority.High);
            Assert.AreEqual(TaskPriority.Normal, w.ProgramPriority(r, TaskKind.Survey), "Demand sets its own priorities");
            w.Assign(new List<WorkTask> { Survey(4), Build(3, 0) }, new ColonyNeeds(true, false));
            Assert.AreEqual(Build(3, 0), r.Task);
            Assert.AreEqual(TaskPriority.High, w.ProgramPriority(r, TaskKind.Build));
            Assert.AreEqual(TaskPriority.Low, w.ProgramPriority(r, TaskKind.Mine));
        }

        [Test]
        public void Demand_KeepsDisabledKindsDisabled()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            w.SetGroupProgram(r.GroupId, OperatingProgram.Demand);
            w.SetGroupPriority(r.GroupId, TaskKind.Build, TaskPriority.Disabled);
            w.Assign(new List<WorkTask> { Build(3, 0) }, new ColonyNeeds(true, true));
            Assert.IsNull(r.Task);
        }

        [Test]
        public void TaskQueue_FirstOpenQueuedTask_IdleWhenEmpty_DoneTasksLeave()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            int g = r.GroupId;
            w.SetGroupProgram(g, OperatingProgram.TaskQueue);
            var open = new List<WorkTask> { Build(3, 0), Mine(5), Survey(6) };
            w.Assign(open);
            Assert.IsNull(r.Task, "an empty queue: idle");
            Assert.IsTrue(w.Enqueue(g, Survey(6)));
            Assert.IsTrue(w.Enqueue(g, Build(3, 0)));
            Assert.IsFalse(w.Enqueue(g, Survey(6)), "already queued");
            w.Assign(open);
            Assert.AreEqual(Survey(6), r.Task, "first in the queue, whatever its priority");
            w.Assign(new List<WorkTask> { Build(3, 0), Mine(5) });   // the survey is done
            Assert.AreEqual(1, w.GetGroup(g).Queue.Count);
            Assert.AreEqual(Build(3, 0), r.Task);
            Assert.IsTrue(w.RemoveFromQueue(g, 0));
        }

        [Test]
        public void Ranking_ChangesTheOrderOfTies()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            int g = r.GroupId;
            var open = new List<WorkTask> { Build(3, 0), Mine(5) };
            w.Assign(open);
            Assert.AreEqual(Build(3, 0), r.Task, "default order: build first");
            w.Assign(new List<WorkTask>());
            for (int i = 0; i < Enum.GetValues(typeof(TaskKind)).Length; i++) w.RaiseInRanking(g, TaskKind.Mine);
            Assert.AreEqual(TaskKind.Mine, w.GetGroup(g).RankedKinds()[0]);
            w.Assign(open);
            Assert.AreEqual(Mine(5), r.Task, "mining ranked first");
            w.ResetRanking(g);
            Assert.IsFalse(w.GetGroup(g).HasCustomRanking);
        }

        [Test]
        public void Ranking_NeverBeatsAHigherPriority()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            for (int i = 0; i < 5; i++) w.RaiseInRanking(r.GroupId, TaskKind.Mine);
            w.SetGroupPriority(r.GroupId, TaskKind.Build, TaskPriority.High);
            w.Assign(new List<WorkTask> { Build(3, 0), Mine(5) });
            Assert.AreEqual(Build(3, 0), r.Task);
        }

        [Test]
        public void CaptureRestore_KeepsFacilityRankingAndQueue()
        {
            Workforce w = Start();
            int g = w.Robots[0].GroupId;
            w.SetGroupProgram(g, OperatingProgram.TaskQueue);
            w.SetGroupFacility(g, 9);
            w.RaiseInRanking(g, TaskKind.Survey);
            w.Enqueue(g, Build(3, 2));
            var copy = new Workforce();
            Assert.IsTrue(copy.Restore(w.Capture()));
            RobotGroup c = copy.GetGroup(g);
            Assert.AreEqual(OperatingProgram.TaskQueue, c.Program);
            Assert.AreEqual(9, c.FacilityTile);
            Assert.IsTrue(c.HasCustomRanking);
            Assert.AreEqual(w.GetGroup(g).RankedKinds(), c.RankedKinds());
            Assert.AreEqual(Build(3, 2), c.Queue[0]);
            Assert.IsTrue(typeof(QueueItemRecord).IsSerializable);
        }

        // ---- In the colony ----

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void InterruptedSurvey_Restarts_D15()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int t = sim.BaseTileId;
            sim.OrderSurvey(t);
            sim.Tick(1.0);
            Assert.AreEqual(0.5, sim.SurveyProgress(t), 1e-9);
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            sim.Tick(0.25);
            Assert.AreEqual(0.0, sim.SurveyProgress(t), 1e-9, "a measurement starts again");
        }

        [Test]
        public void OpenWork_ListsEveryKindOfTask()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int t = sim.BaseTileId;
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            sim.PlaceBattery(t, 0);
            sim.OrderSurvey(t);
            sim.OrderMining(t, KardashevAscent.Simulation.Resources.Material.Rock);
            var kinds = new HashSet<TaskKind>();
            foreach (WorkTask w in sim.OpenWork()) kinds.Add(w.Kind);
            CollectionAssert.AreEquivalent(new[] { TaskKind.Build, TaskKind.Survey, TaskKind.Mine }, kinds);
        }

        [Test]
        public void DemandGroup_InTheColony_BuildsWhileSitesWait()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int t = sim.BaseTileId;
            Robot r = sim.Workforce.Robots[0];
            sim.Workforce.SetGroupProgram(r.GroupId, OperatingProgram.Demand);
            sim.OrderMining(t, KardashevAscent.Simulation.Resources.Material.Rock);
            sim.Workforce.Pause(r.GroupId);
            // A site on the next tile, so it is still waiting when the robot takes it.
            int next = -1;
            foreach (int nb in sim.Sphere.Neighbors[t])
                if (next < 0 && !sim.GetTile(nb).IsWater && sim.CanPlace(KardashevAscent.Simulation.Game.BuildingType.Battery, nb, 0)) next = nb;
            Assert.IsTrue(sim.PlaceBattery(next, 0));
            sim.Workforce.Resume(r.GroupId);
            sim.RefreshWork();
            Assert.IsTrue(sim.Workforce.Needs.SitesWaiting);
            Assert.AreEqual(TaskKind.Build, r.Task.Value.Kind);
        }
    }
}

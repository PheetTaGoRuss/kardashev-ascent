using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Stage B (spec v2 §4): task records and states, scheduler scoring, reservations, return after charging, reasons.</summary>
    public class SchedulerTests
    {
        static WorkTask Build(int slot, int order = 0) => new WorkTask(TaskKind.Build, 1, slot, order);
        static WorkTask Mine(int order = 0) => new WorkTask(TaskKind.Mine, 1, 1, order);
        static WorkTask Survey(int tile = 1) => new WorkTask(TaskKind.Survey, tile, -1);

        // ---- Scoring ----

        [Test]
        public void Specialist_GetsItsOwnWork_GeneralTakesTheRest()
        {
            var w = new Workforce();
            Robot g = w.AddRobot(RobotKind.General, 0);
            Robot m = w.AddRobot(RobotKind.Mining, 0);
            w.Assign(new List<WorkTask> { Mine() });
            Assert.AreEqual(Mine(), m.Task, "the Mining robot, though the General robot comes first by id");
            Assert.IsNull(g.Task);
            w.Assign(new List<WorkTask> { Mine(), Build(2) });
            Assert.AreEqual(Mine(), m.Task);
            Assert.AreEqual(Build(2), g.Task, "nobody left idle while work is open");
        }

        [Test]
        public void FullerBattery_WinsATie()
        {
            var w = new Workforce();
            Robot a = w.AddRobot(RobotKind.General, 0);
            Robot b = w.AddRobot(RobotKind.General, 0);
            a.Battery.Restore(0.0, 0.5 * a.Battery.CapacityWh);
            w.Assign(new List<WorkTask> { Build(2) });
            Assert.AreEqual(Build(2), b.Task);
        }

        [Test]
        public void SameInputs_SameAssignments()
        {
            string Run()
            {
                var w = new Workforce();
                for (int i = 0; i < 6; i++) w.AddRobot(i % 2 == 0 ? RobotKind.General : RobotKind.Construction, 0);
                var tasks = new List<WorkTask>();
                for (int i = 0; i < 5; i++) tasks.Add(Build(i, i));
                tasks.Add(Mine(0));
                w.Assign(tasks);
                string s = "";
                foreach (Robot r in w.Robots) s += r.Task.HasValue ? r.Task.Value.ToString() : "-";
                return s;
            }
            Assert.AreEqual(Run(), Run());
        }

        // ---- Interruption policy ----

        [Test]
        public void Critical_DoesNotInterruptASurvey_ItWouldLoseItsProgress()
        {
            Assert.IsFalse(TaskPolicy.For(TaskKind.Survey).MayBeInterrupted);
            Assert.IsFalse(TaskPolicy.For(TaskKind.Survey).KeepsProgress);
            Assert.IsTrue(TaskPolicy.For(TaskKind.Build).MayBeInterrupted);
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            w.Assign(new List<WorkTask> { Survey() });
            w.SetGroupPriority(r.GroupId, TaskKind.Salvage, TaskPriority.Critical);
            w.Assign(new List<WorkTask> { Survey(), new WorkTask(TaskKind.Salvage, 1, 3) });
            Assert.AreEqual(Survey(), r.Task);
        }

        [Test]
        public void Critical_Interruption_IsLoggedWithItsReason()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            w.Assign(new List<WorkTask> { Build(2) });
            w.SetGroupPriority(r.GroupId, TaskKind.Salvage, TaskPriority.Critical);
            var salvage = new WorkTask(TaskKind.Salvage, 1, 3);
            w.Assign(new List<WorkTask> { Build(2), salvage });
            Assert.AreEqual(salvage, r.Task);
            AssignmentChange last = w.Changes[w.Changes.Count - 1];
            Assert.AreEqual(r.Id, last.RobotId);
            Assert.AreEqual(Build(2), last.From);
            Assert.AreEqual(salvage, last.To);
            StringAssert.Contains("critical", last.Reason);
        }

        // ---- Reservations ----

        [Test]
        public void Reservation_TakesPrecedence_AndTheOtherRobotIsToldWhy()
        {
            var w = new Workforce();
            Robot a = w.AddRobot(RobotKind.General, 0);
            Robot b = w.AddRobot(RobotKind.General, 0);
            w.Assign(new List<WorkTask> { Build(2), Build(3, 1) });
            Assert.AreEqual(Build(2), a.Task);
            Assert.IsTrue(w.Reserve(b.Id, Build(2)));
            w.Assign(new List<WorkTask> { Build(2), Build(3, 1) });
            Assert.AreEqual(Build(2), b.Task, "the reservation wins");
            Assert.AreEqual(Build(3, 1), a.Task, "a takes the other task, not left idle");
            bool told = false;
            foreach (AssignmentChange c in w.Changes)
                told |= c.RobotId == a.Id && c.Reason.Contains("reserved for");
            Assert.IsTrue(told);
        }

        [Test]
        public void ReservedTask_IsNotTakenByOthers_WhileItsRobotIsAway()
        {
            var w = new Workforce();
            Robot a = w.AddRobot(RobotKind.General, 0);
            Robot b = w.AddRobot(RobotKind.General, 0);
            w.Reserve(b.Id, Build(2));
            b.Battery.Restore(0.0, 0.1 * b.Battery.CapacityWh);   // b needs charge: not available
            w.Assign(new List<WorkTask> { Build(2) });
            Assert.IsNull(a.Task, "never silently taken from a reservation");
            Assert.AreEqual(TaskState.Reserved, w.Tasks.Get(Build(2)).State);
            Assert.AreEqual(b.Id, w.Tasks.Get(Build(2)).ReservedFor);
            w.Assign(new List<WorkTask>());
            Assert.IsNull(b.ReservedTask, "the reservation ends with its task");
        }

        [Test]
        public void Reserve_RefusesARobotThatCantDoIt()
        {
            var w = new Workforce();
            Robot m = w.AddRobot(RobotKind.Mining, 0);
            Assert.IsFalse(w.Reserve(m.Id, Build(2)));
            Assert.IsTrue(w.Reserve(m.Id, Mine()));
            Assert.IsTrue(w.Unreserve(m.Id));
        }

        // ---- Temporary unavailability ----

        static readonly List<RobotFacility> Lander = new List<RobotFacility> { new RobotFacility(0, -1, ChargingCapability.Lander) };

        [Test]
        public void LeavingToCharge_AStandInTakesOver_TheOriginalComesBack()
        {
            var w = new Workforce();
            Robot a = w.AddRobot(RobotKind.General, 0);
            Robot b = w.AddRobot(RobotKind.General, 0);
            var tasks = new List<WorkTask> { Build(2) };
            w.Assign(tasks);
            Assert.AreEqual(Build(2), a.Task);
            a.Battery.Restore(0.0, 0.1 * a.Battery.CapacityWh);
            var up = new RobotUpkeep();
            up.Plan(w, Lander, 0.25);
            Assert.AreEqual(RobotActivity.Charging, a.Activity);
            Assert.AreEqual(Build(2), a.ResumeTask, "it remembers its assignment");
            w.Assign(tasks);
            Assert.AreEqual(Build(2), b.Task, "a stand-in");
            a.Battery.Restore(0.0, a.Battery.CapacityWh);
            up.Plan(w, Lander, 0.25);
            w.Assign(tasks);
            Assert.AreEqual(Build(2), a.Task, "the original robot is back on it");
            Assert.IsNull(b.Task);
            bool told = false;
            foreach (AssignmentChange c in w.Changes)
                told |= c.RobotId == b.Id && c.Reason.Contains("came back");
            Assert.IsTrue(told);
        }

        // ---- Task records ----

        [Test]
        public void Book_StatesAndReasons()
        {
            var w = new Workforce();
            Robot c = w.AddRobot(RobotKind.Construction, 0);
            w.Assign(new List<WorkTask> { Build(2), Mine() });
            Assert.AreEqual(TaskState.Working, w.Tasks.Get(Build(2)).State);
            Assert.AreEqual(c.Id, w.Tasks.Get(Build(2)).RobotId);
            TaskRecord mine = w.Tasks.Get(Mine());
            Assert.AreEqual(TaskState.Blocked, mine.State);
            StringAssert.Contains("needs a", mine.Reason);

            w.AddRobot(RobotKind.Construction, 0);
            w.Assign(new List<WorkTask> { Build(2), Build(3, 1), Build(4, 2) });
            Assert.AreEqual(TaskState.Available, w.Tasks.Get(Build(4, 2)).State);
            StringAssert.Contains("waiting", w.Tasks.Get(Build(4, 2)).Reason);

            w.SetGroupPriority(c.GroupId, TaskKind.Salvage, TaskPriority.Disabled);
            w.Assign(new List<WorkTask> { new WorkTask(TaskKind.Salvage, 1, 9) });
            StringAssert.Contains("disabled", w.Tasks.Get(new WorkTask(TaskKind.Salvage, 1, 9)).Reason);
        }

        [Test]
        public void Book_BlockedTooLong_AsksForAssistanceOnce()
        {
            var w = new Workforce();
            w.AddRobot(RobotKind.Construction, 0);
            int asked = 0;
            w.AssistanceNeeded += _ => asked++;
            for (double h = 0.0; h <= TaskBook.AssistanceAfterHours + 2.0; h += 1.0)
            {
                w.NowHours = h;
                w.Assign(new List<WorkTask> { Mine() });
            }
            Assert.AreEqual(TaskState.AssistanceNeeded, w.Tasks.Get(Mine()).State);
            Assert.AreEqual(1, asked);
            Assert.AreEqual(1, w.Tasks.Count(TaskState.AssistanceNeeded));
        }

        [Test]
        public void Book_RecordsCompletedAndCancelled()
        {
            var w = new Workforce();
            w.AddRobot(RobotKind.General, 0);
            w.Assign(new List<WorkTask> { Build(2), Build(3, 1) });
            w.Tasks.MarkCancelled(Build(3, 1));
            w.Assign(new List<WorkTask>());
            Assert.AreEqual(2, w.Tasks.Finished.Count);
            Assert.AreEqual(TaskState.Completed, w.Tasks.Finished[0].State);
            Assert.AreEqual(TaskState.Cancelled, w.Tasks.Finished[1].State);
        }

        // ---- In the colony ----

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void NearestRobot_TakesARemoteTask()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int n = -1;
            foreach (int x in Sphere.Neighbors[sim.BaseTileId]) if (!sim.GetTile(x).IsWater) { n = x; break; }
            Robot far = sim.Workforce.Robots[0];
            Robot near = sim.Workforce.AddRobot(RobotKind.General, n);
            sim.OrderSurvey(n);
            Assert.AreEqual(TaskKind.Survey, near.Task.Value.Kind, "the robot already there");
            Assert.IsNull(far.Task);
        }

        [Test]
        public void CancelledOrders_AreRecordedAsCancelled()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int t = sim.BaseTileId;
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            sim.OrderMining(t, Material.Rock);
            sim.RefreshWork();
            sim.CancelMining(t, Material.Rock);
            IReadOnlyList<TaskRecord> done = sim.Workforce.Tasks.Finished;
            Assert.AreEqual(TaskState.Cancelled, done[done.Count - 1].State);
        }

        [Test]
        public void Save_KeepsReservationsAndTasksToGoBackTo()
        {
            var a = new PlanetSimulation(Sphere, Surface, Earthlike);
            int t = a.BaseTileId;
            Robot r = a.Workforce.Robots[0];
            Robot b = a.Workforce.AddRobot(RobotKind.General, t);
            a.OrderMining(t, Material.Rock);   // the reserved task has to exist
            a.Workforce.Reserve(b.Id, new WorkTask(TaskKind.Mine, t, (int)Material.Rock));
            r.ResumeTask = new WorkTask(TaskKind.Build, t, 3);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var c = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(c.RestoreState(state));
            Assert.AreEqual(new WorkTask(TaskKind.Mine, t, (int)Material.Rock), c.Workforce.GetRobot(b.Id).ReservedTask);
        }
    }
}

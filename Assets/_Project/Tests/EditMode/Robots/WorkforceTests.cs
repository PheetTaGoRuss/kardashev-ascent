using System.Collections.Generic;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    public class WorkforceTests
    {
        static Workforce Start()
        {
            var w = new Workforce();
            w.Reset(5);
            return w;
        }

        static WorkTask Build(int slot, int order) => new WorkTask(TaskKind.Build, 1, slot, order);
        static WorkTask Salvage(int slot, int order) => new WorkTask(TaskKind.Salvage, 1, slot, order);

        [Test]
        public void StartsWithOneGeneralRobotInAnAutomaticGroup()
        {
            Workforce w = Start();
            Assert.AreEqual(1, w.Robots.Count);
            Robot r = w.Robots[0];
            Assert.AreEqual(RobotKind.General, r.Kind);
            Assert.AreEqual(5, r.Tile);
            Assert.AreEqual(RobotState.Idle, r.State);
            RobotGroup g = w.GetGroup(r.GroupId);
            Assert.IsTrue(g.Automatic);
            Assert.AreEqual(RobotKind.General, g.AutoKind);
            Assert.AreEqual(GroupState.Automatic, g.State);
            Assert.AreEqual(OperatingProgram.Priority, g.Program);
        }

        [Test]
        public void AutoOrganise_ByKind_NewGroupWhenFull()
        {
            Workforce w = Start();
            w.GroupCapacity = 2;
            Robot b = w.AddRobot(RobotKind.General, 5);
            Robot c = w.AddRobot(RobotKind.General, 5);
            Robot d = w.AddRobot(RobotKind.Construction, 5);
            Assert.AreEqual(w.Robots[0].GroupId, b.GroupId);
            Assert.AreNotEqual(b.GroupId, c.GroupId, "the first General group is full");
            Assert.IsTrue(w.GetGroup(c.GroupId).Automatic);
            Assert.AreNotEqual(c.GroupId, d.GroupId, "another kind gets its own group");
            Assert.AreEqual(RobotKind.Construction, w.GetGroup(d.GroupId).AutoKind);
        }

        [Test]
        public void ManualGroups_CapacityAndMoves()
        {
            Workforce w = Start();
            w.GroupCapacity = 1;
            Robot a = w.Robots[0];
            Robot b = w.AddRobot(RobotKind.General, 5);
            RobotGroup g = w.CreateGroup("Builders");
            Assert.IsFalse(g.Automatic);
            Assert.IsTrue(w.MoveRobot(a.Id, g.Id));
            Assert.IsFalse(w.MoveRobot(b.Id, g.Id), "group is full");
            Assert.AreEqual(1, g.Count);
            Assert.IsTrue(w.RenameGroup(g.Id, "Site crew"));
            Assert.AreEqual("Site crew", g.Name);
            Assert.IsFalse(w.DeleteGroup(w.GetGroup(b.GroupId).Id), "automatic groups can't be deleted");
            Assert.IsTrue(w.DeleteGroup(g.Id));
            Assert.IsNull(w.GetGroup(g.Id));
            Assert.IsTrue(w.GetGroup(a.GroupId).Automatic, "its robot went back to an automatic group");
        }

        [Test]
        public void MoveRobot_RefusesAnAutomaticGroupOfAnotherKind()
        {
            Workforce w = Start();
            Robot c = w.AddRobot(RobotKind.Construction, 5);
            Assert.IsFalse(w.MoveRobot(w.Robots[0].Id, c.GroupId));
        }

        [Test]
        public void Overrides_SurviveGroupChanges_AndClearBackToTheGroup()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            RobotGroup g = w.GetGroup(r.GroupId);
            Assert.IsTrue(w.SetRobotPriorityOverride(r.Id, TaskKind.Salvage, TaskPriority.High));
            w.SetGroupPriority(g.Id, TaskKind.Salvage, TaskPriority.Low);
            w.SetGroupPriority(g.Id, TaskKind.Build, TaskPriority.Critical);
            Assert.AreEqual(TaskPriority.High, w.EffectivePriority(r, TaskKind.Salvage), "override kept");
            Assert.AreEqual(TaskPriority.Critical, w.EffectivePriority(r, TaskKind.Build), "no override: group's");

            RobotGroup other = w.CreateGroup("Other");
            w.MoveRobot(r.Id, other.Id);
            Assert.AreEqual(TaskPriority.High, w.EffectivePriority(r, TaskKind.Salvage), "override kept across a move");

            w.SetRobotPriorityOverride(r.Id, TaskKind.Salvage, null);
            Assert.AreEqual(TaskPriority.Normal, w.EffectivePriority(r, TaskKind.Salvage), "back to the new group's");
            Assert.IsFalse(r.HasOverrides);
        }

        [Test]
        public void Programs_AllCanBeSet_OverridesStillWork()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            Assert.IsTrue(w.SetGroupProgram(r.GroupId, OperatingProgram.Demand));
            Assert.IsTrue(w.SetRobotProgramOverride(r.Id, OperatingProgram.TaskQueue));
            Assert.AreEqual(OperatingProgram.TaskQueue, w.EffectiveProgram(r));
            w.SetRobotProgramOverride(r.Id, null);
            Assert.AreEqual(OperatingProgram.Demand, w.EffectiveProgram(r));
        }

        [Test]
        public void Assign_TakesTheMostUrgentTask_TiesByKindThenOrder()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            w.Assign(new List<WorkTask> { Salvage(9, 0), Build(3, 1), Build(2, 0) });
            Assert.AreEqual(Build(2, 0), r.Task, "same priority: Build before Salvage, then queue order");
            Assert.AreEqual(RobotState.Working, r.State);

            Workforce v = Start();
            Robot q = v.Robots[0];
            v.SetGroupPriority(q.GroupId, TaskKind.Salvage, TaskPriority.High);
            v.Assign(new List<WorkTask> { Build(2, 0), Salvage(9, 0) });
            Assert.AreEqual(Salvage(9, 0), q.Task);
        }

        [Test]
        public void Assign_NeverTakesDisabledTasks()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            w.SetGroupPriority(r.GroupId, TaskKind.Build, TaskPriority.Disabled);
            w.Assign(new List<WorkTask> { Build(2, 0) });
            Assert.IsNull(r.Task);
        }

        [Test]
        public void Assign_OneRobotPerTask()
        {
            Workforce w = Start();
            Robot b = w.AddRobot(RobotKind.General, 5);
            w.Assign(new List<WorkTask> { Build(2, 0), Build(3, 1) });
            Assert.AreEqual(Build(2, 0), w.Robots[0].Task);
            Assert.AreEqual(Build(3, 1), b.Task);
        }

        [Test]
        public void Assign_RobotsKeepTheirTaskUnlessACriticalOneAppears()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            w.Assign(new List<WorkTask> { Build(2, 0) });
            w.SetGroupPriority(r.GroupId, TaskKind.Salvage, TaskPriority.High);
            w.Assign(new List<WorkTask> { Build(2, 0), Salvage(9, 0) });
            Assert.AreEqual(Build(2, 0), r.Task, "High doesn't interrupt");

            w.SetGroupPriority(r.GroupId, TaskKind.Salvage, TaskPriority.Critical);
            w.Assign(new List<WorkTask> { Build(2, 0), Salvage(9, 0) });
            Assert.AreEqual(Salvage(9, 0), r.Task, "Critical does");

            w.Assign(new List<WorkTask> { Build(2, 0) });
            Assert.AreEqual(Build(2, 0), r.Task, "after the critical task it re-evaluates");
        }

        [Test]
        public void Assign_FinishedTasksAreDropped()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            w.Assign(new List<WorkTask> { Build(2, 0) });
            w.Assign(new List<WorkTask>());
            Assert.IsNull(r.Task);
            Assert.AreEqual(RobotState.Idle, r.State);
        }

        [Test]
        public void Assign_OnlyKindsThatCanDoTheTask()
        {
            var w = new Workforce();
            Robot m = w.AddRobot(RobotKind.Mining, 0);
            Robot c = w.AddRobot(RobotKind.Construction, 0);
            w.Assign(new List<WorkTask> { Build(2, 0) });
            Assert.IsNull(m.Task);
            Assert.AreEqual(Build(2, 0), c.Task);
        }

        [Test]
        public void Pause_StopsWork_KeepsSettings_ResumeReevaluates()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            RobotGroup g = w.GetGroup(r.GroupId);
            w.SetGroupPriority(g.Id, TaskKind.Build, TaskPriority.High);
            w.Assign(new List<WorkTask> { Build(2, 0) });
            Assert.IsTrue(w.Pause(g.Id));
            Assert.AreEqual(GroupState.Paused, g.State);
            Assert.IsNull(r.Task);
            w.Assign(new List<WorkTask> { Build(2, 0) });
            Assert.IsNull(r.Task, "paused robots take nothing");
            Assert.AreEqual(TaskPriority.High, g.PriorityOf(TaskKind.Build), "pausing kept the settings");

            Assert.IsTrue(w.Resume(g.Id));
            Assert.AreEqual(GroupState.Automatic, g.State);
            w.Assign(new List<WorkTask> { Build(2, 0) });
            Assert.AreEqual(Build(2, 0), r.Task);
        }

        [Test]
        public void Interrupt_TemporaryTask_ThenBackToTheProgram()
        {
            Workforce w = Start();
            Robot r = w.Robots[0];
            RobotGroup g = w.GetGroup(r.GroupId);
            w.SetGroupPriority(g.Id, TaskKind.Salvage, TaskPriority.Low);
            w.Assign(new List<WorkTask> { Build(2, 0), Salvage(9, 0) });
            Assert.AreEqual(Build(2, 0), r.Task);

            Assert.IsTrue(w.Interrupt(g.Id, Salvage(9, 0)));
            w.Assign(new List<WorkTask> { Build(2, 0), Salvage(9, 0) });
            Assert.AreEqual(GroupState.Interrupted, g.State);
            Assert.AreEqual(Salvage(9, 0), r.Task, "manual interruption works on any priority");
            Assert.IsTrue(r.OnTemporaryTask);

            w.Assign(new List<WorkTask> { Build(2, 0) });   // the salvage is done
            Assert.AreEqual(GroupState.Automatic, g.State);
            Assert.IsFalse(r.OnTemporaryTask);
            Assert.AreEqual(Build(2, 0), r.Task, "resumed and re-evaluated");
            Assert.AreEqual(TaskPriority.Low, g.PriorityOf(TaskKind.Salvage), "interrupting kept the settings");
        }

        [Test]
        public void Interrupt_RefusedForPausedGroupsAndUnableRobots()
        {
            var w = new Workforce();
            Robot m = w.AddRobot(RobotKind.Mining, 0);
            Assert.IsFalse(w.Interrupt(m.GroupId, Build(2, 0)), "a mining robot can't build");
            Workforce v = Start();
            v.Pause(v.Robots[0].GroupId);
            Assert.IsFalse(v.Interrupt(v.Robots[0].GroupId, Build(2, 0)));
        }

        [Test]
        public void CaptureRestore_RoundTrip()
        {
            Workforce w = Start();
            w.GroupCapacity = 3;
            Robot r = w.Robots[0];
            Robot b = w.AddRobot(RobotKind.Construction, 7);
            RobotGroup g = w.CreateGroup("Crew");
            w.MoveRobot(b.Id, g.Id);
            w.SetGroupPriority(g.Id, TaskKind.Salvage, TaskPriority.Critical);
            w.SetRobotPriorityOverride(r.Id, TaskKind.Build, TaskPriority.Low);
            w.Pause(w.GetGroup(r.GroupId).Id);
            w.Interrupt(g.Id, Build(4, 0));
            w.Assign(new List<WorkTask> { Build(4, 0) });

            var copy = new Workforce();
            Assert.IsTrue(copy.Restore(w.Capture()));
            Assert.AreEqual(3, copy.GroupCapacity);
            Assert.AreEqual(2, copy.Robots.Count);
            Robot r2 = copy.GetRobot(r.Id), b2 = copy.GetRobot(b.Id);
            Assert.AreEqual(TaskPriority.Low, r2.PriorityOverride(TaskKind.Build));
            Assert.AreEqual(GroupState.Paused, copy.GetGroup(r2.GroupId).State);
            Assert.AreEqual(RobotKind.Construction, b2.Kind);
            Assert.AreEqual(7, b2.Tile);
            RobotGroup g2 = copy.GetGroup(b2.GroupId);
            Assert.AreEqual("Crew", g2.Name);
            Assert.IsFalse(g2.Automatic);
            Assert.AreEqual(GroupState.Interrupted, g2.State);
            Assert.AreEqual(Build(4, 0), g2.InterruptTask);
            Assert.AreEqual(TaskPriority.Critical, g2.PriorityOf(TaskKind.Salvage));
            Assert.AreEqual(Build(4, 0), b2.Task);
            Assert.IsTrue(b2.OnTemporaryTask);
            Assert.AreEqual(b.Id + 1, copy.AddRobot(RobotKind.General, 0).Id, "ids continue");
        }

        [Test]
        public void Restore_RefusesAnEmptyWorkforce()
        {
            Workforce w = Start();
            Assert.IsFalse(w.Restore(new WorkforceState()));
            Assert.AreEqual(1, w.Robots.Count, "unchanged");
        }

        [Test]
        public void SaveTypesAreSerializable()
        {
            Assert.IsTrue(typeof(WorkforceState).IsSerializable);
            Assert.IsTrue(typeof(RobotRecord).IsSerializable);
            Assert.IsTrue(typeof(GroupRecord).IsSerializable);
            Assert.IsTrue(typeof(PriorityRecord).IsSerializable);
            Assert.IsTrue(typeof(SalvageOrderRecord).IsSerializable);
        }
    }
}

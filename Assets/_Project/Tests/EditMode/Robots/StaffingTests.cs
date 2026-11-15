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
    /// <summary>Stage C (spec v2 §5): facility capacity, diminishing returns, balanced staffing, replacements, shortages.</summary>
    public class StaffingTests
    {
        static WorkTask MineAt(int tile, int order = 0) => new WorkTask(TaskKind.Mine, tile, (int)Material.Rock, order);

        static Workforce WithRobots(int n, RobotKind kind = RobotKind.General)
        {
            var w = new Workforce { CapacityOf = t => FacilityStaffing.CapacityFor(t.Kind) };
            w.GroupCapacity = 20;
            for (int i = 0; i < n; i++) w.AddRobot(kind, 0);
            return w;
        }

        static int Crew(Workforce w, WorkTask t) => w.WorkersOf(t).Count;

        [Test]
        public void Rate_OneWorkerUnchanged_MoreWorkersDiminish()
        {
            Assert.AreEqual(1.0, FacilityStaffing.Rate(new[] { 1.0 }), 1e-12);
            Assert.AreEqual(2.0, FacilityStaffing.Rate(new[] { 2.0 }), 1e-12);
            Assert.AreEqual(1.0 + 0.6, FacilityStaffing.Rate(new[] { 1.0, 1.0 }), 1e-12);
            Assert.AreEqual(2.0 + 0.6 * 1.0, FacilityStaffing.Rate(new[] { 1.0, 2.0 }), 1e-12, "best worker counts fully");
            Assert.AreEqual(1.0 + 0.6 + 0.36, FacilityStaffing.Rate(new[] { 1.0, 1.0, 1.0 }), 1e-12);
            Assert.Less(FacilityStaffing.Rate(new[] { 1.0, 1.0, 1.0 }), 3.0, "never linear without limit");
        }

        [Test]
        public void Capacity_IsNeverExceeded()
        {
            Workforce w = WithRobots(5);
            w.Assign(new List<WorkTask> { MineAt(1) });
            Assert.AreEqual(FacilityStaffing.MineSiteCapacity, Crew(w, MineAt(1)));
            Assert.AreEqual(1, FacilityStaffing.CapacityFor(TaskKind.Build));
            w.Assign(new List<WorkTask> { new WorkTask(TaskKind.Build, 1, 2) });
            Assert.AreEqual(1, Crew(w, new WorkTask(TaskKind.Build, 1, 2)), "one robot per site");
        }

        [Test]
        public void Staffing_IsBalanced_EveryFacilityGetsSomeoneFirst()
        {
            Workforce w = WithRobots(3);
            var tasks = new List<WorkTask> { MineAt(1, 0), MineAt(2, 1), MineAt(3, 2) };
            w.Assign(tasks);
            Assert.AreEqual(1, Crew(w, MineAt(1)));
            Assert.AreEqual(1, Crew(w, MineAt(2)));
            Assert.AreEqual(1, Crew(w, MineAt(3)), "partial staffing everywhere rather than one full site");
        }

        [Test]
        public void Staffing_IsBalanced_AcrossKindsOfFacility()
        {
            Workforce w = WithRobots(2);
            var workshop = new WorkTask(TaskKind.Manufacture, 1, 5);
            w.Assign(new List<WorkTask> { workshop, MineAt(2) });
            Assert.AreEqual(1, Crew(w, workshop));
            Assert.AreEqual(1, Crew(w, MineAt(2)), "the mine isn't left empty while the Workshop takes a second robot");
        }

        [Test]
        public void Reservation_IntoAFullFacility_ReplacesAnAutomaticWorker()
        {
            Workforce w = WithRobots(4);
            w.Assign(new List<WorkTask> { MineAt(1) });
            Robot outside = null;
            foreach (Robot r in w.Robots) if (!r.Task.HasValue) outside = r;
            Assert.IsNotNull(outside);
            Assert.IsTrue(w.Reserve(outside.Id, MineAt(1)));
            w.Assign(new List<WorkTask> { MineAt(1) });
            Assert.AreEqual(MineAt(1), outside.Task);
            Assert.AreEqual(FacilityStaffing.MineSiteCapacity, Crew(w, MineAt(1)), "still within capacity");
            bool told = false;
            foreach (AssignmentChange c in w.Changes) told |= c.Reason.Contains("reserved for");
            Assert.IsTrue(told);
        }

        static readonly List<RobotFacility> Lander = new List<RobotFacility> { new RobotFacility(0, -1, ChargingCapability.Lander) };

        [Test]
        public void OriginalWorker_ComesBack_IntoAFullFacility_AStandInLeaves()
        {
            Workforce w = WithRobots(4);
            var tasks = new List<WorkTask> { MineAt(1) };
            w.Assign(tasks);
            Robot a = w.WorkersOf(MineAt(1))[0];
            a.Battery.Restore(0.0, 0.1 * a.Battery.CapacityWh);
            var up = new RobotUpkeep();
            up.Plan(w, Lander, 0.25);
            w.Assign(tasks);
            Assert.AreEqual(FacilityStaffing.MineSiteCapacity, Crew(w, MineAt(1)), "a stand-in filled the place");
            a.Battery.Restore(0.0, a.Battery.CapacityWh);
            up.Plan(w, Lander, 0.25);
            w.Assign(tasks);
            Assert.AreEqual(MineAt(1), a.Task, "the original is back");
            Assert.AreEqual(FacilityStaffing.MineSiteCapacity, Crew(w, MineAt(1)), "and the crew is still within capacity");
        }

        [Test]
        public void UnstaffedFacility_IsShortOfWorkers_ThenNeedsHelp()
        {
            Workforce w = WithRobots(1);
            w.SetGroupPriority(w.Robots[0].GroupId, TaskKind.Build, TaskPriority.High);
            var tasks = new List<WorkTask> { new WorkTask(TaskKind.Build, 1, 2), MineAt(3) };
            int asked = 0;
            w.AssistanceNeeded += _ => asked++;
            w.Assign(tasks);
            TaskRecord mine = w.Tasks.Get(MineAt(3));
            StringAssert.Contains("short of workers", mine.Reason);
            Assert.AreEqual(0, mine.Workers);
            Assert.AreEqual(FacilityStaffing.MineSiteCapacity, mine.Capacity);
            for (double h = 1.0; h <= TaskBook.AssistanceAfterHours + 1.0; h += 1.0)
            {
                w.NowHours = h;
                w.Assign(tasks);
            }
            Assert.AreEqual(TaskState.AssistanceNeeded, w.Tasks.Get(MineAt(3)).State);
            Assert.AreEqual(1, asked);
        }

        // ---- In the colony ----

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void TwoRobotsMining_YieldMoreButLessThanDouble()
        {
            double Yield(int robots)
            {
                var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
                sim.Workforce.Reset(sim.BaseTileId, 1);
                sim.Materials.Clear();
                for (int i = 1; i < robots; i++) sim.Workforce.AddRobot(RobotKind.General, sim.BaseTileId);
                sim.OrderMining(sim.BaseTileId, Material.Rock);
                sim.Tick(5.0);
                return sim.Materials[Material.Rock];
            }
            double one = Yield(1), two = Yield(2);
            Assert.AreEqual(5.0 * RobotSpecs.MiningKgPerHour, one, 1e-6);
            Assert.Greater(two, one);
            Assert.Less(two, 2.0 * one);
        }

        [Test]
        public void WorkshopCrew_FinishesAJobSooner()
        {
            double HoursToFinish(int robots)
            {
                var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
                sim.Workforce.Reset(sim.BaseTileId, 1);
                sim.Materials.Clear();
                for (int i = 1; i < robots; i++) sim.Workforce.AddRobot(RobotKind.General, sim.BaseTileId);
                sim.PlaceWorkshop(sim.BaseTileId, 5);
                foreach (Material m in Materials.All) sim.Materials.Add(m, 1000.0);
                Assert.IsNull(sim.OrderRobot(RobotKind.General));
                double h = 0.0;
                while (sim.Jobs.Count > 0 && h < 100.0) { sim.Tick(0.25); h += 0.25; }
                return h;
            }
            Assert.Less(HoursToFinish(3), HoursToFinish(1));
        }
    }
}

using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>K milestones: every 0.1 of planet K, judged on a day's average used power.</summary>
    public class MilestoneTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create(double luminositySun, string id = "test")
        {
            var r = new PlanetRecord { id = id };
            r.solar.luminosityW = luminositySun * SolarConstants.SolarLuminosity;
            r.solar.obliquityDeg = 23.44;
            r.solar.airTemperatureC = 25.0;
            return new PlanetSimulation(Sphere, Surface, r);
        }

        [TestCase(0.0, 0)]
        [TestCase(0.05, 0)]
        [TestCase(0.1, 1)]
        [TestCase(0.25, 2)]
        [TestCase(0.999, 9)]
        [TestCase(1.0, 10)]
        [TestCase(-0.3, 0)]
        public void MilestoneFor_CountsTenths(double planetK, int expected)
        {
            Assert.AreEqual(expected, Kardashev.MilestoneFor(planetK));
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        public void PowerForMilestone_IsWherePlanetKReachesIt(int milestone)
        {
            double target = 6.7e15;
            double p = Kardashev.PowerForMilestone(milestone, target);
            Assert.AreEqual(milestone * 0.1, Kardashev.PlanetK(p, target), 1e-12);
            Assert.AreEqual(milestone, Kardashev.MilestoneFor(Kardashev.PlanetK(p, target)));
        }

        [Test]
        public void EarthTarget_MilestonesAreTenfoldsFromTenMegawatts()
        {
            Assert.AreEqual(1e7, Kardashev.PowerForMilestone(1, 1e16), 1.0);
            Assert.AreEqual(1e9, Kardashev.PowerForMilestone(3, 1e16), 1.0);
        }

        [Test]
        public void NothingHappensBeforeAFullDay()
        {
            var sim = Create(1e-4);
            sim.Tick(12.0);
            Assert.AreEqual(0.0, sim.DayAveragePowerW);
            Assert.AreEqual(0, sim.MilestonesReached);
        }

        [Test]
        public void DimStar_MilestonesFireOnceInOrderAfterADay()
        {
            // A star 10⁴× dimmer than the Sun: the target is 10¹² W, so the lander's ~100 kW is planet K 0.3.
            var sim = Create(1e-4);
            var fired = new List<int>();
            sim.MilestoneReached += n => fired.Add(n);
            sim.Tick(24.0);
            Assert.Greater(sim.DayAveragePowerW, 0.0);
            int expected = Kardashev.MilestoneFor(Kardashev.PlanetK(sim.DayAveragePowerW, sim.TypeITargetW));
            Assert.Greater(expected, 0);
            CollectionAssert.AreEqual(Range(1, expected), fired);
            Assert.AreEqual(expected, sim.MilestonesReached);

            sim.Tick(48.0);
            CollectionAssert.AreEqual(Range(1, expected), fired, "no repeats while power holds");
        }

        [Test]
        public void AbsurdlyDimStar_ReachesTheTarget()
        {
            // 10⁻¹² of the Sun: target 10⁴ W, which the lander's base load already beats.
            var sim = Create(1e-12);
            bool reached = false;
            sim.TargetReached += () => reached = true;
            sim.Tick(24.0);
            Assert.IsTrue(reached);
            Assert.AreEqual(10, sim.MilestonesReached);
        }

        [Test]
        public void Save_KeepsMilestonesSoTheyDontFireAgain()
        {
            var a = Create(1e-4, "dim");
            a.Tick(30.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(a.MilestonesReached, state.MilestonesReached);

            var b = Create(1e-4, "dim");
            var fired = new List<int>();
            b.MilestoneReached += n => fired.Add(n);
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(a.MilestonesReached, b.MilestonesReached);
            Assert.AreEqual(a.DayAveragePowerW, b.DayAveragePowerW, 1e-6);
            b.Tick(48.0);
            CollectionAssert.IsEmpty(fired);
        }

        static List<int> Range(int from, int to)
        {
            var list = new List<int>();
            for (int i = from; i <= to; i++) list.Add(i);
            return list;
        }
    }
}

using System;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>One lumped body: C dT/dt = -G (T - T_air) + P_internal + P_heater (decided 2026-11-26).</summary>
    public class RobotThermalLumpTests
    {
        [Test]
        public void ABodyLeavingShelterIntoDeepColdTakesAboutAnEstimatedThirtyHoursToReachItsLimit_BeforeTheHeaterRuns()
        {
            double g = RobotThermal.Conductance(1.0, false), tau = RobotThermal.BodyHeatCapacityJPerK / g / 3600.0;
            double pInt = RobotThermal.InternalHeatW(0.0, true);
            double tInf = -60.0 + pInt / g;
            double expectedHours = tau * Math.Log((15.0 - tInf) / (RobotThermal.OperatingMinC - tInf));
            TestContext.WriteLine($"tau {tau:0.0} h, idle: reaches {RobotThermal.OperatingMinC} C after {expectedHours:0.0} h");
            var early = RobotThermal.TripHeater(15.0, -60.0, expectedHours - 1.0, 1.0, false, 0.0);
            Assert.AreEqual(0.0, early.Wh, 1e-6, "no heater before the body has cooled to its limit");
            var late = RobotThermal.TripHeater(15.0, -60.0, expectedHours + 10.0, 1.0, false, 0.0);
            Assert.Greater(late.Wh, 0.0, "the heater runs once the limit is reached");
            double hold = g * (RobotThermal.OperatingMinC + 60.0) - pInt;
            Assert.AreEqual(hold * 10.0, late.Wh, hold * 10.0 * 0.15, "and then it draws exactly what holds the body there: G (hold - air) - internal heat");
            Assert.AreEqual(RobotThermal.OperatingMinC, late.EndC, 1e-6);
        }

        [Test]
        public void TheRobotsOwnHeat_ReducesTheHeaterNeed_AndWorkingReducesItMore()
        {
            double idle = RobotThermal.SteadyHeaterW(-60.0, 1.0, false, 0.0), driving = RobotThermal.SteadyHeaterW(-60.0, 1.0, false, 200.0);
            Assert.Less(driving, idle);
            Assert.Greater(idle, 0.0);
            Assert.Less(RobotThermal.SteadyHeaterW(-60.0, 1.0, true, 0.0), idle, "insulation halves the loss");
            Assert.AreEqual(0.0, RobotThermal.SteadyHeaterW(0.0, 1.0, false, 0.0), 1e-9, "mild air needs none");
        }

        [Test]
        public void AHibernatingBody_FollowsTheAirWithTau_AndDrawsNothing()
        {
            double g = RobotThermal.Conductance(1.0, false), tau = RobotThermal.BodyHeatCapacityJPerK / g / 3600.0;
            var r = RobotThermal.Step(15.0, -60.0, 10.0, 1.0, false, 0.0, false, false);
            Assert.AreEqual(0.0, r.HeaterW);
            Assert.AreEqual(-60.0 + 75.0 * Math.Exp(-10.0 / tau), r.BodyC, 1e-6);
        }

        [Test]
        public void ShelterWarmsABodyWithTheBay_AtNoCostToTheBattery()
        {
            var r = RobotThermal.Step(-30.0, -60.0, 1.0, 1.0, false, 0.0, true, true);
            Assert.Greater(r.BodyC, -30.0, "the heated bay warms it");
            Assert.AreEqual(0.0, r.HeaterW, "the bay's heat is free for the robot");
            double t = -30.0; for (int i = 0; i < 24; i++) t = RobotThermal.Step(t, -60.0, 1.0, 1.0, false, 0.0, true, true).BodyC;
            Assert.That(t, Is.InRange(10.0, 31.0), "within a day it is warm (the shelter temperature plus the robot's own heat over the loss)");
            Assert.IsTrue(RobotThermal.CanCharge(t), "and its battery can be charged");
        }

        [Test]
        public void OnAHotWorld_TheCoolerHoldsTheUpperLimit_Symmetrically()
        {
            var late = RobotThermal.TripHeater(15.0, 150.0, 40.0, 1.0, false, 0.0);
            Assert.Greater(late.Wh, 0.0, "a cooler runs");
            Assert.AreEqual(RobotThermal.OperatingMaxC, late.EndC, 1e-6);
        }
    }
}

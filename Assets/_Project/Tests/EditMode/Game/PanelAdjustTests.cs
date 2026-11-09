using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Re-angling a built panel takes a robot (decided 2026-11-09).</summary>
    public class PanelAdjustTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static StubPlanetSimulation Create() => new StubPlanetSimulation(Sphere, Surface, Earthlike);

        static int GroupId(StubPlanetSimulation sim) => sim.Workforce.Robots[0].GroupId;

        static double TiltOf(StubPlanetSimulation sim, int t, int s)
        {
            foreach (BuildingRecord r in sim.CaptureState().Buildings)
                if (r.Tile == t && r.Slot == s) return r.Tilt;
            return double.NaN;
        }

        [Test]
        public void BuiltPanel_KeepsItsAngle_UntilARobotTurnsIt()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlacePanel(t, 0, 0.3, Math.PI));
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            sim.Workforce.Pause(GroupId(sim));
            Assert.IsTrue(sim.OrderPanelAngles(t, 0, 0.6, Math.PI));
            Assert.AreEqual(0.3, TiltOf(sim, t, 0), 1e-9, "no robot yet");
            Assert.IsTrue(sim.TryGetAdjustOrder(t, 0, out double tilt, out _));
            Assert.AreEqual(0.6, tilt, 1e-9);

            var events = new List<(int, int)>();
            sim.PanelAdjusted += (a, b) => events.Add((a, b));
            sim.Workforce.Resume(GroupId(sim));
            sim.Tick(0.25);
            Assert.AreEqual(0.6, TiltOf(sim, t, 0), 1e-9);
            Assert.IsFalse(sim.TryGetAdjustOrder(t, 0, out _, out _));
            CollectionAssert.AreEqual(new[] { (t, 0) }, events);
        }

        [Test]
        public void Site_TakesTheNewAngle_AtOnce()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Workforce.Pause(GroupId(sim));
            Assert.IsTrue(sim.PlacePanel(t, 0, 0.3, Math.PI));
            Assert.IsTrue(sim.IsUnderConstruction(t, 0));
            Assert.IsTrue(sim.OrderPanelAngles(t, 0, 0.5, Math.PI));
            Assert.AreEqual(0.5, TiltOf(sim, t, 0), 1e-9);
            Assert.IsFalse(sim.TryGetAdjustOrder(t, 0, out _, out _));
        }

        [Test]
        public void Cancel_AndAskAgain_ReplacesTheOrder()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.PlacePanel(t, 0, 0.3, Math.PI);
            sim.Workforce.Pause(GroupId(sim));
            sim.OrderPanelAngles(t, 0, 0.6, Math.PI);
            sim.OrderPanelAngles(t, 0, 0.7, Math.PI);
            Assert.IsTrue(sim.TryGetAdjustOrder(t, 0, out double tilt, out _));
            Assert.AreEqual(0.7, tilt, 1e-9);
            Assert.IsTrue(sim.CancelAdjust(t, 0));
            Assert.IsFalse(sim.TryGetAdjustOrder(t, 0, out _, out _));
            Assert.IsFalse(sim.OrderPanelAngles(t, 5, 0.6, Math.PI), "no panel there");
        }

        [Test]
        public void Save_KeepsAdjustOrders()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.PlacePanel(t, 0, 0.3, Math.PI);
            a.Workforce.Pause(GroupId(a));
            a.OrderPanelAngles(t, 0, 0.6, 3.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(20, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsTrue(b.TryGetAdjustOrder(t, 0, out double tilt, out double az));
            Assert.AreEqual(0.6, tilt, 1e-9);
            Assert.AreEqual(3.0, az, 1e-9);
        }

        [Test]
        public void GeneralRobots_CanAdjust()
        {
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.General, TaskKind.Adjust, 0));
            Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Mining, TaskKind.Adjust, 0));
        }
    }
}

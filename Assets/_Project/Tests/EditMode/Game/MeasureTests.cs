using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Solar parameters: estimates with a range until a robot measures them (docs/design/solar-measurements.md, stage B).</summary>
    public class MeasureTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static StubPlanetSimulation Create() => new StubPlanetSimulation(Sphere, Surface, Earthlike);

        static ParameterEffect Effect(List<ParameterEffect> list, SolarParameter p) => list.Find(e => e.Parameter == p);

        [Test]
        public void UnknownParameters_SpreadTheYearlyEnergy()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            List<ParameterEffect> effects = sim.ParameterEffects(t, 0.5, Math.PI);
            Assert.AreEqual(Enum.GetValues(typeof(SolarParameter)).Length, effects.Count);
            foreach (SolarParameter p in new[] { SolarParameter.OpticalDepth, SolarParameter.AirTemperature, SolarParameter.Dust, SolarParameter.Spectrum })
            {
                ParameterEffect e = Effect(effects, p);
                Assert.IsFalse(e.Known, p.ToString());
                Assert.Greater(e.HalfRangeWh, 0.0, p.ToString());
            }
            Assert.IsTrue(Effect(effects, SolarParameter.Clouds).Known, "no cloud model on this planet: nothing to measure");
            Assert.Greater(Effect(effects, SolarParameter.OpticalDepth).HighWh, Effect(effects, SolarParameter.OpticalDepth).LowWh);
        }

        [Test]
        public void ARobotMeasures_ThenTheSpreadIsGone()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            var heard = new List<SolarParameter>();
            sim.ParameterMeasured += heard.Add;
            Assert.IsTrue(sim.OrderMeasure(SolarParameter.AirTemperature, t));
            Assert.IsFalse(sim.OrderMeasure(SolarParameter.AirTemperature, t), "already ordered");
            Assert.IsTrue(sim.IsMeasureOrdered(SolarParameter.AirTemperature));
            for (int i = 0; i < 40 && !sim.Parameters.IsMeasured(SolarParameter.AirTemperature); i++)
                sim.Tick(1.0);
            Assert.IsTrue(sim.Parameters.IsMeasured(SolarParameter.AirTemperature), "the General robot measured it");
            Assert.AreEqual(1.0, sim.MeasureProgress(SolarParameter.AirTemperature));
            CollectionAssert.AreEqual(new[] { SolarParameter.AirTemperature }, heard);
            ParameterEffect e = Effect(sim.ParameterEffects(t, 0.5, Math.PI), SolarParameter.AirTemperature);
            Assert.IsTrue(e.Known);
            Assert.AreEqual(0.0, e.HalfRangeWh, 1e-9);
            Assert.IsFalse(sim.OrderMeasure(SolarParameter.AirTemperature, t), "known now");
        }

        [Test]
        public void CancelWorks()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            Assert.IsTrue(sim.OrderMeasure(SolarParameter.Dust, t));
            Assert.IsTrue(sim.CancelMeasure(SolarParameter.Dust));
            Assert.IsFalse(sim.IsMeasureOrdered(SolarParameter.Dust));
            Assert.IsFalse(sim.CancelMeasure(SolarParameter.Dust));
        }

        [Test]
        public void Research_And_General_CanMeasure()
        {
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.Research, TaskKind.Measure, 0));
            Assert.IsTrue(RobotCatalog.CanDo(RobotKind.General, TaskKind.Measure, 0));
            Assert.Greater(RobotCatalog.Efficiency(RobotKind.Research, TaskKind.Measure, 0),
                           RobotCatalog.Efficiency(RobotKind.General, TaskKind.Measure, 0));
            Assert.IsFalse(RobotCatalog.CanDo(RobotKind.Mining, TaskKind.Measure, 0));
        }

        [Test]
        public void Save_KeepsMeasurementsAndOrders()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.Parameters.MarkMeasured(SolarParameter.Spectrum);
            a.Workforce.Pause(a.Workforce.Robots[0].GroupId);
            a.OrderMeasure(SolarParameter.Albedo, t);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(19, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsTrue(b.Parameters.IsMeasured(SolarParameter.Spectrum));
            Assert.IsTrue(b.IsMeasureOrdered(SolarParameter.Albedo));
        }
    }
}

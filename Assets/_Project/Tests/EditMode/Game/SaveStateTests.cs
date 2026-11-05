using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class SaveStateTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static StubPlanetSimulation Create() => new StubPlanetSimulation(Sphere, Surface, Earthlike);

        static void Near(double expected, double actual, double rel = 1e-6) =>
            Assert.AreEqual(expected, actual, Math.Max(1e-9, Math.Abs(expected) * rel));

        static StubPlanetSimulation Built()
        {
            var a = Create();
            int t = a.BaseTileId;
            double tilt = Math.Abs(a.GetTile(t).Latitude);
            for (int s = 0; s < 6; s++) a.PlacePanel(t, s, tilt, Math.PI);
            a.PlaceBattery(t, 10);
            a.PlaceBattery(t, 11);
            a.Tick(3 * 24.0);
            return a;
        }

        [Test]
        public void RoundTrip_RestoresTimeBuildingsBatteryAndColony()
        {
            var a = Built();
            var state = a.CaptureState();
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            int t = a.BaseTileId;
            Near(a.TimeHours, b.TimeHours);
            Near(a.BatteryStoredWh, b.BatteryStoredWh);
            Near(a.BatteryCapacityWh, b.BatteryCapacityWh);
            Near(a.DemandW, b.DemandW, 1e-2); // A reports the pre-step demand of its last Tick
            Near(a.GenerationW, b.GenerationW, 1e-2); // A reports the last step's value, B recomputes at the restored time
            Near(a.StarDirection.X, b.StarDirection.X, 1e-5);
            Near(a.StarDirection.Y, b.StarDirection.Y, 1e-5);
            Near(a.StarDirection.Z, b.StarDirection.Z, 1e-5);
            Assert.AreEqual(a.PanelCount(t), b.PanelCount(t));
            Assert.AreEqual(a.BatteryCount(t), b.BatteryCount(t));
            foreach (int s in new[] { 0, 5, 10, 11 })
                Assert.AreEqual(a.GetBuilding(t, s), b.GetBuilding(t, s));
        }

        [Test]
        public void AfterRestore_BothRunTheSame()
        {
            var a = Built();
            var b = Create();
            Assert.IsTrue(b.RestoreState(a.CaptureState()));
            a.Tick(24.0);
            b.Tick(24.0);
            Near(a.DemandW, b.DemandW);
            Near(a.BatteryStoredWh, b.BatteryStoredWh);
            Near(a.KardashevK, b.KardashevK);
        }

        [Test]
        public void RestoreReplacesExistingBuildings()
        {
            var a = Built();
            var b = Create();
            int t = b.BaseTileId;
            Assert.IsTrue(b.PlacePanel(t, 30, Math.Abs(b.GetTile(t).Latitude), Math.PI));
            Assert.IsTrue(b.RestoreState(a.CaptureState()));
            Assert.AreEqual(BuildingType.None, b.GetBuilding(t, 30));
        }

        [Test]
        public void DifferentPlanet_IsRefused()
        {
            var a = Built();
            var state = a.CaptureState();
            state.TileCount++;
            var b = Create();
            double before = b.TimeHours;
            Assert.IsFalse(b.RestoreState(state));
            Assert.AreEqual(before, b.TimeHours);
        }

        [Test]
        public void JsonFields()
        {
            Assert.IsTrue(typeof(SimulationState).IsSerializable);
            Assert.IsTrue(typeof(BuildingRecord).IsSerializable);
        }
    }
}

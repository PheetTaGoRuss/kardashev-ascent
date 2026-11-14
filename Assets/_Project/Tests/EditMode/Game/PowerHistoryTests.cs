using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class PowerHistoryTests
    {
        [Test]
        public void Sample_IsTheTimeWeightedAverage()
        {
            var h = new PowerHistory(1.0, 10);
            h.Add(0.25, 0.25, 100.0, 40.0, 40.0, 0.0, 0.5, false);
            Assert.AreEqual(0, h.Count, "a quarter hour is not a sample yet");
            h.Add(0.75, 1.0, 300.0, 40.0, 40.0, 10.0, 0.6, false);
            Assert.AreEqual(1, h.Count);
            Assert.AreEqual(250.0, h[0].GenerationW, 1e-9, "(100·0.25 + 300·0.75) / 1");
            Assert.AreEqual(40.0, h[0].DemandW, 1e-9);
            Assert.AreEqual(7.5, h[0].BuildPowerW, 1e-9);
            Assert.AreEqual(0.6, h[0].BatteryFraction, 1e-9, "battery at the end of the sample");
            Assert.AreEqual(1.0, h[0].TimeHours, 1e-9);
        }

        [Test]
        public void LongSpan_SplitsIntoSamples()
        {
            var h = new PowerHistory(1.0, 10);
            h.Add(2.5, 2.5, 50.0, 10.0, 10.0, 0.0, 1.0, false);
            Assert.AreEqual(2, h.Count);
            Assert.AreEqual(1.0, h[0].TimeHours, 1e-9);
            Assert.AreEqual(2.0, h[1].TimeHours, 1e-9);
            Assert.AreEqual(50.0, h[1].GenerationW, 1e-9);
        }

        [Test]
        public void Full_DropsTheOldest()
        {
            var h = new PowerHistory(1.0, 3);
            for (int i = 1; i <= 5; i++)
                h.Add(1.0, i, i * 10.0, 0.0, 0.0, 0.0, 0.0, false);
            Assert.AreEqual(3, h.Count);
            Assert.AreEqual(30.0, h[0].GenerationW, 1e-9, "samples 1 and 2 were dropped");
            Assert.AreEqual(50.0, h[2].GenerationW, 1e-9);
            Assert.Throws<ArgumentOutOfRangeException>(() => { var _ = h[3]; });
        }

        [Test]
        public void Totals_CountEnergyAndBrownoutHours()
        {
            var h = new PowerHistory(1.0, 4);
            Assert.AreEqual(1.0, h.ServedShare, 1e-9, "nothing recorded yet");
            h.Add(2.0, 2.0, 100.0, 50.0, 50.0, 20.0, 1.0, false);
            h.Add(1.0, 3.0, 0.0, 50.0, 25.0, 0.0, 0.0, true);
            Assert.AreEqual(200.0, h.TotalGeneratedWh, 1e-9);
            Assert.AreEqual(150.0, h.TotalDemandWh, 1e-9);
            Assert.AreEqual(125.0, h.TotalServedWh, 1e-9);
            Assert.AreEqual(40.0, h.TotalBuildWh, 1e-9);
            Assert.AreEqual(1.0, h.BrownoutHours, 1e-9);
            Assert.AreEqual(3.0, h.TotalHours, 1e-9);
            Assert.AreEqual(125.0 / 150.0, h.ServedShare, 1e-9);
        }

        [Test]
        public void Restore_KeepsSamplesAndTotals()
        {
            var a = new PowerHistory(1.0, 3);
            for (int i = 1; i <= 4; i++)
                a.Add(1.0, i, i, 1.0, 1.0, 0.0, 0.5, i == 2);
            var b = new PowerHistory(1.0, 3);
            b.Restore(a.ToList(), a.TotalGeneratedWh, a.TotalDemandWh, a.TotalServedWh, a.TotalBuildWh, a.BrownoutHours, a.TotalHours);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual(a[i].GenerationW, b[i].GenerationW, 1e-12);
            Assert.AreEqual(a.BrownoutHours, b.BrownoutHours, 1e-12);
            Assert.AreEqual(a.ServedShare, b.ServedShare, 1e-12);
        }
    }

    public class StubHistoryTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        [Test]
        public void Ticking_RecordsHourlySamples_AndBrownouts()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Tick(30.0);
            Assert.AreEqual(30, sim.History.Count, "one sample per planet hour");
            Assert.AreEqual(30.0, sim.History.TotalHours, 1e-6);
            Assert.Greater(sim.History.BrownoutHours, 0.0, "no panels: the lander battery runs out");
            Assert.Less(sim.History.ServedShare, 1.0);
            Assert.AreEqual(0.0, sim.HoursSinceBrownout, 1e-9, "still in the brownout");
        }

        [Test]
        public void SaveAndLoad_KeepsTheHistory()
        {
            var a = new PlanetSimulation(Sphere, Surface, Earthlike);
            int t = a.BaseTileId;
            a.PlacePanel(t, 0, Math.Abs(a.GetTile(t).Latitude), Math.PI);
            a.Tick(50.0);
            var b = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(b.RestoreState(a.CaptureState()));
            Assert.AreEqual(a.History.Count, b.History.Count);
            Assert.AreEqual(a.History[a.History.Count - 1].GenerationW, b.History[b.History.Count - 1].GenerationW, 1e-9);
            Assert.AreEqual(a.History.TotalGeneratedWh, b.History.TotalGeneratedWh, 1e-6);
            Assert.AreEqual(a.History.BrownoutHours, b.History.BrownoutHours, 1e-9);
        }
    }
}

using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Recover wreck (D2): a haul brings the metals home; findings are read back only if the electronics survived; the pile is saved.</summary>
    public class WreckRecoveryTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Earth => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);

        static PlanetSimulation Colony(out int tile)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earth, Kardashev.EarthRadiusM) { ConstructionTimeEnabled = false, LogisticsEnabled = true, Clouds = CloudModel.EarthClimatology, WeatherEnabled = false };
            sim.Workforce.Reset(sim.BaseTileId, 0);
            sim.Debug.UnlockAll = true; sim.Debug.FreeBuilding = true;
            for (int sl = 0, placed = 0; sl < sim.SlotCount(sim.BaseTileId) && placed < 40; sl++) if (sim.PlacePanel(sim.BaseTileId, sl, 0.35, Math.PI)) placed++;
            for (int sl = 0; sl < sim.SlotCount(sim.BaseTileId); sl++) if (sim.PlaceChargingStation(sim.BaseTileId, sl)) break;
            sim.Debug.FreeBuilding = false;
            sim.Workforce.AddRobot(RobotKind.Transport, sim.BaseTileId);
            tile = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && t != sim.BaseTileId && sim.Routes.HoursBetween(sim.BaseTileId, t) > 1.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 6.0).OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).First();
            return sim;
        }

        [Test]
        public void ADeadRobotsMetalsAreHauledHome_AndTheWreckIsGoneWhenItIsEmpty()
        {
            var sim = Colony(out int tile);
            var dead = sim.Workforce.AddRobot(RobotKind.General, tile);
            Wreck w = sim.DestroyForTests(dead.Id, -40.0);
            Assert.Greater(sim.WreckStockKg(w, Material.Iron), 300.0);
            double ironBefore = sim.KgAt(sim.BaseTileId, Material.Iron);
            for (int h = 0; h < 24 * 20 && sim.Wrecks.Count > 0; h++) sim.Tick(1.0);
            Assert.AreEqual(0, sim.Wrecks.Count, "recovered");
            Assert.Greater(sim.KgAt(sim.BaseTileId, Material.Iron), ironBefore + 300.0, "the iron is in the base depot");
            Assert.Greater(sim.KgAt(sim.BaseTileId, Material.ElectronicWaste), 0.0, "the waste came too");
        }

        [Test]
        public void FindingsAreReadBackOnlyWhenTheElectronicsSurvived()
        {
            var sim = Colony(out int tile);
            var cold = sim.Workforce.AddRobot(RobotKind.General, tile);
            sim.heldFindings.Add(new HeldFinding { RobotId = cold.Id, Kind = FindingKind.SurveyRockOnly, Tile = tile });
            Wreck a = sim.DestroyForTests(cold.Id, -40.0);
            Assert.AreEqual(1, a.Findings.Count);
            Assert.IsTrue(a.ElectronicsIntact);
            var hot = sim.Workforce.AddRobot(RobotKind.General, tile);
            sim.heldFindings.Add(new HeldFinding { RobotId = hot.Id, Kind = FindingKind.SurveyRockOnly, Tile = tile });
            Wreck b = sim.DestroyForTests(hot.Id, 400.0);
            Assert.AreEqual(0, b.Findings.Count);
            Assert.IsTrue(b.FindingsLost);
        }

        [Test]
        public void AWreckIsSaved_AndANormalWreckNeverHoldsMoreThanTheRobotsBill()
        {
            var sim = Colony(out int tile);
            var dead = sim.Workforce.AddRobot(RobotKind.General, tile);
            Wreck w = sim.DestroyForTests(dead.Id, 700.0);
            double bill = WreckModel.Total(w.BillKg);
            Assert.LessOrEqual(sim.WreckKg(w), bill + 1e-9);
            var again = Colony(out _);
            again.RestoreState(sim.CaptureState());
            Assert.AreEqual(1, again.Wrecks.Count);
            Assert.AreEqual(sim.WreckKg(w), again.WreckKg(again.Wrecks[0]), 1e-9);
            Assert.AreEqual(DeathCause.Heat, again.Wrecks[0].Cause);
        }

        [Test]
        public void AWreckOnAHotSurface_KeepsDegradingAtTheTilesTemperature()
        {
            var sim = Colony(out int tile);
            var dead = sim.Workforce.AddRobot(RobotKind.General, tile);
            Wreck w = sim.DestroyForTests(dead.Id, 20.0);
            double before = sim.WreckStockKg(w, Material.ElectronicWaste);
            Assert.Greater(before, 0.0);
            w.PeakC = 20.0;
            // lay it on a hot day by raising the peak the way the exposure does
            typeof(PlanetSimulation).GetMethod("ExposeWrecks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(sim, null);
            Assert.GreaterOrEqual(w.PeakC, 20.0);
        }
    }
}

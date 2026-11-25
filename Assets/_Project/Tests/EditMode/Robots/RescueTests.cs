using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Robot rescue (R1) and the failure ladder (R2): a stranded robot gets a jump charge or a tow before it is lost.</summary>
    public class RescueTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Params(double meanC) => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, meanC, false, 1.0);

        static PlanetSimulation Colony(double meanC, out int tile, int helpers = 1, bool clouds = false)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Params(meanC), Kardashev.EarthRadiusM) { ConstructionTimeEnabled = false, LogisticsEnabled = true, WeatherEnabled = false };
            sim.Workforce.Reset(sim.BaseTileId, 0);
            sim.Debug.UnlockAll = true; sim.Debug.FreeBuilding = true;
            for (int sl = 0, placed = 0; sl < sim.SlotCount(sim.BaseTileId) && placed < 40; sl++) if (sim.PlacePanel(sim.BaseTileId, sl, 0.35, Math.PI)) placed++;
            for (int sl = 0; sl < sim.SlotCount(sim.BaseTileId); sl++) if (sim.PlaceChargingStation(sim.BaseTileId, sl)) break;
            sim.Debug.FreeBuilding = false;
            for (int i = 0; i < helpers; i++) sim.Workforce.AddRobot(RobotKind.General, sim.BaseTileId);
            tile = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && sim.Routes.HoursBetween(sim.BaseTileId, t) > 2.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 6.0)
                .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).First();
            return sim;
        }

        static Robot Strand(PlanetSimulation sim, int tile)
        {
            var r = sim.Workforce.AddRobot(RobotKind.General, tile);
            r.Battery.Draw(1e12, 1.0);
            return r;
        }

        [TestCase(RescueMethod.Jump)] [TestCase(RescueMethod.Tow)]
        public void BothMethodsWork_FromStartToFinish(RescueMethod method)
        {
            var sim = Colony(15.0, out int tile);
            sim.RescueMethodOverride = method;
            var s = Strand(sim, tile);
            for (int h = 0; h < 60 && sim.RescuesSucceeded == 0; h++) sim.Tick(0.5);
            Assert.AreEqual(1, sim.RescuesSucceeded);
            Assert.AreEqual(method == RescueMethod.Jump ? 1 : 0, sim.RescuesByJump);
            Assert.AreEqual(method == RescueMethod.Tow ? 1 : 0, sim.RescuesByTow);
            if (method == RescueMethod.Jump) Assert.Greater(sim.Workforce.GetRobot(s.Id).Battery.UsableWh, 0.0, "jump-charged where it lay");
            else Assert.AreEqual(sim.BaseTileId, sim.Workforce.GetRobot(s.Id).Tile, "towed to the lander");
        }

        [Test]
        public void AStrandedRobotInMildAir_GetsARescue_AndDrivesHomeOnTheCharge()
        {
            var sim = Colony(15.0, out int tile);
            var s = Strand(sim, tile);
            for (int h = 0; h < 24 && sim.RescuesSucceeded == 0; h++) sim.Tick(0.5);
            TestContext.WriteLine($"requested {sim.RescuesRequested}, succeeded {sim.RescuesSucceeded}, jump {sim.RescuesByJump}, tow {sim.RescuesByTow}, energy {sim.RescueEnergyWh:0} Wh");
            Assert.AreEqual(1, sim.RescuesRequested);
            Assert.AreEqual(1, sim.RescuesSucceeded);
            for (int h = 0; h < 12; h++) sim.Tick(0.5);
            Assert.Greater(sim.Workforce.GetRobot(s.Id).Battery.UsableWh, 0.0, "it has charge now (jumped, or towed to the charger and charging)");
            Assert.AreEqual(0, sim.RobotsDestroyed);
            Assert.IsTrue(sim.OperationsLog.Any(o => o.Quantity == "Rescue"));
        }

        [Test]
        public void TheLadder_SaysWaitingForHelp_WhileAHelperComes_AndNeedsYouWhenNobodyCan()
        {
            var sim = Colony(15.0, out int tile);
            var s = Strand(sim, tile);
            sim.Tick(0.2);
            Assert.AreEqual(LadderState.WaitingForHelp, sim.LadderOf(s.Id).State, sim.LadderOf(s.Id).Text);
            var alone = Colony(15.0, out int tile2, helpers: 0);
            foreach (var other in alone.Workforce.Robots.ToList()) other.Battery.Draw(1e12, 1.0);   // everyone else is dry too
            var s2 = Strand(alone, tile2);
            alone.Tick(0.2);
            Assert.AreEqual(LadderState.NeedsYou, alone.LadderOf(s2.Id).State, alone.LadderOf(s2.Id).Text);
            Assert.IsTrue(alone.OperationsLog.Any(o => o.Quantity == "Rescue impossible" && o.Priority == KardashevAscent.Simulation.Operations.LogPriority.ActNow), "an ACT NOW entry");
        }

        [Test]
        public void ARescueNeverKillsTheRescuer_AndCostsLessThanTheRobotsBill()
        {
            var sim = Colony(-30.0, out int tile, helpers: 2);
            var s = Strand(sim, tile);
            for (int h = 0; h < 48; h++) sim.Tick(0.5);
            Assert.AreEqual(0, sim.Workforce.Robots.Count(r => r.Id != s.Id && r.IsShutDown && r.Battery.UsableWh <= 0.0), "no helper was left dry");
            if (sim.RescuesRequested > 0) Assert.Less(sim.RescueEnergyWh, 642.0 * 100.0, "far below the robot's material bill in energy terms");
        }

        [Test]
        public void RescuesAreSaved()
        {
            var sim = Colony(15.0, out int tile);
            Strand(sim, tile);
            sim.Tick(0.3);
            Assert.AreEqual(1, sim.Rescues.Count);
            var again = Colony(15.0, out _);
            again.RestoreState(sim.CaptureState());
            Assert.AreEqual(1, again.Rescues.Count);
        }
    }
}

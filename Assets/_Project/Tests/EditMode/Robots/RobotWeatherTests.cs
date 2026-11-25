using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>KI-43: robots, their operating range, the heater, shelter, and destruction by an empty battery outside the range.</summary>
    public class RobotWeatherTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSolarParams Planet(double airC) => new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, airC);

        static PlanetSimulation Colony(double airC) => new PlanetSimulation(Sphere, Surface, Planet(airC));

        static int FarLandTile(PlanetSimulation sim)
        {
            int b = sim.BaseTileId;
            return Enumerable.Range(0, sim.TileCount).First(t => t != b && !sim.GetTile(t).IsWater && !Sphere.Neighbors[b].Contains(t)
                && Sphere.Neighbors[t].All(n => n != b));
        }

        static void Empty(Robot r) => r.Battery.Draw(1e12, 1.0);

        // ---- the rules ----
        [Test]
        public void TheOperatingRangeIsTheOverlapOfElectronicsAndBattery_AndTheHeaterScalesWithHowFarOutsideIt()
        {
            Assert.AreEqual(-20.0, RobotThermal.OperatingMinC, "the battery's -20 C is reached before the electronics' -40 C");
            Assert.AreEqual(50.0, RobotThermal.OperatingMaxC, "the electronics' +50 C is reached before the battery's +60 C");
            Assert.AreEqual(0.0, RobotThermal.ExcessK(0.0));
            Assert.AreEqual(0.0, RobotThermal.ExcessK(-20.0));
            Assert.AreEqual(0.0, RobotThermal.ExcessK(50.0));
            Assert.AreEqual(50.0, RobotThermal.ExcessK(-70.0), 1e-12);
            Assert.AreEqual(15.0, RobotThermal.ExcessK(65.0), 1e-12, "a hot world needs cooling: the same rule");
            Assert.AreEqual(100.0, RobotThermal.HeaterW(-70.0, false), 1e-12, "2 W per kelvin");
            Assert.AreEqual(0.0, RobotThermal.HeaterW(25.0, false));
        }

        [Test]
        public void TheBatteryTakesAChargeOnlyBetween0And45()
        {
            Assert.IsFalse(RobotThermal.CanCharge(-1.0));
            Assert.IsTrue(RobotThermal.CanCharge(0.0));
            Assert.IsTrue(RobotThermal.CanCharge(25.0));
            Assert.IsTrue(RobotThermal.CanCharge(45.0));
            Assert.IsFalse(RobotThermal.CanCharge(46.0));
        }

        [Test]
        public void TheBatterySettlesTowardShelterTheHeldLimitOrTheAir()
        {
            Assert.AreEqual(15.0, RobotThermal.BatteryTarget(-80.0, false, sheltered: true, heaterRuns: true), 1e-12);
            Assert.AreEqual(-20.0, RobotThermal.BatteryTarget(-80.0, false, sheltered: false, heaterRuns: true), 1e-12, "the heater holds the limit reached first");
            Assert.AreEqual(-80.0, RobotThermal.BatteryTarget(-80.0, false, sheltered: false, heaterRuns: false), 1e-12, "no heater, no hold");
            Assert.AreEqual(50.0, RobotThermal.BatteryTarget(70.0, false, sheltered: false, heaterRuns: true), 1e-12);
            double after = RobotThermal.BatteryStep(-30.0, 15.0, RobotThermal.BatteryThermalTauHours);
            Assert.AreEqual(15.0 + (-30.0 - 15.0) * Math.Exp(-1.0), after, 1e-9);
        }

        [Test]
        public void InsulationKeepsTheElectronicsAboveTheColdLimitOnAModerateColdWorld()
        {
            Assert.AreEqual(0.0, RobotThermal.HeaterW(-70.0, true), 1e-12);
            Assert.AreEqual(0.0, RobotThermal.EffectiveC(-70.0, true), 1e-12);
        }

        // ---- in the colony ----
        [Test]
        public void OnAnEarthlikeWorldRobotsNeedNoHeater()
        {
            var sim = Colony(15.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            sim.Tick(1.0);
            Assert.AreEqual(0.0, r.HeaterW);
            Assert.IsTrue(double.IsPositiveInfinity(r.ThermalHoursLeft));
            Assert.AreEqual(0, sim.RobotsDestroyed);
        }

        [Test]
        public void OnAColdWorldARobotOutsideDrawsHeaterPowerFromItsBattery()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            sim.Tick(0.1);
            Assert.Greater(r.HeaterW, 100.0, "far below -40 C: a large draw");
            Assert.IsFalse(r.Sheltered);
            Assert.Less(r.ThermalHoursLeft, 1e9);
        }

        [Test]
        public void ShelterRemovesTheDraw_AndANoStarvedRobotThereIsNotDestroyed()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, sim.BaseTileId);
            Empty(r);
            sim.Tick(5.0);
            Assert.IsTrue(r.Sheltered, "the lander shelters it");
            Assert.AreEqual(0.0, r.HeaterW);
            Assert.AreEqual(0, sim.RobotsDestroyed);
            Assert.IsNotNull(sim.Workforce.GetRobot(r.Id));
        }

        [Test]
        public void ARobotWithAnEmptyBatteryOutsideItsRangeIsDestroyed()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            int id = r.Id, count = sim.Workforce.Robots.Count;
            Empty(r);
            sim.Tick(30.0);
            Assert.AreEqual(1, sim.RobotsDestroyed);
            Assert.IsNull(sim.Workforce.GetRobot(id));
            Assert.AreEqual(count - 1, sim.Workforce.Robots.Count);
            var rec = sim.OperationsLog.Last(o => o.Quantity == "Robot destroyed");
            StringAssert.Contains($"Robot {id} was destroyed", rec.Text);
        }

        [Test]
        public void ARobotIsNotDestroyedWhileItsBatteryStillRuns()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            sim.Tick(0.2);
            Assert.AreEqual(0, sim.RobotsDestroyed, "the heater is working");
            Assert.AreEqual(0.0, r.ExposedEmptyHours);
        }

        [Test]
        public void ARobotWhoseBatteryWontLastTheNightIsWarnedOnce()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            sim.Tick(0.1);
            sim.Tick(0.1);
            int warnings = sim.OperationsLog.Count(o => o.Quantity == "Cold or heat warning" && o.RobotId == r.Id);
            Assert.AreEqual(1, warnings, "one warning for this stretch, not one a step");
            StringAssert.Contains("needs shelter", sim.OperationsLog.Last(o => o.Quantity == "Cold or heat warning").Text);
        }

        [Test]
        public void RobotsNeverDieOnASchedule_ARobotThatGetsHomeLives()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            // it carries on for some time with a charged battery and is never destroyed while the battery lasts
            for (int i = 0; i < 10; i++) sim.Tick(0.1);
            Assert.AreEqual(0, sim.RobotsDestroyed);
            Assert.IsNotNull(sim.Workforce.GetRobot(r.Id));
        }

        [Test]
        public void ExposureSurvivesSaveAndLoad_AndAnOlderSaveStartsAtZero()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            int id = r.Id;
            Empty(r);
            sim.Tick(0.4);
            Assert.Greater(r.ExposedEmptyHours, 0.3);
            var state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var loaded = Colony(-150.0);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.Greater(loaded.Workforce.GetRobot(id).ExposedEmptyHours, 0.3);
            state.Version = 48;
            foreach (var rs in state.Workforce.Robots) rs.ExposedEmptyHours = 0.0;
            var old = Colony(-150.0);
            Assert.IsTrue(old.RestoreState(state));
            Assert.AreEqual(0.0, old.Workforce.GetRobot(id).ExposedEmptyHours);
        }

        [Test]
        public void ADestroyedRobotsUnsyncedFindingsAreLost()
        {
            // a held finding is tied to its robot; with the robot gone it is dropped with it
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            int id = r.Id;
            Empty(r);
            sim.Tick(30.0);
            Assert.IsFalse(sim.HeldFindings.Any(f => f.RobotId == id));
        }

        [Test]
        public void ADestroyedRobotsCargoLiesInItsWreckWhereItStopped()
        {
            var sim = Colony(-150.0);
            sim.LogisticsEnabled = true;
            int tile = FarLandTile(sim);
            var r = sim.Workforce.AddRobot(RobotKind.General, tile);
            int id = r.Id;
            var cargo = KardashevAscent.Simulation.Logistics.PlaceId.Cargo(id);
            sim.InventoriesForTests.Add(cargo, KardashevAscent.Simulation.Resources.Material.Iron, 50.0);
            Empty(r);
            sim.Tick(30.0);
            Assert.AreEqual(1, sim.RobotsDestroyed);
            Assert.IsFalse(sim.InventoriesForTests.Has(cargo), "the cargo place is gone with the robot");
            var wreck = sim.Wrecks.Single();
            Assert.AreEqual(tile, wreck.Tile);
            // the 50 kg of cargo and the robot's own 400 kg of iron lie in the wreck (an open pile), and it is cold: structure intact, electronics are waste
            Assert.AreEqual(450.0, sim.WreckStockKg(wreck, KardashevAscent.Simulation.Resources.Material.Iron), 1e-6);
            Assert.Greater(sim.WreckStockKg(wreck, KardashevAscent.Simulation.Resources.Material.ElectronicWaste), 0.0);
            StringAssert.Contains("wreck", sim.OperationsLog.Last(o => o.Quantity == "Robot destroyed").Text);
        }

        [Test]
        public void TheHudGetsNewsOfAWarningAndOfALoss_AndNoneOnAnEarthlikeWorld()
        {
            var calm = Colony(15.0);
            calm.Workforce.AddRobot(RobotKind.General, FarLandTile(calm));
            calm.Tick(1.0);
            Assert.AreEqual(0, calm.RobotWeatherNewsSerial);
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            sim.Tick(0.1);
            Assert.AreEqual(1, sim.RobotWeatherNewsSerial, "the warning");
            StringAssert.Contains("needs shelter", sim.RobotWeatherNews);
            Empty(r);
            sim.Tick(30.0);
            Assert.AreEqual(2, sim.RobotWeatherNewsSerial, "the loss");
            StringAssert.Contains("destroyed", sim.RobotWeatherNews);
        }

        [Test]
        public void AColdRobotAtTheLanderIsWarmedBeforeItIsCharged()
        {
            var w = new Workforce();
            Robot r = w.AddRobot(RobotKind.General, 0);
            r.Battery.Restore(0.0, 0.0);   // flat: it wants a charge
            r.BatteryTempC = -10.0;        // and it is cold
            var lander = new[] { new RobotFacility(0, -1, new ChargingCapability(2, 1000.0, true)) };
            var up = new RobotUpkeep();
            bool everWarming = false;
            double chargedAt = -1.0, hours = 0.0;
            for (int i = 0; i < 60 && chargedAt < 0.0; i++)
            {
                up.Plan(w, lander, 0.05);
                up.Apply(w, 0.05, up.DockRequestW, 0.0, 0.0, false, null, null, -60.0, tile => -60.0, tile => true);
                hours += 0.05;
                everWarming |= r.WarmingUp;
                if (r.WarmingUp) Assert.AreEqual(0.0, r.LastChargeW, 1e-9, "no charge into a battery below 0 C");
                if (r.LastChargeW > 0.0) chargedAt = hours;
            }
            Assert.IsTrue(everWarming, "it was warmed first");
            Assert.Greater(chargedAt, 0.2, "charging began only after the shelter had warmed the battery past 0 C");
            Assert.IsTrue(RobotThermal.CanCharge(r.BatteryTempC));
        }

        [Test]
        public void TheBatteryTemperatureIsSavedAndAnOlderSaveStartsWarm()
        {
            var sim = Colony(-150.0);
            var r = sim.Workforce.AddRobot(RobotKind.General, FarLandTile(sim));
            int id = r.Id;
            r.BatteryTempC = -12.5;
            var state = sim.CaptureState();
            var loaded = Colony(-150.0);
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(-12.5, loaded.Workforce.GetRobot(id).BatteryTempC, 1e-9);
            state.Version = 49;
            foreach (var rs in state.Workforce.Robots) rs.BatteryTempC = 15.0;
            var old = Colony(-150.0);
            Assert.IsTrue(old.RestoreState(state));
            Assert.AreEqual(15.0, old.Workforce.GetRobot(id).BatteryTempC, 1e-9);
        }
    }
}

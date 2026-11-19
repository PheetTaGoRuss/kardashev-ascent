using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The debug switches (F10 panel): they bypass resource rules for testing, are off by default and are not saved.</summary>
    public class DebugModeTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike);

        [Test]
        public void AllSwitchesStartOff_AndNothingIsMarkedUsed()
        {
            var d = Create().Debug;
            Assert.IsFalse(d.AnyOn);
            Assert.IsFalse(d.EverUsed);
        }

        [Test]
        public void FreeBuilding_PlacesBuildingsThatStandAtOnce_WithNoMaterialsTaken()
        {
            PlanetSimulation sim = Create();
            int tile = sim.BaseTileId;
            double ironBefore = sim.Materials[Material.Iron];
            sim.Debug.FreeBuilding = true;
            Assert.IsTrue(sim.PlacePanel(tile, 0, 0.3, Math.PI));
            Assert.IsTrue(sim.PlaceBattery(tile, 10));
            Assert.IsFalse(sim.IsUnderConstruction(tile, 0), "a free panel needs no robot");
            Assert.IsFalse(sim.IsUnderConstruction(tile, 10), "a free battery needs no robot");
            Assert.AreEqual(ironBefore, sim.Materials[Material.Iron], 1e-9, "no materials were used");
            Assert.IsTrue(sim.Debug.EverUsed);
        }

        [Test]
        public void WithoutTheSwitch_ABuildingIsASite_ThatNeedsRobotTime()
        {
            PlanetSimulation sim = Create();
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, 0, 0.3, Math.PI));
            Assert.IsTrue(sim.IsUnderConstruction(sim.BaseTileId, 0));
        }

        [Test]
        public void InfiniteMaterials_KeepsTheStockHigh_AndStopsWhenSwitchedOff()
        {
            PlanetSimulation sim = Create();
            sim.Debug.InfiniteMaterials = true;
            sim.Tick(1.0);
            foreach (Material m in Materials.All)
                Assert.GreaterOrEqual(sim.Materials[m], DebugOptions.MaterialFloorKg - 1.0, m.ToString());
            sim.Debug.InfiniteMaterials = false;
            Assert.IsTrue(sim.Debug.EverUsed, "stays marked for the rest of the session");
        }

        [Test]
        public void UnlockAll_OpensWhatTheMilestonesGate()
        {
            PlanetSimulation sim = Create();
            sim.Materials.Set(Material.Iron, 1e6);
            sim.Materials.Set(Material.Rock, 1e6);
            sim.Materials.Set(Material.Copper, 1e6);
            sim.Materials.Set(Material.Lithium, 1e6);
            Assert.IsTrue(sim.PlaceWorkshop(sim.BaseTileId, 20));
            Assert.AreEqual(0, sim.MilestonesReached);
            Assert.AreEqual(0, sim.PanelTier);
            sim.Debug.UnlockAll = true;
            Assert.Greater(sim.PanelTier, 0, "bigger panel tiers open");
            Assert.AreEqual(0, sim.MilestonesReached, "the real milestone count is not faked");
        }

        [Test]
        public void FullBattery_KeepsTheBatteryFull()
        {
            PlanetSimulation sim = Create();
            sim.Debug.FullBattery = true;
            sim.Tick(48.0);
            Assert.AreEqual(sim.BatteryCapacityWh, sim.BatteryStoredWh, sim.BatteryCapacityWh * 0.02);
        }

        [Test]
        public void Shortcuts_AddMaterials_Robots_AndSurveyEverything()
        {
            PlanetSimulation sim = Create();
            double before = sim.Materials[Material.Lithium];
            sim.DebugAddMaterials(500.0);
            Assert.AreEqual(before + 500.0, sim.Materials[Material.Lithium], 1e-9);
            int robots = sim.Workforce.Robots.Count;
            Assert.IsTrue(sim.DebugAddRobot(RobotKind.General));
            Assert.AreEqual(robots + 1, sim.Workforce.Robots.Count);
            Assert.Greater(sim.DebugSurveyAll(), 0);
            Assert.IsTrue(sim.Deposits.IsSurveyed(sim.BaseTileId));
            Assert.AreEqual(0, sim.DebugSurveyAll(), "nothing new the second time");
        }

        [Test]
        public void Switches_AreNotSaved()
        {
            PlanetSimulation sim = Create();
            sim.Debug.FreeBuilding = true;
            sim.Debug.UnlockAll = true;
            SimulationState state = sim.CaptureState();
            PlanetSimulation other = Create();
            Assert.IsTrue(other.RestoreState(state));
            Assert.IsFalse(other.Debug.AnyOn, "a loaded game starts with every switch off");
        }
    }
}

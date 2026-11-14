using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Floating solar on shallow sea and wetland (decided 2026-11-13): 1.5 times the cost, cooler cells.</summary>
    public class FloatingSolarTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(24);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 15.0, false));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0);
        const double Deg = Math.PI / 180.0;

        static int Find(TerrainType type) => Array.IndexOf(Surface.Terrain, type);

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike);

        [Test]
        public void Floats_OnShallowSea_AndWetland_ButNotOnDeepSeaOrLand()
        {
            PlanetSimulation sim = Create();
            Assert.IsTrue(sim.PlaceFloatingPanel(Find(TerrainType.ShallowOcean), 0, 0.3, Math.PI), "shallow sea");
            Assert.IsTrue(sim.PlaceFloatingPanel(Find(TerrainType.Wetland), 0, 0.3, Math.PI), "wetland");
            Assert.IsFalse(sim.PlaceFloatingPanel(Find(TerrainType.DeepOcean), 0, 0.3, Math.PI), "deep sea");
            Assert.IsFalse(sim.PlaceFloatingPanel(sim.BaseTileId, 0, 0.3, Math.PI), "land");
            Assert.AreEqual(BuildingType.FloatingSolar, sim.GetBuilding(Find(TerrainType.ShallowOcean), 0));
        }

        [Test]
        public void LandPanels_CannotGoOnWater_OrWetland()
        {
            PlanetSimulation sim = Create();
            Assert.IsFalse(sim.PlacePanel(Find(TerrainType.ShallowOcean), 0, 0.3, Math.PI));
            Assert.IsFalse(sim.PlacePanel(Find(TerrainType.Wetland), 0, 0.3, Math.PI), "wetland is too soft");
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, 0, 0.3, Math.PI));
        }

        [Test]
        public void CostsOneAndAHalfTimesALandPanel()
        {
            PlanetSimulation sim = Create();
            Assert.AreEqual(sim.BuildBill(BuildingType.SolarPanel).TotalKg * PlanetSimulation.FloatingCostFactor,
                            sim.BuildBill(BuildingType.FloatingSolar).TotalKg, 1e-6);
            Assert.AreEqual(1.5, PlanetSimulation.FloatingCostFactor);
        }

        [Test]
        public void Preview_WorksOnlyWhereItCanFloat_AndGivesRealNumbers()
        {
            PlanetSimulation sim = Create();
            PanelPreview sea = sim.PreviewFloatingPanel(Find(TerrainType.ShallowOcean), 0.3, Math.PI);
            Assert.IsTrue(sea.CanPlace);
            Assert.Greater(sea.EnergyPerYearWh, 0.0);
            Assert.Greater(sea.Eroi, 0.0);
            Assert.IsFalse(sim.PreviewFloatingPanel(sim.BaseTileId, 0.3, Math.PI).CanPlace);
            StringAssert.Contains("shallow sea or wetland", sim.PreviewFloatingPanel(Find(TerrainType.DeepOcean), 0.3, Math.PI).Reason);
        }

        [Test]
        public void OnlyARobotWithABoat_BuildsIt_AndTakesItDown()
        {
            PlanetSimulation sim = Create();
            // The shallow sea nearest the base by boat (robots only take work they can reach and get back from).
            double[] hours = sim.Routes.Amphibious().HoursFrom(sim.BaseTileId);
            int sea = -1;
            for (int t = 0; t < hours.Length; t++)
                if (Surface.Terrain[t] == TerrainType.ShallowOcean && (sea < 0 || hours[t] < hours[sea])) sea = t;
            Assert.IsTrue(double.IsInfinity(sim.Routes.HoursFrom(sim.BaseTileId)[sea]), "no land route onto the sea");
            Assert.IsTrue(sim.PlaceFloatingPanel(sea, 0, 0.3, Math.PI));
            for (int i = 0; i < 100; i++) sim.Tick(0.5);
            Assert.IsTrue(sim.IsUnderConstruction(sea, 0), "robots without a boat can't reach the sea");

            foreach (var r in sim.Workforce.Robots) r.Modules |= KardashevAscent.Simulation.Robots.RobotModule.Boat;
            sim.RefreshWork();
            for (int i = 0; i < 800 && sim.IsUnderConstruction(sea, 0); i++) sim.Tick(0.5);
            Assert.IsFalse(sim.IsUnderConstruction(sea, 0), "a boat robot sails out and builds it");
            Assert.IsTrue(sim.OrderPanelAngles(sea, 0, 0.5, 90.0 * Deg), "angle changes need no robot");
            Assert.IsTrue(sim.TryGetPanelAngles(sea, 0, out double tilt, out double az));
            Assert.AreEqual(0.5, tilt, 1e-9);
            Assert.AreEqual(90.0 * Deg, az, 1e-9);
            double before = sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron];
            Assert.IsTrue(sim.OrderSalvage(sea, 0));
            for (int i = 0; i < 800 && sim.GetBuilding(sea, 0) != BuildingType.None; i++) sim.Tick(0.5);
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(sea, 0), "a boat robot takes it down");
            Assert.Greater(sim.Materials[KardashevAscent.Simulation.Resources.Material.Iron], before - 50.0, "half its materials came back");
        }

        [Test]
        public void SaveAndLoad_KeepsFloatingPanels_AsFloating()
        {
            PlanetSimulation sim = Create();
            int sea = Find(TerrainType.ShallowOcean);
            Assert.IsTrue(sim.PlaceFloatingPanel(sea, 3, 0.3, 200.0 * Deg));
            SimulationState state = sim.CaptureState();
            Assert.IsTrue(state.Buildings.Any(b => b.Type == BuildingType.FloatingSolar && b.Tile == sea));
            PlanetSimulation other = Create();
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(BuildingType.FloatingSolar, other.GetBuilding(sea, 3));
        }

        [Test]
        public void ASavedLandPanel_OnATileThatIsNowWetland_StillLoads()
        {
            PlanetSimulation sim = Create();
            int wet = Find(TerrainType.Wetland);
            SimulationState state = Create().CaptureState();
            state.Buildings.Add(new BuildingRecord { Tile = wet, Slot = 0, Type = BuildingType.SolarPanel, Tilt = 0.3, Azimuth = Math.PI, AreaM2 = 10000.0 });
            Assert.IsTrue(sim.RestoreState(state));
            Assert.AreEqual(BuildingType.SolarPanel, sim.GetBuilding(wet, 0), "saved colonies keep what they built");
        }
    }
}

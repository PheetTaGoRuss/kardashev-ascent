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
    /// <summary>Iron from rust (A1) and rebuilding a robot from a recovered wreck (A2).</summary>
    public class RebuildTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSimulation Colony()
        {
            var p = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);
            var sim = new PlanetSimulation(Sphere, Surface, p, Kardashev.EarthRadiusM) { ConstructionTimeEnabled = false, WeatherEnabled = false };
            sim.Debug.UnlockAll = true; sim.Debug.FreeBuilding = true;
            for (int sl = 0; sl < sim.SlotCount(sim.BaseTileId); sl++) if (sim.PlaceWorkshop(sim.BaseTileId, sl)) break;
            sim.Debug.FreeBuilding = false;
            return sim;
        }

        [Test]
        public void IronFromRust_FollowsTheStoichiometry_AndKeepsMass()
        {
            RefineRecipe r = Refining.Of(RefineProcess.IronFromRust);
            Assert.AreEqual(Material.Rust, r.Input); Assert.AreEqual(Material.Iron, r.Output);
            Assert.AreEqual(0.6285, r.OutputKg / r.InputKg, 0.0005, "55.845 / 88.851");
            Assert.AreEqual(r.InputKg, r.OutputKg + r.VentedKg + r.SlagKg, 1e-9, "mass is kept");
            Assert.AreEqual(Refining.OxygenPerRust + Refining.WaterPerRust, r.VentedKg / r.InputKg, 1e-3, "oxygen and steam");
            Assert.AreEqual(4.0, r.EnergyWh / r.OutputKg / 1000.0, 1e-9, "4 kWh per kg of iron");
            Assert.AreEqual(RefineProcess.IronFromRust, Refining.For(Material.Iron));
            Assert.AreEqual(RefineryKind.Electrolyser, r.Module);
        }

        [Test]
        public void ARecoveredSoakWreck_CanBeRebuilt_ForItsMaterials_AndAMeltedOneCannot()
        {
            var sim = Colony();
            int tile = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater);
            var dead = sim.Workforce.AddRobot(RobotKind.General, tile);
            Wreck w = sim.DestroyForTests(dead.Id, -45.0);
            Assert.IsTrue(w.Rebuildable);
            StringAssert.Contains("not been recovered", sim.RebuildBlocker(w.Id));
            sim.InventoriesForTests.Remove(KardashevAscent.Simulation.Logistics.PlaceId.Wreck(w.Id));
            sim.LogisticsEnabled = true; sim.Tick(1.0);
            Assert.IsTrue(sim.Wrecks.Single(x => x.Id == w.Id).Recovered, "kept for rebuilding");
            sim.Materials.Set(Material.Copper, 0.0);
            StringAssert.StartsWith("needs", sim.RebuildBlocker(w.Id));
            foreach (var (m, kg) in WreckModel.BillOf(RobotKind.General, false)) sim.Materials.Add(m, kg);
            int robots = sim.Workforce.Robots.Count;
            Assert.IsNull(sim.OrderRebuild(w.Id));
            for (int h = 0; h < 40 && sim.Workforce.Robots.Count == robots; h++) sim.Tick(1.0);
            Assert.AreEqual(robots + 1, sim.Workforce.Robots.Count, "a robot came back");
            Assert.AreEqual(0, sim.Wrecks.Count(x => x.Id == w.Id), "the wreck is used up");
            var hotRobot = sim.Workforce.AddRobot(RobotKind.General, tile);
            Wreck melted = sim.DestroyForTests(hotRobot.Id, 700.0);
            Assert.IsFalse(melted.Rebuildable);
            StringAssert.Contains("melted", sim.RebuildBlocker(melted.Id));
        }
    }
}

using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Chemistry balance (report): two game years of a typical early-colony stock per planet type, loss share by material. Reported, not tuned.</summary>
    public class ChemistryBalanceTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Params(double meanC, double pressureEarth) =>
            new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, meanC, false, pressureEarth);
        static PlanetSolarParams Mars => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);

        [Test]
        public void TwoYears_PerPlanetType_LossShareByMaterial()
        {
            var types = new (string Name, PlanetSolarParams P, CloudModel C)[]
            {
                ("Earth-like 1 atm", Params(15.0, 1.0), CloudModel.EarthClimatology),
                ("Cold Earth-like", Params(-25.0, 1.0), CloudModel.EarthClimatology),
                ("Mars-like", Mars, CloudModel.None),
                ("Hot thick air 2 atm", Params(60.0, 2.0), CloudModel.None),
                ("Airless warm (-50 C = 223 K)", Params(-50.0, 0.0), CloudModel.None),
                ("Airless cold (-153 C = 120 K)", Params(-153.15, 0.0), CloudModel.None),
            };
            var mats = new[] { Material.Iron, Material.Copper, Material.Aluminium, Material.Water, Material.Salt, Material.Rock };
            var rows = new System.Text.StringBuilder("Chemistry balance (2 game years, 1000 kg of each in an open pile on an inland 45 N tile; sodium and lithium 100 kg in an open wreck pile): planet | " + string.Join(" | ", mats.Select(m => m + " % lost")) + " | Sodium in the open % left | Lithium in the open % left | ledger imbalance kg\n");
            foreach (var t in types)
            {
                var sim = new PlanetSimulation(Sphere, Surface, t.P, Kardashev.EarthRadiusM) { Clouds = t.C };
                int tile = Enumerable.Range(0, sim.TileCount).Where(i => i != sim.BaseTileId && !sim.GetTile(i).IsWater && sim.IsOpenGround(i)).OrderBy(i => Math.Abs(Sphere.Latitude(i) - 45.0 * Math.PI / 180.0)).First();
                var depot = PlaceId.Depot(tile);
                foreach (var m in mats) sim.InventoriesForTests.Add(depot, m, 1000.0);
                var dead = sim.Workforce.AddRobot(RobotKind.General, tile);
                var wreck = sim.DestroyForTests(dead.Id, 10.0);
                var pile = PlaceId.Wreck(wreck.Id);
                double na0 = 100.0, li0 = 100.0;
                sim.InventoriesForTests.Add(pile, Material.Sodium, na0); sim.InventoriesForTests.Add(pile, Material.Lithium, li0);
                var stock0 = mats.ToDictionary(m => m, m => sim.InventoriesForTests.StockOf(depot)[m]);
                for (int d = 0; d < (int)(2 * sim.YearLengthHours / 24.0); d++) sim.Tick(24.0);
                var st = sim.InventoriesForTests.StockOf(depot);
                rows.Append($"{t.Name} | {string.Join(" | ", mats.Select(m => $"{(stock0[m] > 0 ? (1.0 - st[m] / stock0[m]) * 100.0 : 0.0):0.0}"))} | {sim.InventoriesForTests.StockOf(pile)[Material.Sodium] / na0 * 100.0:0.0} | {sim.InventoriesForTests.StockOf(pile)[Material.Lithium] / li0 * 100.0:0.0} | {sim.Chemistry.ImbalanceKg:0.000000}\n");
                Assert.AreEqual(0.0, sim.Chemistry.ImbalanceKg, 1e-3, t.Name + ": the environment ledger closes");
            }
            // robot wreck against its bill
            double bill = WreckModel.Total(WreckModel.BillOf(RobotKind.General, false));
            rows.Append($"A General robot's bill is {bill:0} kg; the wreck after a cold death holds {WreckModel.Total(WreckModel.Residue(WreckModel.BillOf(RobotKind.General, false), -40.0)):0} kg, at 700 C {WreckModel.Total(WreckModel.Residue(WreckModel.BillOf(RobotKind.General, false), 700.0)):0} kg, at 1600 C {WreckModel.Total(WreckModel.Residue(WreckModel.BillOf(RobotKind.General, false), 1600.0)):0} kg (all slag), above 2862 C nothing.\n");
            TestContext.WriteLine(rows.ToString());
        }
    }
}

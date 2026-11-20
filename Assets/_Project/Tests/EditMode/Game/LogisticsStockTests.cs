using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Stock per place (MO-02): mining fills the mine tile's pile, a site draws from its own tile, old saves keep one stock.</summary>
    public class LogisticsStockTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Make(bool logistics)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = logistics };
            return sim;
        }

        static int LandNeighbour(PlanetSimulation sim) =>
            Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));

        [Test]
        public void ItIsOffByDefault_AndTheLanderDepotIsTheOldStock()
        {
            var sim = Make(false);
            Assert.IsFalse(sim.LogisticsEnabled);
            double iron = sim.Materials[Material.Iron];
            Assert.Greater(iron, 0.0);
            Assert.AreEqual(iron, sim.KgAt(sim.BaseTileId, Material.Iron), 1e-9);
            Assert.AreEqual(iron, sim.TotalKg(Material.Iron), 1e-9);
            Assert.AreEqual(iron, sim.KgAt(LandNeighbour(sim), Material.Iron), 1e-9, "off: one stock serves every tile");
        }

        [Test]
        public void WithLogisticsOn_OtherTilesHaveTheirOwnDepot_AndTheHudSeesTheSum()
        {
            var sim = Make(true);
            int other = LandNeighbour(sim);
            double iron = sim.Materials[Material.Iron];
            Assert.AreEqual(0.0, sim.KgAt(other, Material.Iron), 1e-9, "nothing lies there yet");
            sim.PutMaterials(other, Material.Iron, 40.0);
            Assert.AreEqual(40.0, sim.KgAt(other, Material.Iron), 1e-9);
            Assert.AreEqual(iron, sim.Materials[Material.Iron], 1e-9, "the lander depot is unchanged");
            Assert.AreEqual(iron + 40.0, sim.TotalKg(Material.Iron), 1e-9);
            Assert.AreEqual(iron + 40.0, ((IResourceCommands)sim).Materials[Material.Iron], 1e-9, "the colony-wide view");
        }

        [Test]
        public void MinedOre_LiesInAPileOnTheMineTile()
        {
            var sim = Make(true);
            int tile = LandNeighbour(sim);
            sim.OrderSurvey(tile);
            sim.OrderMining(tile, Material.Rock);
            double hubRock = sim.Materials[Material.Rock];
            for (int i = 0; i < 1200 && sim.KgAt(tile, Material.Rock) <= 0.0; i++) sim.Tick(1.0);
            Assert.Greater(sim.KgAt(tile, Material.Rock), 0.0, "the ore lies where it was mined");
            Assert.AreEqual(hubRock, sim.Materials[Material.Rock], 1e-9, "and not in the lander depot");
        }

        [Test]
        public void APileThatIsFull_StopsMining()
        {
            var sim = Make(true);
            sim.OpenPileCapacityKg = 1.0;
            int tile = LandNeighbour(sim);
            sim.PutMaterials(tile, Material.Rock, 1.0);
            double before = sim.KgAt(tile, Material.Rock);
            sim.OrderSurvey(tile);
            sim.OrderMining(tile, Material.Rock);
            for (int i = 0; i < 600; i++) sim.Tick(1.0);
            Assert.AreEqual(before, sim.KgAt(tile, Material.Rock), 1e-9, "no room, so nothing more is taken from the ground");
        }

        [Test]
        public void ASiteOnAnotherTile_DrawsOnlyFromItsOwnDepot_AndTheHaulsFillIt()
        {
            var sim = Make(true);
            int tile = LandNeighbour(sim);
            Assert.IsTrue(sim.PlaceBattery(tile, 0));
            sim.Tick(0.25);
            Assert.AreEqual(0.0, sim.ConstructionProgress(tile, 0), 1e-9, "nothing is in the site's own depot yet");
            Assert.IsNotEmpty(sim.HaulOrders(), "a haul was planned from the lander depot");
            for (int i = 0; i < 400 && sim.IsUnderConstruction(tile, 0); i++) sim.Tick(1.0);
            Assert.IsFalse(sim.IsUnderConstruction(tile, 0), "the hauls brought what it needed");
            Assert.Greater(sim.HaulTotals.KgMoved, 0.0);
        }

        [Test]
        public void ASiteOnTheLanderTile_BuildsAsBefore()
        {
            var sim = Make(true);
            Assert.IsTrue(sim.PlaceBattery(sim.BaseTileId, 0));
            for (int i = 0; i < 1200 && sim.IsUnderConstruction(sim.BaseTileId, 0); i++) sim.Tick(1.0);
            Assert.IsFalse(sim.IsUnderConstruction(sim.BaseTileId, 0));
        }

        [Test]
        public void SaveAndLoad_KeepsEveryPile_AndTheFlag()
        {
            var sim = Make(true);
            int tile = LandNeighbour(sim);
            sim.PutMaterials(tile, Material.Copper, 12.5);
            SimulationState state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            Assert.IsTrue(state.LogisticsEnabled);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.IsTrue(other.LogisticsEnabled);
            Assert.AreEqual(12.5, other.KgAt(tile, Material.Copper), 1e-9);
            Assert.AreEqual(sim.TotalKg(Material.Copper), other.TotalKg(Material.Copper), 1e-9);
        }

        [Test]
        public void AnOldSave_LoadsWithTheSameTotals_AllInTheLanderDepot()
        {
            var sim = Make(false);
            SimulationState state = sim.CaptureState();
            state.Version = 37;                 // from before stock per place
            state.LogisticsEnabled = true;      // a flag an old save could never have
            state.Inventories.Clear();
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.IsFalse(other.LogisticsEnabled, "an old save keeps the one stock");
            foreach (Material m in Materials.All)
            {
                Assert.AreEqual(sim.Materials[m], other.Materials[m], 1e-9, m.ToString());
                Assert.AreEqual(sim.Materials[m], other.KgAt(other.BaseTileId, m), 1e-9);
            }
        }

        [Test]
        public void Determinism_TwoRunsGiveTheSameCapturedPiles()
        {
            string Run()
            {
                var sim = Make(true);
                int tile = LandNeighbour(sim);
                sim.OrderSurvey(tile);
                sim.OrderMining(tile, Material.Rock);
                for (int i = 0; i < 300; i++) sim.Tick(1.0);
                var state = sim.CaptureState();
                return string.Join(";", state.Inventories.Select(r => $"{r.Kind}{r.Id}:" + string.Join(",", r.Stock.Select(m => $"{m.Material}={m.Kg:R}"))))
                       + "|" + string.Join(",", state.MaterialsKg.Select(m => $"{m.Material}={m.Kg:R}"));
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}

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
    /// <summary>Storage yard, sealed store, gas tank (MO-05): each holds one class of material; the lander holds all.</summary>
    public class StorageBuildingTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Make() => new PlanetSimulation(Sphere, Surface, Earthlike) { LogisticsEnabled = true };

        static int LandNeighbour(PlanetSimulation sim) =>
            Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));

        static void BuildOut(PlanetSimulation sim, int tile, int slot, int hours = 400)
        {
            for (int i = 0; i < hours && sim.IsUnderConstruction(tile, slot); i++) sim.Tick(1.0);
            Assert.IsFalse(sim.IsUnderConstruction(tile, slot), "the hauls brought the parts and it was built");
        }

        [Test]
        public void TheGasTankIsSizedByTheIdealGasLaw()
        {
            // 1 m3 of oxygen at 20 MPa and 293.15 K, ideal gas (Z = 1): p M / (R T) = 262.5 kg/m3.
            Assert.AreEqual(262.5, StorageSpecs.GasMassKg(1.0, 20e6, 0.031998, 293.15, 1.0), 0.5);
            // The real tank: 10 m3 with Z = 0.95.
            Assert.AreEqual(2763.0, StorageSpecs.TankCapacityKg, 5.0);
            Assert.AreEqual(2.0 * StorageSpecs.GasMassKg(1.0, 20e6, 0.031998, 293.15, 0.95), StorageSpecs.GasMassKg(2.0, 20e6, 0.031998, 293.15, 0.95), 1e-9);
        }

        [Test]
        public void TheStorageBuildings_HaveDefinitionsAndBills()
        {
            foreach (BuildingType t in new[] { BuildingType.StorageYard, BuildingType.SealedStore, BuildingType.GasTank })
            {
                Assert.IsTrue(PlanetSimulation.IsStorage(t));
                Assert.IsNotNull(BuildingCatalog.Get(t), t.ToString());
                Assert.IsFalse(BuildingCatalog.Get(t).FixedBill.IsEmpty, t.ToString());
            }
            Assert.AreEqual(2, BuildingFootprints.StorageYard);
        }

        [Test]
        public void TheLanderHoldsEverything_OtherTilesOnlyWhatTheirStoresAllow()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            Assert.IsTrue(double.IsPositiveInfinity(sim.StorageCapacityKg(sim.BaseTileId, StorageClass.Hazardous)));
            Assert.IsTrue(double.IsPositiveInfinity(sim.StorageCapacityKg(tile, StorageClass.BulkSolid)), "piles are unlimited for now (Q1)");
            Assert.AreEqual(0.0, sim.StorageCapacityKg(tile, StorageClass.Hazardous), 1e-12, "no sealed store, no lithium");
            Assert.AreEqual(0.0, sim.StorageCapacityKg(tile, StorageClass.PressurizedGas), 1e-12);
            Assert.AreEqual(0.0, sim.RoomAt(tile, Material.Lithium), 1e-12);
            Assert.IsTrue(double.IsPositiveInfinity(sim.RoomAt(sim.BaseTileId, Material.Lithium)));
        }

        [Test]
        public void AYardAddsBulkRoom_OnAFiniteTile()
        {
            var sim = Make();
            sim.OpenPileCapacityKg = 1000.0;
            int tile = LandNeighbour(sim);
            Assert.AreEqual(1000.0, sim.StorageCapacityKg(tile, StorageClass.BulkSolid), 1e-9);
            Assert.IsTrue(sim.PlaceStorage(BuildingType.StorageYard, tile, 0));
            Assert.AreEqual(BuildingType.StorageYard, sim.GetBuilding(tile, 0));
            Assert.AreEqual(1000.0 + 0.0, sim.StorageCapacityKg(tile, StorageClass.BulkSolid) - SiteNeed(sim, tile), 1e-6, "a site's own bill may be laid down, but the yard does not count until it is built");
            BuildOut(sim, tile, 0);
            Assert.AreEqual(1, sim.StorageCount(tile, BuildingType.StorageYard));
            Assert.AreEqual(1000.0 + StorageSpecs.YardCapacityKg, sim.StorageCapacityKg(tile, StorageClass.BulkSolid), 1e-6);
        }

        static double SiteNeed(PlanetSimulation sim, int tile)
        {
            double kg = 0.0;
            foreach (Material m in Materials.All)
                if (MaterialCatalog.PropertiesOf(m).StorageClass == StorageClass.BulkSolid)
                    kg += sim.ConstructionRemainingAt(tile, 0)[m];
            return kg;
        }

        [Test]
        public void LithiumIsHauledToATileOnlyOnceItHasASealedStore()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Lithium, 10.0);
            for (int i = 0; i < 24; i++) sim.Tick(1.0);
            Assert.AreEqual(0.0, sim.KgAt(tile, Material.Lithium), 1e-9, "no sealed store: no haul");
            Assert.IsEmpty(sim.HaulOrders());
            Assert.IsTrue(sim.PlaceStorage(BuildingType.SealedStore, tile, 0));
            BuildOut(sim, tile, 0);
            for (int i = 0; i < 400 && sim.KgAt(tile, Material.Lithium) < 10.0 - 1e-6; i++) sim.Tick(1.0);
            Assert.AreEqual(10.0, sim.KgAt(tile, Material.Lithium), 1e-6, "with the store built, the target is met");
        }

        [Test]
        public void ASealedStoreIsLimited_ToItsCapacity()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            Assert.IsTrue(sim.PlaceStorage(BuildingType.SealedStore, tile, 0));
            BuildOut(sim, tile, 0);
            Assert.AreEqual(StorageSpecs.SealedStoreCapacityKg, sim.StorageCapacityKg(tile, StorageClass.Hazardous), 1e-9);
            Assert.AreEqual(StorageSpecs.SealedStoreCapacityKg, sim.StorageCapacityKg(tile, StorageClass.SealedLiquid), 1e-9);
        }

        [Test]
        public void StorageSurvivesSaveAndLoad_AndRemoving_VacatesTheSlot()
        {
            var sim = Make();
            int tile = LandNeighbour(sim);
            Assert.IsTrue(sim.PlaceStorage(BuildingType.GasTank, tile, 0));
            BuildOut(sim, tile, 0);
            Assert.AreEqual(StorageSpecs.TankCapacityKg, sim.StorageCapacityKg(tile, StorageClass.PressurizedGas), 1e-6);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(sim.CaptureState()));
            Assert.AreEqual(BuildingType.GasTank, other.GetBuilding(tile, 0));
            Assert.AreEqual(StorageSpecs.TankCapacityKg, other.StorageCapacityKg(tile, StorageClass.PressurizedGas), 1e-6);
            Assert.IsTrue(other.RemoveBuilding(tile, 0));
            Assert.AreEqual(BuildingType.None, other.GetBuilding(tile, 0));
            Assert.AreEqual(0.0, other.StorageCapacityKg(tile, StorageClass.PressurizedGas), 1e-12);
        }

        [Test]
        public void AFullBulkPile_TakesNoMoreFromAHaul()
        {
            var sim = Make();
            sim.OpenPileCapacityKg = 100.0;
            int tile = LandNeighbour(sim);
            sim.SetStockTarget(tile, Material.Iron, 500.0);
            for (int i = 0; i < 300; i++) sim.Tick(1.0);
            Assert.AreEqual(100.0, sim.KgAt(tile, Material.Iron), 1e-6, "the pile holds 100 kg and no more");
            Assert.IsEmpty(sim.HaulOrders().Where(o => o.Loaded), "no robot is left holding cargo it cannot put down");
        }
    }
}

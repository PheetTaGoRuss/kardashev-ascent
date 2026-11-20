using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Storage buildings (mission operations 3.3, MO-05): a yard for bulk solids, a sealed store for liquids and hazardous
    /// solids, a tank for pressurised gas. A tile's depot holds what these let it hold, class by class; the lander depot holds
    /// everything (the lander is its own store). A construction site may lay down what its own bill needs whatever it holds.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly Dictionary<(int Tile, int Slot), BuildingType> storageBuildings = new Dictionary<(int, int), BuildingType>();
        readonly HashSet<(int, Material)> storageFullReported = new HashSet<(int, Material)>();

        public static bool IsStorage(BuildingType t) => t == BuildingType.StorageYard || t == BuildingType.SealedStore || t == BuildingType.GasTank;

        public bool PlaceStorage(BuildingType type, int tileId, int slot)
        {
            if (!IsStorage(type) || !CanPlace(type, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(type), BuildTimes.Hours(type));
            storageBuildings[(tileId, slot)] = type;
            Occupy(tileId, FootprintFor(type, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        bool RemoveStorage(int tileId, int slot)
        {
            if (!storageBuildings.TryGetValue((tileId, slot), out BuildingType type))
                return false;
            Dismantle(tileId, slot, type);
            storageBuildings.Remove((tileId, slot));
            Vacate(tileId, slot);
            return true;
        }

        /// <summary>Finished storage buildings of a type on a tile.</summary>
        public int StorageCount(int tileId, BuildingType type)
        {
            int n = 0;
            foreach (var kv in storageBuildings)
                if (kv.Key.Tile == tileId && kv.Value == type && !Build.IsQueued(kv.Key.Tile, kv.Key.Slot)) n++;
            return n;
        }

        /// <summary>What a tile's depot can hold of one storage class, kg: infinity at the lander, else its piles, yards, stores or tanks plus what its own sites lay down.</summary>
        public double StorageCapacityKg(int tileId, StorageClass storageClass)
        {
            double? cap = ClassCapacityKg(PlaceId.Depot(tileId), storageClass);
            return cap ?? double.PositiveInfinity;
        }

        double? ClassCapacityKg(PlaceId place, StorageClass storageClass)
        {
            if (place.Kind == PlaceKind.BufferOut)   // a plant's output buffer: a little of each class
                return storageClass == StorageClass.BulkSolid ? PlantOutputBulkKg
                     : storageClass == StorageClass.PressurizedGas ? PlantOutputGasKg : PlantOutputHazardousKg;
            if (place.Kind != PlaceKind.Depot)
                return null;   // cargo and plant input buffers hold what they are given
            // The lander holds every solid and liquid (its own cargo), but no gas: gas needs a tank.
            if (place.Id == BaseTileId && storageClass != StorageClass.PressurizedGas)
                return null;
            int tile = place.Id;
            double cap;
            switch (storageClass)
            {
                case StorageClass.BulkSolid:
                    cap = OpenPileCapacityKg + StorageCount(tile, BuildingType.StorageYard) * StorageSpecs.YardCapacityKg;
                    break;
                case StorageClass.PressurizedGas:
                    cap = StorageCount(tile, BuildingType.GasTank) * StorageSpecs.TankCapacityKg;
                    break;
                default:   // sealed liquids and hazardous solids
                    cap = StorageCount(tile, BuildingType.SealedStore) * StorageSpecs.SealedStoreCapacityKg;
                    break;
            }
            if (double.IsPositiveInfinity(cap))
                return null;
            return cap + SiteLaydownKg(tile, storageClass);
        }

        /// <summary>What the sites on a tile still need of materials of one class: they may hold that much whatever the store.</summary>
        double SiteLaydownKg(int tileId, StorageClass storageClass)
        {
            double kg = 0.0;
            foreach (var site in Build.Sites())
            {
                if (site.Tile != tileId) continue;
                foreach (var (m, need) in Build.RemainingAt(site.Tile, site.Slot).Items())
                    if (MaterialCatalog.PropertiesOf(m).StorageClass == storageClass) kg += need;
            }
            return kg;
        }

        /// <summary>Kg of a material a tile's depot has room for now.</summary>
        public double RoomAt(int tileId, Material material) =>
            !LogisticsEnabled ? double.PositiveInfinity : inventories.FreeFor(PlaceId.Depot(tileId), material);

        /// <summary>Mining a material into a depot with no room says so once, instead of silently doing nothing.</summary>
        void ReportNoRoom(int tileId, Material material)
        {
            if (!storageFullReported.Add((tileId, material))) return;
            StorageClass cls = MaterialCatalog.PropertiesOf(material).StorageClass;
            string what = cls == StorageClass.PressurizedGas ? "a gas tank" : cls == StorageClass.BulkSolid ? "a storage yard" : "a sealed store";
            industrialReports.Add(TimeHours, "Mining", IndustrialBlocker.OutputStorageUnavailable,
                $"No room for {Resources.Materials.Name(material).ToLowerInvariant()} on tile {tileId}: build {what} there, or haul it away.");
        }

        void CaptureStorage(SimulationState state)
        {
            foreach (var kv in storageBuildings)
                state.Buildings.Add(new BuildingRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Type = kv.Value });
        }
    }
}

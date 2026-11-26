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
            if (IsConverter(type)) return PlaceConverter(type, tileId, slot);   // converters are placed through the same build bar path
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

        /// <summary>The store a material needs on a mine tile (KI-49): a yard for bulk solids, a sealed store for liquids and hazardous solids, a gas tank for gas.</summary>
        public BuildingType StoreFor(Material material)
        {
            StorageClass cls = MaterialCatalog.PropertiesOf(material).StorageClass;
            return cls == StorageClass.PressurizedGas ? BuildingType.GasTank : cls == StorageClass.BulkSolid ? BuildingType.StorageYard : BuildingType.SealedStore;
        }

        /// <summary>
        /// Why mining this material on this tile would waste the robots' hours: with logistics on, the ore lies in a pile on the mine tile and the tile has no room for it
        /// (needs a sealed store, a yard or a tank there), or null. The lander tile uses the colony stock and is never blocked.
        /// </summary>
        public string MiningBlocker(int tileId, Material material)
        {
            if (!LogisticsEnabled || tileId == BaseTileId || RoomAt(tileId, material) > 1e-9) return null;
            BuildingType store = StoreFor(material);
            string what = store == BuildingType.GasTank ? "a gas tank" : store == BuildingType.StorageYard ? "a storage yard" : "a sealed store";
            return $"needs {what} on tile {tileId}";
        }

        /// <summary>
        /// A warning for a mine order whose ore cannot be hauled home (KI-49, decided 2026-11-26): with logistics on, ore lies in a pile on the mine tile, and if not even a minimum load can be hauled from there
        /// to the base and back, it stays there. The order is still accepted (a player may be building at that outpost). Null when the ore can get home, or on the lander tile.
        /// </summary>
        public string MiningWarning(int tileId, Material material)
        {
            if (!LogisticsEnabled || tileId == BaseTileId || Routes == null) return null;
            return FeasiblePayloadKg(tileId, BaseTileId, BasePayloadKg) > 0.0 ? null : $"No haul route home: ore stays at tile {tileId}.";
        }

        /// <summary>Kg of a material a tile's depot has room for now.</summary>
        public double RoomAt(int tileId, Material material) =>
            !LogisticsEnabled ? double.PositiveInfinity : inventories.FreeFor(PlaceId.Depot(tileId), material);

        /// <summary>Mining a material into a depot with no room says so once, instead of silently doing nothing.</summary>
        /// <summary>A mine paused because its store at the mine tile is full: said once in the Operations log and the industrial reports.</summary>
        void ReportStoreFull(int tileId, Material material)
        {
            if (!storeFullPaused.Add((tileId, material))) return;
            string text = $"Store full at tile {tileId}: waiting for hauls.";
            industrialReports.Add(TimeHours, "Mining", IndustrialBlocker.OutputStorageUnavailable, text);
            Log($"Tile {tileId}", "Mine paused", 0.0, "", $"T{tileId}", $"The {Resources.Materials.Name(material).ToLowerInvariant()} mine on tile {tileId} is paused. {text}");
        }

        readonly HashSet<(int, Material)> storeFullPaused = new HashSet<(int, Material)>();

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

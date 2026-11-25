using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        /// <summary>Which slots each building covers (stored at its anchor slot).</summary>
        readonly FootprintMap footprintMap = new FootprintMap();

        public int FootprintSize(BuildingType type) => BuildingFootprints.SlotsFor(type, PanelTier);

        TileSite SiteOf(int tileId)
        {
            SlotCount(tileId);
            return sites[tileId];
        }

        public List<int> FootprintFor(BuildingType type, int tileId, int slot)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId))
                return new List<int>();
            return SiteOf(tileId).NearestSlots(slot, FootprintSize(type));
        }

        public bool CanPlace(BuildingType type, int tileId, int slot)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId))
                return false;
            TerrainType ground = GetTile(tileId).Terrain;
            if (type == BuildingType.PowerLine || type == BuildingType.HighVoltageLine || type == BuildingType.AcLine ? !(Terrains.IsBuildableLand(ground) || ground == TerrainType.ShallowOcean)
                : type == BuildingType.FloatingSolar ? !Terrains.AllowsFloating(ground)
                : type == BuildingType.TidalTurbine || type == BuildingType.WaveConverter || type == BuildingType.TidalBarrage ? ground != TerrainType.ShallowOcean
                : !Terrains.IsBuildableLand(ground))
                return false;
            if (!restoringState && !IsOpenGround(tileId))
                return false;   // a mountain or canyon must be opened first (director 2026-11-15)
            if (KnowledgeBlocker(type) != null)
                return false;   // the colony doesn't know enough yet (director 2026-11-14)
            foreach (int s in FootprintFor(type, tileId, slot))
                if (GetBuilding(tileId, s) != BuildingType.None) return false;
            if (!restoringState && WiredGrid && NeedsPower(type) && !IsPowered(tileId) && !IslandHasSource(tileId, true))
                return false;   // wired grid: stations and storage go where the grid reaches, or on an island that has a source (a battery there belongs to the island)
            if (!restoringState && type == BuildingType.WindTurbine && WindTurbineCount(tileId) > 0)
                return false;   // one turbine per tile: no wake model (game rule, TBD)
            if (!restoringState && type == BuildingType.TidalBarrage && BarrageBlocker(tileId) != null)
                return false;   // a barrage needs a bay with a big tide, and only one per tile
            if (!restoringState && RefineryBlocker(type, tileId, slot) != null)
                return false;   // refinery modules touch a Workshop (director 2026-11-15)
            return true;
        }

        public int AnchorOf(int tileId, int slot)
        {
            int a = footprintMap.AnchorOrSelf(tileId, slot);
            return GetBuildingAt(tileId, a) != BuildingType.None ? a : -1;
        }

        /// <summary>The anchor of the building covering a slot, or the slot itself (for calls that take any of its slots).</summary>
        int A(int tileId, int slot) => footprintMap.AnchorOrSelf(tileId, slot);

        public IReadOnlyList<int> FootprintAt(int tileId, int slot)
        {
            int a = AnchorOf(tileId, slot);
            if (a < 0) return new List<int>();
            return footprintMap.TryGetFootprint(tileId, a, out List<int> f) ? f : new List<int> { a };
        }

        void Occupy(int tileId, List<int> footprint)
        {
            footprintMap.Occupy(tileId, footprint);
        }

        void Vacate(int tileId, int anchor)
        {
            footprintMap.Vacate(tileId, anchor);
        }

        public bool PlaceChargingStation(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.ChargingStation, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.ChargingStation), BuildTimes.Hours(BuildingType.ChargingStation));
            stations.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.ChargingStation, tileId, slot));
            Settle((tileId, slot));
            return true;
        }
        /// <summary>Why a charging bay can't be added to this tile's storage yard, or null when it can.</summary>
        public string ChargingBayBlocker(int tileId)
        {
            if (StorageCount(tileId, BuildingType.StorageYard) < 1) return "A charging bay goes on a storage yard: build a yard here first.";
            if (HasChargingBay(tileId)) return "This depot already has a charging bay.";
            return null;
        }

        /// <summary>True when a storage yard's tile has a charger (a charging station finished or being built): a depot with a charging bay.</summary>
        public bool HasChargingBay(int tileId)
        {
            foreach (var s in stations) if (s.Tile == tileId) return true;
            return false;
        }

        /// <summary>Add a charging bay to a storage yard's tile: a charging station at the bay's own, lower cost.</summary>
        public bool PlaceChargingBay(int tileId, int slot)
        {
            if (ChargingBayBlocker(tileId) != null || !CanPlace(BuildingType.ChargingStation, tileId, slot))
                return false;
            StartSite(tileId, slot, Debug.FreeBuilding ? new MaterialBill() : BuildCosts.ChargingBay, BuildTimes.Hours(BuildingType.ChargingStation) * 0.5);
            stations.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.ChargingStation, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        readonly List<ManufacturingJob> jobs = new List<ManufacturingJob>();
        readonly IndustrialReportLog industrialReports = new IndustrialReportLog();
    }
}

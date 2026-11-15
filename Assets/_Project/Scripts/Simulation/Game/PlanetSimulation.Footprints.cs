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
            if (type == BuildingType.FloatingSolar ? !Terrains.AllowsFloating(ground) : !Terrains.IsBuildableLand(ground))
                return false;
            if (!restoringState && !IsOpenGround(tileId))
                return false;   // a mountain or canyon must be opened first (director 2026-11-15)
            if (KnowledgeBlocker(type) != null)
                return false;   // the colony doesn't know enough yet (director 2026-11-14)
            foreach (int s in FootprintFor(type, tileId, slot))
                if (GetBuilding(tileId, s) != BuildingType.None) return false;
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
            StartSite(tileId, slot, BuildBill(BuildingType.ChargingStation));
            stations.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.ChargingStation, tileId, slot));
            Settle((tileId, slot));
            return true;
        }
        readonly List<ManufacturingJob> jobs = new List<ManufacturingJob>();
        readonly IndustrialReportLog industrialReports = new IndustrialReportLog();
    }
}

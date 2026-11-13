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
        /// <summary>Covered slot (not the anchor) → its building's anchor slot.</summary>
        readonly Dictionary<(int Tile, int Slot), int> coveredBy = new Dictionary<(int, int), int>();
        /// <summary>Anchor → every slot the building covers (anchor first).</summary>
        readonly Dictionary<(int Tile, int Slot), List<int>> footprints = new Dictionary<(int, int), List<int>>();

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
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId) || GetTile(tileId).IsWater)
                return false;
            foreach (int s in FootprintFor(type, tileId, slot))
                if (GetBuilding(tileId, s) != BuildingType.None) return false;
            return true;
        }

        public int AnchorOf(int tileId, int slot)
        {
            int a = coveredBy.TryGetValue((tileId, slot), out int anchor) ? anchor : slot;
            return GetBuildingAt(tileId, a) != BuildingType.None ? a : -1;
        }

        /// <summary>The anchor of the building covering a slot, or the slot itself (for calls that take any of its slots).</summary>
        int A(int tileId, int slot) => coveredBy.TryGetValue((tileId, slot), out int anchor) ? anchor : slot;

        public IReadOnlyList<int> FootprintAt(int tileId, int slot)
        {
            int a = AnchorOf(tileId, slot);
            if (a < 0) return new List<int>();
            return footprints.TryGetValue((tileId, a), out List<int> f) ? f : new List<int> { a };
        }

        void Occupy(int tileId, List<int> footprint)
        {
            int anchor = footprint[0];
            footprints[(tileId, anchor)] = new List<int>(footprint);
            for (int i = 1; i < footprint.Count; i++)
                coveredBy[(tileId, footprint[i])] = anchor;
        }

        void Vacate(int tileId, int anchor)
        {
            if (!footprints.TryGetValue((tileId, anchor), out List<int> f))
                return;
            for (int i = 1; i < f.Count; i++)
                coveredBy.Remove((tileId, f[i]));
            footprints.Remove((tileId, anchor));
        }

        public bool PlaceChargingStation(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.ChargingStation, tileId, slot))
                return false;
            Build.Start(tileId, slot, BuildCostWh(BuildingType.ChargingStation), 0.0, false);
            stations.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.ChargingStation, tileId, slot));
            Settle((tileId, slot));
            return true;
        }
        readonly List<ManufacturingJob> jobs = new List<ManufacturingJob>();
        readonly IndustrialReportLog industrialReports = new IndustrialReportLog();
    }
}

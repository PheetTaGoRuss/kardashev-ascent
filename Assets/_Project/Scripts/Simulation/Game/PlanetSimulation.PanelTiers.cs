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
        /// <summary>Index into <see cref="PanelTiers.All"/> of what new panels are built as.</summary>
        public int PanelTier => PanelTiers.For(UnlockLevel);

        /// <summary>Ground one new panel covers on a tile, m²: the tier's size, capped at its footprint's share of the tile.</summary>
        public double NewPanelAreaM2(int tileId)
        {
            double area = PanelAreaM2 * PanelTiers.All[PanelTier].Scale;
            if (tileId >= 0 && tileId < TileCount)
                area = Math.Min(area, FootprintSize(BuildingType.SolarPanel) * tileAreaM2 / SlotCount(tileId));
            return area;
        }

        /// <summary>Ground the panel in a slot covers, m² (0 if there is none).</summary>
        public double PanelAreaAt(int tileId, int slot) => panels.TryGetValue((tileId, A(tileId, slot)), out var p) ? p.Area : 0.0;

        MaterialBill PanelCost(double areaM2) => BuildCosts.Panel(areaM2);

        bool CanPlacePanel(int tileId, int slot, double tilt, double azimuth) =>
            CanPlace(BuildingType.SolarPanel, tileId, slot) && PreviewPanel(tileId, tilt, azimuth).CanPlace;

        /// <summary>A saved panel's own slot is free and allowed (its footprint is put back from the save).</summary>
        bool CanRestorePanel(int tileId, int slot, double tilt, double azimuth, bool floating = false) =>
            tileId >= 0 && tileId < TileCount && slot >= 0 && slot < SlotCount(tileId) && GetBuilding(tileId, slot) == BuildingType.None
            && PreviewPanelCore(tileId, tilt, azimuth, floating).CanPlace;

        void AddPanel(int tileId, int slot, double tilt, double azimuth, double area)
        {
            panels[(tileId, slot)] = (tilt, azimuth, area);
            panelsPerTile[tileId] = PanelCount(tileId) + 1;
            GenerationW = ComputeGeneration() + RtgW;
        }

        public bool SetPanelAngles(int tileId, int slot, double tilt, double azimuth)
        {
            slot = A(tileId, slot);
            if (!panels.ContainsKey((tileId, slot)) || !PreviewPanelCore(tileId, tilt, azimuth, floatingPanels.Contains((tileId, slot))).CanPlace)
                return false;
            panels[(tileId, slot)] = (tilt, azimuth, panels[(tileId, slot)].Area);
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        public bool OrderPanelAngles(int tileId, int slot, double tilt, double azimuth)
        {
            slot = A(tileId, slot);
            bool floating = floatingPanels.Contains((tileId, slot));
            if (!panels.ContainsKey((tileId, slot)) || !PreviewPanelCore(tileId, tilt, azimuth, floating).CanPlace)
                return false;
            if (Build.IsQueued(tileId, slot) || floating)
            {   // not built yet, or floating (turned by raft crews at once)
                CancelAdjust(tileId, slot);
                return SetPanelAngles(tileId, slot, tilt, azimuth);   // not built yet: it is just built that way
            }
            int i = adjustOrders.FindIndex(o => o.Tile == tileId && o.Slot == slot);
            if (i >= 0)
                adjustOrders[i] = (tileId, slot, tilt, azimuth);
            else
                adjustOrders.Add((tileId, slot, tilt, azimuth));
            Settle(null);
            return true;
        }

        public bool TryGetPanelAngles(int tileId, int slot, out double tilt, out double azimuth)
        {
            slot = A(tileId, slot);
            bool found = panels.TryGetValue((tileId, slot), out var p);
            tilt = found ? p.Tilt : 0.0;
            azimuth = found ? p.Azimuth : 0.0;
            return found;
        }

        public bool TryGetAdjustOrder(int tileId, int slot, out double tilt, out double azimuth)
        {
            slot = A(tileId, slot);
            int i = adjustOrders.FindIndex(o => o.Tile == tileId && o.Slot == slot);
            tilt = i >= 0 ? adjustOrders[i].Tilt : 0.0;
            azimuth = i >= 0 ? adjustOrders[i].Azimuth : 0.0;
            return i >= 0;
        }

        public bool CancelAdjust(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (adjustOrders.RemoveAll(o => o.Tile == tileId && o.Slot == slot) == 0)
                return false;
            Workforce.Tasks.MarkCancelled(new WorkTask(TaskKind.Adjust, tileId, slot));
            Settle(null);
            return true;
        }
    }
}

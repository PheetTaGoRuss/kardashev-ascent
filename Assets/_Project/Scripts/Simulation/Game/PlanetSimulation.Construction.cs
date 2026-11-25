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
        public int ConstructionQueueCount => Build.QueueCount;

        /// <summary>
        /// Buildings take robot work hours (true, director 2026-11-14) or are finished as soon as their materials are in
        /// (false, as before; tests about other things use it, like <see cref="MovementEnabled"/>).
        /// </summary>
        public bool ConstructionTimeEnabled
        {
            get => Build.TimeEnabled;
            set => Build.TimeEnabled = value;
        }
        public MaterialBill ConstructionRemaining => Build.Remaining;
        public MaterialBill ConstructionShortfall => Build.Remaining.Minus(LogisticsEnabled ? inventories.Totals() : Materials);
        public MaterialBill ConstructionRemainingAt(int tileId, int slot) => Build.RemainingAt(tileId, A(tileId, slot));
        public double ConstructionMaterialShare(int tileId, int slot) => Build.MaterialShare(tileId, A(tileId, slot));
        public (double Done, double Total) ConstructionWork(int tileId, int slot) => Build.WorkAt(tileId, A(tileId, slot));
        public bool IsBeingBuilt(int tileId, int slot)
        {
            int a = A(tileId, slot);
            return Build.IsQueued(tileId, a) && Workforce.IsWorkedOnSite(new WorkTask(TaskKind.Build, tileId, a));
        }
        public bool IsUnderConstruction(int tileId, int slot) => Build.IsQueued(tileId, A(tileId, slot));
        public double ConstructionProgress(int tileId, int slot) => Build.Progress(tileId, A(tileId, slot));

        public MaterialBill BuildBill(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.SolarPanel: return PanelCost(PanelAreaM2 * PanelTiers.All[PanelTier].Scale);
                case BuildingType.Battery: return BuildCosts.Battery(BatteryUnitWh);
                case BuildingType.FloatingSolar: return PanelCost(PanelAreaM2 * PanelTiers.All[PanelTier].Scale).Times(FloatingCostFactor);
                default: return BuildingCatalog.Get(type)?.FixedBill ?? new MaterialBill();
            }
        }

        /// <summary>What a building cost in build parts before save version 26, Wh (to read old construction sites).</summary>
        double LegacyCostWh(BuildingType type, double areaM2)
        {
            switch (type)
            {
                case BuildingType.SolarPanel: return BuildCosts.LegacyPanelWh(EmbodiedWhPerM2, areaM2);
                case BuildingType.FloatingSolar: return BuildCosts.LegacyPanelWh(EmbodiedWhPerM2, areaM2) * FloatingCostFactor;
                case BuildingType.Battery: return BuildCosts.LegacyBatteryWh(BatteryUnitWh);
                case BuildingType.Workshop: return BuildCosts.LegacyWorkshopWh;
                case BuildingType.ChargingStation: return BuildCosts.LegacyChargingStationWh;
                case BuildingType.Relay: return BuildCosts.LegacyRelayWh;
                default: return 0.0;
            }
        }

        /// <summary>A site got all its parts: it starts working. A building finished by the command that placed it raises no event.</summary>
        void Finish(int tileId, int slot, bool raise)
        {
            if (batteries.Contains((tileId, slot)))
                AddBatteryCapacity(tileId, slot);
            else if (!stepping)
                GenerationW = ComputeGeneration() + RtgW;   // during a step, the next step counts it (keeps the step's books balanced)
            if (relays.Contains((tileId, slot)))
                RefreshNetwork();
            if (barrages.Contains((tileId, slot)))
                RefreshBarrageBlocks();
            if (powerLines.Contains((tileId, slot)) || highVoltageLines.Contains((tileId, slot)))
            {
                wiringStamp = double.NaN;
                flowDirty = true;
            }
            if (raise)
                BuildingCompleted?.Invoke(tileId, slot);
        }
    }
}

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
        public double BuildStockWh => Build.StockWh;
        public double BuildPowerW { get; private set; }
        public int ConstructionQueueCount => Build.QueueCount;
        public double ConstructionRemainingWh => Build.RemainingWh;
        public bool IsUnderConstruction(int tileId, int slot) => Build.IsQueued(tileId, A(tileId, slot));
        public double ConstructionProgress(int tileId, int slot) => Build.Progress(tileId, A(tileId, slot));

        public double BuildCostWh(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.SolarPanel: return PanelCost(PanelAreaM2 * PanelTiers.All[PanelTier].Scale);
                case BuildingType.Battery: return BuildCosts.Battery(BatteryUnitWh);
                default: return BuildingCatalog.Get(type)?.FixedPartsWh ?? 0.0;
            }
        }

        /// <summary>A site got all its parts: it starts working. A building finished by the command that placed it raises no event.</summary>
        void Finish(int tileId, int slot, bool raise)
        {
            if (batteries.Contains((tileId, slot)))
                Grid.Battery.AddCapacity(BatteryUnitWh);
            else
                GenerationW = ComputeGeneration() + RtgW;
            if (relays.Contains((tileId, slot)))
                RefreshNetwork();
            if (raise)
                BuildingCompleted?.Invoke(tileId, slot);
        }
    }
}

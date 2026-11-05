using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Everything needed to restore a <see cref="IPlanetSimulation"/> later. Public fields so Unity's JsonUtility can serialize it.</summary>
    [Serializable]
    public sealed class SimulationState
    {
        /// <summary>2 added build parts and construction sites; a version-1 save loads with the lander's parts and every building finished.</summary>
        public int Version = 2;
        public int TileCount;
        public int BaseTileId;
        public double TimeHours;
        public double BatteryStoredWh;
        public double ColonyDemandW;
        public double ColonyHoursSinceBrownout;
        public double ColonyAverageGenerationW;
        public double ColonyDayEnergyWh;
        public double ColonyDayClockHours;
        /// <summary>Build parts on hand, Wh.</summary>
        public double BuildStockWh;
        public List<BuildingRecord> Buildings = new List<BuildingRecord>();
    }

    /// <summary>One building in a saved game. Tilt/Azimuth (radians) are only used for panels.</summary>
    [Serializable]
    public sealed class BuildingRecord
    {
        public int Tile;
        public int Slot;
        public BuildingType Type;
        public double Tilt;
        public double Azimuth;
        /// <summary>True for a construction site still waiting for parts.</summary>
        public bool UnderConstruction;
        /// <summary>Parts already put into the site, Wh.</summary>
        public double PaidWh;
        /// <summary>The site's place in the construction queue (0 = next to finish).</summary>
        public int QueueOrder;
    }
}

using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Everything needed to restore a <see cref="IPlanetSimulation"/> later. Public fields so Unity's JsonUtility can serialize it.</summary>
    [Serializable]
    public sealed class SimulationState
    {
        public int Version = 1;
        public int TileCount;
        public int BaseTileId;
        public double TimeHours;
        public double BatteryStoredWh;
        public double ColonyDemandW;
        public double ColonyHoursSinceBrownout;
        public double ColonyAverageGenerationW;
        public double ColonyDayEnergyWh;
        public double ColonyDayClockHours;
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
    }
}

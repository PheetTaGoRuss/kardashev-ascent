using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Everything needed to restore a <see cref="IPlanetSimulation"/> later. Public fields so Unity's JsonUtility can serialize it.</summary>
    [Serializable]
    public sealed class SimulationState
    {
        /// <summary>2 added build parts and construction sites; a version-1 save loads with the lander's parts and every building finished. 7 added robots.</summary>
        public int Version = 7;
        /// <summary>Planet id from planets.json (version 3); empty in older saves.</summary>
        public string PlanetId = "";
        /// <summary>Milestones reached and the day-average power behind them (version 4; 0 in older saves).</summary>
        public int MilestonesReached;
        public double DayAveragePowerW;
        public double DayEnergyWh;
        public double DayHours;
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
        /// <summary>Recent power samples, oldest first, and the totals since landing (missing in older saves: they start empty).</summary>
        public List<PowerSample> History = new List<PowerSample>();
        public double HistoryGeneratedWh;
        public double HistoryDemandWh;
        public double HistoryServedWh;
        public double HistoryBuildWh;
        public double HistoryBrownoutHours;
        public double HistoryHours;
        public List<BuildingRecord> Buildings = new List<BuildingRecord>();
        /// <summary>What the colony has measured about its sky (version 6; empty in older saves).</summary>
        public Solar.SkyNotebookState Notebook = new Solar.SkyNotebookState();
        /// <summary>Robots and groups (version 7; older saves start with one General robot at the base).</summary>
        public Robots.WorkforceState Workforce = new Robots.WorkforceState();
        /// <summary>Finished buildings waiting for a robot to take them down (version 7).</summary>
        public List<Robots.SalvageOrderRecord> SalvageOrders = new List<Robots.SalvageOrderRecord>();
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
        /// <summary>Ground a panel covers, m² (version 5; 0 in older saves = a base panel field).</summary>
        public double AreaM2;
    }
}

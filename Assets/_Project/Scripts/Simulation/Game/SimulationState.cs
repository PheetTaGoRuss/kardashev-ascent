using KardashevAscent.Simulation.Resources;
using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Everything needed to restore a <see cref="IPlanetSimulation"/> later. Public fields so Unity's JsonUtility can serialize it.</summary>
    [Serializable]
    public sealed class SimulationState
    {
        /// <summary>2 added build parts and construction sites; a version-1 save loads with the lander's parts and every building finished. 7 added robots; 8 their batteries and condition; 9 resources; 10 the Workshop; 11 programs (facility, ranking, queue); 12 movement; 13 critical damage (stage A); 14 reservations and tasks to go back to (stage B); 15 battery ageing by depth, maintenance rules (stage D); 16 building footprints. 23 industry reports. 29 experiments. 30 survey puzzles answered one material at a time. 31 stand-ins (aluminium, sodium) in building bills. 32 deposits run out (what was mined from each). 33 mountains and canyons opened (tunnel, blast, bridge). 34 refinery modules, batches, queue and auto targets.</summary>
        public const int CurrentVersion = 43;

        /// <summary>Wired grid (version 35): generators and stations need power lines to the lander tile. Older saves are not wired.</summary>
        public bool WiredGrid;

        /// <summary>The version this state was saved with (<see cref="CurrentVersion"/> for a new one).</summary>
        public int Version = CurrentVersion;
        /// <summary>Planet id from planets.json (version 3); empty in older saves.</summary>
        public string PlanetId = "";
        /// <summary>
        /// The seed of the map this colony was played on (version 28: every new game gets a random map; director
        /// 2026-11-14). 0 in older saves: the planet's own map.
        /// </summary>
        public int TerrainSeed;
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
        /// <summary>Build parts on hand, Wh (before version 26; buildings now take materials).</summary>
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
        /// <summary>37: buildings wired to the grid by the player. A save from before it wires every building it has.</summary>
        public List<Robots.SalvageOrderRecord> WiredBuildings = new List<Robots.SalvageOrderRecord>();
        /// <summary>43: each power line's stable electrical connection id, and its condition and open state when not whole. Older saves get ids on load.</summary>
        public List<ElectricalLinkRecord> ElectricalLinks = new List<ElectricalLinkRecord>();
        /// <summary>43: the next electrical connection id (0 in older saves).</summary>
        public int NextElectricalConnectionId;
        public List<Robots.SalvageOrderRecord> SalvageOrders = new List<Robots.SalvageOrderRecord>();
        /// <summary>Panels waiting for a robot to turn them to new angles (version 17; older saves have none).</summary>
        public List<Robots.AdjustOrderRecord> AdjustOrders = new List<Robots.AdjustOrderRecord>();
        /// <summary>Solar parameters measured (version 18; older saves: none) and measurements under way.</summary>
        public List<int> MeasuredParameters = new List<int>();
        public List<MeasureOrderRecord> MeasureOrders = new List<MeasureOrderRecord>();
        /// <summary>Latitude puzzles waiting for the player (version 19).</summary>
        public List<Solar.LatitudePuzzle> LatitudePuzzles = new List<Solar.LatitudePuzzle>();
        /// <summary>Light puzzles waiting, and parameters measured roughly (version 20).</summary>
        public List<Solar.ParameterPuzzle> ParameterPuzzles = new List<Solar.ParameterPuzzle>();
        public List<int> RoughParameters = new List<int>();
        /// <summary>Core samples waiting for the player's grade estimate, and tiles surveyed only roughly (version 21).</summary>
        public List<Resources.SurveyPuzzle> SurveyPuzzles = new List<Resources.SurveyPuzzle>();
        /// <summary>Survey and measurement results cut-off robots are carrying (version 25; older saves: none).</summary>
        public List<HeldFinding> HeldFindings = new List<HeldFinding>();
        public List<int> RoughSurveys = new List<int>();
        /// <summary>Resources (version 9): surveyed tiles, orders, materials on hand and work done on field tasks.</summary>
        public List<int> SurveyedTiles = new List<int>();
        public List<int> SurveyOrders = new List<int>();
        public List<MiningOrderRecord> MiningOrders = new List<MiningOrderRecord>();
        /// <summary>What was mined from each deposit, kg (version 32; older saves: nothing, every deposit full).</summary>
        public List<MinedRecord> Mined = new List<MinedRecord>();
        /// <summary>Barrier tiles opened or being opened (version 33; older saves: none).</summary>
        public List<PassageRecord> Passages = new List<PassageRecord>();
        /// <summary>Refining (version 34): batches running in refinery modules, the order queue, auto targets.</summary>
        public List<RefineBatchRecord> RefineBatches = new List<RefineBatchRecord>();
        public List<RefineProcess> RefineQueue = new List<RefineProcess>();
        public List<AutoRefineRecord> AutoRefine = new List<AutoRefineRecord>();
        public List<MaterialRecord> MaterialsKg = new List<MaterialRecord>();
        /// <summary>38: logistics on (stock kept per place; sites draw from their own tile). An older save keeps the one stock in the lander depot.</summary>
        public bool LogisticsEnabled;
        /// <summary>38: every place but the lander depot (which is <see cref="MaterialsKg"/>), with what it holds.</summary>
        public List<InventoryRecord> Inventories = new List<InventoryRecord>();
        /// <summary>38: what hauling has cost so far.</summary>
        public double HaulWh;
        public List<HaulLedgerRecord> HaulLedger = new List<HaulLedgerRecord>();
        /// <summary>39: hauls under way and stock targets.</summary>
        public int NextHaulId = 1;
        public List<HaulOrderRecord> HaulOrders = new List<HaulOrderRecord>();
        public List<StockTargetRecord> StockTargets = new List<StockTargetRecord>();
        /// <summary>40: plants' byproduct policies (only Store is saved; Vent is the default), gas released so far, and the waste heat of finished batches.</summary>
        public List<PlantPolicyRecord> PlantPolicies = new List<PlantPolicyRecord>();
        public List<MaterialRecord> VentedKg = new List<MaterialRecord>();
        public double IndustrialWasteHeatKWh;
        /// <summary>41: the rulebook with every version, the plants' version, which rule holds for whom, the overrides rules set, paused plants and the Operations log.</summary>
        public List<Operations.RulebookVersion> RulebookVersions = new List<Operations.RulebookVersion>();
        public int PlantRulebookVersion;
        public List<RuleStateRecord> RuleStates = new List<RuleStateRecord>();
        public List<RuleOverrideRecord> RuleOverrides = new List<RuleOverrideRecord>();
        public List<PausedPlantRecord> PausedPlants = new List<PausedPlantRecord>();
        public List<Operations.OperationRecord> OperationsLog = new List<Operations.OperationRecord>();
        public int NextOperationId = 1;
        /// <summary>42: the last week of mass flows (mined, hauled, plants) for the Flowsheet view.</summary>
        public List<Logistics.FlowEntry> FlowEntries = new List<Logistics.FlowEntry>();
        public List<FieldWorkRecord> FieldWork = new List<FieldWorkRecord>();
        /// <summary>Workshop jobs in order (version 10).</summary>
        public List<Robots.JobRecord> Jobs = new List<Robots.JobRecord>();
        /// <summary>Recent industry reports: what a process did or why it is waiting (version 23).</summary>
        public List<Resources.IndustrialReport> IndustrialReports = new List<Resources.IndustrialReport>();
        /// <summary>Saved panel angle layouts (version 24; empty in older saves).</summary>
        public List<AnglePreset> AnglePresets = new List<AnglePreset>();
        public int NextAnglePresetId = 1;
        /// <summary>Experiments done and waiting at a Research Station (version 29; older saves: none).</summary>
        public List<int> KnownExperiments = new List<int>();
        public List<ExperimentOrderRecord> ExperimentOrders = new List<ExperimentOrderRecord>();
    }

    [Serializable]
    public sealed class ExperimentOrderRecord
    {
        public Planet.ExperimentKind Kind;
        public int Tile;
    }

    [Serializable]
    public sealed class MeasureOrderRecord
    {
        public Solar.SolarParameter Parameter;
        public int Tile;
    }

    [Serializable]
    public sealed class MiningOrderRecord
    {
        public int Tile;
        public Resources.Material Material;
    }

    [Serializable]
    public sealed class RefineBatchRecord
    {
        public int Tile;
        public int Slot;
        public RefineProcess Process;
        public double DoneHours;
    }

    [Serializable]
    public sealed class AutoRefineRecord
    {
        public RefineProcess Process;
        public double KeepKg;
    }

    [Serializable]
    public sealed class PassageRecord
    {
        public int Tile;
        public PassageKind Kind;
        /// <summary>True: open; false: ordered and being worked on.</summary>
        public bool Open;
    }

    [Serializable]
    public sealed class MinedRecord
    {
        public int Tile;
        public Resources.Material Material;
        public double Kg;
    }

    [Serializable]
    public sealed class InventoryRecord
    {
        public Logistics.PlaceKind Kind;
        public int Id;
        public List<MaterialRecord> Stock = new List<MaterialRecord>();
    }

    [Serializable]
    public sealed class RuleStateRecord
    {
        public int RuleId;
        public bool IsPlant;
        public int Subject;
        public bool Active;
        public double Started;
    }

    [Serializable]
    public sealed class RuleOverrideRecord
    {
        public int RobotId;
        public Robots.TaskKind Kind;
        public Robots.TaskPriority Priority;
    }

    [Serializable]
    public sealed class PausedPlantRecord
    {
        public int Tile;
        public int Slot;
    }

    [Serializable]
    public sealed class PlantPolicyRecord
    {
        public int Tile;
        public int Slot;
        public Resources.ByproductPolicy Policy;
    }

    [Serializable]
    public sealed class HaulOrderRecord
    {
        public int Id;
        public int FromTile;
        public int ToTile;
        public Resources.Material Material;
        public double Kg;
        public int Stage;
        public int RobotId;
    }

    [Serializable]
    public sealed class StockTargetRecord
    {
        public int Tile;
        public Resources.Material Material;
        public double Kg;
    }

    [Serializable]
    public sealed class HaulLedgerRecord
    {
        public Resources.Material Material;
        public double Kg;
        public double KgKm;
    }

    [Serializable]
    public sealed class MaterialRecord
    {
        public Resources.Material Material;
        public double Kg;
    }

    [Serializable]
    public sealed class FieldWorkRecord
    {
        public Robots.TaskKind Kind;
        public int Tile;
        public int Slot;
        public double Hours;
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
        /// <summary>True for a construction site still waiting for materials.</summary>
        public bool UnderConstruction;
        /// <summary>Parts already put into the site, Wh (before version 26).</summary>
        public double PaidWh;
        /// <summary>Share of the site's bill of materials already put in, 0–1 (version 26).</summary>
        public double PaidShare;
        /// <summary>Robot work already put into the site, hours (version 27; older saves: as far as its materials).</summary>
        public double WorkDoneHours;
        /// <summary>The site's place in the construction queue (0 = next to finish).</summary>
        public int QueueOrder;
        /// <summary>Ground a panel covers, m² (version 5; 0 in older saves = a base panel field).</summary>
        public double AreaM2;
        /// <summary>Aluminium or sodium in place of copper or lithium in its bill (version 31; older saves: none).</summary>
        public StandIn StandIns;
        /// <summary>For a power line: the neighbouring tile it links this tile to (version 36; -1 otherwise).</summary>
        public int LinkTo = -1;
        /// <summary>The other slots the building covers (version 16; empty in older saves: one slot).</summary>
        public List<int> Covered = new List<int>();
    }

    /// <summary>A power line's electrical identity (version 43).</summary>
    [Serializable]
    public sealed class ElectricalLinkRecord
    {
        public int Tile;
        public int Slot;
        public int ConnectionId;
        public double Condition = 1.0;
        public bool Open;
    }
}

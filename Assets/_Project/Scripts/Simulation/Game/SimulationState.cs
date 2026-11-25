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
        public const int CurrentVersion = 67;   // 67: power load order (no state, behaviour change); 66: comminution energy counter; 65: mining counters (rock moved, tailings; deposits are bigger and the grade falls); 64: rebuild jobs and recovered wrecks; 63: robot body temperature (hibernation and cold soak); 62: robot rescue orders; 61: colony knowledge store; 60: storage chemistry (rust, hydroxides, melt, brine) on; 59: robot wrecks; 58: 58: wind turbine and wind mast (new building and experiment); 57: haul go-anyway after a storm forecast; 56: weather on; 55: log priorities; 54: day-night temperature; 51: experiment fit puzzles and range shares; 52: assay sampling in progress; 53: contour routing

        /// <summary>Wired grid (version 35): generators and stations need power lines to the lander tile. Older saves are not wired.</summary>
        public bool WiredGrid;

        /// <summary>Version 53: routes weigh climb and heater against time (older saves: time only).</summary>
        public bool ContourRouting;

        /// <summary>Version 54: the air temperature swings between day and night (older saves: the yearly temperature only).</summary>
        public bool DiurnalTemperature;

        /// <summary>Version 56: day-to-day weather (older saves: none).</summary>
        public bool WeatherOn;
        /// <summary>Version 60: the chemistry of stored materials is on (older saves: off, so their stock does not change).</summary>
        public bool ChemistryOn;
        /// <summary>Version 61: the colony knowledge store (K1) and whether it is on.</summary>
        public bool KnowledgeOn;
        /// <summary>Version 62: rescues under way.</summary>
        public int NextRescueId = 1;
        public List<RescueRecord> Rescues = new List<RescueRecord>();
        public List<Knowledge.Observation> Observations = new List<Knowledge.Observation>();

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
        /// <summary>44: the saved circuit blueprints, circuits installed in charging stations, and the next instance id. Older saves have none.</summary>
        public List<Circuits.CircuitBlueprint> CircuitLibrary = new List<Circuits.CircuitBlueprint>();
        public List<CircuitInstanceRecord> CircuitInstances = new List<CircuitInstanceRecord>();
        public int NextCircuitInstanceId;
        /// <summary>45: tiles whose line has a meter, and lines inspected (with the hour of the inspection). Older saves have none.</summary>
        public List<int> MeteredLines = new List<int>();
        public List<InspectedLineRecord> InspectedLines = new List<InspectedLineRecord>();
        /// <summary>46: electrical jobs waiting for a robot (inspect a line, repair a line, install a circuit). Older saves have none.</summary>
        public List<ElectricalOrderRecord> ElectricalOrders = new List<ElectricalOrderRecord>();
        /// <summary>47: fuses and breakers fitted to links (by the two tiles). Older saves have none.</summary>
        public List<LineProtectionRecord> LineProtections = new List<LineProtectionRecord>();
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
        /// <summary>59: robot wrecks (the stock lies in the inventories as Wreck places).</summary>
        public int NextWreckId = 1;
        /// <summary>Version 65: rock moved and tailings left by mining so far, kg.</summary>
        public double RockMovedKg, TailingsKg, MiningWorkHours, ComminutionWh;
        public List<WreckRecord> Wrecks = new List<WreckRecord>();
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
        /// <summary>Version 51: experiments whose fit puzzle is waiting, and the range share each done experiment keeps (older saves: exact).</summary>
        public List<int> ExperimentPuzzles = new List<int>();
        public List<ExperimentShareRecord> ExperimentShares = new List<ExperimentShareRecord>();
        /// <summary>Version 52: sampling that costs robot time (the ore assay), in progress.</summary>
        public List<ExperimentSamplingRecord> ExperimentSampling = new List<ExperimentSamplingRecord>();
    }

    [Serializable]
    public sealed class ExperimentSamplingRecord
    {
        public Planet.ExperimentKind Kind;
        public double Hours;
        public double Share;
    }

    [Serializable]
    public sealed class ExperimentShareRecord
    {
        public Planet.ExperimentKind Kind;
        public double Share;
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
        /// <summary>Version 57: the player overrode a forecast-storm wait.</summary>
        public bool GoAnyway;
        /// <summary>Version 59: a recover-wreck order loads from this wreck.</summary>
        public int WreckId;
    }

    [Serializable]
    public sealed class RescueRecord
    {
        public int Id, StrandedId, RescuerId, Stage, Shelter;
        public RescueMethod Method;
        /// <summary>Negative: no deadline.</summary>
        public double DeadlineHours, PlannedWh, HandlingLeftHours, TransferWh;
    }

    [Serializable]
    public sealed class WreckRecord
    {
        public int Id, Tile, RobotId;
        public Robots.RobotKind Kind;
        public Robots.DeathCause Cause;
        public double PeakC;
        public bool FindingsLost;
        /// <summary>Version 64: the metals are home and the wreck waits to be rebuilt.</summary>
        public bool Recovered;
        public List<MaterialRecord> Bill = new List<MaterialRecord>();
        public List<HeldFinding> Findings = new List<HeldFinding>();
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
        /// <summary>48: the class of an AC line, 33000 or 500000 V (0 for anything else; an AC line with 0 loads as 500000).</summary>
        public int AcClass;
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

    /// <summary>A circuit installed in the charging station on a slot (version 44).</summary>
    [Serializable]
    public sealed class CircuitInstanceRecord
    {
        public int Tile;
        public int Slot;
        public Circuits.CircuitInstance Instance;
    }

    /// <summary>A line inspected at a game hour (version 45).</summary>
    [Serializable]
    public sealed class InspectedLineRecord
    {
        public int Tile;
        public double AtHours;
    }

    /// <summary>An electrical job waiting for a robot (version 46).</summary>
    [Serializable]
    public sealed class ElectricalOrderRecord
    {
        public int Action;
        public int Tile;
        public int Slot;
        public int Arg;
    }

    /// <summary>A fuse or breaker fitted to a link (version 47).</summary>
    [Serializable]
    public sealed class LineProtectionRecord
    {
        public int TileA;
        public int TileB;
        public int Kind;
    }
}

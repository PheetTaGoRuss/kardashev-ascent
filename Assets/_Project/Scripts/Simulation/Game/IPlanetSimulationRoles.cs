using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Energy;
using System;
using System.Numerics;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    // The simulation interface, split by what a caller wants. `IPlanetSimulation` inherits all of them,
    // so existing code keeps working; new code can depend on just the role it needs.

    /// <summary>Where and when: the tile layout, the clock and the star.</summary>
    public interface IPlanetReadout
    {
        /// <summary>Tile layout; tile ids are indices into it (0 .. TileCount − 1).</summary>
        HexSphere Sphere { get; }

        int TileCount { get; }

        /// <summary>Static facts about one tile (position, terrain).</summary>
        TileInfo GetTile(int tileId);

        /// <summary>The tile where the colony starts; day/night events follow this tile.</summary>
        int BaseTileId { get; }

        /// <summary>Hours since landing.</summary>
        double TimeHours { get; }

        /// <summary>Length of a solar day in hours; 0 for a tidally locked planet.</summary>
        double DayLengthHours { get; }

        double YearLengthHours { get; }

        /// <summary>Tilt of the spin axis against the orbit, radians.</summary>
        double ObliquityRad { get; }

        /// <summary>Unit vector from the planet's centre toward the star, in the planet frame.</summary>
        Vector3 StarDirection { get; }

        /// <summary>True while the star is above the horizon at <see cref="BaseTileId"/>.</summary>
        bool IsDayAtBase { get; }
    }

    /// <summary>The colony power grid, the Kardashev numbers and milestones (read only).</summary>
    public interface IEnergyReadout
    {
        /// <summary>Power being generated now, W.</summary>
        double GenerationW { get; }

        /// <summary>Power the colony needs now, W. It grows toward <see cref="DemandTargetW"/> (see ColonyDemand).</summary>
        double DemandW { get; }

        /// <summary>What the colony's demand is growing toward: a share of its average generation, W.</summary>
        double DemandTargetW { get; }

        /// <summary>True while the colony is growing (a day with no brownout and room to grow).</summary>
        bool IsColonyGrowing { get; }

        /// <summary>Game hours since the last brownout ended (the colony grows only after a full planet day without one).</summary>
        double HoursSinceBrownout { get; }

        /// <summary>Recent power samples and totals since landing, for the colony overview.</summary>
        PowerHistory History { get; }

        double BatteryStoredWh { get; }

        double BatteryCapacityWh { get; }

        /// <summary>Of <see cref="GenerationW"/>, the part from the lander's RTGs, W.</summary>
        double RtgW { get; }

        /// <summary>Battery power now: + charging, − discharging, W.</summary>
        double BatteryPowerW { get; }

        /// <summary>Surplus thrown away because the battery is full or at its power limit, W.</summary>
        double CurtailedW { get; }

        /// <summary>True while demand is not being met (generation + battery fall short).</summary>
        bool IsBrownout { get; }

        /// <summary>Power actually used (loads, battery charging, build parts; not curtailed), W. K is computed from it.</summary>
        double KardashevPowerW { get; }

        /// <summary>Absolute Kardashev K = (log₁₀ P − 6) / 10 of <see cref="KardashevPowerW"/>.</summary>
        double KardashevK { get; }

        /// <summary>
        /// Planet K: K with this planet's target counted as Type I (1 at <see cref="TypeITargetW"/>).
        /// A game index; show it next to absolute K, never instead of it.
        /// </summary>
        double PlanetK { get; }

        /// <summary>This planet's Type I target power, W (10¹⁶ W × S·R² / S⊕·R⊕²; a game rule).</summary>
        double TypeITargetW { get; }

        /// <summary>Used power averaged over the last full day (24 h on a tidally locked planet), W. Milestones use it.</summary>
        double DayAveragePowerW { get; }

        /// <summary>Highest milestone reached (0–10). Milestone n is planet K n × 0.1; 10 is the target.</summary>
        int MilestonesReached { get; }
    }

    /// <summary>Construction sites and what buildings cost in materials (read only).</summary>
    public interface IConstructionReadout
    {
        /// <summary>
        /// Materials one building of this type takes (director, 2026-11-14: buildings are built from materials, not
        /// power). A robot brings them from the colony's stock while it works on the site.
        /// </summary>
        Resources.MaterialBill BuildBill(BuildingType type);

        /// <summary>
        /// What one building of this type would take if placed now: <see cref="BuildBill"/> with aluminium for copper or
        /// sodium for lithium when the colony has no copper or lithium for it and no known ore (director 2026-11-15).
        /// </summary>
        Resources.MaterialBill BillToBuild(BuildingType type);

        /// <summary>The stand-ins the building or site at this slot was made with.</summary>
        Resources.StandIn StandInsAt(int tileId, int slot);

        /// <summary>Grid power the Workshop's jobs draw now, W.</summary>
        double WorkshopPowerW { get; }

        /// <summary>Construction sites waiting for materials or a robot.</summary>
        int ConstructionQueueCount { get; }

        /// <summary>Materials still needed to finish every site.</summary>
        Resources.MaterialBill ConstructionRemaining { get; }

        /// <summary>What the sites still need beyond the stock on hand (mine or refine these).</summary>
        Resources.MaterialBill ConstructionShortfall { get; }

        /// <summary>What the site in a slot still needs (empty for a finished building or an empty slot).</summary>
        Resources.MaterialBill ConstructionRemainingAt(int tileId, int slot);

        /// <summary>Share of a site's materials delivered, 0–1 (1 for no site).</summary>
        double ConstructionMaterialShare(int tileId, int slot);

        /// <summary>Robot work done and needed on a site, hours (construction takes time; director 2026-11-14).</summary>
        (double Done, double Total) ConstructionWork(int tileId, int slot);

        /// <summary>True while a robot is at work on the site in a slot (for the building animation).</summary>
        bool IsBeingBuilt(int tileId, int slot);

        /// <summary>True while the building in a slot is a site waiting for materials (it does nothing yet).</summary>
        bool IsUnderConstruction(int tileId, int slot);

        /// <summary>How far the site in a slot is built, 0–1; 1 for a finished building or an empty slot.</summary>
        double ConstructionProgress(int tileId, int slot);
    }

    /// <summary>Placing, turning and removing buildings, and asking what stands where.</summary>
    public interface IBuildingCommands
    {
        /// <summary>What new panels are built as: an index into <see cref="PanelTiers.All"/> (field, farm, park, array).</summary>
        int PanelTier { get; }

        /// <summary>Ground one new panel would cover on this tile, m² (the tier's size, capped at the slot's share of the tile).</summary>
        double NewPanelAreaM2(int tileId);

        /// <summary>Ground the panel in a slot covers, m², or 0. Built panels keep the size they were built at.</summary>
        double PanelAreaAt(int tileId, int slot);

        /// <summary>
        /// Rings of building slots on every tile, laid out by <see cref="TileSite.Create"/> to cover the whole tile
        /// (a hexagonal tile holds 3R(R+1)+1 slots). One panel per slot.
        /// </summary>
        int SiteRings { get; }

        /// <summary>Building slots on a tile (61 on a hexagon with the default 4 rings; pentagons differ).</summary>
        int SlotCount(int tileId);

        /// <summary>
        /// Build a panel in one slot of a tile. Returns false (and changes nothing) if the slot cannot take one.
        /// The panel starts as a construction site; a robot brings its <see cref="IConstructionReadout.BuildBill"/> of materials.
        /// </summary>
        bool PlacePanel(int tileId, int slot, double tilt, double azimuth);

        /// <summary>Remove the panel in a slot. Returns false if there was none.</summary>
        bool RemovePanel(int tileId, int slot);

        bool HasPanel(int tileId, int slot);

        /// <summary>Panels built on a tile (all slots).</summary>
        int PanelCount(int tileId);

        /// <summary>What stands in a slot.</summary>
        BuildingType GetBuilding(int tileId, int slot);

        /// <summary>Storage added by one battery building, Wh.</summary>
        double BatteryUnitWh { get; }

        /// <summary>Build a battery in an empty land slot (adds <see cref="BatteryUnitWh"/> to the colony battery).</summary>
        bool PlaceBattery(int tileId, int slot);

        /// <summary>Batteries built on a tile.</summary>
        int BatteryCount(int tileId);

        /// <summary>
        /// Remove whatever stands in a slot (panel or battery). Returns false if it was empty.
        /// A site gives back the parts put into it; a finished building gives back half its cost.
        /// </summary>
        bool RemoveBuilding(int tileId, int slot);

        /// <summary>Turn a built panel (or a panel site) to new angles; free, nothing is rebuilt. Returns false if no panel is there or the angles are not allowed.</summary>
        bool SetPanelAngles(int tileId, int slot, double tilt, double azimuth);

        /// <summary>
        /// Ask a robot to turn a built panel to new angles (radians). The panel keeps its old angles until a robot
        /// gets there. A site not yet built just takes the new angles. Asking again replaces the order.
        /// </summary>
        bool OrderPanelAngles(int tileId, int slot, double tilt, double azimuth);

        /// <summary>The angles a panel is waiting to be turned to, if a robot has been asked to.</summary>
        bool TryGetAdjustOrder(int tileId, int slot, out double tilt, out double azimuth);

        bool CancelAdjust(int tileId, int slot);

        /// <summary>A panel's angles now (radians), if a panel stands there.</summary>
        bool TryGetPanelAngles(int tileId, int slot, out double tilt, out double azimuth);

        /// <summary>Build a Robot Workshop in an empty land slot (a construction site until a robot finishes it).</summary>
        bool PlaceWorkshop(int tileId, int slot);

        /// <summary>Build solar panels on pontoons in a slot of a shallow-sea or wetland tile (1.5 times the cost of a land panel, cooler cells).</summary>
        bool PlaceFloatingPanel(int tileId, int slot, double tilt, double azimuth);

        /// <summary>How many slots a new building of this type covers (solar panels: by tier).</summary>
        int FootprintSize(BuildingType type);

        /// <summary>The slots a new building of this type would cover with its anchor at <paramref name="slot"/>.</summary>
        System.Collections.Generic.List<int> FootprintFor(BuildingType type, int tileId, int slot);

        /// <summary>Would a new building of this type fit with its anchor at <paramref name="slot"/> (all its slots free land)?</summary>
        bool CanPlace(BuildingType type, int tileId, int slot);

        /// <summary>The slots the building in this slot covers (its anchor first); empty if the slot is empty.</summary>
        System.Collections.Generic.IReadOnlyList<int> FootprintAt(int tileId, int slot);

        /// <summary>The anchor slot of the building covering this slot (where it is stored), or −1 if the slot is empty.</summary>
        int AnchorOf(int tileId, int slot);

        /// <summary>Build a robot charging station in an empty land slot (a construction site until a robot finishes it).</summary>
        bool PlaceChargingStation(int tileId, int slot);

        /// <summary>A network relay on an empty slot (stage E). One per tile is enough: coverage is per tile.</summary>
        bool PlaceRelay(int tileId, int slot);

        /// <summary>A Research Station on an empty land slot: where measurements and experiments are done.</summary>
        bool PlaceResearchStation(int tileId, int slot);

        /// <summary>What the colony still has to find out before it may build this, or null (knowledge gates buildings).</summary>
        string KnowledgeBlocker(BuildingType type);
    }

    /// <summary>The robot workforce: orders, movement and the work waiting.</summary>
    public interface IRobotCommands
    {
        /// <summary>
        /// The colony's robots and groups. A construction site only takes parts while a robot works on it; robots take
        /// work by priority. Settings changed here apply at the next tick or <see cref="RefreshWork"/>.
        /// </summary>
        Robots.Workforce Workforce { get; }

        /// <summary>Power going into robot batteries now, W (it counts as used power).</summary>
        double RobotChargingW { get; }

        /// <summary>Send a robot to drive to a tile (hybrid movement, spec §10). False if it can't go (busy charging, no route).</summary>
        bool SendRobot(int robotId, int tileId);

        /// <summary>Driving hours between two tiles (∞ if there is no land route).</summary>
        double DriveHours(int fromTile, int toTile);

        /// <summary>Every open task now (sites, salvage and survey orders, mining, Workshop jobs), in a stable order.</summary>
        System.Collections.Generic.IReadOnlyList<Robots.WorkTask> OpenWork();
        /// <summary>For a build site that is a leapfrog outpost: whether a robot may go one way and why (empty for other tiles).</summary>
        string OutpostText(int tile);

        /// <summary>Hand out work now (after changing workforce settings), instead of waiting for the next tick.</summary>
        void RefreshWork();

        /// <summary>
        /// Ask a robot to take down the building in a slot. A construction site is cancelled at once (its parts come
        /// back); a finished building keeps working until a robot salvages it (<see cref="BuildingSalvaged"/>).
        /// Returns false if the slot is empty or already ordered.
        /// </summary>
        bool OrderSalvage(int tileId, int slot);

        /// <summary>True while a finished building waits for a robot to take it down.</summary>
        bool IsSalvageOrdered(int tileId, int slot);

        /// <summary>Withdraw a salvage order. Returns false if there was none.</summary>
        bool CancelSalvage(int tileId, int slot);

        /// <summary>The colony network: hub, relays, coverage, power (stage E).</summary>
        Robots.ColonyNetwork Network { get; }

        /// <summary>Colony computing the robots' processors ask for, W, and whether it runs now (stage E).</summary>
        double ComputingRequestW { get; }
        bool ComputingOn { get; }

        /// <summary>Survey and measurement results cut-off robots are carrying until they can sync (stage E).</summary>
        System.Collections.Generic.IReadOnlyList<HeldFinding> HeldFindings { get; }
    }

    /// <summary>The ground and the materials: surveys, mining and what is on hand.</summary>
    public interface IResourceCommands
    {
        /// <summary>Why robots cannot work on this tile from the nearest charger (too far for one charge), or null.</summary>
        string WorkReachProblem(int tileId);

        /// <summary>What lies in each tile's ground, and which tiles are surveyed (only those are shown to the player).</summary>
        Resources.DepositMap Deposits { get; }

        /// <summary>Materials on hand, kg.</summary>
        Resources.MaterialStock Materials { get; }

        /// <summary>Stock kept per place (depots, buffers, cargo) instead of one colony stock; hauls and sites draw by place. Off in old saves and in the simulation tests.</summary>
        bool LogisticsEnabled { get; set; }

        /// <summary>Kg of one material in the depot on a tile.</summary>
        double KgAt(int tileId, Resources.Material material);

        /// <summary>Kg of one material in every place together.</summary>
        double TotalKg(Resources.Material material);

        /// <summary>Kg moved, kg-km and the energy of hauling so far.</summary>
        (double KgMoved, double KgKm, double Wh) HaulTotals { get; }

        /// <summary>Keep at least this much of a material in a tile's depot: hauls are made until it is met. Zero removes the target.</summary>
        bool SetStockTarget(int tileId, Resources.Material material, double kg);

        double StockTarget(int tileId, Resources.Material material);

        /// <summary>Build a storage yard, sealed store or gas tank on a slot.</summary>
        bool PlaceStorage(BuildingType type, int tileId, int slot);

        /// <summary>What a tile's depot can hold of one storage class, kg (infinity at the lander).</summary>
        double StorageCapacityKg(int tileId, Resources.StorageClass storageClass);

        /// <summary>Kg of a material a tile's depot has room for now.</summary>
        double RoomAt(int tileId, Resources.Material material);

        IReadOnlyList<(int Tile, Resources.Material Material, double Kg)> StockTargets();

        /// <summary>Everything every place holds: depots, plant buffers, robots' cargo (kind, id, material, kg).</summary>
        System.Collections.Generic.IEnumerable<(Logistics.PlaceKind Kind, int Id, Resources.Material Material, double Kg)> Holdings();

        /// <summary>Hauls under way: id, from, to, material, kg, loaded, and the carrier once loaded.</summary>
        IReadOnlyList<(int Id, int From, int To, Resources.Material Material, double Kg, bool Loaded, int RobotId)> HaulOrders();
        /// <summary>Add a charging bay (a cheaper charging station) to a storage yard's tile, so hauls can be staged through the depot.</summary>
        bool PlaceChargingBay(int tileId, int slot);
        string ChargingBayBlocker(int tileId);
        bool HasChargingBay(int tileId);
        /// <summary>Advice only: tiles where a storage yard would save the most haul energy per day, best first.</summary>
        IReadOnlyList<(int Tile, double SavedWhPerDay, double CopperKg, int LineTiles, double BuildHours)> DepotSites(int max);
        /// <summary>Why a haul is waiting (a stop lost power, no chain fits), or null.</summary>
        string HaulBlockedReason(int haulId);
        /// <summary>The structured reason behind a haul (0 ok, 1 no route, 2 no stop to start from, 3 no stop to end at, 4 a leg longer than one charge, 5 a planned stop was lost), the energy of its longest leg, one charge, and the lost stop.</summary>
        (int Kind, double NeedWh, double BudgetWh, int StopTile) HaulWhyOf(int haulId);
        IReadOnlyList<int> HaulStops(int haulId);
        /// <summary>What the weather does to the stock on a tile, as lines (rate per year, marked estimated where humidity is not measured).</summary>
        System.Collections.Generic.IReadOnlyList<string> StorageNotes(int tileId);
        /// <summary>The failure ladder of a robot (none, trying alternatives, waiting for help, needs you) with a line for the player.</summary>
        (LadderState State, string Text) LadderOf(int robotId);
        /// <summary>Hours before a robot in danger is lost (infinity when it is not).</summary>
        double RescueDeadlineHours(int robotId);
        /// <summary>Ask for a rescue of this robot now (the player's Rescue button); false when nobody can help.</summary>
        bool RequestRescue(int robotId);
        /// <summary>The recovered wrecks and what stops each from being rebuilt: (wreck id, robot id, robot kind, rebuildable, why not or null).</summary>
        System.Collections.Generic.IReadOnlyList<(int Id, int RobotId, string Kind, bool Rebuildable, string Blocker)> RecoveredWrecks();
        /// <summary>Why a recovered wreck cannot be rebuilt (the electronics melted, no Workshop, materials missing), or null when the order can be placed.</summary>
        string RebuildBlocker(int wreckId);
        /// <summary>Order a robot rebuilt from a recovered wreck at the Workshop; null when it was placed, else why not.</summary>
        string OrderRebuild(int wreckId);
        /// <summary>The wrecks as lines for the player: cause, what lies there, and the state of the recovery haul.</summary>
        System.Collections.Generic.IReadOnlyList<string> WreckLines();
        /// <summary>The player overrides a forecast-storm wait on a haul that has not loaded; the energy check still applies.</summary>
        bool GoAnywayHaul(int haulId);
        /// <summary>True when the haul waits for a forecast storm (not for energy).</summary>
        bool HaulWaitsForStorm(int haulId);
        /// <summary>One line on what a haul costs a robot: round-trip energy split into driving, climbing and the heater.</summary>
        string HaulCostText(int fromTile, int toTile, double cargoKg);

        /// <summary>Ask a robot to survey a land tile's ground. False if it is water, surveyed or already ordered.</summary>
        bool OrderSurvey(int tileId);
        bool IsSurveyOrdered(int tileId);
        bool CancelSurvey(int tileId);

        /// <summary>How much of a tile's survey is done, 0–1.</summary>
        double SurveyProgress(int tileId);

        /// <summary>Keep robots mining a known material on a tile, in batches. False if it isn't known there or already ordered.</summary>
        bool OrderMining(int tileId, Resources.Material material);
        bool IsMiningOrdered(int tileId, Resources.Material material);
        bool CancelMining(int tileId, Resources.Material material);
    }

    /// <summary>The Robot Workshop: jobs for new robots, upgrades and refined materials.</summary>
    public interface IManufacturingCommands
    {
        /// <summary>Recent facility and robot reports explaining industrial outcomes and constraints.</summary>
        System.Collections.Generic.IReadOnlyList<Resources.IndustrialReport> IndustrialReports { get; }

        /// <summary>Finished Workshops (each works one job at a time).</summary>
        int WorkshopCount { get; }

        /// <summary>Jobs waiting or under way, in order; the first <see cref="WorkshopCount"/> are being worked.</summary>
        System.Collections.Generic.IReadOnlyList<Robots.ManufacturingJob> Jobs { get; }

        /// <summary>Order a new robot. Returns null if ordered, else why not (locked, no Workshop, missing materials or parts).</summary>
        string OrderRobot(Robots.RobotKind kind);

        /// <summary>Order an upgrade for a robot. Returns null if ordered, else why not.</summary>
        string OrderUpgrade(int robotId, Robots.RobotModule module);

        /// <summary>Order a batch of a refined material (aluminium or sodium). Returns null if ordered, else why not.</summary>
        string OrderRefine(Resources.Material output);

        // ---- refinery modules (director 2026-11-15) ----

        /// <summary>Queue one batch of a refining process (needs a module of its kind next to a Workshop).</summary>
        string OrderRefine(Resources.RefineProcess process);
        /// <summary>Why the process can't start now (no module, not enough input), or null.</summary>
        string RefineBlocker(Resources.RefineProcess process);
        System.Collections.Generic.IReadOnlyList<Resources.RefineProcess> RefineQueue { get; }
        bool CancelRefineOrder(int index);
        /// <summary>Keep this much output in stock (0: off): batches start by themselves below it.</summary>
        void SetAutoRefine(Resources.RefineProcess process, double keepKg);
        double AutoRefineTarget(Resources.RefineProcess process);
        int RefineryCount(Resources.RefineryKind kind);
        System.Collections.Generic.List<RefineryLine> RefineryLines();
        double RefineryPowerW { get; }
        /// <summary>A refinery module in this slot; must touch a Workshop.</summary>
        bool PlaceRefinery(BuildingType type, int tileId, int slot);
        string RefineryBlocker(BuildingType type, int tileId, int slot);

        /// <summary>Cancel a job; its materials and parts come back.</summary>
        bool CancelJob(int index);
    }

    /// <summary>The colony's knowledge store (K1): what is known, with its source, confidence and age. Read only for the display.</summary>
    public interface IColonyKnowledge
    {
        Knowledge.KnowledgeBase KnowledgeStore { get; }
        /// <summary>The most recent discrepancies between records, newest last (for the Why? chain).</summary>
        System.Collections.Generic.List<Knowledge.KnowledgeEvent> RecentDiscrepancies { get; }
    }

    /// <summary>What a panel would make and what the sky is doing (read only, safe from a worker thread).</summary>
    public interface ISolarAdvice
    {
        /// <summary>
        /// The sky over a tile right now (declination, hour angle, sun altitude and azimuth) and what a new panel at
        /// these angles (radians) would get: incidence, light and power. Read-only; for the sky view.
        /// </summary>
        Solar.SkyReport DescribeSky(int tileId, double tilt, double azimuth);

        /// <summary>What the colony has measured about its sky (saved with the game). Unlocks best-angle advice per tile.</summary>
        Solar.SkyNotebook Notebook { get; }

        /// <summary>
        /// Numbers for one panel on a tile (every slot of a tile gets the same sunlight); places nothing.
        /// Must be read-only and safe to call from a worker thread (the best-angle search runs in the background).
        /// </summary>
        PanelPreview PreviewPanel(int tileId, double tilt, double azimuth);

        /// <summary>The same numbers for a floating panel on shallow sea or wetland (read-only, safe from a worker thread).</summary>
        PanelPreview PreviewFloatingPanel(int tileId, double tilt, double azimuth);

        /// <summary>
        /// Energy per "month" (twelfths of the year from periapsis, as the game clock counts) of one panel on a tile at
        /// these angles, Wh; null where <see cref="PreviewPanel"/> could not place one. Read-only like PreviewPanel.
        /// </summary>
        double[] MonthlyEnergyWh(int tileId, double tilt, double azimuth);
    }

    /// <summary>Measuring the sky: solar parameters, their puzzles and what each one is worth.</summary>
    /// <summary>
    /// Barriers (director 2026-11-15): mountains and canyons block driving, building and mining until opened with a
    /// tunnel, blasting or a bridge.
    /// </summary>
    public interface IPassageCommands
    {
        bool IsBarrier(int tileId);
        /// <summary>No barrier, or one the colony has opened.</summary>
        bool IsOpenGround(int tileId);
        Resources.PassageKind PassageAt(int tileId);
        Resources.PassageKind PassageOrdered(int tileId);
        double PassageProgress(int tileId);
        Resources.MaterialBill PassageBill(int tileId, Resources.PassageKind kind);
        double PassageHours(int tileId, Resources.PassageKind kind);
        string PassageBlocker(int tileId, Resources.PassageKind kind);
        string OrderPassage(int tileId, Resources.PassageKind kind);
        bool CancelPassage(int tileId);
        string ClosedGroundReason(int tileId);
        /// <summary>A cave the colony knows of (shown after a survey).</summary>
        bool KnowsCave(int tileId);
        /// <summary>Every opened barrier tile and how it was opened.</summary>
        System.Collections.Generic.IReadOnlyDictionary<int, Resources.PassageKind> OpenedPassages { get; }
        event Action<int, Resources.PassageKind> PassageOpened;
    }

    public interface IResearchCommands
    {
        /// <summary>Planet-wide parameters measured so far (latitude is in <see cref="Notebook"/>).</summary>
        Solar.ParameterBook Parameters { get; }

        /// <summary>Is this parameter known for this tile (latitude per tile, the rest for the planet)?</summary>
        bool IsParameterKnown(Solar.SolarParameter parameter, int tileId);

        /// <summary>Send a robot to measure a parameter, working on this tile. False if known, ordered or on water.</summary>
        bool OrderMeasure(Solar.SolarParameter parameter, int tileId);
        bool IsMeasureOrdered(Solar.SolarParameter parameter);
        bool CancelMeasure(Solar.SolarParameter parameter);
        /// <summary>0–1 while a robot measures (1 once known).</summary>
        double MeasureProgress(Solar.SolarParameter parameter);

        /// <summary>Why a measurement can't be ordered for this tile (no Research Station), or null.</summary>
        string MeasureBlocker(Solar.SolarParameter parameter, int tileId);

        // ---- the Research Station and its experiments (director 2026-11-14) ----

        bool HasResearchStation { get; }
        bool HasResearchStationOn(int tileId);
        /// <summary>A tile with a finished Research Station (preferTile when it has one); -1 if none.</summary>
        int FinishedStationTile(int preferTile);
        /// <summary>A finished Research Station's tile that has been surveyed (experiments need one); -1 if none.</summary>
        int SurveyedStationTile(int preferTile);
        bool IsExperimentKnown(Planet.ExperimentKind kind);
        bool IsExperimentOrdered(Planet.ExperimentKind kind);
        /// <summary>Why an experiment can't be ordered now (no station, a prerequisite, done), or null.</summary>
        string ExperimentBlocker(Planet.ExperimentKind kind);
        bool OrderExperiment(Planet.ExperimentKind kind, int preferTile = -1);
        bool CancelExperiment(Planet.ExperimentKind kind);
        double ExperimentProgress(Planet.ExperimentKind kind);
        /// <summary>What an experiment found, for the player, or null when not done.</summary>
        string ExperimentResult(Planet.ExperimentKind kind);
        /// <summary>Experiments whose fit puzzle is waiting (RS-13).</summary>
        System.Collections.Generic.IReadOnlyList<Planet.ExperimentKind> PendingPuzzles { get; }
        Planet.FitPuzzle PuzzleFor(Planet.ExperimentKind kind);
        string AnswerFitPuzzle(Planet.ExperimentKind kind, double[] handles);
        string AutoFitPuzzle(Planet.ExperimentKind kind);
        /// <summary>The share of an experiment's original range still left: 1 when just done, 0.1 after a good fit.</summary>
        double ExperimentRangeShare(Planet.ExperimentKind kind);
        /// <summary>What the colony believes about five quantities: a range until measured, exact after (RS-14).</summary>
        Planet.KnowledgeEstimate PressureEstimate { get; }
        Planet.KnowledgeEstimate TemperatureEstimate { get; }
        Planet.KnowledgeEstimate HighestPointEstimate { get; }
        Planet.KnowledgeEstimate TideRangeEstimate { get; }
        /// <summary>The grade of an ore at a surveyed tile: a range until the ore assay is done.</summary>
        Planet.KnowledgeEstimate GradeEstimate(int tileId, Resources.Material material);
        /// <summary>What one mining robot digs out of this tile, kg per hour: a range until the ore assay.</summary>
        Planet.KnowledgeEstimate MiningRateEstimate(int tileId, Resources.Material material);
        /// <summary>What a deposit holds, kg of product: a range until the ore assay, exact after (B3).</summary>
        Planet.KnowledgeEstimate ReserveEstimate(int tileId, Resources.Material material);
        /// <summary>The ore grade, kg of product per kg of rock moved: a range until the assay (B3).</summary>
        Planet.KnowledgeEstimate OreGradeEstimate(int tileId, Resources.Material material);
        /// <summary>The share of a deposit still in the ground, 0 to 1 (1 for an endless one).</summary>
        double DepositShareLeft(int tileId, Resources.Material material);
        /// <summary>Rock moved and tailings left by mining so far, kg.</summary>
        double RockMovedKg { get; }
        double TailingsKg { get; }
        double MiningWorkHours { get; }
        /// <summary>Deposits that were mined and are used up now (B3): where the mine stopped.</summary>
        System.Collections.Generic.IReadOnlyList<(int Tile, Resources.Material Material, double HeldKg)> ExhaustedDeposits();
        /// <summary>The best known deposits of a material that still hold some, richest first: tile and ore grade (a range until the assay).</summary>
        System.Collections.Generic.IReadOnlyList<(int Tile, Planet.KnowledgeEstimate OreGrade, double ShareLeft)> BestDeposits(Resources.Material material, int count);
        double ComminutionWh { get; }
        /// <summary>Crushing and grinding energy per tonne of product now, kWh (Bond), a range until the assay.</summary>
        Planet.KnowledgeEstimate ComminutionEstimate(int tileId, Resources.Material material);
        /// <summary>True where Peek's law does not apply (thin air, the Paschen region): cable ratings there use a labelled fallback.</summary>
        bool CableOutsideModel { get; }
        /// <summary>The share of its rating a cable is given while the air pressure is unmeasured.</summary>
        double CableDesignShare { get; }
        /// <summary>Share of their real output solar panels deliver while values are unknown (a safety margin).</summary>
        double SolarDesignShare { get; }
        /// <summary>Share of battery capacity in use until the surface temperature is known.</summary>
        double BatteryUsableShare { get; }

        /// <summary>
        /// What each parameter's uncertainty can change about a panel's yearly energy here (Earth years, Wh): the model
        /// run at both ends of each range. Known parameters have no spread.
        /// </summary>
        System.Collections.Generic.List<Solar.ParameterEffect> ParameterEffects(int tileId, double tilt, double azimuth);

        /// <summary>Shadow readings waiting for the player to work out the latitude (stage C).</summary>
        System.Collections.Generic.IReadOnlyList<Solar.LatitudePuzzle> LatitudePuzzles { get; }

        /// <summary>
        /// Answer a latitude puzzle (degrees, + north). A close answer fixes the tile's latitude; a wrong one keeps
        /// the puzzle open. Returns what happened, for the player.
        /// </summary>
        string AnswerLatitudePuzzle(int tileId, double latitudeDeg, out Solar.LatitudePuzzle.Grade grade);

        /// <summary>Readings waiting for the player to work out haze, diffuse share or clouds (stage D).</summary>
        System.Collections.Generic.IReadOnlyList<Solar.ParameterPuzzle> ParameterPuzzles { get; }

        /// <summary>Answer a light puzzle. Precise: the value is known; rough: half its range is left; wrong: try again.</summary>
        string AnswerParameterPuzzle(Solar.SolarParameter parameter, double value, out Solar.LatitudePuzzle.Grade grade);

        /// <summary>Core samples waiting for the player to estimate the tile's grades (stage F).</summary>
        System.Collections.Generic.IReadOnlyList<Resources.SurveyPuzzle> SurveyPuzzles { get; }

        /// <summary>
        /// Answer a survey puzzle: one grade estimate per material in the puzzle's order. A close answer surveys the
        /// tile; a rough one surveys it marked ±; a wrong one keeps the cores waiting.
        /// </summary>
        string AnswerSurveyPuzzle(int tileId, System.Collections.Generic.IReadOnlyList<double> estimates, out Solar.LatitudePuzzle.Grade grade);

        /// <summary>Answer one material of a survey puzzle; the tile is surveyed once every material is worked out.</summary>
        string AnswerSurveyMaterial(int tileId, int materialIndex, double estimate, out Solar.LatitudePuzzle.Grade grade);

        /// <summary>The tile was surveyed from a rough estimate: its grades are shown as ±.</summary>
        bool IsRoughSurvey(int tileId);
    }

    /// <summary>Saved panel angle layouts: the player's names and notes, with the tile's latitude and longitude.</summary>
    public interface IAnglePresets
    {
        IReadOnlyList<AnglePreset> AnglePresets { get; }

        /// <summary>Save the angles of the panel in a slot as a named layout. Null if no panel stands there.</summary>
        AnglePreset SaveAnglePreset(int tileId, int slot, string name, string note);

        /// <summary>Save a layout with these angles (radians) for a slot of a tile, whether or not a panel stands there yet.</summary>
        AnglePreset SaveAnglePresetAt(int tileId, int slot, string name, string note, double tilt, double azimuth);

        bool UpdateAnglePreset(int id, string name, string note);

        bool DeleteAnglePreset(int id);

        /// <summary>Ask a robot to turn the panel in a slot to a saved layout's angles (same rules as <see cref="IBuildingCommands.OrderPanelAngles"/>).</summary>
        bool ApplyAnglePreset(int id, int tileId, int slot);
    }

    /// <summary>Tides and free-flow tidal power (director 2026-11-19).</summary>
    public interface ITidalPower
    {
        /// <summary>The world's moon (generated from its seed), or a moon that does not exist when there is none.</summary>
        Planet.Moon Moon { get; }

        /// <summary>Hours between two high tides.</summary>
        double TidePeriodHours { get; }

        /// <summary>Tidal range (high minus low) at this tile, m: the open-ocean range times how much the coast funnels it.</summary>
        double TidalRangeAtM(int tileId);

        /// <summary>Peak tidal stream speed at this tile, m/s.</summary>
        double PeakStreamAtMs(int tileId);

        /// <summary>Stream speed at this tile now, m/s.</summary>
        double StreamAtMs(int tileId);

        /// <summary>Electric power all finished tidal turbines give now, W.</summary>
        double TidalPowerW();

        /// <summary>Put a free-flow tidal turbine on a shallow-sea tile.</summary>
        bool PlaceTidalTurbine(int tileId, int slot);
    }

    /// <summary>The wired grid (director 2026-11-20): generators and stations must be connected to the lander by power lines.</summary>
    /// <summary>Plants with buffers, byproducts and waste heat (mission operations MO-06, MO-07, MO-08).</summary>
    public interface IFlowsheetCommands
    {
        /// <summary>What a refinery module does with its gas: store it (a full store stops the plant) or vent it (logged).</summary>
        bool SetByproductPolicy(int tileId, int slot, Resources.ByproductPolicy policy);

        Resources.ByproductPolicy ByproductPolicyOf(int tileId, int slot);

        /// <summary>A plant's blocker, buffers and heat; null if no module is there.</summary>
        PlantReport PlantReportAt(int tileId, int slot);

        /// <summary>Gas released to the air so far, kg.</summary>
        double VentedKg(Resources.Material material);

        /// <summary>Waste heat of the batches running now, W (a readout only).</summary>
        double IndustrialWasteHeatW { get; }

        /// <summary>Waste heat of the batches finished so far, kWh.</summary>
        double IndustrialWasteHeatKWh { get; }

        /// <summary>The last days of mass flows with closure, waste heat and blocked plants (the Flowsheet view reads this and computes nothing).</summary>
        FlowsheetSnapshot FlowsheetSnapshot(int days = 7);
    }

    /// <summary>Flight rules: the versioned rulebook, its uplink and the Operations log (mission operations 5, MO-10 to MO-14).</summary>
    public interface IRulebookCommands
    {
        /// <summary>The newest rulebook version (0: none published).</summary>
        int RulebookVersion { get; }

        /// <summary>Every version ever published, oldest first.</summary>
        System.Collections.Generic.IReadOnlyList<Operations.RulebookVersion> RulebookHistory { get; }

        /// <summary>Publish the rules as the next version (they are copied). Null on success; else why not (a rule with no rationale is refused).</summary>
        string PublishRulebook(System.Collections.Generic.IReadOnlyList<Operations.Rule> rules, string note);

        /// <summary>Publish the starter rulebook if there is no rulebook yet.</summary>
        bool InstallStarterRulebook();

        /// <summary>The rulebook version a robot runs now (0: none yet).</summary>
        int RobotRulebookVersion(int robotId);

        /// <summary>The version the plants run.</summary>
        int PlantRulebookVersion { get; }

        /// <summary>How many robots run each version.</summary>
        System.Collections.Generic.IReadOnlyDictionary<int, int> RulebookUplink();

        /// <summary>Rule firings, syncs, notices and other operations records, oldest first.</summary>
        System.Collections.Generic.IReadOnlyList<Operations.OperationRecord> OperationsLog { get; }
        /// <summary>The weather at a tile now (climatology plus the day's anomaly).</summary>
        WeatherSample WeatherAt(int tileId);
        bool HasWeather { get; }
        bool StormAt(int tileId);
        bool WeatherMeasuredAt(int tileId);
        WeatherKnowledge WeatherKnownAt(int tileId);
        WeatherKnowledge? ForecastAt(int tileId, int daysAhead);
        double StormThresholdMs(int tileId);
        /// <summary>Put an event (a save, a milestone) into the field log; its priority follows its kind.</summary>
        void NoteEvent(string subject, string quantity, string text);

        /// <summary>A plant a rule paused.</summary>
        bool IsPlantPaused(int tileId, int slot);
    }

    /// <summary>
    /// The electrical networks the finished power lines form (electrical redesign, stage 1; read model only). The lander's network feeds
    /// the single power bus; a tile on another network is connected to its neighbours but not on the main grid.
    /// </summary>
    /// <summary>
    /// Part of the five separate states of a building's power (connected, compatible, operational, receiving power, receiving enough).
    /// This reports the first three plus "on the main grid"; operational and "receiving enough" come from other systems.
    /// </summary>
    public readonly struct BuildingElectricalState
    {
        public readonly bool HasTerminal;
        public readonly Electrical.TerminalRole Role;
        /// <summary>The terminal is connected to a node.</summary>
        public readonly bool Connected;
        /// <summary>The terminal's kind and class fit the node it is connected to.</summary>
        public readonly bool Compatible;
        /// <summary>It is on the lander's network, the one the single power bus serves.</summary>
        public readonly bool OnMainGrid;

        public BuildingElectricalState(bool hasTerminal, Electrical.TerminalRole role, bool connected, bool compatible, bool onMainGrid)
        {
            HasTerminal = hasTerminal; Role = role; Connected = connected; Compatible = compatible; OnMainGrid = onMainGrid;
        }
    }

    public enum ReadingSource { None, Meter, Inspection }

    /// <summary>Protection fitted to a line (saved as integers: append only). A fuse blows and must be replaced; a breaker trips and is reset.</summary>
    public enum ProtectionKind { None = 0, Fuse = 1, Breaker = 2 }

    /// <summary>
    /// What the colony knows about the line from a tile toward the lander (electrical stage 7). Capacity is a design figure and always known.
    /// The flow is known only when <see cref="Measured"/>; otherwise <see cref="FlowW"/> is NaN and the line is only known to carry
    /// between 0 and its capacity.
    /// </summary>
    public readonly struct LineReadout
    {
        public readonly bool HasLine;
        public readonly ReadingSource Source;
        /// <summary>Hours since the inspection (0 for a meter).</summary>
        public readonly double AgeHours;
        public readonly double FlowW;
        /// <summary>Known only when measured.</summary>
        public readonly bool Overloaded;
        public readonly double MeasuredFlowW;
        public readonly double CapacityW;
        public bool Measured => Source != ReadingSource.None;

        public LineReadout(bool hasLine, ReadingSource source, double ageHours, double flowW, bool overloaded, double measuredFlowW, double capacityW)
        {
            HasLine = hasLine; Source = source; AgeHours = ageHours; FlowW = flowW; Overloaded = overloaded; MeasuredFlowW = measuredFlowW; CapacityW = capacityW;
        }
    }

    /// <summary>Meters and inspections (electrical stage 7): the readout of a line says whether the colony knows what it carries.</summary>
    /// <summary>News of robots and the weather for the HUD: a serial that rises with each warning or loss, and the latest line.</summary>
    public interface IRobotWeather
    {
        int RobotWeatherNewsSerial { get; }
        string RobotWeatherNews { get; }
        int RobotsDestroyed { get; }
    }

    public interface IElectricalMeters
    {
        /// <summary>Puts a meter on the line from this tile toward the lander. False when there is no line on the grid there.</summary>
        bool InstallLineMeter(int tileId);
        bool RemoveLineMeter(int tileId);
        bool HasLineMeter(int tileId);

        /// <summary>Inspects the line now (a stand-in for a robot walking it); the reading stays valid for a day.</summary>
        bool InspectLine(int tileId);

        LineReadout LineReadout(int tileId);

        /// <summary>Lines that have failed or been opened (tile and slot of the line, and its condition), in order.</summary>
        System.Collections.Generic.IEnumerable<(int Tile, int Slot, double Condition)> FailedLines();

        /// <summary>What repairing the line takes now (its build bill times the condition lost; stand-in, TBD).</summary>
        Resources.MaterialBill RepairBill(int tileId, int slot);

        /// <summary>Repairs the line from stock. Null when done, else why not.</summary>
        string RepairLine(int tileId, int slot);

        /// <summary>Sends a robot to inspect a line (a day-long reading when it is done). False when there is no line there or one is on its way.</summary>
        bool OrderLineInspection(int tileId);

        /// <summary>Sends a robot to repair a failed line; the repair bill is taken from stock when it finishes. Null when ordered, else why not.</summary>
        string OrderLineRepair(int tileId, int slot);

        /// <summary>Sends a robot to install a saved circuit in a charging station. Null when ordered, else why not.</summary>
        string OrderCircuitInstall(int tileId, int slot, int blueprintIndex);

        /// <summary>Sends a robot to put a meter on a line; the price (2 Cu, 1 Fe) is taken when it finishes. Null when ordered, else why not.</summary>
        string OrderLineMeter(int tileId);

        /// <summary>The fuse or breaker on the line from this tile toward the lander.</summary>
        ProtectionKind LineProtectionOf(int tileId);

        /// <summary>The condition of a line, 0 (failed) to 1.</summary>
        double LineCondition(int tileId, int slot);

        /// <summary>Sends a robot to fit a fuse or breaker to a line; the price is taken when it finishes. Null when ordered, else why not.</summary>
        string OrderLineProtection(int tileId, ProtectionKind kind);

        /// <summary>Electrical jobs waiting for a robot, in order.</summary>
        System.Collections.Generic.IReadOnlyList<ElectricalOrder> ElectricalOrders();

        /// <summary>Takes back a waiting job.</summary>
        bool CancelElectricalOrder(ElectricalAction action, int tileId, int slot);

        /// <summary>Why the last electrical order was dropped when its robot finished (empty when none was).</summary>
        string LastElectricalProblem { get; }
    }

    /// <summary>What the circuit editor and windows see of an installed circuit (a copy; nothing live).</summary>
    public sealed class StationCircuitView
    {
        public Circuits.CircuitBlueprint Design;
        public int BlueprintVersion;
        /// <summary>The station charges robots now (its circuit's enable port is high).</summary>
        public bool Enabled;
        public Circuits.CircuitResult Result;
        public System.Collections.Generic.List<Circuits.CircuitFault> Faults;
        /// <summary>Switch and button positions by component id.</summary>
        public System.Collections.Generic.Dictionary<int, bool> SwitchClosed;
    }

    /// <summary>Player-built circuits (electrical redesign stage 9). The first host is the charging station: its circuit's "enable" port decides whether it charges.</summary>
    public interface ICircuitCommands
    {
        /// <summary>Saves a copy of a design in the colony's library (a design with the same name is replaced). Returns its index.</summary>
        int SaveCircuitBlueprint(Circuits.CircuitBlueprint design);

        /// <summary>Copies of the saved designs.</summary>
        System.Collections.Generic.IReadOnlyList<Circuits.CircuitBlueprint> CircuitBlueprints();

        bool DeleteCircuitBlueprint(int index);

        /// <summary>Solves a design on its own with its starting switch positions, without installing it.</summary>
        Circuits.CircuitResult TestCircuit(Circuits.CircuitBlueprint design);

        /// <summary>Why a design does not fit a charging station, or null when it does.</summary>
        string CircuitFitProblem(Circuits.CircuitBlueprint design);

        /// <summary>Installs a saved design in a finished charging station. Null when done, else the reason it was refused.</summary>
        string InstallStationCircuit(int tileId, int slot, int blueprintIndex);

        bool RemoveStationCircuit(int tileId, int slot);

        /// <summary>Opens or closes a switch or button of an installed circuit.</summary>
        bool SetCircuitSwitch(int tileId, int slot, int componentId, bool closed);

        /// <summary>What the installed circuits draw from the grid now, W (their supplies count as the station's load).</summary>
        double CircuitLoadW { get; }

        /// <summary>The finished charging stations, in tile order.</summary>
        System.Collections.Generic.IEnumerable<(int Tile, int Slot)> ChargingStations();

        /// <summary>The installed circuit of a station, or null.</summary>
        StationCircuitView StationCircuit(int tileId, int slot);
    }

    public interface IElectricalNetworks
    {
        /// <summary>How many separate networks the finished lines form.</summary>
        int ElectricalNetworkCount { get; }

        /// <summary>Tiles on the lander's network, in tile order.</summary>
        System.Collections.Generic.IReadOnlyCollection<int> LanderNetworkTiles();

        /// <summary>The tile has a line end and is on a network separate from the lander's.</summary>
        bool IsOnElectricalIsland(int tileId);

        /// <summary>The AC lines as (from tile, to tile, finished, class in volts).</summary>
        System.Collections.Generic.IEnumerable<(int From, int To, bool Built, int AcClass)> AcLinks();

        /// <summary>Runs an AC line (33000 or 500000 V) between two neighbouring tiles. False: see <see cref="IWiredGrid.WireProblem"/>.</summary>
        bool PlaceAcWire(int fromTile, int toTile, int acClass);

        /// <summary>The converters (transformer, inverter, rectifier) on a tile.</summary>
        System.Collections.Generic.IEnumerable<BuildingType> ConvertersOn(int tileId);

        /// <summary>The share of a generator's power on this tile that survives the converters on its way to the lander.</summary>
        double ConversionEfficiencyFrom(int tileId);

        /// <summary>Conversion loss at the converters at the last flow, W (heat).</summary>
        double ConverterLossW { get; }

        /// <summary>Every building that makes or takes power (the ones a wire connects), with where it stands.</summary>
        System.Collections.Generic.IEnumerable<(int Tile, int Slot, BuildingType Type)> WireableBuildings();

        /// <summary>The state of a building's electrical terminal (none when it is not wired or has no terminal).</summary>
        BuildingElectricalState BuildingElectrical(int tileId, int slot);
    }

    public interface IWiredGrid
    {
        /// <summary>When on, generators make power only on tiles wired to the lander tile, and stations and storage can only be built there. Off in old saves and tests.</summary>
        bool WiredGrid { get; set; }

        /// <summary>The tile is on the grid: the lander tile, or a tile with a finished power line joined to it through neighbouring wired tiles. Always true when the grid is not wired.</summary>
        bool IsPowered(int tileId);

        /// <summary>How many tiles are on the grid.</summary>
        int PoweredTileCount { get; }

        /// <summary>Generator buildings (panels, turbines, wave lines, barrages) standing off the grid.</summary>
        int GeneratorsOffGrid { get; }

        /// <summary>Share of a generator's power that reaches the lander tile (less for each tile of line out).</summary>
        double GridEfficiency(int tileId);

        /// <summary>Power lost to resistance in the lines now, W.</summary>
        double TransmissionLossW { get; }

        /// <summary>Power the lines are too thin to carry now, W.</summary>
        double OverloadLossW { get; }

        /// <summary>What this tile's line carries now, W.</summary>
        double LineFlowW(int tileId);

        /// <summary>What this tile's line can carry, W (infinite on the lander tile).</summary>
        double LineCapacityW(int tileId);

        /// <summary>The line on this tile carries more than it can: power beyond it is lost.</summary>
        bool IsLineOverloaded(int tileId);

        /// <summary>Tiles with a power line (finished or a site), for drawing the wires.</summary>
        System.Collections.Generic.IEnumerable<int> LineTiles();

        /// <summary>A line is on this tile (finished only, or a site too).</summary>
        bool HasLineOn(int tileId, bool finishedOnly);

        /// <summary>A high-voltage wire from this tile to a neighbouring one (opens at milestone 1).</summary>
        bool PlaceHighVoltageLine(int tileId, int slot, int targetTile);

        /// <summary>A wire from this tile to a neighbouring one, built on a slot of this tile.</summary>
        bool PlacePowerLine(int tileId, int slot, int targetTile);

        /// <summary>Run a wire from one tile to a neighbouring one (the planet wire tool). False: see <see cref="WireProblem"/>.</summary>
        bool PlaceWire(int fromTile, int toTile, bool highVoltage);

        string WireProblem { get; }

        /// <summary>Wire the building on this slot to the grid (a power line dragged onto it). False: see <see cref="WireProblem"/>.</summary>
        bool PlaceBuildingWire(int tileId, int slot);

        /// <summary>The building on this slot is wired to the grid (true for all when the grid is not wired).</summary>
        bool IsBuildingWired(int tileId, int slot);

        /// <summary>Buildings still waiting for a wire.</summary>
        System.Collections.Generic.IEnumerable<(int Tile, int Slot, BuildingType Type)> UnwiredBuildings();

        /// <summary>Wire every building built so far (what Skip setup does).</summary>
        void WireAllBuildings();

        /// <summary>Every wire, tile to tile: the two tiles, whether it is finished, whether it is high voltage.</summary>
        System.Collections.Generic.IEnumerable<(int From, int To, bool Built, bool HighVoltage)> WireLinks();
    }

    /// <summary>Tidal barrage (director 2026-11-19).</summary>
    public interface ITidalBarrage
    {
        /// <summary>Why a barrage cannot go on this tile (not a bay, tide too small, one already), or null.</summary>
        string BarrageBlocker(int tileId);

        /// <summary>Electric power all finished barrages give now, W.</summary>
        double BarragePowerW();

        /// <summary>Put a barrage across a bay: it covers three slots in a row from this one.</summary>
        bool PlaceBarrage(int tileId, int slot);

        /// <summary>Whether a finished barrage dams this tile.</summary>
        bool HasBarrage(int tileId);
    }

    /// <summary>Sea waves and wave-energy converters (director 2026-11-19).</summary>
    public interface IWavePower
    {
        /// <summary>Significant wave height at this sea tile now, m (zero on land and where there is no air).</summary>
        double WaveHeightAtM(int tileId);

        /// <summary>Mean significant wave height at this sea tile, m (without the weather swing).</summary>
        double MeanWaveHeightAtM(int tileId);

        /// <summary>Electric power all finished wave converters give now, W.</summary>
        double WavePowerW();

        /// <summary>Put a wave-converter line on a shallow-sea tile.</summary>
        bool PlaceWaveConverter(int tileId, int slot);

        /// <summary>Put a wind turbine on a land tile (one per tile).</summary>
        bool PlaceWindTurbine(int tileId, int slot);

        /// <summary>Electric power all finished wind turbines give now, W.</summary>
        double WindPowerW();

        /// <summary>The dust sizing advice (estimated): shortfall, extra storage or panels, for a dusty season or a global storm.</summary>
        DustSizing DustSizingAdvice(bool globalStorm);

        /// <summary>Why the turbine on this tile makes what it makes now (or why it is idle), in one line.</summary>
        string WindTurbineStatus(int tileId);

        /// <summary>What one turbine would make on this tile today (mean over the day), W.</summary>
        double WindTurbinePowerAtW(int tileId);

        /// <summary>The daily mean wind at the hub of a turbine on this tile, m/s.</summary>
        double HubWindAt(int tileId);

        int WindTurbineCount(int tileId);
        /// <summary>The tiles that have a wind turbine.</summary>
        System.Collections.Generic.IReadOnlyList<int> WindTurbineTiles();
    }

    /// <summary>Testing switches and shortcuts (the debug panel). Not saved.</summary>
    public interface IDebugTools
    {
        DebugOptions Debug { get; }

        /// <summary>Add this many kg of every material to the stock.</summary>
        void DebugAddMaterials(double kg);

        /// <summary>A new robot of this kind at the base, ready to work.</summary>
        bool DebugAddRobot(Robots.RobotKind kind);

        /// <summary>Mark every land tile surveyed (its ground becomes known). Returns how many were new.</summary>
        int DebugSurveyAll();
    }

    /// <summary>Capturing and restoring the whole colony.</summary>
    public interface ISimulationSave
    {
        /// <summary>Everything needed to restore this simulation later (time, buildings, battery, colony).</summary>
        SimulationState CaptureState();

        /// <summary>Replace this simulation's state with a captured one. Returns false (and changes nothing) if it was captured on a different planet layout (tile count or base tile differ).</summary>
        bool RestoreState(SimulationState state);
    }

    /// <summary>Everything the simulation announces (raised during Tick or a command).</summary>
    public interface ISimulationEvents
    {
        /// <summary>A measurement finished (the parameter).</summary>
        event Action<Solar.SolarParameter> ParameterMeasured;

        /// <summary>A robot finished readings and a puzzle is waiting.</summary>
        event Action<Solar.SolarParameter> PuzzleReady;

        /// <summary>Brownout started (true) or ended (false).</summary>
        event Action<bool> BrownoutChanged;

        /// <summary>Sunrise (true) or sunset (false) at <see cref="BaseTileId"/>.</summary>
        event Action<bool> DayNightChanged;

        /// <summary>A day's average used power reached <see cref="TypeITargetW"/> (planet K 1, milestone 10).</summary>
        event Action TargetReached;

        /// <summary>
        /// A new milestone (1–10): a day's average used power reached planet K n × 0.1, ten times the power of the
        /// one before. Raised once per milestone, in order.
        /// </summary>
        event Action<int> MilestoneReached;

        /// <summary>A construction site (tile, slot) got all its parts and now works.</summary>
        event Action<int, int> BuildingCompleted;

        /// <summary>A robot took down an ordered building (tile, slot); the slot is empty now.</summary>
        event Action<int, int> BuildingSalvaged;

        /// <summary>A robot turned a panel to its ordered angles (tile, slot).</summary>
        event Action<int, int> PanelAdjusted;

        /// <summary>A robot finished surveying a tile: its deposits are known now.</summary>
        event Action<int> TileSurveyed;

        /// <summary>A deposit was mined out (tile, material); its mining order ended (director 2026-11-15).</summary>
        event Action<int, Resources.Material> DepositExhausted;
    }

}

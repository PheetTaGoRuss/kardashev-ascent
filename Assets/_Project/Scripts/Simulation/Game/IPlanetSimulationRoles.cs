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
        /// <summary>What lies in each tile's ground, and which tiles are surveyed (only those are shown to the player).</summary>
        Resources.DepositMap Deposits { get; }

        /// <summary>Materials on hand, kg.</summary>
        Resources.MaterialStock Materials { get; }

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

        /// <summary>Cancel a job; its materials and parts come back.</summary>
        bool CancelJob(int index);
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
    }

}

using System;
using System.Numerics;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What the Presentation layer may ask of, and hear from, the simulation of one planet.
    /// Presentation sends commands down (Tick, panel commands) and reads state and events back;
    /// the simulation never references Unity or Presentation.
    /// Units: SI except time (hours) and energy (Wh). Angles in radians.
    /// Directions use the planet frame described in <see cref="PlanetFrame"/>.
    /// </summary>
    public interface IPlanetSimulation
    {
        // Planet and tiles

        /// <summary>Tile layout; tile ids are indices into it (0 .. TileCount − 1).</summary>
        HexSphere Sphere { get; }

        int TileCount { get; }

        /// <summary>Static facts about one tile (position, terrain).</summary>
        TileInfo GetTile(int tileId);

        /// <summary>The tile where the colony starts; day/night events follow this tile.</summary>
        int BaseTileId { get; }

        // Time and the star

        /// <summary>Hours since landing.</summary>
        double TimeHours { get; }

        /// <summary>Length of a solar day in hours; 0 for a tidally locked planet.</summary>
        double DayLengthHours { get; }

        double YearLengthHours { get; }

        /// <summary>Unit vector from the planet's centre toward the star, in the planet frame.</summary>
        Vector3 StarDirection { get; }

        /// <summary>True while the star is above the horizon at <see cref="BaseTileId"/>.</summary>
        bool IsDayAtBase { get; }

        // Energy

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

        /// <summary>What new panels are built as: an index into <see cref="PanelTiers.All"/> (field, farm, park, array).</summary>
        int PanelTier { get; }

        /// <summary>Ground one new panel would cover on this tile, m² (the tier's size, capped at the slot's share of the tile).</summary>
        double NewPanelAreaM2(int tileId);

        /// <summary>Ground the panel in a slot covers, m², or 0. Built panels keep the size they were built at.</summary>
        double PanelAreaAt(int tileId, int slot);

        /// <summary>
        /// The sky over a tile right now (declination, hour angle, sun altitude and azimuth) and what a new panel at
        /// these angles (radians) would get: incidence, light and power. Read-only; for the sky view.
        /// </summary>
        Solar.SkyReport DescribeSky(int tileId, double tilt, double azimuth);

        /// <summary>What the colony has measured about its sky (saved with the game). Unlocks best-angle advice per tile.</summary>
        Solar.SkyNotebook Notebook { get; }

        // Commands

        /// <summary>Advance the simulation by <paramref name="hours"/> of game time.</summary>
        void Tick(double hours);

        /// <summary>
        /// Numbers for one panel on a tile (every slot of a tile gets the same sunlight); places nothing.
        /// Must be read-only and safe to call from a worker thread (the best-angle search runs in the background).
        /// </summary>
        PanelPreview PreviewPanel(int tileId, double tilt, double azimuth);

        /// <summary>
        /// Rings of building slots on every tile, laid out by <see cref="TileSite.Create"/> to cover the whole tile
        /// (a hexagonal tile holds 3R(R+1)+1 slots). One panel per slot.
        /// </summary>
        int SiteRings { get; }

        /// <summary>Building slots on a tile (61 on a hexagon with the default 4 rings; pentagons differ).</summary>
        int SlotCount(int tileId);

        /// <summary>
        /// Energy per "month" (twelfths of the year from periapsis, as the game clock counts) of one panel on a tile at
        /// these angles, Wh; null where <see cref="PreviewPanel"/> could not place one. Read-only like PreviewPanel.
        /// </summary>
        double[] MonthlyEnergyWh(int tileId, double tilt, double azimuth);

        /// <summary>
        /// Build a panel in one slot of a tile. Returns false (and changes nothing) if the slot cannot take one.
        /// The panel takes <see cref="BuildCostWh"/> from the parts in stock; without enough it starts as a construction site.
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

        // Construction (see Construction and BuildCosts)

        /// <summary>Build parts on hand, Wh. Spare power is turned into parts; new buildings take their cost from them.</summary>
        double BuildStockWh { get; }

        /// <summary>Spare power going into build parts now, W.</summary>
        double BuildPowerW { get; }

        /// <summary>Parts one building of this type costs, Wh.</summary>
        double BuildCostWh(BuildingType type);

        /// <summary>Construction sites waiting for parts.</summary>
        int ConstructionQueueCount { get; }

        /// <summary>Parts still needed to finish every site, Wh.</summary>
        double ConstructionRemainingWh { get; }

        /// <summary>True while the building in a slot is a site waiting for parts (it does nothing yet).</summary>
        bool IsUnderConstruction(int tileId, int slot);

        /// <summary>How much of the site in a slot is paid for, 0–1; 1 for a finished building or an empty slot.</summary>
        double ConstructionProgress(int tileId, int slot);

        /// <summary>Everything needed to restore this simulation later (time, buildings, battery, colony).</summary>
        SimulationState CaptureState();

        /// <summary>Replace this simulation's state with a captured one. Returns false (and changes nothing) if it was captured on a different planet layout (tile count or base tile differ).</summary>
        bool RestoreState(SimulationState state);

        // Events (raised during Tick or a command)

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
    }

    /// <summary>What a building slot holds.</summary>
    public enum BuildingType
    {
        None,
        SolarPanel,
        Battery,
    }

    /// <summary>Static facts about one tile.</summary>
    public readonly struct TileInfo
    {
        public readonly int Id;
        /// <summary>Radians, + north.</summary>
        public readonly double Latitude;
        /// <summary>Radians, −π..π, + east.</summary>
        public readonly double Longitude;
        public readonly TerrainType Terrain;
        /// <summary>Relative to sea level, about −1..1.</summary>
        public readonly float Elevation;
        /// <summary>Tile area on the real planet, m².</summary>
        public readonly double AreaM2;

        public TileInfo(int id, double latitude, double longitude, TerrainType terrain, float elevation, double areaM2)
        {
            Id = id;
            Latitude = latitude;
            Longitude = longitude;
            Terrain = terrain;
            Elevation = elevation;
            AreaM2 = areaM2;
        }

        public bool IsWater => Terrains.IsWater(Terrain);
    }

    /// <summary>What one panel on one tile would produce; shown before placing.</summary>
    public readonly struct PanelPreview
    {
        public readonly bool CanPlace;
        /// <summary>Why the panel cannot be placed (empty when it can).</summary>
        public readonly string Reason;
        /// <summary>Energy per Earth year (365.256 days, <see cref="Solar.SolarConstants.EarthYearHours"/>), Wh, on any planet.</summary>
        public readonly double EnergyPerYearWh;
        public readonly double CapacityFactor;
        public readonly double Eroi;
        public readonly double PaybackYears;

        public PanelPreview(bool canPlace, string reason, double energyPerYearWh, double capacityFactor, double eroi, double paybackYears)
        {
            CanPlace = canPlace;
            Reason = reason ?? "";
            EnergyPerYearWh = energyPerYearWh;
            CapacityFactor = capacityFactor;
            Eroi = eroi;
            PaybackYears = paybackYears;
        }
    }
}

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

    /// <summary>Build parts and construction sites (read only).</summary>
    public interface IConstructionReadout
    {
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

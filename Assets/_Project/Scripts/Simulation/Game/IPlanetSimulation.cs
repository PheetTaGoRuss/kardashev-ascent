using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Energy;
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
    public interface IPlanetSimulation : IPlanetReadout, IEnergyReadout, IConstructionReadout, IBuildingCommands, IRobotCommands, IResourceCommands, IManufacturingCommands, ISolarAdvice, IResearchCommands, IPassageCommands, IAnglePresets, IDebugTools, ISimulationSave, ISimulationEvents
    {
        /// <summary>Advance the simulation by <paramref name="hours"/> of game time.</summary>
        void Tick(double hours);


    }

    /// <summary>What a building slot holds.</summary>
    public enum BuildingType
    {
        None,
        SolarPanel,
        Battery,
        /// <summary>Robot Workshop (phase 3b): makes robots, upgrades and refined materials.</summary>
        Workshop,
        /// <summary>Dedicated robot charging station (spec v2 §7).</summary>
        ChargingStation,
        /// <summary>Network relay (workforce v2 stage E): extends the colony network's reach; needs grid power.</summary>
        Relay,
        /// <summary>Solar panels on pontoons over shallow sea or wetland (decided 2026-11-13): 1.5 times the cost, cooler cells.</summary>
        FloatingSolar,
        /// <summary>Research Station (director 2026-11-14): where robots measure the planet's values and run experiments.</summary>
        ResearchStation,
        /// <summary>Refinery module (director 2026-11-15): makes alumina, step 1 of aluminium. Next to a Workshop.</summary>
        AluminaPlant,
        /// <summary>Refinery module: electrolysis (aluminium, high-purity aluminium, sodium). Next to a Workshop.</summary>
        Electrolyser,
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

        /// <summary>Ground a normal building can stand on (not open water, not wetland).</summary>
        public bool IsBuildableLand => Terrains.IsBuildableLand(Terrain);
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

using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What one kind of building is, in one place: its name, how many slots it covers and what it costs when the cost
    /// is a fixed amount. Solar panels and battery yards are priced from the physics (panel area, battery size), so
    /// their <see cref="FixedPartsWh"/> is null. A new building is a new row here, a new <see cref="BuildingType"/>
    /// member (appended: saves store it as an integer), and whatever it does in the simulation.
    /// All sizes and costs are TBD balance placeholders unless a comment elsewhere says otherwise.
    /// </summary>
    public sealed class BuildingDefinition
    {
        public BuildingDefinition(BuildingType type, string name, int footprintSlots, double? fixedPartsWh = null)
        {
            Type = type;
            Name = name;
            FootprintSlots = footprintSlots;
            FixedPartsWh = fixedPartsWh;
        }

        public BuildingType Type { get; }

        /// <summary>The name shown to the player.</summary>
        public string Name { get; }

        /// <summary>Slots covered on one tile (a solar installation grows with its tier; see <see cref="BuildingFootprints"/>).</summary>
        public int FootprintSlots { get; }

        /// <summary>Build parts it costs, Wh, or null when the cost is worked out from physics.</summary>
        public double? FixedPartsWh { get; }
    }

    public static class BuildingCatalog
    {
        static readonly Dictionary<BuildingType, BuildingDefinition> Definitions = new Dictionary<BuildingType, BuildingDefinition>
        {
            { BuildingType.SolarPanel, new BuildingDefinition(BuildingType.SolarPanel, "solar panel", 1) },
            { BuildingType.Battery, new BuildingDefinition(BuildingType.Battery, "battery yard", BuildingFootprints.BatteryYard) },
            { BuildingType.Workshop, new BuildingDefinition(BuildingType.Workshop, "Robot Workshop", BuildingFootprints.Workshop, Robots.RobotTech.WorkshopPartsWh) },
            { BuildingType.ChargingStation, new BuildingDefinition(BuildingType.ChargingStation, "charging station", BuildingFootprints.ChargingStation, Robots.RobotTech.ChargingStationPartsWh) },
            { BuildingType.Relay, new BuildingDefinition(BuildingType.Relay, "relay", BuildingFootprints.Relay, Robots.RobotTech.RelayPartsWh) },
            { BuildingType.FloatingSolar, new BuildingDefinition(BuildingType.FloatingSolar, "floating solar", 1) },
        };

        /// <summary>Every building the player can build.</summary>
        public static IEnumerable<BuildingDefinition> All => Definitions.Values;

        /// <summary>The definition of a building type, or null for <see cref="BuildingType.None"/>.</summary>
        public static BuildingDefinition Get(BuildingType type) => Definitions.TryGetValue(type, out BuildingDefinition d) ? d : null;

        /// <summary>The player-facing name, or "building" for an unknown type.</summary>
        public static string NameOf(BuildingType type) => Get(type)?.Name ?? "building";
    }
}

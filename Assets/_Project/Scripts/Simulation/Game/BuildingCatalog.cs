using System.Collections.Generic;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What one kind of building is, in one place: its name, how many slots it covers and what it costs when the cost
    /// is a fixed bill of materials. Solar panels and battery yards are priced from their size (panel area, battery
    /// capacity: <see cref="BuildCosts"/>), so their <see cref="FixedBill"/> is null. A new building is a new row here, a new <see cref="BuildingType"/>
    /// member (appended: saves store it as an integer), and whatever it does in the simulation.
    /// All sizes and costs are TBD balance placeholders unless a comment elsewhere says otherwise.
    /// </summary>
    public sealed class BuildingDefinition
    {
        public BuildingDefinition(BuildingType type, string name, int footprintSlots, MaterialBill fixedBill = null)
        {
            Type = type;
            Name = name;
            FootprintSlots = footprintSlots;
            FixedBill = fixedBill;
        }

        public BuildingType Type { get; }

        /// <summary>The name shown to the player.</summary>
        public string Name { get; }

        /// <summary>Slots covered on one tile (a solar installation grows with its tier; see <see cref="BuildingFootprints"/>).</summary>
        public int FootprintSlots { get; }

        /// <summary>Materials it costs, or null when the bill is worked out from its size.</summary>
        public MaterialBill FixedBill { get; }
    }

    public static class BuildingCatalog
    {
        static readonly Dictionary<BuildingType, BuildingDefinition> Definitions = new Dictionary<BuildingType, BuildingDefinition>
        {
            { BuildingType.SolarPanel, new BuildingDefinition(BuildingType.SolarPanel, "solar panel", 1) },
            { BuildingType.Battery, new BuildingDefinition(BuildingType.Battery, "battery yard", BuildingFootprints.BatteryYard) },
            { BuildingType.Workshop, new BuildingDefinition(BuildingType.Workshop, "Robot Workshop", BuildingFootprints.Workshop, BuildCosts.Workshop) },
            { BuildingType.ChargingStation, new BuildingDefinition(BuildingType.ChargingStation, "charging station", BuildingFootprints.ChargingStation, BuildCosts.ChargingStation) },
            { BuildingType.Relay, new BuildingDefinition(BuildingType.Relay, "relay", BuildingFootprints.Relay, BuildCosts.Relay) },
            { BuildingType.FloatingSolar, new BuildingDefinition(BuildingType.FloatingSolar, "floating solar", 1) },
            { BuildingType.ResearchStation, new BuildingDefinition(BuildingType.ResearchStation, "Research Station", BuildingFootprints.ResearchStation, BuildCosts.ResearchStation) },
            { BuildingType.AluminaPlant, new BuildingDefinition(BuildingType.AluminaPlant, "Alumina plant", BuildingFootprints.RefineryModule, BuildCosts.AluminaPlant) },
            { BuildingType.WaveConverter, new BuildingDefinition(BuildingType.WaveConverter, "wave converter", BuildingFootprints.WaveConverter, BuildCosts.WaveConverter) },
            { BuildingType.TidalTurbine, new BuildingDefinition(BuildingType.TidalTurbine, "tidal turbine", BuildingFootprints.TidalTurbine, BuildCosts.TidalTurbine) },
            { BuildingType.Electrolyser, new BuildingDefinition(BuildingType.Electrolyser, "Electrolysis cell", BuildingFootprints.RefineryModule, BuildCosts.Electrolyser) },
        };

        /// <summary>Every building the player can build.</summary>
        public static IEnumerable<BuildingDefinition> All => Definitions.Values;

        /// <summary>The definition of a building type, or null for <see cref="BuildingType.None"/>.</summary>
        public static BuildingDefinition Get(BuildingType type) => Definitions.TryGetValue(type, out BuildingDefinition d) ? d : null;

        /// <summary>The player-facing name, or "building" for an unknown type.</summary>
        public static string NameOf(BuildingType type) => Get(type)?.Name ?? "building";
    }
}

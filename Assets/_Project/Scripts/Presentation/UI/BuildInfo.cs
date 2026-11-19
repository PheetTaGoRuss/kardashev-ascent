using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The build bar's categories and the (i) descriptions: what each building does, what it costs, how many slots it
    /// covers and what it draws. Numbers come from the simulation, so they follow the placeholder values.
    /// </summary>
    public static class BuildInfo
    {
        public enum Category { Power, Stations, Industry }

        public static Category CategoryOf(BuildMode m) =>
            m == BuildMode.SolarPanel || m == BuildMode.FloatingSolar || m == BuildMode.Battery || m == BuildMode.TidalTurbine || m == BuildMode.WaveConverter ? Category.Power
            : m == BuildMode.Workshop || m == BuildMode.AluminaPlant || m == BuildMode.Electrolyser ? Category.Industry
            : Category.Stations;

        public static string Title(BuildMode m)
        {
            switch (m)
            {
                case BuildMode.SolarPanel: return "Solar panel";
                case BuildMode.FloatingSolar: return "Floating solar";
                case BuildMode.Battery: return "Battery yard";
                case BuildMode.Workshop: return "Robot Workshop";
                case BuildMode.ChargingStation: return "Charging station";
                case BuildMode.ResearchStation: return "Research Station";
                case BuildMode.AluminaPlant: return "Alumina plant";
                case BuildMode.Electrolyser: return "Electrolysis cell";
                case BuildMode.TidalTurbine: return "Tidal turbine";
                case BuildMode.WaveConverter: return "Wave converter";
                default: return "Network relay";
            }
        }

        /// <summary>
        /// One or two short lines (director 2026-11-15: fewer words; the cost is shown as material icons under it).
        /// </summary>
        public static string Describe(BuildMode m, PanelPlacer placer)
        {
            string what;
            switch (m)
            {
                case BuildMode.SolarPanel:
                    what = "Power from starlight. Tilt and facing matter; bigger tiers at milestones.";
                    break;
                case BuildMode.FloatingSolar:
                    what = "A panel on shallow sea, kept cooler by the water. Built by a boat robot.";
                    break;
                case BuildMode.TidalTurbine:
                    what = Short(BuildingType.TidalTurbine);
                    break;
                case BuildMode.WaveConverter:
                    what = Short(BuildingType.WaveConverter);
                    break;
                case BuildMode.Battery:
                    what = $"Stores {Format.Energy(placer.BatteryUnitWh)} for the night. Robots can charge here.";
                    break;
                case BuildMode.Workshop:
                    what = "Builds robots and upgrades. Refinery modules attach to it. Manufacturing (U).";
                    break;
                case BuildMode.AluminaPlant:
                case BuildMode.Electrolyser:
                    what = Short(m == BuildMode.AluminaPlant ? BuildingType.AluminaPlant : BuildingType.Electrolyser);
                    break;
                case BuildMode.ChargingStation:
                    ChargingCapability c = ChargingCapability.Station;
                    what = $"Charges {c.Slots} robots at {Format.Power(c.PowerW)}. Build it near their work.";
                    break;
                case BuildMode.ResearchStation:
                    what = "Measurements and experiments. Unlocks buildings, removes safety margins. Research (T).";
                    break;
                default:
                    what = $"Network +{ColonyNetwork.RangeTiles} tiles. Uses {Format.Power(ColonyNetwork.RelayPowerW)}; off in a brownout.";
                    break;
            }
            what += $"\nTakes about {placer.BuildHoursOf(m):0} robot-hours to build (a crew is faster).";
            int slots = placer.FootprintOf(m);
            return what + $"\n{slots} slot{(slots == 1 ? "" : "s")} · drag it onto the site · cost (red = short):";
        }

        /// <summary>One or two lines on a building, for the hover card.</summary>
        public static string Short(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.SolarPanel: return "Makes power from starlight; tilt and facing set how much.";
                case BuildingType.FloatingSolar: return "A solar panel on pontoons, cooled by the water.";
                case BuildingType.Battery: return "Stores power for the night; robots can charge here.";
                case BuildingType.Workshop: return "Builds robots and upgrades. Refinery modules attach to its sides.";
                case BuildingType.ChargingStation: return "Charges robots from the grid near their work.";
                case BuildingType.Relay: return "Extends the robot network; needs grid power.";
                case BuildingType.WaveConverter: return "A floating line of hinged floats on shallow sea. Power follows the waves: none in a calm, a cap in a good sea, shut down in a storm. Built by a boat robot.";
                case BuildingType.TidalTurbine: return "A free-flow rotor on shallow sea. Power follows the tide: none at slack water. Coasts with land around them give the strongest stream. Built by a boat robot.";
                case BuildingType.ResearchStation: return "Measurements and experiments (Research, T).";
                case BuildingType.AluminaPlant:
                    return "Refinery module: sinters anorthosite or rock and leaches out alumina (step 1 of aluminium). One batch at a time, 3–4 days each. Must touch a Workshop.";
                case BuildingType.Electrolyser:
                    return "Refinery module: electrolysis. Alumina to aluminium (about a week), salt to sodium, aluminium to 99.99 %. Must touch a Workshop.";
                default: return "";
            }
        }
    }
}

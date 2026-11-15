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
        public enum Category { Power, Stations }

        public static Category CategoryOf(BuildMode m) =>
            m == BuildMode.SolarPanel || m == BuildMode.FloatingSolar || m == BuildMode.Battery ? Category.Power : Category.Stations;

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
                default: return "Network relay";
            }
        }

        /// <summary>The description, with this colony's numbers.</summary>
        public static string Describe(BuildMode m, PanelPlacer placer)
        {
            string what;
            switch (m)
            {
                case BuildMode.SolarPanel:
                    what = "Turns starlight into power. Output depends on the tile's sunlight, the panel's tilt and facing, " +
                           "clouds, heat and the star's spectrum. Bigger panel tiers unlock at milestones.";
                    break;
                case BuildMode.FloatingSolar:
                    what = "A solar panel on shallow sea. Works like a land panel; the water keeps it cooler.";
                    break;
                case BuildMode.Battery:
                    what = $"Stores {Format.Energy(placer.BatteryUnitWh)}: charges from surplus by day and carries the colony " +
                           "through the night. Robots can also charge here.";
                    break;
                case BuildMode.Workshop:
                    what = "Makes new robots, robot upgrades (bigger battery, processors, insulation...) and refines aluminium " +
                           "or sodium from rock. A robot has to work there; order jobs in Manufacturing (U).";
                    break;
                case BuildMode.ChargingStation:
                    ChargingCapability c = ChargingCapability.Station;
                    what = $"Charges up to {c.Slots} robots at once at {Format.Power(c.PowerW)} each, from the grid. Robots drive " +
                           "to the nearest charger, so build one near where they work.";
                    break;
                case BuildMode.ResearchStation:
                    what = "A lab where robots measure the planet's solar values and run experiments: air pressure, water's " +
                           "boiling point, surface temperature, gravity. What they learn unlocks buildings (floating solar needs " +
                           "to know water stays liquid) and removes the safety margins on panels and batteries. Research (T).";
                    break;
                default:
                    what = $"Extends the colony network {ColonyNetwork.RangeTiles} tiles around it, so robots working out there " +
                           $"stay connected. Draws {Format.Power(ColonyNetwork.RelayPowerW)}; down in a brownout. Network (N).";
                    break;
            }
            int slots = placer.FootprintOf(m);
            return what + $"\nTakes: {placer.CostOf(m).Describe()} · covers {slots} slot{(slots == 1 ? "" : "s")}" +
                   (m == BuildMode.FloatingSolar ? "\nBuilt by a robot with a boat hull (Workshop upgrade)." : "") +
                   "\nDrag it from the bar onto the site. Trees and rocks on its slots are cleared.";
        }
    }
}

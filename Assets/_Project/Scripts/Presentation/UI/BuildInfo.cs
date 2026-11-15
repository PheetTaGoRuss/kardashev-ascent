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
                case BuildMode.Battery:
                    what = $"Stores {Format.Energy(placer.BatteryUnitWh)} for the night. Robots can charge here.";
                    break;
                case BuildMode.Workshop:
                    what = "Builds robots and upgrades, refines aluminium and sodium. Manufacturing (U).";
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
            int slots = placer.FootprintOf(m);
            return what + $"\n{slots} slot{(slots == 1 ? "" : "s")} · drag it onto the site · cost (red = short):";
        }
    }
}

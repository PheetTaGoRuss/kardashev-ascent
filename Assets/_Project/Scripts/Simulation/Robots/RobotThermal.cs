using System;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// Robots and the weather (decision KI-43, 2026-11-23). A robot works within an operating range of air temperature. Outside it a heater (or
    /// a cooler on a hot world) runs from its own battery, drawing power in proportion to how far outside it is. Shelter at the lander, a
    /// battery yard, a charging station or a Workshop removes the draw. A robot is destroyed only when its battery is empty while it stands
    /// outside its range with no shelter, so the heater can no longer protect it: the way the rovers Opportunity and InSight ended. Robots never
    /// die on a schedule; on a cold night a robot gets home with margin or it does not.
    /// Every number is a game rule, TBD, from rover practice: electronics and battery are kept between about -40 and +40 C.
    /// </summary>
    public static class RobotThermal
    {
        /// <summary>Lowest air temperature a robot works in without its heater, C (rover practice, recalled, TBD).</summary>
        public const double OperatingMinC = -40.0;

        /// <summary>Highest air temperature a robot works in without cooling, C (rover practice, recalled, TBD).</summary>
        public const double OperatingMaxC = 40.0;

        /// <summary>Heater or cooler power per kelvin outside the range, W/K (game rule, TBD).</summary>
        public const double HeaterWPerK = 2.0;

        /// <summary>How long a robot with an empty battery can stand outside its range before it is destroyed, hours (game rule, TBD).</summary>
        public const double DestroyAfterHours = 1.0;

        /// <summary>The colony is warned when a robot's battery will not last this many hours at its heater draw (a night; game rule, TBD).</summary>
        public const double WarnWithinHours = 12.0;

        /// <summary>The temperature the robot's electronics feel: insulation keeps them at least at the insulated battery temperature.</summary>
        public static double EffectiveC(double airC, bool insulated) => insulated ? Math.Max(airC, RobotTech.InsulatedBatteryC) : airC;

        /// <summary>How many kelvin outside the operating range a temperature is (0 inside).</summary>
        public static double ExcessK(double celsius) => Math.Max(0.0, Math.Max(OperatingMinC - celsius, celsius - OperatingMaxC));

        /// <summary>Heater or cooler power at an air temperature, W.</summary>
        public static double HeaterW(double airC, bool insulated) => HeaterWPerK * ExcessK(EffectiveC(airC, insulated));
    }
}

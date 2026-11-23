using System;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// Robots and the weather (decision KI-43, 2026-11-23; battery range added the same day). A robot works within an operating range of air
    /// temperature, the overlap of what its electronics and its battery tolerate. Outside it a heater (or a cooler on a hot world) runs from its
    /// own battery, drawing power in proportion to how far outside it is; the heater holds whichever limit is reached first. Shelter at the
    /// lander, a battery yard, a charging station or a Workshop removes the draw and warms the battery. A lithium-ion battery cannot be charged
    /// below 0 C (lithium plating), so a cold robot at a charger is warmed first and only then charged. A robot is destroyed only when its
    /// battery is empty while it stands outside its range with no shelter, so the heater can no longer protect it: the way the rovers
    /// Opportunity and InSight ended. Robots never die on a schedule.
    /// Every number is a game rule, TBD.
    /// </summary>
    public static class RobotThermal
    {
        /// <summary>Electronics, lowest and highest: Curiosity keeps its sensitive electronics and instruments between -40 and +50 C [verified: NASA paper on the MSL heat exchangers, NTRS].</summary>
        public const double ElectronicsMinC = -40.0, ElectronicsMaxC = 50.0;

        /// <summary>
        /// Lithium-ion battery while discharging, lowest and highest, C: typical cell datasheets give about -20 to +60 C [recalled, TBD; the Mars
        /// Exploration Rover battery had an operating range of -20 to +30 C, JPL thermal design overview, TFAWS 2005, a narrower, rover-specific value].
        /// </summary>
        public const double BatteryDischargeMinC = -20.0, BatteryDischargeMaxC = 60.0;

        /// <summary>
        /// Lithium-ion battery while charging, lowest and highest, C: about 0 to +45 C on typical datasheets [recalled, TBD]. Below 0 C charging plates
        /// metallic lithium on the graphite, damaging the cell; plating has been seen at -10 C reversibly and -20 C irreversibly, and at 5 C at 1C
        /// [verified: Yang et al., PNAS 115, 2018, "Fast charging of lithium-ion batteries at all temperatures"; Robinson et al., J. Power Sources, UCL].
        /// </summary>
        public const double BatteryChargeMinC = 0.0, BatteryChargeMaxC = 45.0;

        /// <summary>The range a robot works in without its heater or cooler: where electronics and discharging battery both stay inside their limits, C.</summary>
        public static double OperatingMinC => Math.Max(ElectronicsMinC, BatteryDischargeMinC);
        public static double OperatingMaxC => Math.Min(ElectronicsMaxC, BatteryDischargeMaxC);

        /// <summary>Heater or cooler power per kelvin outside the range, W/K (game rule, TBD).</summary>
        public const double HeaterWPerK = 2.0;

        /// <summary>How long a robot with an empty battery can stand outside its range before it is destroyed, hours (game rule, TBD).</summary>
        public const double DestroyAfterHours = 1.0;

        /// <summary>The colony is warned when a robot's battery will not last this many hours at its heater draw (a night; game rule, TBD).</summary>
        public const double WarnWithinHours = 12.0;

        /// <summary>The temperature of a battery in shelter, C (a heated bay; game rule, TBD).</summary>
        public const double ShelterBatteryC = 15.0;

        /// <summary>How fast a battery follows its surroundings, hours (a time constant; game rule, TBD).</summary>
        public const double BatteryThermalTauHours = 1.0;

        /// <summary>The temperature the robot's electronics feel: insulation keeps them at least at the insulated battery temperature.</summary>
        public static double EffectiveC(double airC, bool insulated) => insulated ? Math.Max(airC, RobotTech.InsulatedBatteryC) : airC;

        /// <summary>How many kelvin outside the operating range a temperature is (0 inside).</summary>
        public static double ExcessK(double celsius) => Math.Max(0.0, Math.Max(OperatingMinC - celsius, celsius - OperatingMaxC));

        /// <summary>Heater or cooler power at an air temperature, W.</summary>
        public static double HeaterW(double airC, bool insulated) => HeaterWPerK * ExcessK(EffectiveC(airC, insulated));

        /// <summary>The battery can take a charge at this temperature.</summary>
        public static bool CanCharge(double batteryC) => batteryC >= BatteryChargeMinC && batteryC <= BatteryChargeMaxC;

        /// <summary>The temperature the battery settles toward: the shelter's, the held limit when the heater runs, or the air.</summary>
        public static double BatteryTarget(double airC, bool insulated, bool sheltered, bool heaterRuns)
        {
            if (sheltered) return ShelterBatteryC;
            double eff = EffectiveC(airC, insulated);
            return heaterRuns ? Math.Max(OperatingMinC, Math.Min(OperatingMaxC, eff)) : eff;
        }

        /// <summary>The battery temperature after a step: exponential approach to the target.</summary>
        public static double BatteryStep(double batteryC, double target, double hours) =>
            target + (batteryC - target) * Math.Exp(-hours / BatteryThermalTauHours);
    }
}

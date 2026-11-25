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

        // ---- hibernation and cold or heat soak (S1, decided 2026-11-25) ----
        // A robot with an empty battery outside shelter hibernates: no heater, no cooler. Its body then follows the air with the time constant tau = m cp / G and is destroyed
        // only when its body leaves the non-operating (storage) range, the narrower of the electronics' and the battery's.

        /// <summary>
        /// Storage temperature limits, C. Electronics: -55 to +125 C, the usual military-grade component storage range (MIL-STD-883 and MIL-PRF-38535 qualified parts, Class B). Battery: a lithium
        /// iron phosphate cell is stored between -40 and +60 C (e.g. the A123 Systems ANR26650M1-B datasheet). The robot's limits are the narrower of the two.
        /// </summary>
        public const double ElectronicsStorageMinC = -55.0, ElectronicsStorageMaxC = 125.0, BatteryStorageMinC = -40.0, BatteryStorageMaxC = 60.0;
        public static double StorageMinC => Math.Max(ElectronicsStorageMinC, BatteryStorageMinC);
        public static double StorageMaxC => Math.Min(ElectronicsStorageMaxC, BatteryStorageMaxC);

        /// <summary>
        /// The specific heat of what a robot is made of, kJ per kg per K: steel 0.46, rock (basalt, the hull stand-in) 0.84, aluminium 0.90, copper 0.385, lithium 3.58, sodium 1.23 (CRC Handbook of
        /// Chemistry and Physics, standard values at 25 C).
        /// </summary>
        public static double SpecificHeatKJ(Resources.Material m)
        {
            switch (m)
            {
                case Resources.Material.Iron: return 0.46;
                case Resources.Material.Rock: return 0.84;
                case Resources.Material.Aluminium: return 0.90;
                case Resources.Material.Copper: return 0.385;
                case Resources.Material.Lithium: return 3.58;
                case Resources.Material.Sodium: return 1.23;
                default: return 0.84;
            }
        }

        /// <summary>The heat capacity of a robot, J/K: the mass-weighted specific heat of its bill.</summary>
        public static double BodyHeatCapacityJPerK
        {
            get { double c = 0.0; foreach (var (m, kg) in WreckModel.BillOf(RobotKind.General, false)) c += kg * SpecificHeatKJ(m) * 1000.0; return c; }
        }

        /// <summary>The time constant of a hibernating robot, hours: tau = m cp / G with G the loss conductance the heater rule implies (<see cref="HeaterWPerK"/>, times the wind factor).</summary>
        public static double BodyTauHours(double windFactor = 1.0) => BodyHeatCapacityJPerK / (HeaterWPerK * Math.Max(0.1, windFactor)) / 3600.0;

        /// <summary>Hours until a hibernating body at <paramref name="bodyC"/> reaches the storage limit in air held at <paramref name="airC"/>: infinity when the air is within the limits.</summary>
        public static double HibernationHoursUntilLimit(double bodyC, double airC, double tauHours)
        {
            double limit = airC < (StorageMinC + StorageMaxC) / 2.0 ? StorageMinC : StorageMaxC;
            if (airC >= StorageMinC && airC <= StorageMaxC) return double.PositiveInfinity;
            double now = bodyC - airC, end = limit - airC;
            if (Math.Abs(now) <= Math.Abs(end) && Math.Sign(now) == Math.Sign(end)) return 0.0;   // already past the limit
            if (Math.Sign(now) != Math.Sign(end)) return double.PositiveInfinity;
            return tauHours * Math.Log(now / end);
        }

        /// <summary>One step of a hibernating body toward the air temperature.</summary>
        public static double BodyStep(double bodyC, double airC, double hours, double tauHours) => airC + (bodyC - airC) * Math.Exp(-hours / Math.Max(1e-6, tauHours));

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

        /// <summary>
        /// Wind chill on the heater (Weather, decided 2026-11-24). The convective heat-transfer coefficient of a body in wind follows the Siple and Passel
        /// formula h = 10.45 - v + 10 sqrt(v) (kcal per m2 per hour per degree, v in m/s; Siple and Passel 1945, "Measurements of dry atmospheric cooling in
        /// subfreezing temperatures"), so the loss grows by h(v)/h(0) with the wind. Game rules, TBD: the wind is capped at 20 m/s (the formula is fitted to
        /// walkers' winds); convection is 70 % of a robot's loss (the rest is radiation, which wind does not change); in thin air the convective gain is
        /// scaled by the square root of the pressure (forced convection falls with air density). The heater constant is for a reference wind of 4 m/s, so a
        /// planet with its usual wind keeps its old balance and only gusts and storms cost more.
        /// </summary>
        public const double ReferenceWindMs = 4.0, MaxWindMs = 20.0, ConvectiveShare = 0.7;

        public static double SiplePassel(double windMs)
        {
            double v = Math.Max(0.0, Math.Min(MaxWindMs, windMs));
            return (10.45 - v + 10.0 * Math.Sqrt(v)) / 10.45;
        }

        static double RawWindFactor(double windMs, double pressureEarth)
        {
            double conv = 1.0 + (SiplePassel(windMs) - 1.0) * Math.Sqrt(Math.Max(0.0, Math.Min(1.0, pressureEarth)));
            return (1.0 - ConvectiveShare) + ConvectiveShare * conv;
        }

        /// <summary>The factor on the heater for this wind, 1 at the reference wind.</summary>
        public static double WindFactor(double windMs, double pressureEarth) => RawWindFactor(windMs, pressureEarth) / RawWindFactor(ReferenceWindMs, pressureEarth);

        /// <summary>Heater or cooler power at an air temperature with a wind factor, W.</summary>
        public static double HeaterW(double airC, bool insulated, double windFactor) => HeaterWPerK * ExcessK(EffectiveC(airC, insulated)) * windFactor;

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

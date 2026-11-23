using System;

namespace KardashevAscent.Simulation.Electrical
{
    /// <summary>
    /// Simplified cable heating and ageing (electrical redesign stage 8, W-31 and W-32). One lumped temperature per link:
    /// C dT/dt = I^2 R - (T - T_air) / Rth, solved exactly over a step. Not a thermal-fluid model and not IEC 60287; the form is the
    /// standard first-order thermal circuit [form verified: cable thermal circuits as in IEC 60287]. Everything numeric below that is
    /// not marked verified is a game rule, TBD.
    /// </summary>
    public static class CableThermal
    {
        /// <summary>Highest continuous conductor temperature of XLPE cable, C [verified: IEC 60840 and 62067, via cable-engineering sources].</summary>
        public const double MaxConductorC = 90.0;

        /// <summary>
        /// Insulation life halves for each this many kelvin above the limit: the empirical 8 to 10 K rule of thumb [verified as a rule of
        /// thumb: Montsinger, later 10-degree rule; less accurate than Arrhenius]. The game takes 10 K (game rule, TBD).
        /// </summary>
        public const double LifeHalvingK = 10.0;

        /// <summary>Design life at the temperature limit, hours: 40 years [verified as the usual XLPE design life; game value TBD].</summary>
        public const double DesignLifeHours = 40.0 * 8766.0;

        /// <summary>Thermal resistance from the conductor to the surroundings per metre of cable in Earth air, K m / W (game rule, TBD; buried cable is about 0.5 to 1).</summary>
        public const double ThermalResistanceKmPerW = 0.5;

        /// <summary>
        /// Heat leaving a cable by radiation, W per metre per kelvin (game rule, TBD): the only way out when there is no air (decision of
        /// 2026-11-23). The rest of the Earth-air conductance, 1/0.5 - this, is convection and conduction and scales with the relative air
        /// density (capped at 1).
        /// </summary>
        public const double RadiationConductanceWPerMK = 0.2;

        /// <summary>Thermal resistance at a relative air density, K m / W: radiation plus an air path that thins with the air. 0.5 at density 1.</summary>
        public static double ThermalResistance(double airDensity)
        {
            double air = 1.0 / ThermalResistanceKmPerW - RadiationConductanceWPerMK;
            return 1.0 / (RadiationConductanceWPerMK + air * Math.Max(0.0, Math.Min(1.0, airDensity)));
        }

        /// <summary>Specific heat of copper, J/(kg K) [recalled, TBD]; the conductor's heat capacity stands for the whole cable.</summary>
        public const double CopperSpecificHeat = 385.0;

        /// <summary>Heat capacity per metre of a spec, J/(m K): conductor mass per metre times specific heat (aluminium uses 900 J/(kg K) [recalled, TBD]).</summary>
        public static double HeatCapacityPerM(CableSpec spec) =>
            spec.Material.Density * spec.AreaM2 * spec.ConductorsInLoop * (spec.Material == ConductorMaterial.Aluminium ? 900.0 : CopperSpecificHeat);

        /// <summary>Heating per metre at a current, W/m: I^2 rho(T) / A per conductor, times the conductors in the loop.</summary>
        public static double HeatPerM(CableSpec spec, double currentA, double celsius) => currentA * currentA * spec.Material.Rho(celsius) / spec.AreaM2 * spec.ConductorsInLoop;

        /// <summary>Temperature the cable settles at, C, for a steady heating per metre.</summary>
        public static double SteadyCelsius(double airC, double heatPerMW, double airDensity = 1.0) => airC + heatPerMW * ThermalResistance(airDensity);

        /// <summary>Time constant of the link, hours (it does not depend on length: both the resistance and the capacity scale with it).</summary>
        public static double TimeConstantHours(CableSpec spec, double airDensity = 1.0) => ThermalResistance(airDensity) * HeatCapacityPerM(spec) / 3600.0;

        /// <summary>The temperature after dt hours, moving exponentially toward the steady value.</summary>
        public static double Step(double celsius, double airC, double heatPerMW, CableSpec spec, double dtHours, double airDensity = 1.0)
        {
            double target = SteadyCelsius(airC, heatPerMW, airDensity);
            double k = Math.Exp(-dtHours / TimeConstantHours(spec, airDensity));
            return target + (celsius - target) * k;
        }

        /// <summary>
        /// How fast the insulation ages, as a multiple of its design rate: 1 at the temperature limit, 2 per 10 K above, 1/2 per 10 K below
        /// (the same rule both ways: a cool cable lasts longer).
        /// </summary>
        public static double AgeingFactor(double celsius) => Math.Pow(2.0, (celsius - MaxConductorC) / LifeHalvingK);

        /// <summary>Condition lost in dt hours: the share of the design life used.</summary>
        public static double ConditionLoss(double celsius, double dtHours) => AgeingFactor(celsius) * dtHours / DesignLifeHours;

        /// <summary>The conductor is hotter than its continuous limit.</summary>
        public static bool Overheated(double celsius) => celsius > MaxConductorC;
    }
}

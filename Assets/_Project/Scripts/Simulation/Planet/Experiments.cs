using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The experiments a Research Station runs to learn what is safe and doable on this planet (director 2026-11-14).
    /// Saved as integers: append new members at the end only.
    /// </summary>
    public enum ExperimentKind { AirPressure, BoilingPoint, SurfaceTemperature, Gravity, SeismicSurvey }

    /// <summary>
    /// The physics behind each experiment, and what each one needs. Sources are in
    /// <c>docs/physics/physics-verification.md</c> (Research Station section).
    /// </summary>
    public static class Experiments
    {
        public static readonly ExperimentKind[] All =
            { ExperimentKind.AirPressure, ExperimentKind.BoilingPoint, ExperimentKind.SurfaceTemperature, ExperimentKind.Gravity, ExperimentKind.SeismicSurvey };

        /// <summary>Standard atmosphere at sea level, Pa (ISO 2533 / US Standard Atmosphere 1976).</summary>
        public const double StandardPressurePa = 101_325.0;
        /// <summary>Antoine constants for water, pressure in mmHg and temperature in °C (Dortmund Data Bank values,
        /// as tabulated in the literature): 1–100 °C and 99–374 °C.</summary>
        public const double AntoineLowA = 8.07131, AntoineLowB = 1730.63, AntoineLowC = 233.426;
        public const double AntoineHighA = 8.14019, AntoineHighB = 1810.94, AntoineHighC = 244.485;
        /// <summary>One mmHg in Pa (101 325 / 760).</summary>
        public const double PaPerMmHg = StandardPressurePa / 760.0;
        /// <summary>Water's triple point pressure, Pa (IAPWS: 611.657 Pa). Below it liquid water cannot exist: ice turns straight to vapour.</summary>
        public const double WaterTriplePointPa = 611.657;
        /// <summary>Water's triple point temperature, °C (0.01 °C by definition of the old Kelvin scale; IAPWS).</summary>
        public const double WaterTriplePointC = 0.01;
        /// <summary>Length of the station's test pendulum, m (game rule: a lab pendulum).</summary>
        public const double PendulumLengthM = 1.0;

        public static string Name(ExperimentKind k)
        {
            switch (k)
            {
                case ExperimentKind.AirPressure: return "Air pressure";
                case ExperimentKind.BoilingPoint: return "Water boiling point";
                case ExperimentKind.SurfaceTemperature: return "Surface temperature";
                case ExperimentKind.SeismicSurvey: return "Seismic survey";
                default: return "Gravity";
            }
        }

        /// <summary>One line on what the experiment is, for the player.</summary>
        public static string What(ExperimentKind k)
        {
            switch (k)
            {
                case ExperimentKind.AirPressure: return "A barometer reading of the air's weight. Needed before water can be boiled.";
                case ExperimentKind.BoilingPoint: return "Heat water in the lab at this air pressure. Says whether water stays liquid here: floating builds, cooling, steam.";
                case ExperimentKind.SurfaceTemperature: return "Log the ground's coldest and warmest months. Says whether batteries and machines are safe here.";
                case ExperimentKind.SeismicSurvey: return "Thump the ground and time the echoes from deep rock layers (a reflection survey, as oil and mining geologists do). Finds deep ore: rare earths in mountains, canyons and caves.";
                default: return "Time a 1 m pendulum's swing. Gravity sets how hard robots work to drive and how tall things can stand.";
            }
        }

        /// <summary>Robot hours at the station (game rule, TBD).</summary>
        public static double WorkHours(ExperimentKind k)
        {
            switch (k)
            {
                case ExperimentKind.AirPressure: return 4.0;
                case ExperimentKind.BoilingPoint: return 8.0;
                case ExperimentKind.SurfaceTemperature: return 24.0;
                case ExperimentKind.SeismicSurvey: return 16.0;
                default: return 4.0;
            }
        }

        /// <summary>The experiment that has to be done first, or null.</summary>
        /// <remarks>
        /// The seismic survey needs gravity first: rock density from the echoes and the planet's g together say what lies
        /// below (gravity and seismic surveys are read together in exploration geophysics). Game rule.
        /// </remarks>
        public static ExperimentKind? Needs(ExperimentKind k) =>
            k == ExperimentKind.BoilingPoint ? ExperimentKind.AirPressure
            : k == ExperimentKind.SeismicSurvey ? ExperimentKind.Gravity
            : (ExperimentKind?)null;

        /// <summary>
        /// Every experiment needs the Research Station's tile surveyed first (director 2026-11-15): the lab's instruments
        /// stand on bedrock the survey has cored, and the seismic survey starts from those cores. Game rule.
        /// </summary>
        public const bool NeedsSurveyedStation = true;

        // ---- physics ----

        /// <summary>Surface pressure, Pa, from the planet's pressure in Earth atmospheres.</summary>
        public static double PressurePa(double pressureEarth) => Math.Max(0.0, pressureEarth) * StandardPressurePa;

        /// <summary>
        /// Water's boiling point, °C, at <paramref name="pressurePa"/>: the Antoine equation solved for temperature,
        /// T = B / (A − log₁₀ P) − C, P in mmHg, with the low-temperature constants up to 1 atm and the high ones above.
        /// Within about 0.15 °C of steam tables from 1 to 200 kPa, and 0.25 °C at 1 MPa. NaN below the triple point, where water cannot be
        /// liquid at any temperature.
        /// </summary>
        public static double WaterBoilingPointC(double pressurePa)
        {
            if (!(pressurePa > WaterTriplePointPa))
                return double.NaN;
            double logP = Math.Log10(pressurePa / PaPerMmHg);
            return pressurePa <= StandardPressurePa
                ? AntoineLowB / (AntoineLowA - logP) - AntoineLowC
                : AntoineHighB / (AntoineHighA - logP) - AntoineHighC;
        }

        /// <summary>Period of a small-swing pendulum, s: T = 2π √(L / g).</summary>
        public static double PendulumPeriodS(double lengthM, double gravityMs2) =>
            gravityMs2 > 0.0 ? 2.0 * Math.PI * Math.Sqrt(lengthM / gravityMs2) : double.PositiveInfinity;

        /// <summary>Gravity from a pendulum's period: g = 4π² L / T².</summary>
        public static double GravityFromPeriod(double lengthM, double periodS) =>
            periodS > 0.0 ? 4.0 * Math.PI * Math.PI * lengthM / (periodS * periodS) : 0.0;

        /// <summary>Whether liquid water can exist between these temperatures at this pressure (above freezing, below boiling).</summary>
        public static bool WaterStaysLiquid(double pressurePa, double coldestC, double warmestC)
        {
            double boil = WaterBoilingPointC(pressurePa);
            return !double.IsNaN(boil) && warmestC < boil && coldestC > 0.0;
        }
    }

    /// <summary>
    /// What a colony knows before it lands, from orbit and the NASA records (director 2026-11-14): rough values with
    /// wide error bars, and whether robots can work there. The error bars are game rules (TBD).
    /// </summary>
    public static class OrbitalBrief
    {
        /// <summary>± °C on the planet's mean temperature seen from orbit (game rule, TBD).</summary>
        public const double TemperatureSpreadC = 15.0;
        /// <summary>± share on surface gravity from mass and radius (game rule, TBD).</summary>
        public const double GravitySpreadShare = 0.10;
        /// <summary>± share on surface pressure from orbit (game rule, TBD).</summary>
        public const double PressureSpreadShare = 0.50;

        // Robot working range. A robot runs on lithium iron phosphate (LFP) cells and industrial electronics:
        // LFP cells charge between 0 and 45 °C and discharge between −20 and 60 °C (typical LFP cell datasheets, e.g.
        // A123 ANR26650); industrial-grade electronics are rated −40 to +85 °C. Game rule built on those ranges, TBD.
        public const double ChargeMinC = 0.0, ChargeMaxC = 45.0;
        public const double SurviveMinC = -40.0, SurviveMaxC = 85.0;

        public enum RobotVerdict { Works, NeedsHeatingOrCooling, CannotSurvive }

        /// <summary>Whether robots can work at this mean temperature.</summary>
        public static RobotVerdict Robots(double meanTemperatureC)
        {
            if (meanTemperatureC >= ChargeMinC && meanTemperatureC <= ChargeMaxC)
                return RobotVerdict.Works;
            if (meanTemperatureC >= SurviveMinC && meanTemperatureC <= SurviveMaxC)
                return RobotVerdict.NeedsHeatingOrCooling;
            return RobotVerdict.CannotSurvive;
        }

        public static string Say(RobotVerdict v)
        {
            switch (v)
            {
                case RobotVerdict.Works: return "Robots can work here.";
                case RobotVerdict.NeedsHeatingOrCooling: return "Robots survive, but their batteries need heating or cooling to charge.";
                default: return "Too hot or cold: robots would not survive outside.";
            }
        }
    }
}

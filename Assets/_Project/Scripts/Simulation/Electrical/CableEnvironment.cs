using System;

namespace KardashevAscent.Simulation.Electrical
{
    /// <summary>
    /// How a planet shapes its cables (electrical redesign stage 6, W-26 to W-28). Pure functions.
    /// </summary>
    public static class CableEnvironment
    {
        /// <summary>Standard atmosphere, Pa.</summary>
        public const double StandardPressurePa = 101325.0;
        /// <summary>Peek's reference temperature, K (25 C).</summary>
        public const double ReferenceKelvin = 298.15;

        /// <summary>
        /// Relative air density delta = (p / p0) (T0 / T), with p0 = 76 cmHg and T0 = 25 C (the reference conditions of Peek's law)
        /// [verified: Wikipedia, Peek's law; MDPI Energies 11(7) 1908].
        /// </summary>
        public static double RelativeAirDensity(double pressurePa, double celsius) =>
            pressurePa / StandardPressurePa * ReferenceKelvin / (celsius + 273.15);

        /// <summary>
        /// Peek's law: the field at a round conductor's surface where corona starts, peak kV/cm:
        /// E = 30 m delta (1 + 0.301 / sqrt(delta r)), r in cm, m a surface factor (1 for a polished wire, about 0.8 for a stranded one)
        /// [verified form: Wikipedia, Peek's law; the constant is 0.301 cm^1/2 in kV/cm units, other papers use 0.308].
        /// Only meaningful in air-like gas; <see cref="PeekValid"/> says where the game trusts it.
        /// </summary>
        public static double CoronaOnsetKvPerCm(double delta, double radiusCm, double surfaceFactor = 0.8) =>
            30.0 * surfaceFactor * delta * (1.0 + 0.301 / Math.Sqrt(delta * radiusCm));

        /// <summary>Radius of a round solid conductor of this area, cm.</summary>
        public static double RadiusCm(double areaM2) => Math.Sqrt(areaM2 / Math.PI) * 100.0;

        /// <summary>
        /// Range of relative air density where the game uses Peek's law (game rule, TBD): about a tenth of Earth's air up to twice.
        /// Outside it (thin air, near vacuum, a different gas) the law is not valid and a labelled fallback applies.
        /// </summary>
        public static bool PeekValid(double delta) => delta >= 0.1 && delta <= 2.0;

        /// <summary>Below this relative air density there is effectively no gas to ionise (game rule, TBD): no corona.</summary>
        public const double VacuumBelowDelta = 1e-6;

        public enum Regime { Peek, Paschen, Vacuum }

        /// <summary>
        /// Which law governs breakdown (director 2026-11-23): Peek's law in air-like density (0.1 to 2); the Paschen region, where thin gas
        /// breaks down at the lowest voltage (Mars-like air; game rule, labelled), from 1e-6 to 0.1; and vacuum below that, where there is
        /// nothing to ionise and no corona.
        /// </summary>
        public static Regime RegimeOf(double delta) => delta >= 0.1 ? Regime.Peek : delta >= VacuumBelowDelta ? Regime.Paschen : Regime.Vacuum;

        /// <summary>
        /// The share of a cable's standard voltage class that may be used on this planet's air: 1 at Earth sea level (delta = 1, 25 C),
        /// lower in thin air, above 1 in dense air, capped at 1 (a cable never runs above its rating). Outside Peek's validity the
        /// fallback is proportional to delta, floored at 0.1 (game rule, TBD: no source; airless worlds are an open question).
        /// </summary>
        public static double VoltageDerate(double delta, double areaM2, out bool fallback)
        {
            Regime regime = RegimeOf(delta);
            fallback = regime == Regime.Paschen;
            if (regime == Regime.Vacuum) return 1.0;   // no gas, no corona
            if (fallback) return Math.Max(0.1, Math.Min(1.0, delta));
            double r = RadiusCm(areaM2);
            double ratio = CoronaOnsetKvPerCm(delta, r) / CoronaOnsetKvPerCm(1.0, r);
            return Math.Min(1.0, ratio);
        }
    }

    /// <summary>Sea crossings (W-28): which cables are submarine, and how far alternating current can go under water.</summary>
    public static class SubseaRules
    {
        /// <summary>
        /// Practical limit of an AC submarine cable, m: cable capacitance draws a charging current that eats the cable's capacity, so
        /// beyond roughly 50 to 100 km HVDC is used [verified: industry guides; Lauria et al. 2016 (about 80 km break-even; the longest
        /// AC subsea cable in service, Malta-Sicily, is 120 km); SINTEF presentation (50-100 km)]. The game takes 100 km (game rule, TBD).
        /// </summary>
        public const double MaxAcLengthM = 100_000.0;

        /// <summary>Null when the link is allowed; otherwise why not. DC has no such limit.</summary>
        public static string Check(NetworkType type, double lengthM, bool touchesSea)
        {
            if (!touchesSea || type != NetworkType.Ac || lengthM <= MaxAcLengthM) return null;
            return $"An alternating-current cable cannot run {lengthM / 1000.0:0} km under water: its charging current uses up the cable. " +
                   $"Beyond about {MaxAcLengthM / 1000.0:0} km the sea link must be direct current.";
        }
    }
}

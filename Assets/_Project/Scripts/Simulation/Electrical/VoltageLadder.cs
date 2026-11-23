namespace KardashevAscent.Simulation.Electrical
{
    /// <summary>
    /// The voltage ladder and the converter catalogue (director, confirmed 2026-11-23: "apply them all"; recorded in the feature registry).
    /// One ladder to learn: DC 24 V (robots and tools only), 400 V, 33 kV, 500 kV; AC 400 V, 33 kV, 500 kV at 50 Hz only (the IEC
    /// standard, and the frequency Thailand uses; EGAT runs 500 kV AC). Class ids are the voltage in volts so a class says what it is.
    /// All efficiencies are TBD until tuned; the figures are recalled from published ranges and must be checked before tuning.
    /// </summary>
    public static class VoltageLadder
    {
        /// <summary>The one AC frequency, Hz. No 60 Hz option.</summary>
        public const double AcFrequencyHz = 50.0;

        public const int Dc24 = 24, Dc400 = 400, Dc33k = 33_000, Dc500k = 500_000;
        public const int Ac400 = 400, Ac33k = 33_000, Ac500k = 500_000;

        /// <summary>The DC classes, lowest first. 24 V is DC-only.</summary>
        public static readonly int[] DcClasses = { Dc24, Dc400, Dc33k, Dc500k };

        /// <summary>The AC classes, lowest first.</summary>
        public static readonly int[] AcClasses = { Ac400, Ac33k, Ac500k };

        /// <summary>Machines under this voltage are DC-only (robots and tools).</summary>
        public static bool IsDcOnly(int volts) => volts <= Dc24;

        /// <summary>Converters on the ladder (game rule: efficiencies TBD; each is a published range, not a measurement).</summary>
        public static class Converters
        {
            /// <summary>Power transformer, 33 kV to 500 kV and back: 99.5 % (large power transformers are 99.5 to 99.8 % [recalled, TBD]).</summary>
            public static ConverterSpec PowerTransformer(bool stepUp) => new ConverterSpec("Power transformer", ConverterKind.Transformer, stepUp ? Ac33k : Ac500k, stepUp ? Ac500k : Ac33k, AcFrequencyHz, 0.995);

            /// <summary>Distribution transformer, 400 V to 33 kV and back: 98.5 % [recalled, TBD].</summary>
            public static ConverterSpec DistributionTransformer(bool stepUp) => new ConverterSpec("Distribution transformer", ConverterKind.Transformer, stepUp ? Ac400 : Ac33k, stepUp ? Ac33k : Ac400, AcFrequencyHz, 0.985);

            /// <summary>400 V inverter (DC to AC): 97 % (CEC-weighted efficiency of solar inverters [recalled, TBD]).</summary>
            public static ConverterSpec Inverter400() => new ConverterSpec("400 V inverter", ConverterKind.Inverter, Dc400, Ac400, AcFrequencyHz, 0.97);

            /// <summary>400 V rectifier (AC to DC): 97 % [recalled, TBD].</summary>
            public static ConverterSpec Rectifier400() => new ConverterSpec("400 V rectifier", ConverterKind.Rectifier, Ac400, Dc400, AcFrequencyHz, 0.97);

            /// <summary>33 kV converter, either way: 98.5 % [recalled, TBD].</summary>
            public static ConverterSpec Inverter33k() => new ConverterSpec("33 kV inverter", ConverterKind.Inverter, Dc33k, Ac33k, AcFrequencyHz, 0.985);
            public static ConverterSpec Rectifier33k() => new ConverterSpec("33 kV rectifier", ConverterKind.Rectifier, Ac33k, Dc33k, AcFrequencyHz, 0.985);

            /// <summary>500 kV HVDC converter station: 99 %, about 1 % loss per station (voltage-sourced converters, ABB and Hitachi figures [recalled, TBD]).</summary>
            public static ConverterSpec Inverter500k() => new ConverterSpec("500 kV HVDC station (inverter)", ConverterKind.Inverter, Dc500k, Ac500k, AcFrequencyHz, 0.99);
            public static ConverterSpec Rectifier500k() => new ConverterSpec("500 kV HVDC station (rectifier)", ConverterKind.Rectifier, Ac500k, Dc500k, AcFrequencyHz, 0.99);

            /// <summary>DC-DC between neighbouring DC classes: 98 % (game rule, TBD).</summary>
            public static ConverterSpec DcDc(int fromClass, int toClass) => new ConverterSpec("DC-DC converter", ConverterKind.DcDc, fromClass, toClass, 0.0, 0.98);
        }
    }
}

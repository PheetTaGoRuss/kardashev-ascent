using System;

namespace KardashevAscent.Simulation.Logistics
{
    /// <summary>
    /// What the storage buildings hold (mission operations 3.3, MO-05). The gas tank is sized by the ideal gas law with a
    /// compressibility factor: n = pV / (Z R T), so mass = p V M / (Z R T). The other capacities are game rules, TBD.
    /// Sources and the open numbers are in docs/physics/physics-verification.md ("Mission operations: sources").
    /// </summary>
    public static class StorageSpecs
    {
        /// <summary>Universal gas constant, J/(mol K).</summary>
        public const double GasConstant = 8.314462618;

        /// <summary>Storage yard: kg of bulk solids on its two slots. Game rule, TBD.</summary>
        public const double YardCapacityKg = 100_000.0;

        /// <summary>Sealed store: kg of liquids and hazardous solids (sodium, lithium). Game rule, TBD.</summary>
        public const double SealedStoreCapacityKg = 20_000.0;

        /// <summary>Gas tank volume, m3, and filling pressure, Pa (20 MPa, a standard high-pressure cylinder bank). Game rule, TBD.</summary>
        public const double TankVolumeM3 = 10.0;
        public const double TankPressurePa = 20e6;

        /// <summary>Temperature the tank is sized at, K (20 C).</summary>
        public const double TankTemperatureK = 293.15;

        /// <summary>The gas the capacity is quoted for: oxygen, 0.031998 kg/mol. Chlorine is liquefied and goes in a sealed store instead.</summary>
        public const double ReferenceMolarMassKg = 0.031998;

        /// <summary>Compressibility factor of oxygen at 20 MPa and 293 K, about 0.95 (to verify; recalled from tables).</summary>
        public const double ReferenceCompressibility = 0.95;

        /// <summary>Mass of gas in a vessel, kg: p V M / (Z R T).</summary>
        public static double GasMassKg(double volumeM3, double pressurePa, double molarMassKg, double temperatureK, double compressibility)
        {
            if (temperatureK <= 0.0 || compressibility <= 0.0) throw new ArgumentOutOfRangeException(nameof(temperatureK));
            return pressurePa * volumeM3 * molarMassKg / (compressibility * GasConstant * temperatureK);
        }

        /// <summary>What one gas tank holds, kg.</summary>
        public static double TankCapacityKg =>
            GasMassKg(TankVolumeM3, TankPressurePa, ReferenceMolarMassKg, TankTemperatureK, ReferenceCompressibility);
    }
}

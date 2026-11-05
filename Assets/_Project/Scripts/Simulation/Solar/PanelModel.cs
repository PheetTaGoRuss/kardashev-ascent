using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Steps 4, 6 and 7 of the solar model: what one panel collects and produces.</summary>
    public static class PanelModel
    {
        /// <summary>Panel normal in (east, north, up). Tilt β from horizontal, azimuth γ clockwise from north.</summary>
        public static Double3 Normal(double tilt, double azimuth)
        {
            double sb = Math.Sin(tilt);
            return new Double3(sb * Math.Sin(azimuth), sb * Math.Cos(azimuth), Math.Cos(tilt));
        }

        /// <summary>cos θ = max(0, n̂·ŝ).</summary>
        public static double CosIncidence(Double3 normal, Double3 sun) => Math.Max(0.0, normal.Dot(sun));

        /// <summary>
        /// Plane-of-array irradiance, isotropic sky:
        /// f_shade·DNI·cos θ + DHI·(1 + cos β)/2 + ρ_g·GHI·(1 − cos β)/2.
        /// f_shade is 0 when the panel is in shadow, 1 in full sun.
        /// </summary>
        public static double PlaneOfArray(double dni, double dhi, double ghi, double cosIncidence,
                                          double tilt, double albedo, double fShade = 1.0)
        {
            double ct = Math.Cos(tilt);
            return fShade * dni * cosIncidence
                   + dhi * (1.0 + ct) / 2.0
                   + albedo * ghi * (1.0 - ct) / 2.0;
        }

        /// <summary>T_cell = T_air + (NOCT − 20)/800 · POA, °C.</summary>
        public static double CellTemperature(double airTemperature, double poa, double noct = 45.0) =>
            airTemperature + (noct - 20.0) / 800.0 * poa;

        /// <summary>
        /// P = POA·A·η·[1 + γ(T_cell − 25)]·f_spec·(1 − f_dust), W.
        /// γ is the temperature coefficient (silicon ≈ −0.004 /°C); f_dust is the fraction of light dust blocks.
        /// </summary>
        public static double Power(double poa, double area, double efficiency, double cellTemperature,
                                   double tempCoefficient = -0.004, double fSpec = 1.0, double fDust = 0.0) =>
            poa * area * efficiency * (1.0 + tempCoefficient * (cellTemperature - 25.0)) * fSpec * (1.0 - fDust);
    }
}

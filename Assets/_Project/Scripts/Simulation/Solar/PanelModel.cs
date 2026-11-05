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

        /// <summary>T_cell = T_air + (NOCT − 20)/800 · POA, °C. Assumes Earth-like air and 1 m/s wind.</summary>
        public static double CellTemperature(double airTemperature, double poa, double noct = 45.0) =>
            airTemperature + (noct - 20.0) / 800.0 * poa;

        /// <summary>Front absorptance α of a cell for the radiative model.</summary>
        public const double RadiativeAbsorptance = 0.9;

        /// <summary>Thermal emissivity ε of glass front and back sheet.</summary>
        public const double RadiativeEmissivity = 0.85;

        /// <summary>Below this surface pressure (× Earth's) panels cool only by radiation.</summary>
        public const double VacuumPressureEarth = 0.01;

        /// <summary>Above this surface pressure (× Earth's) the NOCT (wind-cooled) model applies fully.</summary>
        public const double AirPressureEarth = 0.1;

        /// <summary>
        /// Cell temperature with no air (docs/physics/physics-verification.md, P0 item 6), °C:
        /// absorbed light that is not turned into electricity is radiated from both faces,
        /// α·G·(1 − η) = (ε_f + ε_b)·σ·T⁴. 1361 W/m² with the defaults → 317.5 K (44.4 °C).
        /// Pass emissivityBack = 0 for a panel lying on insulating ground (377.6 K at 1361 W/m²).
        /// </summary>
        public static double RadiativeCellTemperature(double poa, double efficiency,
                                                      double absorptance = RadiativeAbsorptance,
                                                      double emissivityFront = RadiativeEmissivity,
                                                      double emissivityBack = RadiativeEmissivity)
        {
            double absorbed = Math.Max(0.0, absorptance * poa * (1.0 - efficiency));
            return Math.Pow(absorbed / ((emissivityFront + emissivityBack) * StefanBoltzmann), 0.25) - 273.15;
        }

        /// <summary>
        /// Share of the wind-cooled (NOCT) model, 0–1, from surface pressure in multiples of Earth's:
        /// 0 at ≤ 0.01 (Mars, Moon), 1 at ≥ 0.1, straight line in log₁₀ pressure between. A game rule [inference].
        /// </summary>
        public static double ConvectiveWeight(double surfacePressureEarth)
        {
            if (surfacePressureEarth <= VacuumPressureEarth)
                return 0.0;
            if (surfacePressureEarth >= AirPressureEarth)
                return 1.0;
            return Math.Log10(surfacePressureEarth / VacuumPressureEarth) / Math.Log10(AirPressureEarth / VacuumPressureEarth);
        }

        /// <summary>
        /// Cell temperature on any planet, °C: NOCT in thick air, radiative balance in vacuum,
        /// blended by <see cref="ConvectiveWeight"/> in thin air.
        /// </summary>
        public static double CellTemperature(double airTemperature, double poa, double noct,
                                             double surfacePressureEarth, double efficiency)
        {
            double w = ConvectiveWeight(surfacePressureEarth);
            if (w >= 1.0)
                return CellTemperature(airTemperature, poa, noct);
            double radiative = RadiativeCellTemperature(poa, efficiency);
            return w <= 0.0 ? radiative : w * CellTemperature(airTemperature, poa, noct) + (1.0 - w) * radiative;
        }

        const double StefanBoltzmann = 5.670374419e-8;

        /// <summary>
        /// P = POA·A·η·[1 + γ(T_cell − 25)]·f_spec·(1 − f_dust), W.
        /// γ is the temperature coefficient (silicon ≈ −0.004 /°C); f_dust is the fraction of light dust blocks.
        /// Never negative: a cell hotter than ~275 °C just produces nothing.
        /// </summary>
        public static double Power(double poa, double area, double efficiency, double cellTemperature,
                                   double tempCoefficient = -0.004, double fSpec = 1.0, double fDust = 0.0) =>
            poa * area * efficiency * Math.Max(0.0, 1.0 + tempCoefficient * (cellTemperature - 25.0)) * fSpec * (1.0 - fDust);
    }
}

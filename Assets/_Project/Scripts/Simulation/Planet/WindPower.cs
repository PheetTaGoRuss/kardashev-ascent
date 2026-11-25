using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// Wind turbine physics (T2, decided 2026-11-25, director delegated). One reference machine, the Northern Power NPS 100C-21 (100 kW class, 3 blades, direct drive,
    /// IEC class II/A), from the manufacturer's datasheet (Northern Power Systems, NPS 100C-21 brochure and specification sheet, 2020): rated 100 kW at 15 m/s, cut-in 3 m/s,
    /// cut-out 25 m/s, extreme (survival) wind 59.5 m/s, rotor 20.7 m, hub height 22, 29 or 37 m (37 m used), nacelle and rotor 6 500 kg, 37 m tower 12 000 kg, power curve
    /// measured at an air density of 1.225 kg/m3. Pure functions.
    /// </summary>
    public static class WindPower
    {
        public const double RatedW = 100_000.0, CutInMs = 3.0, RatedMs = 15.0, CutOutMs = 25.0, ExtremeMs = 59.5;
        public const double RotorDiameterM = 20.7, HubHeightM = 37.0, NacelleRotorMassKg = 6_500.0, TowerMassKg = 12_000.0, CurveDensity = 1.225;
        public static double SweptAreaM2 => Math.PI * RotorDiameterM * RotorDiameterM / 4.0;
        public static double MassKg => NacelleRotorMassKg + TowerMassKg;

        /// <summary>The datasheet power curve, kW, at 0 to 25 m/s in steps of 1 (the datasheet lists -0.6 kW below cut-in, the self-consumption; here 0).</summary>
        static readonly double[] CurveKw =
        {
            0, 0, 0, 0.5, 4.1, 10.5, 19.0, 29.4, 41.0, 54.3, 66.8, 77.7, 86.4, 92.8, 97.8, 100.0, 99.9, 99.2, 98.4, 97.5, 96.8, 96.4, 96.3, 96.8, 98.0, 99.2
        };

        /// <summary>Electric power at a steady hub wind speed in air of the datasheet density, W: zero below cut-in and above cut-out.</summary>
        public static double CurveW(double hubWindMs)
        {
            if (hubWindMs < CutInMs || hubWindMs > CutOutMs) return 0.0;
            int i = Math.Min(CurveKw.Length - 2, (int)Math.Floor(hubWindMs));
            double f = hubWindMs - i;
            return 1000.0 * (CurveKw[i] + f * (CurveKw[i + 1] - CurveKw[i]));
        }

        /// <summary>
        /// Electric power at a steady hub wind speed in air of density rho, W. Below rated, power goes with the air density (P = 1/2 rho A v^3 Cp, so the curve scales by rho / 1.225),
        /// and the generator caps it at the rating (game rule, TBD: the datasheet curve is not published for other densities).
        /// </summary>
        public static double PowerW(double hubWindMs, double airDensity) =>
            Math.Min(RatedW, CurveW(hubWindMs) * Math.Max(0.0, airDensity) / CurveDensity);

        /// <summary>The power coefficient the curve implies at a wind speed: P / (1/2 rho A v^3) at the datasheet density (about 0.39 at 8 m/s; the Betz limit is 0.593).</summary>
        public static double PowerCoefficient(double hubWindMs) =>
            hubWindMs <= 0.0 ? 0.0 : CurveW(hubWindMs) / (0.5 * CurveDensity * SweptAreaM2 * hubWindMs * hubWindMs * hubWindMs);

        /// <summary>
        /// The wind at hub height from the 10 m wind: the 1/7 power law of the neutral boundary layer, U(z) = U10 (z / 10)^(1/7) (about 1.20 at 37 m)
        /// (Hsu, Meindl and Gilhousen 1994, J. Appl. Meteorol. 33).
        /// </summary>
        public static double HubWindMs(double wind10Ms) => Math.Max(0.0, wind10Ms) * Math.Pow(HubHeightM / 10.0, 1.0 / 7.0);

        /// <summary>Air density, kg/m3, from the ideal gas law rho = p / (R_specific T) with R_specific = R / M.</summary>
        public static double AirDensity(double pressurePa, double airC, double molarMassKg) =>
            pressurePa <= 0.0 || molarMassKg <= 0.0 ? 0.0 : pressurePa / (8.314462618 / molarMassKg * (airC + 273.15));

        /// <summary>
        /// The mean power over a day with this daily mean hub wind, W: the power curve integrated over a Rayleigh distribution (a Weibull with shape k = 2)
        /// of the speed within the day, f(v) = (pi v / (2 m^2)) exp(-pi v^2 / (4 m^2)) with m the mean (Justus, Hargraves, Mikhail and Graber 1978, J. Appl. Meteorol. 17).
        /// Not the power at the mean speed: the cube makes the gusts count more. Zero when the daily mean itself is above cut-out.
        /// </summary>
        public static double DailyMeanPowerW(double meanHubWindMs, double airDensity)
        {
            if (meanHubWindMs <= 0.0 || airDensity <= 0.0 || meanHubWindMs >= CutOutMs) return 0.0;
            double m = meanHubWindMs, sum = 0.0;
            const double dv = 0.25;
            for (double v = dv * 0.5; v < 60.0; v += dv)
                sum += PowerW(v, airDensity) * (Math.PI * v / (2.0 * m * m)) * Math.Exp(-Math.PI * v * v / (4.0 * m * m)) * dv;
            return sum;
        }
    }
}

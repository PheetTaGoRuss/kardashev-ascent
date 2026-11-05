using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Step 8 of the solar model: a panel's energy over one orbit.</summary>
    public static class AnnualYield
    {
        /// <summary>
        /// Sums <paramref name="days"/> representative days (evenly spaced in mean anomaly)
        /// × <paramref name="slots"/> time steps per day. Does not handle tidally locked planets yet.
        /// </summary>
        public static AnnualYieldResult Calculate(in PlanetSolarParams planet, in PanelParams panel,
                                                  int days = 24, int slots = 48)
        {
            Double3 normal = PanelModel.Normal(panel.Tilt, panel.Azimuth);
            double dtHours = planet.DayLengthHours / slots;
            double dayWeight = planet.YearLengthHours / planet.DayLengthHours / days;
            double totalWh = 0.0;

            for (int d = 0; d < days; d++)
            {
                double meanAnomaly = 2.0 * Math.PI * (d + 0.5) / days;
                double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
                double r = Orbit.Distance(planet.SemiMajorAxis, planet.Eccentricity, nu);
                double g0 = Orbit.ExtraterrestrialIrradiance(planet.Luminosity, r);
                double declination = SunPosition.Declination(planet.Obliquity, nu + planet.PerihelionLongitude);

                double dayWh = 0.0;
                for (int s = 0; s < slots; s++)
                {
                    double h = SunPosition.HourAngle((s + 0.5) * dtHours, planet.DayLengthHours);
                    Double3 sun = SunPosition.Direction(panel.Latitude, declination, h);
                    if (sun.Up <= 0.0)
                        continue;

                    double airMass = Atmosphere.AirMassKastenYoung(SunPosition.ZenithDegrees(sun), panel.PressureRatio);
                    double dni = Atmosphere.DirectNormal(g0, planet.OpticalDepth, airMass);
                    double dhi = Atmosphere.DiffuseHorizontal(g0, sun.Up, planet.OpticalDepth, airMass, planet.DiffuseK);
                    double ghi = Atmosphere.GlobalHorizontal(dni, dhi, sun.Up);
                    double poa = PanelModel.PlaneOfArray(dni, dhi, ghi, PanelModel.CosIncidence(normal, sun),
                                                         panel.Tilt, panel.Albedo, panel.FShade);
                    double cellT = PanelModel.CellTemperature(planet.AirTemperatureC, poa, panel.Noct);
                    dayWh += PanelModel.Power(poa, panel.Area, panel.Efficiency, cellT,
                                              panel.TempCoefficient, panel.FSpec, panel.FDust) * dtHours;
                }
                totalWh += dayWh * dayWeight;
            }

            double ratedW = panel.Area * panel.Efficiency * SolarConstants.StcIrradiance;
            return new AnnualYieldResult(totalWh, totalWh / (ratedW * planet.YearLengthHours));
        }
    }
}

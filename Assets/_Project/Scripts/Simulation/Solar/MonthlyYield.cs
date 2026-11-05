using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>
    /// A panel's energy month by month (#13). The planet's year is split into equal "months" counted from periapsis,
    /// the same zero as the game clock (on Earth, periapsis is 3 January, so month 1 ≈ January).
    /// Same physics and sampling as <see cref="AnnualYield"/>: with 2 samples a month the 12 months land on exactly
    /// the 24 orbit points AnnualYield uses, so they add up to its yearly total.
    /// </summary>
    public static class MonthlyYield
    {
        public static double[] Calculate(in PlanetSolarParams planet, in PanelParams panel,
                                         int months = 12, int samplesPerMonth = 2, int slots = 48)
        {
            if (months < 1) throw new ArgumentOutOfRangeException(nameof(months));
            if (samplesPerMonth < 1) throw new ArgumentOutOfRangeException(nameof(samplesPerMonth));
            if (!planet.TidallyLocked && planet.DayLengthHours <= 0.0)
                throw new ArgumentException("DayLengthHours must be > 0 unless the planet is tidally locked.", nameof(planet));

            Double3 normal = PanelModel.Normal(panel.Tilt, panel.Azimuth);
            int samples = months * samplesPerMonth;
            var result = new double[months];
            for (int m = 0; m < months; m++)
            {
                double monthWh = 0.0;
                for (int j = 0; j < samplesPerMonth; j++)
                {
                    int d = m * samplesPerMonth + j;
                    double meanAnomaly = 2.0 * Math.PI * (d + 0.5) / samples;
                    double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
                    double g0 = Orbit.ExtraterrestrialIrradiance(planet.Luminosity,
                                                                 Orbit.Distance(planet.SemiMajorAxis, planet.Eccentricity, nu));
                    double declination = SunPosition.Declination(planet.Obliquity, nu + planet.PerihelionLongitude);

                    if (planet.TidallyLocked)
                    {
                        Double3 fixedSun = SunPosition.Direction(panel.Latitude, declination, panel.Longitude);
                        monthWh += AnnualYield.InstantPower(planet, panel, normal, g0, fixedSun) * planet.YearLengthHours / samples;
                        continue;
                    }

                    double dtHours = planet.DayLengthHours / slots;
                    double dayWh = 0.0;
                    for (int s = 0; s < slots; s++)
                    {
                        double h = SunPosition.HourAngle((s + 0.5) * dtHours, planet.DayLengthHours);
                        dayWh += AnnualYield.InstantPower(planet, panel, normal, g0,
                                                          SunPosition.Direction(panel.Latitude, declination, h)) * dtHours;
                    }
                    monthWh += dayWh * (planet.YearLengthHours / planet.DayLengthHours) / samples;
                }
                result[m] = monthWh;
            }
            return result;
        }
    }
}

using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Step 8 of the solar model: a panel's energy over one orbit.</summary>
    public static class AnnualYield
    {
        /// <summary>Above this eccentricity the orbit is sampled more densely (fast pass near periapsis).</summary>
        public const double EccentricOrbit = 0.3;

        /// <summary>Orbit samples used when <c>days</c> is 0: 24, or 72 when e &gt; 0.3.</summary>
        public static int SampleDays(in PlanetSolarParams planet) => planet.Eccentricity > EccentricOrbit ? 72 : 24;

        /// <summary>
        /// Energy per orbit from <paramref name="days"/> points spread evenly in mean anomaly (0 = <see cref="SampleDays"/>).
        /// Rotating planets: <paramref name="slots"/> time steps across each of those days.
        /// Tidally locked planets: the star stays put at hour angle h = panel longitude − sub-stellar longitude
        /// (0 here, no libration), so each sample stands for 1/days of the orbit.
        /// </summary>
        /// <exception cref="ArgumentException">Day length ≤ 0 on a planet that is not tidally locked.</exception>
        public static AnnualYieldResult Calculate(in PlanetSolarParams planet, in PanelParams panel,
                                                  int days = 0, int slots = 48)
        {
            if (days <= 0)
                days = SampleDays(planet);
            if (!planet.TidallyLocked && planet.DayLengthHours <= 0.0)
                throw new ArgumentException("DayLengthHours must be > 0 unless the planet is tidally locked.", nameof(planet));

            Double3 normal = PanelModel.Normal(panel.Tilt, panel.Azimuth);
            double totalWh = 0.0;

            for (int d = 0; d < days; d++)
            {
                double meanAnomaly = 2.0 * Math.PI * (d + 0.5) / days;
                double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
                double r = Orbit.Distance(planet.SemiMajorAxis, planet.Eccentricity, nu);
                double g0 = Orbit.ExtraterrestrialIrradiance(planet.Luminosity, r);
                double declination = SunPosition.Declination(planet.Obliquity, nu + planet.PerihelionLongitude);

                if (planet.TidallyLocked)
                {
                    Double3 fixedSun = SunPosition.Direction(panel.Latitude, declination, panel.Longitude);
                    totalWh += InstantPower(planet, panel, normal, g0, fixedSun) * planet.YearLengthHours / days;
                    continue;
                }

                double dtHours = planet.DayLengthHours / slots;
                double dayWh = 0.0;
                for (int s = 0; s < slots; s++)
                {
                    double h = SunPosition.HourAngle((s + 0.5) * dtHours, planet.DayLengthHours);
                    dayWh += InstantPower(planet, panel, normal, g0, SunPosition.Direction(panel.Latitude, declination, h)) * dtHours;
                }
                totalWh += dayWh * (planet.YearLengthHours / planet.DayLengthHours) / days;
            }

            double ratedW = panel.Area * panel.Efficiency * SolarConstants.StcIrradiance;
            return new AnnualYieldResult(totalWh, totalWh / (ratedW * planet.YearLengthHours));
        }

        /// <summary>Panel power (W) for one star direction; 0 when the star is below the horizon.</summary>
        public static double InstantPower(in PlanetSolarParams planet, in PanelParams panel, Double3 normal, double g0, Double3 sun)
        {
            if (sun.Up <= 0.0)
                return 0.0;
            double poa = PlaneOfArray(planet, panel, normal, g0, sun);
            double cellT = PanelModel.CellTemperature(planet.AirTemperatureC, poa, panel.Noct);
            return PanelModel.Power(poa, panel.Area, panel.Efficiency, cellT,
                                    panel.TempCoefficient, panel.FSpec, panel.FDust);
        }

        /// <summary>Light on the panel (W/m²) from one star of above-atmosphere irradiance g0; 0 below the horizon.</summary>
        public static double PlaneOfArray(in PlanetSolarParams planet, in PanelParams panel, Double3 normal, double g0, Double3 sun)
        {
            if (sun.Up <= 0.0 || g0 <= 0.0)
                return 0.0;
            double airMass = Atmosphere.AirMassKastenYoung(SunPosition.ZenithDegrees(sun), panel.PressureRatio);
            double dni = Atmosphere.DirectNormal(g0, planet.OpticalDepth, airMass);
            double dhi = Atmosphere.DiffuseHorizontal(g0, sun.Up, planet.OpticalDepth, airMass, planet.DiffuseK);
            double ghi = Atmosphere.GlobalHorizontal(dni, dhi, sun.Up);
            return PanelModel.PlaneOfArray(dni, dhi, ghi, PanelModel.CosIncidence(normal, sun),
                                           panel.Tilt, panel.Albedo, panel.FShade);
        }
    }
}

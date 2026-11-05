using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>One star of a binary.</summary>
    public readonly struct StarParams
    {
        public readonly double MassKg;
        public readonly double RadiusM;
        public readonly double TemperatureK;
        /// <summary>W.</summary>
        public readonly double Luminosity;

        public StarParams(double massKg, double radiusM, double temperatureK, double luminosity)
        {
            MassKg = massKg;
            RadiusM = radiusM;
            TemperatureK = temperatureK;
            Luminosity = luminosity;
        }

        /// <summary>Luminosity from size and temperature: L = 4πR²σT⁴.</summary>
        public static StarParams FromRadiusAndTemperature(double massKg, double radiusM, double temperatureK) =>
            new StarParams(massKg, radiusM, temperatureK,
                           4.0 * Math.PI * radiusM * radiusM * 5.670374419e-8 * Math.Pow(temperatureK, 4.0));
    }

    /// <summary>
    /// A circumbinary (P-type) system: the planet orbits both stars' barycentre.
    /// Simplifications: the binary orbit is circular and coplanar with the planet's orbit.
    /// </summary>
    public readonly struct BinaryParams
    {
        public readonly StarParams A;
        public readonly StarParams B;
        /// <summary>Distance between the two stars, m.</summary>
        public readonly double SeparationM;
        public readonly double PeriodHours;

        public BinaryParams(StarParams a, StarParams b, double separationM, double periodHours)
        {
            A = a;
            B = b;
            SeparationM = separationM;
            PeriodHours = periodHours;
        }

        public double TotalLuminosity => A.Luminosity + B.Luminosity;
    }

    /// <summary>One star as seen from the planet at one moment.</summary>
    public readonly struct StarView
    {
        public readonly double DistanceM;
        /// <summary>Angle from the barycentre direction along the orbital plane, radians (+ = ahead in the planet's orbit).</summary>
        public readonly double OffsetAngle;
        /// <summary>Apparent radius of the disc, radians.</summary>
        public readonly double AngularRadius;
        /// <summary>Starlight above the atmosphere, W/m², already reduced by any eclipse.</summary>
        public readonly double Irradiance;
        /// <summary>Fraction of the disc not hidden by the other star (1 = no eclipse).</summary>
        public readonly double VisibleFraction;

        public StarView(double distanceM, double offsetAngle, double angularRadius, double irradiance, double visibleFraction)
        {
            DistanceM = distanceM;
            OffsetAngle = offsetAngle;
            AngularRadius = angularRadius;
            Irradiance = irradiance;
            VisibleFraction = visibleFraction;
        }
    }

    /// <summary>
    /// Light from two stars on a circumbinary planet (docs/physics/physics-verification.md, P1 item 12):
    /// POA = Σᵢ POAᵢ with each star's own direction, distance and f_spec, plus mutual eclipses.
    /// </summary>
    public static class BinaryStar
    {
        const double SolarMass = 1.98847e30;
        const double SolarRadius = 6.957e8;

        /// <summary>
        /// Kepler-16 (AB), Doyle et al. 2011, Science 333:1602. Luminosities from L = 4πR²σT⁴
        /// (A ≈ 0.149 L☉, B ≈ 0.0055 L☉). Binary eccentricity 0.159 is ignored (circular binary).
        /// </summary>
        public static readonly BinaryParams Kepler16 = new BinaryParams(
            StarParams.FromRadiusAndTemperature(0.6897 * SolarMass, 0.6489 * SolarRadius, 4450.0),
            StarParams.FromRadiusAndTemperature(0.20255 * SolarMass, 0.22623 * SolarRadius, 3311.0),
            0.22431 * SolarConstants.AstronomicalUnit,
            41.079220 * 24.0);

        /// <summary>
        /// Where each star appears from the planet. <paramref name="planetAngle"/> is the planet's angle around the
        /// barycentre and <paramref name="binaryPhase"/> the binary's angle, both in the same orbital plane.
        /// </summary>
        public static void View(in BinaryParams binary, double planetDistanceM, double planetAngle, double binaryPhase,
                                out StarView a, out StarView b)
        {
            double m = binary.A.MassKg + binary.B.MassKg;
            double ra = binary.SeparationM * binary.B.MassKg / m;   // A's distance from the barycentre
            double rb = binary.SeparationM * binary.A.MassKg / m;

            // Work in a frame where the planet sits on +x; the stars are at angle (phase − planetAngle).
            double rel = binaryPhase - planetAngle;
            Raw(ra, rel + Math.PI, planetDistanceM, binary.A, out double dA, out double offA, out double angA);
            Raw(rb, rel, planetDistanceM, binary.B, out double dB, out double offB, out double angB);

            // The nearer star can hide part of the farther one.
            double sep = Math.Abs(offA - offB);
            double visA = 1.0, visB = 1.0;
            if (dA > dB)
                visA = VisibleFraction(sep, angB, angA);
            else
                visB = VisibleFraction(sep, angA, angB);

            a = new StarView(dA, offA, angA, visA * binary.A.Luminosity / (4.0 * Math.PI * dA * dA), visA);
            b = new StarView(dB, offB, angB, visB * binary.B.Luminosity / (4.0 * Math.PI * dB * dB), visB);
        }

        static void Raw(double starOrbitRadius, double starAngle, double planetDistance, in StarParams star,
                        out double distance, out double offset, out double angularRadius)
        {
            // Vector from planet (planetDistance, 0) to star.
            double dx = starOrbitRadius * Math.Cos(starAngle) - planetDistance;
            double dy = starOrbitRadius * Math.Sin(starAngle);
            distance = Math.Sqrt(dx * dx + dy * dy);
            // Barycentre direction from the planet is −x (angle π); offset is measured from it.
            offset = Math.Atan2(-dy, -dx);
            angularRadius = Math.Asin(Math.Min(1.0, star.RadiusM / distance));
        }

        /// <summary>
        /// Fraction of the far disc (radius <paramref name="farRadius"/>) left visible when the near disc
        /// (radius <paramref name="nearRadius"/>) sits <paramref name="separation"/> away. Uniform discs (no limb darkening).
        /// </summary>
        public static double VisibleFraction(double separation, double nearRadius, double farRadius)
        {
            double d = Math.Abs(separation), r1 = nearRadius, r2 = farRadius;
            if (d >= r1 + r2)
                return 1.0;
            double farArea = Math.PI * r2 * r2;
            if (d <= Math.Abs(r1 - r2))
                return r1 >= r2 ? 0.0 : 1.0 - r1 * r1 / (r2 * r2);
            double a1 = r1 * r1 * Math.Acos(Clamp((d * d + r1 * r1 - r2 * r2) / (2.0 * d * r1)));
            double a2 = r2 * r2 * Math.Acos(Clamp((d * d + r2 * r2 - r1 * r1) / (2.0 * d * r2)));
            double a3 = 0.5 * Math.Sqrt(Math.Max(0.0, (-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2)));
            return 1.0 - (a1 + a2 - a3) / farArea;
        }

        static double Clamp(double x) => Math.Max(-1.0, Math.Min(1.0, x));

        /// <summary>
        /// Days between repeats of the stars' arrangement seen from the planet: 1 / (1/P_bin − 1/P_planet).
        /// The stars line up twice per synodic period. Kepler-16b: ≈ 50 days.
        /// </summary>
        public static double SynodicPeriodHours(double binaryPeriodHours, double planetPeriodHours) =>
            1.0 / (1.0 / binaryPeriodHours - 1.0 / planetPeriodHours);

        /// <summary>
        /// Panel energy per planet orbit with two stars. Each orbit sample is averaged over
        /// <paramref name="phases"/> evenly spaced binary phases (the binary and planet periods are not in step).
        /// <paramref name="planet"/>.Luminosity is ignored; each star's f_spec comes from <paramref name="material"/>,
        /// and <paramref name="panel"/>.FSpec is ignored.
        /// </summary>
        public static AnnualYieldResult AnnualYield(in PlanetSolarParams planet, in PanelParams panel, in BinaryParams binary,
                                                    CellMaterial material, int days = 24, int slots = 48, int phases = 16)
        {
            if (planet.TidallyLocked || planet.DayLengthHours <= 0.0)
                throw new ArgumentException("Circumbinary yield needs a rotating planet (DayLengthHours > 0).", nameof(planet));

            Double3 normal = PanelModel.Normal(panel.Tilt, panel.Azimuth);
            double fA = SpectralMatch.FSpec(material, binary.A.TemperatureK);
            double fB = SpectralMatch.FSpec(material, binary.B.TemperatureK);
            double dtHours = planet.DayLengthHours / slots;
            double totalWh = 0.0;

            for (int d = 0; d < days; d++)
            {
                double meanAnomaly = 2.0 * Math.PI * (d + 0.5) / days;
                double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
                double r = Orbit.Distance(planet.SemiMajorAxis, planet.Eccentricity, nu);
                double baryLongitude = nu + planet.PerihelionLongitude;

                double dayWh = 0.0;
                for (int p = 0; p < phases; p++)
                {
                    double phase = 2.0 * Math.PI * (p + 0.5) / phases;
                    View(binary, r, nu, phase + nu, out StarView a, out StarView b);
                    for (int s = 0; s < slots; s++)
                    {
                        double h = SunPosition.HourAngle((s + 0.5) * dtHours, planet.DayLengthHours);
                        dayWh += PowerFromStars(planet, panel, normal, baryLongitude, h, a, fA, b, fB) * dtHours;
                    }
                }
                totalWh += dayWh / phases * (planet.YearLengthHours / planet.DayLengthHours) / days;
            }

            double ratedW = panel.Area * panel.Efficiency * SolarConstants.StcIrradiance;
            return new AnnualYieldResult(totalWh, totalWh / (ratedW * planet.YearLengthHours));
        }

        /// <summary>
        /// Panel power, W, from both stars at hour angle <paramref name="baryHourAngle"/> of the barycentre direction.
        /// Each star's ecliptic longitude is the barycentre's plus its offset; its hour angle shifts by the difference
        /// in right ascension. Cell temperature uses the combined light.
        /// </summary>
        public static double PowerFromStars(in PlanetSolarParams planet, in PanelParams panel, Double3 normal,
                                            double baryLongitude, double baryHourAngle,
                                            in StarView a, double fSpecA, in StarView b, double fSpecB)
        {
            double poaA = StarPoa(planet, panel, normal, baryLongitude, baryHourAngle, a);
            double poaB = StarPoa(planet, panel, normal, baryLongitude, baryHourAngle, b);
            double poa = poaA + poaB;
            if (poa <= 0.0)
                return 0.0;
            double cellT = PanelModel.CellTemperature(planet.AirTemperatureC, poa, panel.Noct);
            double effectiveFSpec = (poaA * fSpecA + poaB * fSpecB) / poa;
            return PanelModel.Power(poa, panel.Area, panel.Efficiency, cellT,
                                    panel.TempCoefficient, effectiveFSpec, panel.FDust);
        }

        static double StarPoa(in PlanetSolarParams planet, in PanelParams panel, Double3 normal,
                              double baryLongitude, double baryHourAngle, in StarView star)
        {
            double lambda = baryLongitude + star.OffsetAngle;
            double declination = SunPosition.Declination(planet.Obliquity, lambda);
            double dRa = RightAscension(planet.Obliquity, lambda) - RightAscension(planet.Obliquity, baryLongitude);
            dRa = Math.IEEERemainder(dRa, 2.0 * Math.PI);   // wrap to −π..π
            double hourAngle = baryHourAngle - dRa;
            Double3 sun = SunPosition.Direction(panel.Latitude, declination, hourAngle);
            return Solar.AnnualYield.PlaneOfArray(planet, panel, normal, star.Irradiance, sun);
        }

        static double RightAscension(double obliquity, double eclipticLongitude) =>
            Math.Atan2(Math.Cos(obliquity) * Math.Sin(eclipticLongitude), Math.Cos(eclipticLongitude));
    }
}

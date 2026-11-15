using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// Where a star sits in the Milky Way, for the star map: sky position (right ascension and declination, J2000)
    /// to galactic longitude and latitude, and from those plus distance to a position around the Sun.
    /// Galactic frame from the J2000 position of the north galactic pole (RA 192.85948°, Dec 27.12825°) and the
    /// galactic longitude of the north celestial pole (122.93192°), as used by Hipparcos (ESA 1997, The Hipparcos and
    /// Tycho Catalogues, vol. 1, sec. 1.5.3) and SIMBAD.
    /// </summary>
    public static class GalacticCoordinates
    {
        public const double PoleRaDeg = 192.85948;
        public const double PoleDecDeg = 27.12825;
        public const double CelestialPoleLongitudeDeg = 122.93192;
        /// <summary>The Sun's distance from the galactic centre, parsecs (GRAVITY Collaboration 2019, A&amp;A 625, L10).</summary>
        public const double SunToCentrePc = 8178.0;
        /// <summary>Light years in one parsec (IAU 2015: 1 pc = 648 000/π au; 1 ly = 63 241.077 au).</summary>
        public const double LightYearsPerParsec = 3.261563777;

        /// <summary>Galactic longitude l (0–360°, 0 towards the galactic centre) and latitude b (−90 to 90°).</summary>
        public static void FromEquatorial(double raDeg, double decDeg, out double lDeg, out double bDeg)
        {
            double ra = raDeg * Deg, dec = decDeg * Deg;
            double raP = PoleRaDeg * Deg, decP = PoleDecDeg * Deg;
            double sinB = Math.Sin(dec) * Math.Sin(decP) + Math.Cos(dec) * Math.Cos(decP) * Math.Cos(ra - raP);
            bDeg = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinB))) / Deg;
            double y = Math.Cos(dec) * Math.Sin(ra - raP);
            double x = Math.Sin(dec) * Math.Cos(decP) - Math.Cos(dec) * Math.Sin(decP) * Math.Cos(ra - raP);
            lDeg = CelestialPoleLongitudeDeg - Math.Atan2(y, x) / Deg;
            lDeg = ((lDeg % 360.0) + 360.0) % 360.0;
        }

        /// <summary>
        /// Position around the Sun in parsecs: x towards the galactic centre, y towards l = 90° (the way the Sun
        /// orbits), z towards the north galactic pole.
        /// </summary>
        public static void ToCartesian(double lDeg, double bDeg, double distancePc, out double x, out double y, out double z)
        {
            double l = lDeg * Deg, b = bDeg * Deg;
            x = distancePc * Math.Cos(b) * Math.Cos(l);
            y = distancePc * Math.Cos(b) * Math.Sin(l);
            z = distancePc * Math.Sin(b);
        }

        const double Deg = Math.PI / 180.0;
    }
}

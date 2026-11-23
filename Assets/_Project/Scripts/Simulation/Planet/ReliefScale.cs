using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The real height of the terrain (KI-42, decided 2026-11-23). A tile's <c>Elevation</c> is relative, about -1 to 1. This maps it onto the real
    /// relief of the planet so hauling can pay for climbing: below zero the lowest point, above zero the highest, linearly.
    /// Earth twins use Earth's relief; Mars uses the MOLA relief; every other world scales Earth's relief with surface gravity, h = h_earth * g_earth / g,
    /// the first-order rule for how high a mountain of rock can stand. The scaling is an assumption, to be replaced by real topography where it exists.
    /// </summary>
    public static class ReliefScale
    {
        /// <summary>Earth, lowest and highest, m: the Challenger Deep about -10 935 m and Everest 8 849 m (rounded as about -11 km to +8.8 km) [verified: standard figures; the director's decision uses -11 km and +8.8 km].</summary>
        public const double EarthMinM = -11_000.0, EarthMaxM = 8_800.0;

        /// <summary>Mars, lowest and highest, m: the Hellas floor about -8 200 m and the summit of Olympus Mons about +21 200 m on the MOLA areoid [verified: Mars Orbiter Laser Altimeter results, Smith et al. 1999, Science 284].</summary>
        public const double MarsMinM = -8_200.0, MarsMaxM = 21_200.0;

        public const double EarthGravityMs2 = 9.80665;

        /// <summary>The lowest and highest real height of a planet, m. An unknown or non-positive gravity gives Earth's relief.</summary>
        public static (double MinM, double MaxM) For(string planetId, double surfaceGravityMs2)
        {
            string id = planetId ?? "";
            if (string.Equals(id, "mars", StringComparison.OrdinalIgnoreCase)) return (MarsMinM, MarsMaxM);
            if (string.Equals(id, "viridia", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "earth", StringComparison.OrdinalIgnoreCase)) return (EarthMinM, EarthMaxM);
            if (surfaceGravityMs2 <= 0.0) return (EarthMinM, EarthMaxM);
            double k = EarthGravityMs2 / surfaceGravityMs2;   // h proportional to 1/g (assumption)
            return (EarthMinM * k, EarthMaxM * k);
        }

        /// <summary>The height of a tile above its planet's sea level, m, for a relative elevation (below 0 the low end, above 0 the high end).</summary>
        public static double HeightM(double elevation, (double MinM, double MaxM) relief)
        {
            double e = Math.Max(-1.0, Math.Min(1.0, elevation));
            return e < 0.0 ? -e * relief.MinM : e * relief.MaxM;
        }
    }
}

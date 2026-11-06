using System;

namespace KardashevAscent.Simulation.Solar
{
    public static class SolarConstants
    {
        /// <summary>Astronomical unit, m.</summary>
        public const double AstronomicalUnit = 1.495978707e11;

        /// <summary>Luminosity of the Sun, W.</summary>
        public const double SolarLuminosity = 3.828e26;

        /// <summary>
        /// Earth's orbital (sidereal) year, h. Yearly figures the player compares across planets (energy per year,
        /// payback, lifetime EROI) use Earth years; a planet's own year can be 11 days (Proxima b) or longer than ours.
        /// </summary>
        public const double EarthYearHours = 365.256 * 24.0;

        /// <summary>Irradiance at which panels are rated (STC), W/m².</summary>
        public const double StcIrradiance = 1000.0;

        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;
    }
}

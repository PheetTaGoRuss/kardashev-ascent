using System;

namespace KardashevAscent.Simulation.Solar
{
    public static class SolarConstants
    {
        /// <summary>Astronomical unit, m.</summary>
        public const double AstronomicalUnit = 1.495978707e11;

        /// <summary>Luminosity of the Sun, W.</summary>
        public const double SolarLuminosity = 3.828e26;

        /// <summary>Irradiance at which panels are rated (STC), W/m².</summary>
        public const double StcIrradiance = 1000.0;

        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;
    }
}

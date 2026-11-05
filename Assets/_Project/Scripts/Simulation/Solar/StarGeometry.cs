using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>How big a star is and how big it looks from a planet.</summary>
    public static class StarGeometry
    {
        /// <summary>Stefan–Boltzmann constant, W m⁻² K⁻⁴.</summary>
        public const double StefanBoltzmann = 5.670374419e-8;

        /// <summary>Star radius from L = 4πR²σT⁴, m.</summary>
        public static double Radius(double luminosity, double effectiveTemperatureK) =>
            Math.Sqrt(luminosity / (4.0 * Math.PI * StefanBoltzmann * Math.Pow(effectiveTemperatureK, 4.0)));

        /// <summary>Angular diameter of a sphere of <paramref name="radius"/> seen from <paramref name="distance"/>, radians.</summary>
        public static double AngularDiameter(double radius, double distance) =>
            distance <= radius ? Math.PI : 2.0 * Math.Asin(radius / distance);
    }
}

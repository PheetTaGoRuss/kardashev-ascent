using System;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// How much a standard solar panel would make per year at each place on the planet, for the
    /// light-potential map (#6). Clear sky, no clouds; compares places, not panel choices.
    /// </summary>
    public static class LightPotential
    {
        /// <summary>Efficiency of the standard comparison panel.</summary>
        public const double StandardEfficiency = 0.20;

        /// <summary>
        /// The comparison panel: 1 m², 20 % efficient silicon, tilted at |latitude| and facing the equator,
        /// at sea level on ground with albedo 0.2, clean, unshaded.
        /// </summary>
        public static PanelParams StandardPanel(double latitude, double longitude = 0.0)
        {
            double tilt = Math.Abs(latitude);
            double azimuth = latitude >= 0.0 ? Math.PI : 0.0;
            return new PanelParams(latitude, tilt, azimuth, 1.0, StandardEfficiency, 45.0, -0.004,
                                   0.2, 1.0, 1.0, 0.0, 1.0, longitude);
        }

        /// <summary>Energy from the standard panel over one orbit, kWh per m² of panel.</summary>
        public static double AnnualKwhPerM2(in PlanetSolarParams planet, double latitude, double longitude = 0.0) =>
            AnnualYield.Calculate(planet, StandardPanel(latitude, longitude)).EnergyWh / 1000.0;

        /// <summary>
        /// <see cref="AnnualKwhPerM2"/> for every tile. On a rotating planet the result depends only on latitude,
        /// so it is computed at <paramref name="latitudeSamples"/> evenly spaced latitudes and interpolated;
        /// on a tidally locked planet each tile is computed directly (latitude and longitude both matter).
        /// Pure function, safe to run on a background thread.
        /// </summary>
        public static double[] ForTiles(HexSphere sphere, PlanetSolarParams planet, int latitudeSamples = 181)
        {
            if (sphere == null)
                throw new ArgumentNullException(nameof(sphere));
            var result = new double[sphere.TileCount];

            if (planet.TidallyLocked)
            {
                for (int t = 0; t < result.Length; t++)
                    result[t] = AnnualKwhPerM2(planet, sphere.Latitude(t), sphere.Longitude(t));
                return result;
            }

            int n = Math.Max(2, latitudeSamples);
            var table = new double[n];
            for (int i = 0; i < n; i++)
                table[i] = AnnualKwhPerM2(planet, LatitudeOfSample(i, n));

            for (int t = 0; t < result.Length; t++)
            {
                double position = (sphere.Latitude(t) + Math.PI / 2.0) / Math.PI * (n - 1);
                int i0 = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(position)));
                double f = position - i0;
                result[t] = table[i0] + (table[i0 + 1] - table[i0]) * f;
            }
            return result;
        }

        static double LatitudeOfSample(int i, int n) => -Math.PI / 2.0 + Math.PI * i / (n - 1);
    }
}

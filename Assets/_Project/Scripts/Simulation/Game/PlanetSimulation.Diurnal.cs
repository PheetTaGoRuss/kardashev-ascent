using System;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The day-night swing of the air temperature (decided 2026-11-24, director delegated): the annual temperature of a tile plus an amplitude times
    /// the sine of the Sun's height, so afternoons are warmer and nights colder. The amplitude is the one the temperature puzzle measures
    /// (10 K plus a tenth of the annual range: a game rule, TBD, to be replaced by a real diurnal model). A tidally locked planet has no day, so no swing.
    /// On for new missions; a save before version 54 keeps the yearly temperature.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        public bool DiurnalTemperature { get; set; } = true;

        double[] diurnalAmps;

        /// <summary>The amplitude of a tile's day-night swing, K.</summary>
        public double DiurnalAmplitudeK(int tileId)
        {
            if (planet.TidallyLocked) return 0.0;
            diurnalAmps ??= new double[TileCount];
            if (diurnalAmps[tileId] <= 0.0)
            {
                var (lo, hi) = SurfaceTemperatureRange(tileId);
                diurnalAmps[tileId] = DiurnalBaseK + DiurnalRangeShare * (hi - lo);
            }
            return diurnalAmps[tileId];
        }

        /// <summary>Game rule, TBD: the swing is this many kelvin plus this share of the annual range.</summary>
        public const double DiurnalBaseK = 10.0, DiurnalRangeShare = 0.1;

        /// <summary>The sine of the Sun's height above the horizon at a tile at a game time (negative at night).</summary>
        public double SunHeightSine(int tileId, double timeHours)
        {
            double meanAnomaly = YearLengthHours > 0 ? 2.0 * Math.PI * timeHours / YearLengthHours : 0.0;
            double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
            double declination = SunPosition.Declination(planet.Obliquity, nu + planet.PerihelionLongitude);
            double spin = DayLengthHours > 0 ? 2.0 * Math.PI * timeHours / DayLengthHours : 0.0;
            double lat = Sphere.Latitude(tileId);
            double hour = Sphere.Longitude(tileId) - (subStellarLongitudeAtStart - spin);
            return Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hour);
        }

        /// <summary>Air temperature at a tile at a game time, C: the annual value, plus the day-night swing when it is on.</summary>
        public double DiurnalAirTemperatureAt(int tileId, double timeHours)
        {
            double mean = YearLengthHours > 0 ? 2.0 * Math.PI * timeHours / YearLengthHours : 0.0;
            double annual = AirTemperatureAt(tileId, mean);
            if (!DiurnalTemperature || planet.TidallyLocked) return annual;
            return annual + DiurnalAmplitudeK(tileId) * Math.Max(-1.0, Math.Min(1.0, SunHeightSine(tileId, timeHours)));
        }
    }
}

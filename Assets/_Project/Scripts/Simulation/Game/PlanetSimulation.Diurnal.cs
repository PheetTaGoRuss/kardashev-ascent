using System;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The day-night swing of the air temperature (decided 2026-11-24, director delegated): the annual temperature of a tile plus an amplitude times
    /// the sine of the Sun's height, so afternoons are warmer and nights colder. The amplitude depends on how much air there is to hold the
    /// day's heat: 10 K under a thick atmosphere, rising to 35 K under a thin one (game rule, TBD: amplitude = 10 K + 25 K x (1 - pressure in
    /// Earth atmospheres, at most 1)). It matches Mars: Curiosity's REMS saw daily air temperatures from about -70 C to 0 C at Gale crater, a range of
    /// about 70 K, so an amplitude of 35 K [JPL, PIA16913, "Steady temperatures at Mars' Gale crater"; REMS overview: Gomez-Elvira et al. 2014, JGR Planets 119].
    /// The temperature puzzle measures this amplitude. A tidally locked planet has no day, so no swing.
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
                diurnalAmps[tileId] = DiurnalBaseK + DiurnalThinAirK * (1.0 - Math.Max(0.0, Math.Min(1.0, planet.SurfacePressureEarth)));
            }
            return diurnalAmps[tileId];
        }

        /// <summary>Game rule, TBD: the swing is this many kelvin under thick air, plus up to this many more under no air.</summary>
        public const double DiurnalBaseK = 10.0, DiurnalThinAirK = 25.0;

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

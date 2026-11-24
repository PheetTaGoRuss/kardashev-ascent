using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>The weather at a tile now: the climatology plus the day's anomaly (Weather, PL-07, decided 2026-11-24, director delegated).</summary>
    public readonly struct WeatherSample
    {
        public readonly double CloudFactor, AirC, PressurePa, WindMs, DustTau;
        public readonly bool Storm;
        public WeatherSample(double cloud, double airC, double pressurePa, double windMs, double dustTau, bool storm)
        { CloudFactor = cloud; AirC = airC; PressurePa = pressurePa; WindMs = windMs; DustTau = dustTau; Storm = storm; }
    }

    public sealed partial class PlanetSimulation
    {
        WeatherModel weatherModel;
        readonly Dictionary<int, WeatherAnomaly> weatherCache = new Dictionary<int, WeatherAnomaly>();
        long weatherCacheHour = long.MinValue;

        /// <summary>Weather on or off. On for new missions; a save before version 56 has none, so its balance does not shift.</summary>
        public bool WeatherEnabled { get; set; } = true;

        WeatherModel WeatherModel => weatherModel ??= new WeatherModel(TerrainSeed, planetRadiusM, planet.SurfacePressureEarth, Clouds == CloudModel.EarthClimatology);

        /// <summary>The extra optical depth above which a dust storm counts as a storm day (game rule, TBD).</summary>
        public const double DustStormTau = 1.5;

        /// <summary>True when this world has weather (it has air) and it is on.</summary>
        public bool HasWeather => WeatherEnabled && WeatherModel.HasWeather;

        double YearFractionNow => Climate.CloudClimate.YearFractionFromMeanAnomaly(CurrentMeanAnomaly());

        /// <summary>The day's anomaly at a tile (zero without weather). Remembered for the current game hour.</summary>
        WeatherAnomaly AnomalyAt(int tileId)
        {
            if (!HasWeather || tileId < 0 || tileId >= TileCount) return WeatherAnomaly.None;
            long hour = (long)Math.Floor(TimeHours);
            if (hour != weatherCacheHour) { weatherCache.Clear(); weatherCacheHour = hour; }
            if (!weatherCache.TryGetValue(tileId, out WeatherAnomaly a))
                weatherCache[tileId] = a = WeatherModel.At(Sphere.Latitude(tileId), Sphere.Longitude(tileId), TimeHours, YearFractionNow);
            return a;
        }

        /// <summary>The air temperature now at a tile: the climatology, the day-night swing and the day's weather. For robots, in play (not for plans, which cannot know the weather ahead).</summary>
        public double LiveAirTemperatureAt(int tileId)
        {
            Upkeep.WindFactorAt ??= tile => HasWeather ? Robots.RobotThermal.WindFactor(AnomalyAt(tile).WindMs, planet.SurfacePressureEarth) : 1.0;
            return DiurnalAirTemperatureAt(tileId, TimeHours) + AnomalyAt(tileId).TemperatureK;
        }

        /// <summary>The planet's solar (areocentric) longitude Ls at a time, degrees: 0 at the northern spring equinox.</summary>
        public double SolarLongitudeDegAt(double timeHours)
        {
            double meanAnomaly = YearLengthHours > 0 ? 2.0 * Math.PI * timeHours / YearLengthHours : 0.0;
            double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
            double ls = (nu + planet.PerihelionLongitude) * 180.0 / Math.PI % 360.0;
            return ls < 0.0 ? ls + 360.0 : ls;
        }

        /// <summary>A thin CO2 air like Mars's (a game-rule class by surface pressure): it has the seasonal pressure cycle and the dust.</summary>
        public bool ThinCo2Air => HasWeather && ThinAirClimate.Applies(planet.SurfacePressureEarth);

        /// <summary>The weather at a tile now: cloud factor, air temperature, pressure, wind and dust, climatology plus the day's anomaly (and, in thin CO2 air, the pressure cycle and the dust).</summary>
        public WeatherSample WeatherAt(int tileId)
        {
            double mean = CurrentMeanAnomaly();
            WeatherAnomaly a = AnomalyAt(tileId);
            double dust = OpticalDepthAt(tileId, mean), pressureFactor = 1.0;
            bool dustStorm = false;
            if (ThinCo2Air && tileId >= 0 && tileId < TileCount)
            {
                double ls = SolarLongitudeDegAt(TimeHours);
                pressureFactor = ThinAirClimate.PressureFactor(ls);
                double extra = ThinAirClimate.GlobalStormTau(TerrainSeed, TimeHours, YearLengthHours, DayLengthHours)
                             + ThinAirClimate.RegionalStormTau(TerrainSeed, Sphere.Latitude(tileId), Sphere.Longitude(tileId), TimeHours, YearLengthHours, DayLengthHours, SolarLongitudeDegAt);
                dust = dust * ThinAirClimate.SeasonFactor(ls) + extra;
                dustStorm = extra >= DustStormTau;
            }
            double cloud = Math.Max(0.02, Math.Min(1.2, CloudFactor(tileId, mean) + a.CloudFactor));
            return new WeatherSample(cloud, AirTemperatureAt(tileId, mean) + a.TemperatureK, Math.Max(0.0, PressurePaAt(tileId) * pressureFactor + a.PressurePa),
                                     a.WindMs, dust, a.Storm || dustStorm);
        }
    }
}

using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Planet;

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

        /// <summary>The weather at a tile now: cloud factor, air temperature, pressure, wind and dust, climatology plus the day's anomaly.</summary>
        public WeatherSample WeatherAt(int tileId)
        {
            double mean = CurrentMeanAnomaly();
            WeatherAnomaly a = AnomalyAt(tileId);
            double cloud = Math.Max(0.02, Math.Min(1.2, CloudFactor(tileId, mean) + a.CloudFactor));
            return new WeatherSample(cloud, AirTemperatureAt(tileId, mean) + a.TemperatureK, Math.Max(0.0, PressurePaAt(tileId) + a.PressurePa),
                                     a.WindMs, OpticalDepthAt(tileId, mean), a.Storm);
        }
    }
}

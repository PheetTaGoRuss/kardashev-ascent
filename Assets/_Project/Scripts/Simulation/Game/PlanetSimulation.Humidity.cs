using System;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Humidity and the air's oxygen (M1, decided 2026-11-25). Earth-like planets use NASA POWER's RH2M by latitude and month with a daily anomaly; other worlds use a game rule
    /// by air class: a thin CO2 air like Mars's is nearly dry (about 0.1 % against liquid water: a column of a few precipitable micrometres, MSL REMS, Harri et al. 2014, J. Geophys. Res. Planets 119),
    /// a world with no air has none, any other world with air 40 % (game rule, TBD).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The mean relative humidity and its day-to-day spread at a tile now, percent.</summary>
        public (double Mean, double Sd) HumidityClimateAt(int tileId)
        {
            if (!WeatherModel.HasWeather) return (0.0, 0.0);
            if (Clouds == CloudModel.EarthClimatology) return HumidityClimate.At(Sphere.Latitude(tileId), YearFractionNow);
            if (planet.SurfacePressureEarth >= ThinAirClimate.MinPressureEarth && planet.SurfacePressureEarth <= ThinAirClimate.MaxPressureEarth) return (0.1, 0.05);
            return (40.0, 10.0);
        }

        /// <summary>The relative humidity now at a tile, percent: the climate plus the day's anomaly (the climate alone when weather is off).</summary>
        public double HumidityAt(int tileId)
        {
            var (mean, sd) = HumidityClimateAt(tileId);
            if (mean <= 0.0 || !HasWeather || tileId < 0 || tileId >= TileCount) return mean;
            double rh = mean + sd * WeatherModel.Noise(WeatherModel.Channel.Humidity, Sphere.Latitude(tileId), Sphere.Longitude(tileId), TimeHours, 0.6);
            return Math.Max(0.0, Math.Min(100.0, rh));
        }

        /// <summary>The oxygen fraction of the air (game rule, TBD): Earth-like planets 21 %, every other world about none.</summary>
        public double OxygenFraction => Clouds == CloudModel.EarthClimatology && planet.SurfacePressureEarth > 0.1 ? 0.2095 : 0.0;
    }
}

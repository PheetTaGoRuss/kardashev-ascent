using System;
using System.Linq;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>What the colony knows of the weather at a tile (UI marks): measured values are exact, the rest is climatology with a range of one standard deviation.</summary>
    public readonly struct WeatherKnowledge
    {
        public readonly bool Measured;
        public readonly KnowledgeEstimate AirC, WindMs, CloudFactor, PressurePa;
        /// <summary>Relative humidity, percent: exact where measured, else the climate with its spread (M1).</summary>
        public readonly KnowledgeEstimate Humidity;
        public WeatherKnowledge(bool measured, KnowledgeEstimate air, KnowledgeEstimate wind, KnowledgeEstimate cloud, KnowledgeEstimate pressure, KnowledgeEstimate humidity = default)
        { Humidity = humidity; Measured = measured; AirC = air; WindMs = wind; CloudFactor = cloud; PressurePa = pressure; }
    }

    public sealed partial class PlanetSimulation
    {
        /// <summary>
        /// Where today's weather is measured (game rule, TBD): the lander's own weather station, a Research Station, and any tile a robot stands on (it reads
        /// its own sensors). Elsewhere the colony only knows the climatology.
        /// </summary>
        public bool WeatherMeasuredAt(int tileId) =>
            HasWeather && tileId >= 0 && tileId < TileCount && (tileId == BaseTileId || HasResearchStationOn(tileId) || Workforce.Robots.Any(r => r.Tile == tileId));

        /// <summary>The weather the colony can state for a tile now: exact where it is measured, else climatology with its day-to-day spread.</summary>
        public WeatherKnowledge WeatherKnownAt(int tileId)
        {
            WeatherSample w = WeatherAt(tileId);
            if (!HasWeather || WeatherMeasuredAt(tileId))
                return new WeatherKnowledge(HasWeather, new KnowledgeEstimate(LiveAirTemperatureAt(tileId), 0.0), new KnowledgeEstimate(w.WindMs, 0.0),
                                            new KnowledgeEstimate(w.CloudFactor, 0.0), new KnowledgeEstimate(w.PressurePa, 0.0), new KnowledgeEstimate(w.RhPercent, 0.0));
            WeatherModel.Spread s = WeatherModel.SpreadAt(Sphere.Latitude(tileId), YearFractionNow);
            double mean = CurrentMeanAnomaly();
            return new WeatherKnowledge(false, new KnowledgeEstimate(DiurnalAirTemperatureAt(tileId, TimeHours), s.TemperatureSd), new KnowledgeEstimate(s.WindMean, s.WindSd),
                                        new KnowledgeEstimate(CloudFactor(tileId, mean), s.CloudSd), new KnowledgeEstimate(PressurePaAt(tileId), s.PressureSd), new KnowledgeEstimate(HumidityClimateAt(tileId).Mean, HumidityClimateAt(tileId).Sd));
        }

        /// <summary>
        /// A forecast <paramref name="daysAhead"/> days out (1 to 5), or null. It needs today measured at the tile. The best prediction is the climatology plus
        /// today's anomaly times rho^k, and the error has a standard deviation of sigma sqrt(1 - rho^(2k)), so the range is honest by construction (the model is
        /// a smoothed noise whose day-to-day correlation is the table's; it is not exactly a first-order process, so the true spread differs a little).
        /// </summary>
        public WeatherKnowledge? ForecastAt(int tileId, int daysAhead)
        {
            if (daysAhead < 1 || daysAhead > MaxForecastDays || !WeatherMeasuredAt(tileId)) return null;
            double lat = Sphere.Latitude(tileId), tNow = TimeHours, tK = TimeHours + 24.0 * daysAhead;
            double yfK = Climate.CloudClimate.YearFractionFromMeanAnomaly(YearLengthHours > 0 ? 2.0 * Math.PI * tK / YearLengthHours : 0.0);
            WeatherModel.Spread s = WeatherModel.SpreadAt(lat, YearFractionNow);
            WeatherAnomaly a = AnomalyAt(tileId);
            double meanK = YearLengthHours > 0 ? 2.0 * Math.PI * tK / YearLengthHours : 0.0;
            double Damp(double rho) => Math.Pow(Math.Max(0.0, rho), daysAhead);
            double Err(double sd, double rho) => sd * Math.Sqrt(Math.Max(0.0, 1.0 - Math.Pow(Math.Max(0.0, rho), 2 * daysAhead)));
            var air = new KnowledgeEstimate(DiurnalAirTemperatureAt(tileId, tK) + Damp(s.TemperatureRho) * a.TemperatureK, Err(s.TemperatureSd, s.TemperatureRho));
            var wind = new KnowledgeEstimate(Math.Max(0.0, s.WindMean + Damp(s.WindRho) * (a.WindMs - s.WindMean)), Err(s.WindSd, s.WindRho));
            var cloud = new KnowledgeEstimate(Math.Max(0.02, Math.Min(1.2, CloudFactor(tileId, meanK) + Damp(s.CloudRho) * a.CloudFactor)), Err(s.CloudSd, s.CloudRho));
            var pressure = new KnowledgeEstimate(Math.Max(0.0, PressurePaAt(tileId) + Damp(s.PressureRho) * a.PressurePa), Err(s.PressureSd, s.PressureRho));
            return new WeatherKnowledge(false, air, wind, cloud, pressure);
        }

        /// <summary>How far ahead a forecast goes, days.</summary>
        public const int MaxForecastDays = 5;
    }
}

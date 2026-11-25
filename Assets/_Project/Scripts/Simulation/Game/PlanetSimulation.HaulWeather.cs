using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Weather-aware haul planning (P2, decided 2026-11-25, director delegated): the planner reads the forecast before a loaded leg starts. Game rules, TBD:
    /// it plans at a pessimistic bound (one standard deviation toward the worse side, about the 84th percentile of a normal forecast error); a tile with no
    /// measurement within one weather region has only the climatology and its wide spread; a storm forecast on the route within the trip makes the haul wait
    /// unless the player says go anyway (that never overrides the energy round-trip check of KI-46).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>How many standard deviations the planner adds on the worse side (1 is the 84th percentile).</summary>
        public const double PlanningSigmas = 1.0;

        /// <summary>Forecast planning on (the default). Off, a haul ignores the forecast and the leg budget uses the coldest hour of the next day (the W8 rule): for balance comparisons.</summary>
        public bool ForecastPlanningEnabled { get; set; } = true;

        /// <summary>Robot-hours spent holding in shelter in a storm, and haul-hours spent waiting for a forecast storm (reports and balance runs, not saved).</summary>
        public double StormShelterRobotHours { get; private set; }
        public double HaulStormWaitHours { get; private set; }

        void AccumulateWeatherWaits(double dt)
        {
            if (!HasWeather) return;
            foreach (var r in Workforce.Robots) if (r.StormHold) StormShelterRobotHours += dt;
            foreach (HaulOrder o in haulBook.Orders) if (o.WeatherWait) HaulStormWaitHours += dt;
        }

        /// <summary>With the wind mast done, the Research Station tile is a measuring site for forecasts (a mast reads wind, pressure and temperature continuously).</summary>
        IEnumerable<int> ExtraMeasuredTiles()
        {
            if (!HasWeather || !IsExperimentKnown(ExperimentKind.WindMast)) yield break;
            int t = FinishedStationTile(-1);
            if (t >= 0) yield return t;
        }

        IEnumerable<int> MeasuredTiles()
        {
            yield return BaseTileId;
            foreach (var r in Workforce.Robots) yield return r.Tile;
            foreach (int t in ExtraMeasuredTiles()) yield return t;
        }

        /// <summary>The measured tile whose weather stands for this one (itself, or one within a weather region), or -1.</summary>
        int ForecastSource(int tile)
        {
            if (WeatherMeasuredAt(tile)) return tile;
            int best = -1; double bestKm = WeatherModel.RegionKm;
            foreach (int m in MeasuredTiles()) { double km = DistanceKm(tile, m); if (km < bestKm) { bestKm = km; best = m; } }
            return best;
        }

        /// <summary>The wind a haul should plan on at a tile k days ahead: the forecast plus one sigma, m/s, with its spread and whether a measurement informs it.</summary>
        (double Bound, double Sd, bool Informed, int Source) WindBound(int tile, int k)
        {
            int src = ForecastSource(tile);
            WeatherModel.Spread s = WeatherModel.SpreadAt(Sphere.Latitude(tile), YearFractionNow);
            if (src >= 0)
            {
                if (k == 0) { double w = WeatherAt(src).WindMs; return (w, 0.0, true, src); }
                WeatherKnowledge? f = ForecastAt(src, k);
                if (f.HasValue) return (f.Value.WindMs.Value + PlanningSigmas * f.Value.WindMs.HalfRange, f.Value.WindMs.HalfRange, true, src);
            }
            return (s.WindMean + PlanningSigmas * s.WindSd, s.WindSd, false, tile);
        }

        /// <summary>A storm forecast on the route within the trip: the first day, the tile, the bound and the threshold, or null.</summary>
        public (int Day, int Tile, double WindMs, double SdMs, double ThresholdMs)? StormForecastOnRoute(int from, int to, double tripHours)
        {
            if (!HasWeather || !ForecastPlanningEnabled || Routes == null) return null;
            List<int> route = Routes.Route(from, to);
            if (route == null) return null;
            route.Insert(0, from);
            int days = Math.Min(MaxForecastDays, (int)Math.Ceiling(Math.Max(1.0, tripHours) / 24.0));
            int step = Math.Max(1, route.Count / 7);
            for (int k = 0; k <= days; k++)
                for (int i = 0; i < route.Count; i += step)
                {
                    int tile = route[i];
                    var b = WindBound(tile, k);
                    double thr = StormThresholdMs(b.Source);   // a measured tile stands for its region, so the storm is judged against its own threshold
                    if (b.Bound >= thr) return (k, tile, b.Bound, b.Sd, thr);
                }
            return null;
        }

        /// <summary>The trip's duration, hours: the run out to the pick-up and the loaded leg, there and back, about twice the route time.</summary>
        double TripHours(int from, int to) => Routes == null ? 24.0 : 2.0 * Routes.HoursBetween(from, to);

        long lowForecastHour = long.MinValue; double lowForecastC;

        /// <summary>
        /// The lowest air temperature the planner expects at the base over today and tomorrow, C: the day-night low plus the forecast anomaly, one sigma colder
        /// (an unmeasured base would use the climatology and its full spread). The base is the lander's weather station, so it is always measured.
        /// </summary>
        double PessimisticLowC()
        {
            long hour = (long)Math.Floor(TimeHours);
            if (hour == lowForecastHour) return lowForecastC;
            lowForecastHour = hour;
            double lowest = double.MaxValue;
            WeatherModel.Spread s = WeatherModel.SpreadAt(Sphere.Latitude(BaseTileId), YearFractionNow);
            WeatherAnomaly a = AnomalyAt(BaseTileId);
            for (int k = 0; k <= 1; k++)
            {
                double anomaly, sd;
                if (WeatherMeasuredAt(BaseTileId)) { anomaly = a.TemperatureK * Math.Pow(Math.Max(0.0, s.TemperatureRho), k); sd = s.TemperatureSd * Math.Sqrt(Math.Max(0.0, 1.0 - Math.Pow(Math.Max(0.0, s.TemperatureRho), 2 * k))); }
                else { anomaly = 0.0; sd = s.TemperatureSd; }
                for (int j = 0; j < 12; j++)
                    lowest = Math.Min(lowest, DiurnalAirTemperatureAt(BaseTileId, TimeHours + 24.0 * k + j * 2.0) + anomaly - PlanningSigmas * sd);
            }
            lowForecastC = lowest;
            return lowest;
        }

        /// <summary>The player overrides a weather wait: the haul goes although a storm is forecast. It never overrides the energy check.</summary>
        public bool GoAnywayHaul(int haulId)
        {
            foreach (HaulOrder o in haulBook.Orders)
                if (o.Id == haulId && o.Stage == 0) { o.GoAnyway = true; return true; }
            return false;
        }

        public bool HaulWaitsForStorm(int haulId) { foreach (HaulOrder o in haulBook.Orders) if (o.Id == haulId) return o.WeatherWait; return false; }

        public bool HaulGoesAnyway(int haulId) { foreach (HaulOrder o in haulBook.Orders) if (o.Id == haulId) return o.GoAnyway; return false; }

        /// <summary>The structured storm reason of a waiting haul: the day it comes, the wind bound, the threshold and the tile; null when the wait is not the weather.</summary>
        public (int Day, double WindMs, double SdMs, double ThresholdMs, int Tile)? HaulStormWait(int haulId)
        {
            foreach (HaulOrder o in haulBook.Orders)
                if (o.Id == haulId && o.WeatherWait)
                {
                    var st = StormForecastOnRoute(o.FromTile, o.ToTile, TripHours(o.FromTile, o.ToTile));
                    if (st.HasValue) return (st.Value.Day, st.Value.WindMs, st.Value.SdMs, st.Value.ThresholdMs, st.Value.Tile);
                }
            return null;
        }
    }
}

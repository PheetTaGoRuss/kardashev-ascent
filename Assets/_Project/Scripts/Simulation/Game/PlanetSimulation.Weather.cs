using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>The weather at a tile now: the climatology plus the day's anomaly (Weather, PL-07, decided 2026-11-24, director delegated).</summary>
    public readonly struct WeatherSample
    {
        public readonly double CloudFactor, AirC, PressurePa, WindMs, DustTau;
        /// <summary>Relative humidity at 2 m, percent (M1).</summary>
        public readonly double RhPercent;
        /// <summary>A wind storm: the wind is above the local 98th percentile. Robots stop outdoor work.</summary>
        public readonly bool Storm;
        /// <summary>A dust storm: the extra optical depth is above <see cref="PlanetSimulation.DustStormTau"/> (it dims the panels but does not stop robots).</summary>
        public readonly bool DustStorm;
        public WeatherSample(double cloud, double airC, double pressurePa, double windMs, double dustTau, bool storm, bool dustStorm = false, double rhPercent = 0.0)
        { RhPercent = rhPercent; CloudFactor = cloud; AirC = airC; PressurePa = pressurePa; WindMs = windMs; DustTau = dustTau; Storm = storm; DustStorm = dustStorm; }
    }

    public sealed partial class PlanetSimulation
    {
        WeatherModel weatherModel;
        readonly Dictionary<int, WeatherAnomaly> weatherCache = new Dictionary<int, WeatherAnomaly>();
        long weatherCacheHour = long.MinValue;

        /// <summary>Weather on or off. On for new missions; a save before version 56 has none, so its balance does not shift.</summary>
        public bool WeatherEnabled { get; set; } = true;

        WeatherModel WeatherModel => weatherModel ??= new WeatherModel(TerrainSeed, planetRadiusM, planet.SurfacePressureEarth, Clouds == CloudModel.EarthClimatology);

        /// <summary>The wind speed, m/s, above which today is a storm day at this tile.</summary>
        public double StormThresholdMs(int tileId) => WeatherModel.StormWindMs(Sphere.Latitude(tileId), YearFractionNow);

        /// <summary>A wind storm over a tile now.</summary>
        public bool StormAt(int tileId) => HasWeather && AnomalyAt(tileId).Storm;

        bool dustySeasonLogged, globalStormLogged;

        /// <summary>The share of direct sunlight left when the optical depth rises by this much (Beer-Lambert along a vertical path; a rough guide, ignores the diffuse light).</summary>
        public static double SolarShareForExtraTau(double extraTau) => Math.Exp(-Math.Max(0.0, extraTau));

        /// <summary>
        /// Dust warnings from state the simulation already has (W8): one WATCH entry when the dusty season starts (Ls 180) and one when a global storm begins.
        /// The outlook is a range (a thin-air planet's own seasonal factor, regional storms up to +2, global storms up to the model's peak), marked estimated.
        /// </summary>
        void HandleDustWarnings()
        {
            if (!ThinCo2Air) return;
            double ls = SolarLongitudeDegAt(TimeHours);
            bool dusty = ls >= 180.0 && ls < 360.0;
            if (dusty && !dustySeasonLogged)
            {
                double tau = OpticalDepthAt(BaseTileId, CurrentMeanAnomaly()) * ThinAirClimate.SeasonFactor(270.0);
                double now = OpticalDepthAt(BaseTileId, CurrentMeanAnomaly());
                Log("Colony", "Dusty season", tau, "tau", "Weather", $"The dusty season begins (Ls 180): ◐ optical depth about {tau:0.0}, up to {tau + 2.0:0.0} in a regional storm; the panels' sunlight about {SolarShareForExtraTau(tau - now) * 100.0:0} % of the clear half, down to {SolarShareForExtraTau(tau + 2.0 - now) * 100.0:0} % in a storm. A global storm is possible (about 1 year in 3). ◐ {SizingText(false)}");
            }
            dustySeasonLogged = dusty;
            double global = ThinAirClimate.GlobalStormTau(TerrainSeed, TimeHours, YearLengthHours, DayLengthHours);
            bool storm = global > 1.0;
            if (storm && !globalStormLogged)
                Log("Colony", "Global dust storm", global, "tau", "Weather", $"A global dust storm begins: optical depth now +{global:0.0}, ◐ it may reach {ThinAirClimate.GlobalPeakMin:0} to {ThinAirClimate.GlobalPeakMax:0} within about {ThinAirClimate.RiseDays:0} sols and decay over weeks to months; panels would make about {SolarShareForExtraTau(ThinAirClimate.GlobalPeakMin) * 100.0:0.0} to {SolarShareForExtraTau(ThinAirClimate.GlobalPeakMax) * 100.0:0.00} % of their usual sunlight at the peak. Rely on the RTG and the battery. ◐ {SizingText(true)}");
            globalStormLogged = storm;
        }

        bool baseStormLogged;
        readonly HashSet<int> stormHeld = new HashSet<int>();

        /// <summary>
        /// Storm days (W5): a robot under a storm does no outdoor work and goes to the nearest shelter (the lander, a charger, a battery yard or a workshop).
        /// The colony is told once when a storm reaches the base. A sheltered robot just waits it out; the existing destroyed-when-unsheltered rule is unchanged.
        /// </summary>
        void HandleStorms()
        {
            HandleDustWarnings();
            if (!HasWeather) { foreach (Robot r0 in Workforce.Robots) r0.StormHold = false; return; }
            bool baseStorm = StormAt(BaseTileId);
            if (baseStorm && !baseStormLogged)
            {
                var w = WeatherAt(BaseTileId);
                Log("Colony", "Storm", w.WindMs, "m/s", "Weather", $"A storm reaches the base: wind {w.WindMs:0.#} m/s, above the local 98th percentile. Robots stop outdoor work and shelter until it passes.");
            }
            baseStormLogged = baseStorm;
            var shelters = ShelterTiles();
            foreach (Robot r in Workforce.Robots)
            {
                bool storm = StormAt(r.Tile);
                r.StormHold = storm;
                if (!storm || shelters.Contains(r.Tile) || r.ManualTarget.HasValue || Routes == null) continue;
                int best = -1; double bestH = double.PositiveInfinity;
                foreach (int s in shelters) { double h = Routes.HoursBetween(r.Tile, s); if (h < bestH) { bestH = h; best = s; } }
                if (best >= 0 && !double.IsInfinity(bestH)) Workforce.SendTo(r.Id, best);
            }
        }

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
            Workforce.OutdoorHeaterWAt ??= (r, tile) => Robots.RobotThermal.SteadyHeaterW(LiveAirTemperatureAt(tile), HasWeather ? Robots.RobotThermal.WindFactor(AnomalyAt(tile).WindMs, planet.SurfacePressureEarth) : 1.0, r.Has(Robots.RobotModule.ThermalInsulation), Workforce.Movement != null ? Workforce.Movement.DrivePowerW : 150.0);
            Workforce.OutdoorTripHeater ??= (r, tile, hours) =>
            {
                double wind = HasWeather ? Robots.RobotThermal.WindFactor(AnomalyAt(tile).WindMs, planet.SurfacePressureEarth) : 1.0;
                var tr = Robots.RobotThermal.TripHeater(r.BodyTempC, LiveAirTemperatureAt(tile), hours, wind, r.Has(Robots.RobotModule.ThermalInsulation), Workforce.Movement != null ? Workforce.Movement.DrivePowerW : 150.0);
                return (tr.Wh, tr.MinC);
            };
            Workforce.OutdoorAirIsCold ??= tile => LiveAirTemperatureAt(tile) < Robots.RobotThermal.OperatingMinC;
            Workforce.OneWayOutpostOk ??= (r, tile) => OneWayVerdict(r, tile).Allowed;
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
                                     a.WindMs, dust, a.Storm, dustStorm, HumidityAt(tileId));
        }
    }
}

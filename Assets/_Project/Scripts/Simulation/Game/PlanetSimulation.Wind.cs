using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Wind turbines (T2, decided 2026-11-25, director delegated): 100 kW class machines on land whose power follows the day's wind and the air density.</summary>
    public sealed partial class PlanetSimulation
    {
        readonly HashSet<(int Tile, int Slot)> windTurbines = new HashSet<(int, int)>();
        readonly Dictionary<int, double> rawWind = new Dictionary<int, double>();

        /// <summary>The day's mean wind at 10 m at a tile, m/s: the weather's wind, or the climatology mean when weather is off (zero without air).</summary>
        double Wind10At(int tileId)
        {
            if (HasWeather) return AnomalyAt(tileId).WindMs;
            return WeatherModel.HasWeather ? WeatherModel.SpreadAt(Sphere.Latitude(tileId), YearFractionNow).WindMean : 0.0;
        }

        /// <summary>The air density at a tile now, kg/m3 (the ideal gas law on the day's pressure and air temperature).</summary>
        public double AirDensityAt(int tileId)
        {
            WeatherSample w = WeatherAt(tileId);
            return WindPower.AirDensity(w.PressurePa, w.AirC, AirMolarMassKg);
        }

        /// <summary>The wind at the hub of a turbine on this tile now (the daily mean), m/s.</summary>
        public double HubWindAt(int tileId) => WindPower.HubWindMs(Wind10At(tileId));

        /// <summary>One turbine's mean electric power today at a tile, W (before wiring and lines): the power curve over a Rayleigh day, zero above cut-out.</summary>
        public double WindTurbinePowerAtW(int tileId) => WindPower.DailyMeanPowerW(HubWindAt(tileId), AirDensityAt(tileId));

        /// <summary>Why the turbine on this tile makes what it makes now, in a line (the reason is shown when it is idle).</summary>
        public string WindTurbineStatus(int tileId)
        {
            double hub = HubWindAt(tileId), rho = AirDensityAt(tileId);
            if (rho <= 0.0 || hub <= 0.0) return "idle: no air to push the rotor";
            if (hub >= WindPower.CutOutMs) return $"shut down: hub wind {hub:0.#} m/s is above the {WindPower.CutOutMs:0} m/s cut-out";
            double p = WindPower.DailyMeanPowerW(hub, rho);
            if (p < 1.0) return $"idle: hub wind {hub:0.#} m/s is mostly below the {WindPower.CutInMs:0} m/s cut-in";
            return $"{p / 1000.0:0.0} kW: hub wind {hub:0.#} m/s, air density {rho:0.000} kg/m3";
        }

        public bool PlaceWindTurbine(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.WindTurbine, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.WindTurbine), BuildTimes.Hours(BuildingType.WindTurbine));
            windTurbines.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.WindTurbine, tileId, slot));
            GenerationW = ComputeGeneration() + RtgW;
            Settle((tileId, slot));
            return true;
        }

        /// <summary>Wind turbines on this tile (a game rule allows one: no wake model).</summary>
        public int WindTurbineCount(int tileId) { int n = 0; foreach (var k in windTurbines) if (k.Tile == tileId) n++; return n; }

        public System.Collections.Generic.IReadOnlyList<int> WindTurbineTiles() { var l = new List<int>(); foreach (var k in windTurbines) if (!l.Contains(k.Tile)) l.Add(k.Tile); l.Sort(); return l; }

        long windPowerHour = long.MinValue;
        readonly Dictionary<int, double> windPowerCache = new Dictionary<int, double>();

        void RawWindByTile(Dictionary<int, double> map)
        {
            if (windTurbines.Count == 0) return;
            long hour = (long)Math.Floor(TimeHours);
            if (hour != windPowerHour) { windPowerCache.Clear(); windPowerHour = hour; }
            foreach (var key in windTurbines)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot)) continue;
                if (!WiredOrOff(key)) continue;
                if (!windPowerCache.TryGetValue(key.Tile, out double raw)) windPowerCache[key.Tile] = raw = WindTurbinePowerAtW(key.Tile);
                map[key.Tile] = (map.TryGetValue(key.Tile, out double v) ? v : 0.0) + raw;
            }
        }

        public double WindPowerW() { EnsureFlow(); return Delivered(rawWind); }
    }
}

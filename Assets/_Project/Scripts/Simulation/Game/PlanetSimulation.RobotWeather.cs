using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Robots and the weather on the planet side (KI-43, decided 2026-11-23): where shelter is, what happens when a robot is destroyed, and the
    /// warning before it comes to that. The rules are in <see cref="RobotThermal"/> and <see cref="RobotUpkeep"/>.
    /// </summary>
    public sealed partial class PlanetSimulation : IRobotWeather
    {
        /// <summary>Rises by one each time the colony has news of a robot and the weather (a warning or a loss): the HUD shows a short toast for each.</summary>
        public int RobotWeatherNewsSerial { get; private set; }

        /// <summary>The latest of those, in a short line.</summary>
        public string RobotWeatherNews { get; private set; } = "";

        /// <summary>Robots lost to the weather so far (not saved; the operations log keeps the record).</summary>
        public int RobotsDestroyed { get; private set; }

        /// <summary>Test hook: the inventories (so a test can load a robot with cargo).</summary>
        internal InventoryBook InventoriesForTests => inventories;

        /// <summary>The tiles where a robot is sheltered: the lander, battery yards, charging stations and Workshops (finished).</summary>
        HashSet<int> ShelterTiles()
        {
            var tiles = new HashSet<int> { BaseTileId };
            foreach (var b in batteries) if (!Build.IsQueued(b.Tile, b.Slot)) tiles.Add(b.Tile);
            foreach (var c in stations) if (!Build.IsQueued(c.Tile, c.Slot)) tiles.Add(c.Tile);
            foreach (var w in workshops) if (!Build.IsQueued(w.Tile, w.Slot)) tiles.Add(w.Tile);
            return tiles;
        }

        void HandleRobotWeather()
        {
            HandleStorms();
            // The warning: a robot outside its range whose battery will not last the night.
            foreach (Robot r in Workforce.Robots)
            {
                // Until the ground temperature is measured the warning goes by the cautious end of the estimate (decided 2026-11-23).
                double air = LiveAirTemperatureAt(r.Tile);
                double heater = r.HeaterW, hoursLeft = r.ThermalHoursLeft;
                double tempSpread = Planet.OrbitalBrief.TemperatureSpreadC * ExperimentRangeShare(Planet.ExperimentKind.SurfaceTemperature);
                bool estimated = tempSpread > 0.0;
                if (estimated && !r.Sheltered)
                {
                    double mid = (RobotThermal.OperatingMinC + RobotThermal.OperatingMaxC) / 2.0;
                    double cautious = air + (air < mid ? -tempSpread : tempSpread);
                    heater = RobotThermal.HeaterW(cautious, r.Has(RobotModule.ThermalInsulation));
                    hoursLeft = heater > 0.0 ? r.Battery.UsableWh / heater : double.PositiveInfinity;
                }
                if (heater > 0.0 && hoursLeft < RobotThermal.WarnWithinHours && !r.ThermalWarned)
                {
                    r.ThermalWarned = true;
                    RobotWeatherNews = estimated
                        ? $"Robot {r.Id} may be outside its range (ground temperature not measured, plus or minus {Planet.OrbitalBrief.TemperatureSpreadC:0} C): battery may last only {hoursLeft:0.0} h. It needs shelter, or measure the surface temperature."
                        : $"Robot {r.Id} is outside its range: battery lasts {hoursLeft:0.0} h. It needs shelter.";
                    RobotWeatherNewsSerial++;
                    Log($"Robot {r.Id}", "Cold or heat warning", hoursLeft, "h",
                        $"R{r.Id}", $"Robot {r.Id} is {(estimated ? "possibly " : "")}outside its operating range ({air:0} C{(estimated ? ", unmeasured" : "")}) and its battery lasts about {hoursLeft:0.0} h at the heater draw ({heater:0} W). It needs shelter.", r.Id);
                }
            }
            if (Upkeep.DestroyedRobotIds.Count == 0) return;
            foreach (int id in Upkeep.DestroyedRobotIds.ToList()) DestroyRobot(id);
            Settle(null);
        }

        void DestroyRobot(int robotId)
        {
            Robot r = Workforce.GetRobot(robotId);
            if (r == null) return;
            int tile = r.Tile;
            double air = AirTemperatureAt(tile, CurrentMeanAnomaly());
            // What it had not yet handed in is lost; what it carried stays where it stopped.
            int lost = heldFindings.RemoveAll(f => f.RobotId == robotId);
            double cargoKg = 0.0;
            var place = PlaceId.Cargo(robotId);
            if (inventories != null && inventories.Has(place))
            {
                var stock = inventories.StockOf(place);
                foreach (Material m in Resources.Materials.All)
                {
                    double kg = stock[m];
                    if (kg <= 0.0) continue;
                    cargoKg += inventories.Move(place, PlaceId.Depot(tile), m, kg);
                }
                inventories.Remove(place);
            }
            Log($"Robot {robotId}", "Robot destroyed", air, "C",
                $"R{robotId}", $"Robot {robotId} was destroyed on tile {tile}: its battery ran out outside its operating range ({air:0} C)."
                + (lost > 0 ? $" {lost} unsynced finding{(lost == 1 ? "" : "s")} lost." : "")
                + (cargoKg > 0.0 ? $" Its {cargoKg:0} kg of cargo stays on the tile." : ""), robotId);
            Workforce.RemoveRobot(robotId);
            RobotsDestroyed++;
            RobotWeatherNews = $"Robot {robotId} was destroyed: battery empty at {air:0} C.";
            RobotWeatherNewsSerial++;
        }
    }
}

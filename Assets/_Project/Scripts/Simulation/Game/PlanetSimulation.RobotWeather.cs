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
    public sealed partial class PlanetSimulation
    {
        /// <summary>Robots lost to the weather so far (not saved; the operations log keeps the record).</summary>
        public int RobotsDestroyed { get; private set; }

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
            // The warning: a robot outside its range whose battery will not last the night.
            foreach (Robot r in Workforce.Robots)
            {
                if (r.HeaterW > 0.0 && r.ThermalHoursLeft < RobotThermal.WarnWithinHours && !r.ThermalWarned)
                {
                    r.ThermalWarned = true;
                    double air = AirTemperatureAt(r.Tile, CurrentMeanAnomaly());
                    Log($"Robot {r.Id}", "Cold or heat warning", r.ThermalHoursLeft, "h",
                        $"R{r.Id}", $"Robot {r.Id} is outside its operating range ({air:0} C) and its battery lasts about {r.ThermalHoursLeft:0.0} h at the heater draw ({r.HeaterW:0} W). It needs shelter.", r.Id);
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
        }
    }
}

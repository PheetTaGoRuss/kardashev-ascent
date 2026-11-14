using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        /// <summary>Driving routes on this planet (terrain pace, drive power ∝ surface gravity).</summary>
        public RouteFinder Routes { get; private set; }

        /// <summary>Robots drive between tiles (true) or work any tile from where they are (false, as before movement).</summary>
        public bool MovementEnabled { get; set; } = true;

        string chargerKey;
        Movement movement;

        /// <summary>Routes plus driving hours to the nearest charger from every tile (recomputed when chargers change).</summary>
        Movement CurrentMovement()
        {
            if (!MovementEnabled)
                return null;
            List<RobotFacility> facilities = RobotFacilities();
            var tiles = new List<int>();
            foreach (RobotFacility f in facilities)
                if (!tiles.Contains(f.Tile)) tiles.Add(f.Tile);
            tiles.Sort();
            string key = string.Join(",", tiles);
            if (movement == null || key != chargerKey)
            {
                movement = new Movement(Routes, Routes.HoursFromNearest(tiles));
                chargerKey = key;
            }
            return movement;
        }

        public double DriveHours(int fromTile, int toTile) =>
            fromTile < 0 || toTile < 0 || fromTile >= TileCount || toTile >= TileCount ? double.PositiveInfinity : Routes.HoursBetween(fromTile, toTile);

        public bool SendRobot(int robotId, int tileId)
        {
            Workforce.Movement = CurrentMovement();
            if (!Workforce.SendTo(robotId, tileId))
                return false;
            Settle(null);
            return true;
        }
    }
}

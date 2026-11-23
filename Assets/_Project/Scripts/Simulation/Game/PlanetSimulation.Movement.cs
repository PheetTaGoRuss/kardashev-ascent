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
            // Only places that charge count for the way back (the lander repairs but doesn't charge).
            foreach (RobotFacility f in facilities)
                if (f.Capability.Charges && !tiles.Contains(f.Tile)) tiles.Add(f.Tile);
            tiles.Sort();
            string key = string.Join(",", tiles);
            if (movement == null || key != chargerKey)
            {
                movement = new Movement(Routes, Routes.HoursFromNearest(tiles), Routes.Amphibious().HoursFromNearest(tiles));
                if (ClimbEnabled) { movement.HeightM = TileHeightsM(); movement.GravityMs2 = SurfaceGravityMs2; }
                chargerKey = key;
            }
            return movement;
        }

        /// <summary>Whether hauling pays for climbing (KI-42, decided 2026-11-23). On by default.</summary>
        public bool ClimbEnabled { get; set; } = true;

        double[] tileHeightsM;

        /// <summary>The planet's relief (lowest and highest, m): Earth's, Mars's from MOLA, or Earth's scaled by gravity (assumption).</summary>
        public (double MinM, double MaxM) Relief => ReliefScale.For(PlanetId, SurfaceGravityMs2);

        /// <summary>The height of a tile above sea level, m (0 on water).</summary>
        public double TileHeightM(int tileId) => TileHeightsM()[tileId];

        double[] TileHeightsM()
        {
            if (tileHeightsM != null) return tileHeightsM;
            var relief = Relief;
            tileHeightsM = new double[TileCount];
            for (int t = 0; t < TileCount; t++)
                tileHeightsM[t] = GetTile(t).IsWater ? 0.0 : ReliefScale.HeightM(surface.Elevation[t], relief);
            return tileHeightsM;
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

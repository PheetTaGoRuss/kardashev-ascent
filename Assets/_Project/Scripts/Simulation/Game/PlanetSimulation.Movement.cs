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
        RouteFinder routes;
        bool routeCostSet;

        public RouteFinder Routes
        {
            get { if (!routeCostSet && routes != null) ConfigureRouteCost(); return routes; }
            private set { routes = value; routeCostSet = false; }
        }

        /// <summary>
        /// Contour-following routes (L4, decided 2026-11-24, director delegated): the route finder weighs the climb and the heater against time
        /// with one weight. On for new missions; an older save keeps time-only routes. Game rule, TBD.
        /// </summary>
        public bool ContourRouting
        {
            get => contourRouting;
            set { contourRouting = value; routeCostSet = false; movement = null; }
        }
        bool contourRouting = true;

        /// <summary>Hours of driving one Wh is worth when choosing a route (game rule, TBD): one Wh costs as much as the time to drive it at the drive power.</summary>
        double RouteHoursPerWh => routes.DrivePowerW > 0.0 ? 1.0 / routes.DrivePowerW : 0.0;

        void ConfigureRouteCost()
        {
            routeCostSet = true;
            movement = null;
            if (!contourRouting || !ClimbEnabled) { routes.SetEnergyCost(null, 0.0); return; }
            double[] heights = TileHeightsM();
            double g = SurfaceGravityMs2;
            double[] meanAirC = new double[TileCount];
            for (int t = 0; t < TileCount; t++) meanAirC[t] = double.NaN;
            routes.SetEnergyCost((from, to, hopHours) =>
            {
                double up = heights[to] - heights[from];
                double wh = up > 0.0 ? Logistics.HaulPhysics.ClimbEnergyWh(Logistics.HaulPhysics.DefaultRobotMassKg, 0.0, g, up, 1.0) * Logistics.HaulPhysics.ClimbEffortShare : 0.0;
                if (double.IsNaN(meanAirC[to])) meanAirC[to] = AnnualAirTemperature(to);
                wh += RobotThermal.HeaterW(meanAirC[to], false) * hopHours;
                return wh;
            }, RouteHoursPerWh);
        }

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
                if (ClimbEnabled)
                {
                    movement.HeightM = TileHeightsM(); movement.GravityMs2 = SurfaceGravityMs2;
                    // the height of the nearest charger (straight line) for each tile: what a robot may have to climb on the way back
                    var near = new double[TileCount];
                    for (int t = 0; t < TileCount; t++)
                    {
                        double best = double.MaxValue; int bestTile = -1;
                        foreach (int c in tiles)
                        {
                            var ca = Sphere.Centers[t]; var cb = Sphere.Centers[c]; double d = -(ca.X * cb.X + ca.Y * cb.Y + ca.Z * cb.Z);   // nearest on the sphere = largest dot product
                            if (d < best) { best = d; bestTile = c; }
                        }
                        near[t] = bestTile >= 0 ? movement.HeightM[bestTile] : movement.HeightM[t];
                    }
                    movement.NearestChargerHeightM = near;
                }
                chargerKey = key;
            }
            // Until the terrain relief is surveyed, robots keep a climb reserve for the high end of the relief range (RS-14, decided 2026-11-24).
            var high = HighestPointEstimate;
            movement.ReliefReserveScale = high.Value > 0.0 ? Math.Max(1.0, high.High / high.Value) : 1.0;
            return movement;
        }

        /// <summary>Whether hauling pays for climbing (KI-42, decided 2026-11-23). On by default.</summary>
        public bool ClimbEnabled { get => climbEnabled; set { climbEnabled = value; routeCostSet = false; } }
        bool climbEnabled = true;

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

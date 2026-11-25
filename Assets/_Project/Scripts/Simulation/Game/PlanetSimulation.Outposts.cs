using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Leapfrog outposts (decided 2026-11-25, director's delegate). A build trip to a site that is not on the lander grid, with a charging station and solar panels
    /// among its sites, may be one-way when the robot's battery covers the outbound leg and the build at the cold capacity, and the outpost's own panels then charge it
    /// to its return reserve before it is lost. Otherwise the KI-46 round-trip rule stands. The materials come through the build bill as for any site (no separate kit).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The result of the one-way check for a robot and an outpost tile.</summary>
        public readonly struct OutpostVerdict
        {
            public readonly bool IsOutpost, Allowed;
            public readonly int Sol;
            public readonly string Text;
            public OutpostVerdict(bool isOutpost, bool allowed, int sol, string text) { IsOutpost = isOutpost; Allowed = allowed; Sol = sol; Text = text; }
        }

        /// <summary>Longest wait for the outpost's panels to charge a robot, sols (game rule, TBD).</summary>
        public const int OutpostMaxSols = 10;

        /// <summary>The panel sites and buildings, and the station sites, on a tile that is off the lander grid.</summary>
        bool OutpostSites(int tile, out int panelCount, out double buildHours)
        {
            panelCount = 0; buildHours = 0.0;
            bool station = false;
            for (int s = 0; s < SlotCount(tile); s++)
            {
                BuildingType type = GetBuilding(tile, s);
                bool queued = Build.QueueCount > 0 && Build.IsQueued(tile, s);
                if (type == BuildingType.SolarPanel) { panelCount++; if (queued) buildHours += BuildTimes.Hours(type); }
                else if (type == BuildingType.ChargingStation) { station = true; if (queued) buildHours += BuildTimes.Hours(type); }
                else if (type == BuildingType.Battery && queued) buildHours += BuildTimes.Hours(type);
            }
            return station && panelCount > 0 && buildHours > 0.0;
        }

        /// <summary>
        /// Where a site takes its materials from: its own tile's stock, except an outpost (off the grid, with a station and panels among its sites), whose kit the robot carries from
        /// the base (the existing cargo rule: the drive energy of the outbound leg counts the kit's mass, see <see cref="OneWayVerdict"/>).
        /// </summary>
        Resources.MaterialStock BuildStockAt(int tile) => WiredGrid && !IsPowered(tile) && tile != BaseTileId && OutpostSites(tile, out _, out _) ? StockAt(BaseTileId) : StockAt(tile);

        /// <summary>Mass of the materials still to be fitted on a tile, kg (the kit a robot carries to an outpost).</summary>
        double KitKg(int tile)
        {
            double kg = 0.0;
            for (int s = 0; s < SlotCount(tile); s++) if (GetBuilding(tile, s) != BuildingType.None && Build.QueueCount > 0 && Build.IsQueued(tile, s)) kg += Build.RemainingAt(tile, s).TotalKg;
            return kg;
        }

        /// <summary>Is a one-way build trip to this tile allowed for this robot? Simulated hour by hour from the robot's arrival.</summary>
        public OutpostVerdict OneWayVerdict(Robot r, int tile)
        {
            Movement mv = Workforce.Movement;
            if (!WiredGrid || IsPowered(tile) || mv == null) return new OutpostVerdict(false, false, 0, "");
            if (!OutpostSites(tile, out int panels, out double buildHours)) return new OutpostVerdict(false, false, 0, "");
            double speed = r.Attribute(RobotAttribute.Speed);
            double go = mv.RoutesFor(r).HoursBetween(r.Tile, tile) / speed;
            double back = mv.HoursToChargerFor(r)[tile] / speed;   // the way home from the outpost, if it must
            if (double.IsInfinity(go)) return new OutpostVerdict(true, false, 0, "refused: there is no route to tile " + tile);
            double airDest = LiveAirTemperatureAt(tile);
            bool cold = airDest < RobotThermal.OperatingMinC;
            double heaterW = RobotThermal.HeaterW(airDest, r.Has(RobotModule.ThermalInsulation));
            // the cold capacity: the battery is held at the operating limit outdoors (Bressan et al. 2025 curve, RobotBattery.ColdCapacityFactor)
            double coldFactor = cold ? RobotBattery.ColdCapacityFactor(RobotThermal.OperatingMinC) : 1.0;
            double cap = r.Battery.CapacityWh * Math.Min(r.Battery.ColdFactor, coldFactor);
            double have = Math.Max(0.0, r.Battery.UsableWh - r.Battery.CapacityWh * Math.Max(0.0, r.Battery.ColdFactor - coldFactor));
            double kit = KitKg(tile);
            double outbound = mv.EnergyWh(go) * Logistics.HaulPhysics.LoadFactor(Logistics.HaulPhysics.DefaultRobotMassKg, kit) / r.Attribute(RobotAttribute.EnergyEfficiency) + mv.TripClimbWh(r, r.Tile, tile) + heaterW * go;   // the kit rides as cargo
            double build = buildHours * (RobotSpecs.BuildPowerW + heaterW);
            double left = have - outbound - build;
            if (left < 0.0)
            {
                // the battery runs out before the station stands: the robot hibernates exposed
                double tau = RobotThermal.BodyTauHours();
                double minBody = airDest + (RobotThermal.OperatingMinC - airDest) * Math.Exp(-12.0 / tau);
                return new OutpostVerdict(true, false, 0, minBody < RobotThermal.StorageMinC
                    ? $"refused: its battery would run out during the build and it would freeze at {minBody:0} C before sunrise"
                    : $"refused: its battery would run out {(-left):0} Wh short of finishing the build");
            }
            // after the build the robot sits in the station (a shelter, battery 15 C) and the panels charge it: simulate until it holds the return reserve
            double returnNeed = RobotSpecs.ReturnToChargeFraction * cap + (mv.EnergyWh(back) / r.Attribute(RobotAttribute.EnergyEfficiency) + heaterW * back) + 0.05 * cap;
            double meanW = panels * PreviewPanel(tile, 0.35, Math.PI).EnergyPerYearWh / Solar.SolarConstants.EarthYearHours;
            double day = DayLengthHours, e = left;
            int sol = 0;
            for (int h = 0; h < OutpostMaxSols * (int)Math.Ceiling(day) && e < returnNeed; h++)
            {
                double phase = ((TimeHours + go + buildHours + h) % day) / day;
                // the daylight half of the day as a half-sine whose mean over the whole day is the panels' mean power: peak = mean x pi
                double shape = phase < 0.5 ? Math.Sin(Math.PI * phase * 2.0) : 0.0;
                e = Math.Min(cap, e + meanW * Math.PI * shape);
                sol = (int)(h / day) + 1;
            }
            if (e < returnNeed)
                return new OutpostVerdict(true, false, 0, $"refused: the outpost's {panels} panel(s) would not charge it to its return reserve within {OutpostMaxSols} sols");
            return new OutpostVerdict(true, true, sol, $"one-way: the outpost will charge it by sol {sol}");
        }

        /// <summary>The text for the Why? chain of a build site that is an outpost candidate (empty for other tiles).</summary>
        public string OutpostText(int tile)
        {
            if (Workforce.Robots.Count == 0) return "";
            OutpostVerdict first = default;
            foreach (Robot r in Workforce.Robots)
            {
                var v = OneWayVerdict(r, tile);
                if (!v.IsOutpost) return "";
                if (v.Allowed) return v.Text;
                if (!first.IsOutpost) first = v;
            }
            return first.Text;
        }
    }
}

using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What a haul really costs a robot (logistics on the climb and weather costs, decided 2026-11-24, director delegated;
    /// docs/design/logistics-routes.md). The planner chooses sources by this cost, not by driving hours alone: a source up a mountain or in the
    /// cold costs more than one on the flat in mild air. All numbers are game rules, TBD, built on the climb term (KI-42) and the robot
    /// weather rules (KI-43).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The energy of one leg of a haul, Wh, split by cause.</summary>
        public readonly struct HaulLeg
        {
            public readonly double Hours, DriveWh, ClimbWh, HeaterWh;
            public HaulLeg(double hours, double drive, double climb, double heater) { Hours = hours; DriveWh = drive; ClimbWh = climb; HeaterWh = heater; }
            public double TotalWh => DriveWh + ClimbWh + HeaterWh;
            public bool Reachable => !double.IsInfinity(Hours);
        }

        /// <summary>One leg of a haul from one tile to another with this much cargo (loaded out, empty back).</summary>
        public HaulLeg HaulLegCost(int from, int to, double cargoKg)
        {
            if (Routes == null || from == to) return new HaulLeg(0.0, 0.0, 0.0, 0.0);
            List<int> route = Routes.Route(from, to);
            if (route == null) return new HaulLeg(double.PositiveInfinity, 0.0, 0.0, 0.0);
            double mean = CurrentMeanAnomaly();
            double hours = 0.0, drive = 0.0, climb = 0.0, heater = 0.0;
            double power = cargoKg > 0.0 ? Routes.DrivePowerW * HaulPhysics.LoadFactor(HaulPhysics.DefaultRobotMassKg, cargoKg) : Routes.DrivePowerW;
            double[] heights = ClimbEnabled ? TileHeightsM() : null;
            // the climb is planned at the high end of the relief range until the terrain is surveyed (decided 2026-11-24)
            var high = HighestPointEstimate;
            double scale = high.Value > 0.0 ? Math.Max(1.0, high.High / high.Value) : 1.0;
            int at = from;
            foreach (int t in route)
            {
                double step = Routes.HopHoursAt(t);
                hours += step;
                drive += power * step;
                if (heights != null)
                    climb += HaulPhysics.ClimbEnergyWh(HaulPhysics.DefaultRobotMassKg, cargoKg, SurfaceGravityMs2, heights[t] - heights[at], 1.0) * HaulPhysics.ClimbEffortShare * scale;
                heater += RobotThermal.HeaterW(AirTemperatureAt(t, mean), false) * step;
                at = t;
            }
            return new HaulLeg(hours, drive, climb, heater);
        }

        public string HaulCostText(int from, int to, double cargoKg)
        {
            var (o, back) = HaulTripCost(from, to, cargoKg);
            if (!o.Reachable) return "no route";
            double drive = o.DriveWh + back.DriveWh, climb = o.ClimbWh + back.ClimbWh, heater = o.HeaterWh + back.HeaterWh;
            return $"{o.Hours + back.Hours:0} h, {drive + climb + heater:0} Wh round trip (driving {drive:0}, climbing {climb:0}, heater {heater:0})";
        }

        /// <summary>The round trip of a haul: loaded out, empty back.</summary>
        public (HaulLeg Out, HaulLeg Back) HaulTripCost(int from, int to, double cargoKg) => (HaulLegCost(from, to, cargoKg), HaulLegCost(to, from, 0.0));

        /// <summary>
        /// The cost the planner ranks sources by, in hours: the driving hours plus the extra energy of the climb and of the heater, as hours of
        /// flat driving at the drive power (Wh over W). Equal to the driving hours on flat ground in mild air.
        /// </summary>
        public double HaulCostHours(int from, int to, double cargoKg)
        {
            var (o, _) = HaulTripCost(from, to, cargoKg);
            if (!o.Reachable) return double.PositiveInfinity;
            double extraWh = o.ClimbWh + o.HeaterWh;
            return o.Hours + (Routes.DrivePowerW > 0.0 ? extraWh / Routes.DrivePowerW : 0.0);
        }
    }
}

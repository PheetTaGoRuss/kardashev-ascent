using System;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        /// <summary>
        /// Why robots cannot work on this tile (director's balance pass 2026-11-20: a new colony never mined its copper because it
        /// was out of range and nothing said so), or null. A robot drives out and back on one charge and keeps a fifth in reserve,
        /// so there is a farthest tile from a charger it can work. Null when it can, and when there is no charger at all
        /// (then robots work until their battery is empty).
        /// </summary>
        public string WorkReachProblem(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount || !MovementEnabled)
                return null;
            Movement m = CurrentMovement();
            if (m == null || !m.HasCharger)
                return null;
            double back = m.HoursToCharger[tileId];
            double spareWh = RobotSpecs.BatteryCapacityWh * (1.0 - RobotSpecs.ReturnToChargeFraction);
            double reachHours = spareWh / (2.0 * m.DrivePowerW);   // one way, at the standard pace
            if (double.IsInfinity(back))
                return "No robot can drive there from a charger (water or a barrier is in the way).";
            if (m.EnergyWh(2.0 * back) <= spareWh)
                return null;
            return $"Out of range: {back:0} h of driving from the nearest charger, and a robot can go {reachHours:0} h out and back on one charge. Build a charging station nearer.";
        }
    }
}

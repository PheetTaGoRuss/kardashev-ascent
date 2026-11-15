using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// Facility workforce (spec v2 §5): how many robots a kind of work can use at once, and how their work adds up.
    /// <list type="bullet">
    /// <item>Workshop jobs and mine sites take several robots; building, salvage and surveys take one.</item>
    /// <item>Workers add with diminishing returns: the best worker counts fully, the next ×<see cref="ExtraWorkerFactor"/>,
    /// the next ×<see cref="ExtraWorkerFactor"/>², … (rate = Σ pᵢ·fⁱ, best first), so one robot works exactly as before.</item>
    /// <item>Panels and battery yards run without workers (design default while question Q2 is open).</item>
    /// </list>
    /// All values are TBD balance placeholders (D4).
    /// </summary>
    public static class FacilityStaffing
    {
        public const int WorkshopCapacity = 3;
        public const int MineSiteCapacity = 3;
        public const double ExtraWorkerFactor = 0.6;

        /// <summary>Robots that can work one construction site together (director 2026-11-15: more workers, sooner). TBD.</summary>
        public const int BuildCrewCapacity = 6;

        /// <summary>How many robots can work a task at once.</summary>
        public static int CapacityFor(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.Manufacture: return WorkshopCapacity;
                case TaskKind.Mine: return MineSiteCapacity;
                case TaskKind.Excavate: return Resources.Passages.CrewCapacity;
                case TaskKind.Build: return BuildCrewCapacity;
                default: return 1;
            }
        }

        /// <summary>Combined work rate of a crew from each worker's productivity (hours of work per hour).</summary>
        public static double Rate(IEnumerable<double> productivities)
        {
            var list = new List<double>(productivities);
            list.Sort((a, b) => b.CompareTo(a));
            double rate = 0.0, weight = 1.0;
            foreach (double p in list)
            {
                rate += Math.Max(0.0, p) * weight;
                weight *= ExtraWorkerFactor;
            }
            return rate;
        }
    }
}

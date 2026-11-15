using System;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// How long buildings take to put up. Director 2026-11-15: back to the times from before the real-world change
    /// (hours, not days to weeks): a base time plus a little per square root of a tonne of materials, so bigger builds
    /// take longer, less than in proportion. Game rule, TBD. Crews of several robots still build faster.
    /// </summary>
    public static class BuildTimes
    {
        /// <summary>Robot work hours every building takes at least (game rule, TBD).</summary>
        public const double BaseHours = 2.0;

        /// <summary>Further work hours per square root of a tonne of materials (game rule, TBD).</summary>
        public const double HoursPerRootTonne = 4.0;

        /// <summary>Robot work hours for a building with this bill (one General robot; a crew is faster).</summary>
        public static double FromBill(MaterialBill bill) =>
            bill == null || bill.IsEmpty ? 0.0 : BaseHours + HoursPerRootTonne * Math.Sqrt(bill.TotalKg / 1000.0);

        /// <summary>Robot work hours for a building of this type (a panel of this area; the default battery yard).</summary>
        public static double Hours(BuildingType type, double areaM2 = 0.0)
        {
            switch (type)
            {
                case BuildingType.SolarPanel: return FromBill(BuildCosts.Panel(Math.Max(areaM2, 1.0)));
                case BuildingType.FloatingSolar: return FromBill(BuildCosts.Panel(Math.Max(areaM2, 1.0)).Times(PlanetSimulation.FloatingCostFactor));
                case BuildingType.Battery: return FromBill(BuildCosts.Battery(20_000_000.0));   // the default 20 MWh yard
                default: return FromBill(BuildingCatalog.Get(type)?.FixedBill);
            }
        }
    }
}

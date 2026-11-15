using System;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// Stand-ins a building's bill can use (director 2026-11-15: a colony must be able to start on every planet, also
    /// one with no copper or lithium ore). Saved with each building: add new flags after the last, never before.
    /// </summary>
    [Flags]
    public enum StandIn
    {
        None = 0,
        /// <summary>Aluminium wiring instead of copper (<see cref="StandIns.AluminiumPerCopperKg"/>).</summary>
        AluminiumForCopper = 1,
        /// <summary>Sodium-ion cells instead of lithium (<see cref="StandIns.SodiumPerLithiumKg"/>).</summary>
        SodiumForLithium = 2,
    }

    /// <summary>
    /// Real substitutions. Aluminium and sodium are refined from ordinary rock (<see cref="Robots.RobotTech.Refine"/>),
    /// which every rocky planet has, so neither needs an ore deposit.
    /// </summary>
    public static class StandIns
    {
        /// <summary>
        /// Conductivity of EC-grade aluminium, share of the International Annealed Copper Standard (61 % IACS; copper
        /// 100 %). Density of aluminium 2.70 g/cm³, of copper 8.96 g/cm³.
        /// </summary>
        public const double AluminiumIacs = 0.61, AluminiumDensity = 2.70, CopperDensity = 8.96;

        /// <summary>
        /// Aluminium for one kg of copper wiring with the same resistance over the same length: the cross-section must be
        /// 1 / 0.61 = 1.64 times larger, and aluminium is 2.70 / 8.96 as dense, so 2.70 / (8.96 × 0.61) = 0.494 kg.
        /// This is why overhead power lines are aluminium: half the mass for the same conductance.
        /// </summary>
        public const double AluminiumPerCopperKg = AluminiumDensity / (CopperDensity * AluminiumIacs);

        /// <summary>
        /// Sodium for one kg of lithium in the cells: the sodium cathode (NaFePO₄ in place of LiFePO₄) has one Na⁺ for every
        /// Li⁺, and each carries one charge, so the same charge takes the same number of ions: molar masses 22.99 / 6.94 =
        /// 3.31 kg. Sodium cells run at a slightly lower voltage, so a little less energy per charge (not modelled, TBD).
        /// </summary>
        public const double SodiumPerLithiumKg = 22.99 / 6.94;

        /// <summary>The bill with its stand-ins used.</summary>
        public static MaterialBill Apply(MaterialBill bill, StandIn use)
        {
            if (bill == null) return new MaterialBill();
            MaterialBill b = bill;
            if ((use & StandIn.AluminiumForCopper) != 0 && bill[Material.Copper] > 0.0)
                b = b.Without(Material.Copper).Add(Material.Aluminium, bill[Material.Copper] * AluminiumPerCopperKg);
            if ((use & StandIn.SodiumForLithium) != 0 && bill[Material.Lithium] > 0.0)
                b = b.Without(Material.Lithium).Add(Material.Sodium, bill[Material.Lithium] * SodiumPerLithiumKg);
            return b;
        }
    }
}

namespace KardashevAscent.Simulation.Energy
{
    /// <summary>
    /// What buildings cost (game rules on top of real embodied-energy numbers).
    /// The colony builds from <em>build parts</em>, counted in Wh of the energy that went into making them. Only the
    /// on-site share of a building's embodied energy is paid on the planet (assembly, site work, local processing);
    /// the refined materials (wafers, cells, glass) come with the lander's cargo. That share is a game rule.
    /// </summary>
    public static class BuildCosts
    {
        /// <summary>Share of a building's embodied energy paid on site (game rule).</summary>
        public const double OnSiteShare = 0.01;

        /// <summary>
        /// Embodied energy of Li-ion storage per Wh of capacity, electricity-equivalent (about 100; cradle-to-gate
        /// studies give roughly 300 Wh of primary energy per Wh, about a third of that as electricity).
        /// </summary>
        public const double LiIonEmbodiedWhPerWh = 100.0;

        /// <summary>Build parts the lander brings: enough for the first few buildings, Wh.</summary>
        public const double LanderStockWh = 200_000_000.0;

        /// <summary>Share of a finished building's cost that comes back as parts when it is taken down (game rule).</summary>
        public const double SalvageShare = 0.5;

        /// <summary>On-site cost of a panel of <paramref name="areaM2"/>, Wh.</summary>
        public static double Panel(double embodiedWhPerM2, double areaM2) => OnSiteShare * embodiedWhPerM2 * areaM2;

        /// <summary>On-site cost of a battery building of <paramref name="capacityWh"/>, Wh.</summary>
        public static double Battery(double capacityWh) => OnSiteShare * LiIonEmbodiedWhPerWh * capacityWh;
    }
}

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// How many building slots each kind of building covers (inside one tile; decided 2026-11-09). Solar panels grow
    /// with their tier. All sizes are TBD balance placeholders.
    /// </summary>
    public static class BuildingFootprints
    {
        /// <summary>Slots a solar installation covers at each tier: field, farm, park, array.</summary>
        public static readonly int[] SolarByTier = { 1, 1, 3, 7 };

        public const int BatteryYard = 2;
        public const int Workshop = 3;
        public const int ChargingStation = 1;
        public const int Relay = 1;
        public const int ResearchStation = 1;
        public const int RefineryModule = 1;
        public const int TidalTurbine = 1;

        public static int SlotsFor(BuildingType type, int panelTier)
        {
            switch (type)
            {
                case BuildingType.SolarPanel:
                case BuildingType.FloatingSolar:
                    return SolarByTier[System.Math.Max(0, System.Math.Min(SolarByTier.Length - 1, panelTier))];
                default: return BuildingCatalog.Get(type)?.FootprintSlots ?? 1;
            }
        }
    }
}

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Which buildings give power to the grid and which take it (what a wire connects).</summary>
    public static class WireRoles
    {
        /// <summary>Makes power (panels, turbines, wave lines, barrages): an output.</summary>
        public static bool IsOutput(BuildingType type) => PlanetSimulation.IsWireOutput(type);

        /// <summary>Takes power (storage, stations, refinery modules): an input.</summary>
        public static bool IsInput(BuildingType type) => PlanetSimulation.IsWireInput(type);
    }
}

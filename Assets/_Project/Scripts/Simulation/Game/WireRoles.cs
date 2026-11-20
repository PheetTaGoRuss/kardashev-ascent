namespace KardashevAscent.Simulation.Game
{
    /// <summary>Which buildings give power to the grid and which take it (what a wire connects).</summary>
    public static class WireRoles
    {
        /// <summary>Makes power (panels, turbines, wave lines, barrages): an output.</summary>
        public static bool IsOutput(BuildingType type) => PlanetSimulation.IsWireOutput(type);

        /// <summary>Takes power (storage, stations, refinery modules): an input.</summary>
        public static bool IsInput(BuildingType type) => PlanetSimulation.IsWireInput(type);

        /// <summary>
        /// The electrical terminal a building has (electrical redesign, W-16): generators are sources, a battery stores, every other
        /// consumer is a load. All are DC, voltage class unspecified until the class ladder is approved (D2). Buildings that are
        /// neither generators nor consumers have no terminal.
        /// </summary>
        public static Electrical.TerminalRole TerminalRoleOf(BuildingType type)
        {
            if (IsOutput(type)) return Electrical.TerminalRole.Source;
            if (type == BuildingType.Battery) return Electrical.TerminalRole.Storage;
            if (IsInput(type)) return Electrical.TerminalRole.Load;
            return Electrical.TerminalRole.Unspecified;
        }
    }
}

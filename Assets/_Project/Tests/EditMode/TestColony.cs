using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Tests
{
    /// <summary>Shared test helpers for a colony.</summary>
    public static class TestColony
    {
        /// <summary>
        /// A charging dock for facility-level tests: the lander used to charge robots like this before the director
        /// asked (2026-11-15) that only chargers charge them.
        /// </summary>
        public static ChargingCapability TestDock => new ChargingCapability(2, 1_000.0, true);

        /// <summary>
        /// A finished charging station on <paramref name="tile"/> (the base by default), in the highest free slot, so
        /// robots can recharge. Returns its slot.
        /// </summary>
        public static int AddCharger(PlanetSimulation sim, int tile = -1)
        {
            if (tile < 0) tile = sim.BaseTileId;
            int slot = -1;
            for (int s = sim.SlotCount(tile) - 1; s >= 0 && slot < 0; s--)
                if (sim.CanPlace(BuildingType.ChargingStation, tile, s)) slot = s;
            foreach (var (m, kg) in sim.BuildBill(BuildingType.ChargingStation).Items()) sim.Materials.Add(m, kg);
            bool timed = sim.ConstructionTimeEnabled;
            sim.ConstructionTimeEnabled = false;
            sim.PlaceChargingStation(tile, slot);
            for (int i = 0; i < 200 && sim.IsUnderConstruction(tile, slot); i++) sim.Tick(0.25);
            sim.ConstructionTimeEnabled = timed;
            return slot;
        }
    }
}

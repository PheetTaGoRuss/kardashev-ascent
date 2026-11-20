using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The wires: which slots hold a power line or a high-voltage line, which tile each one links to, and which
    /// buildings the player has wired to the grid. Pure bookkeeping; the simulation decides what a wire does (the
    /// grid, the flow, the cost). Extracted from the orchestrator (architecture step M1).
    /// </summary>
    public sealed class WireBook
    {
        /// <summary>Slots with a power line (a wire building, finished or a site).</summary>
        public readonly HashSet<(int Tile, int Slot)> PowerLines = new HashSet<(int, int)>();

        /// <summary>Slots with a high-voltage line.</summary>
        public readonly HashSet<(int Tile, int Slot)> HighVoltageLines = new HashSet<(int, int)>();

        /// <summary>A line building: the tile it links its own tile to.</summary>
        public readonly Dictionary<(int Tile, int Slot), int> LinkTargets = new Dictionary<(int, int), int>();

        /// <summary>Buildings the player has wired to their tile's grid.</summary>
        public readonly HashSet<(int Tile, int Slot)> WiredBuildings = new HashSet<(int, int)>();

        /// <summary>The two tiles are linked by a wire of this tier (finished or a site).</summary>
        public bool Linked(int a, int b, bool highVoltage)
        {
            foreach (var key in highVoltage ? HighVoltageLines : PowerLines)
                if (LinkTargets.TryGetValue(key, out int to) && ((key.Tile == a && to == b) || (key.Tile == b && to == a))) return true;
            return false;
        }

        /// <summary>A line of this tier touches the tile (it stands on it or links to it) and passes <paramref name="counts"/>.</summary>
        public bool TouchesTile(int tile, bool highVoltage, Func<(int Tile, int Slot), bool> counts)
        {
            foreach (var key in highVoltage ? HighVoltageLines : PowerLines)
                if ((key.Tile == tile || (LinkTargets.TryGetValue(key, out int to) && to == tile)) && counts(key)) return true;
            return false;
        }

        /// <summary>Forget the line on this slot (it was taken down).</summary>
        public void RemoveLine((int Tile, int Slot) key)
        {
            PowerLines.Remove(key);
            HighVoltageLines.Remove(key);
            LinkTargets.Remove(key);
        }
    }
}

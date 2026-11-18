using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The colony's research bookkeeping: where research stations stand, which experiments are known and which are
    /// ordered (a robot picks an order by index). The simulation decides what an experiment costs and what it
    /// reveals. Extracted from the orchestrator (architecture step M1).
    /// </summary>
    public sealed class ResearchBook
    {
        public HashSet<(int Tile, int Slot)> Stations { get; } = new HashSet<(int, int)>();
        public HashSet<ExperimentKind> Known { get; } = new HashSet<ExperimentKind>();
        public List<(ExperimentKind Kind, int Tile)> Orders { get; } = new List<(ExperimentKind, int)>();

        /// <summary>Forget known experiments and orders (a load). Stations are rebuilt from the saved buildings.</summary>
        public void ClearExperiments()
        {
            Known.Clear();
            Orders.Clear();
        }
    }
}

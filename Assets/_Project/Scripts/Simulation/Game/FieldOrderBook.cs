using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The player's standing field orders: tiles to survey and (tile, material) pairs to mine. Pure bookkeeping, in
    /// order of placement (a robot picks by index). The simulation decides whether an order is allowed and what
    /// finishing one does. Extracted from the orchestrator (architecture step M1).
    /// </summary>
    public sealed class FieldOrderBook
    {
        readonly List<int> survey = new List<int>();
        readonly List<(int Tile, Material Material)> mining = new List<(int, Material)>();

        public IReadOnlyList<int> Survey => survey;
        public IReadOnlyList<(int Tile, Material Material)> Mining => mining;

        public bool HasSurvey(int tile) => survey.Contains(tile);
        public bool HasMining(int tile, Material material) => mining.Contains((tile, material));

        /// <summary>False when the order already exists.</summary>
        public bool AddSurvey(int tile)
        {
            if (survey.Contains(tile)) return false;
            survey.Add(tile);
            return true;
        }

        public bool AddMining(int tile, Material material)
        {
            if (mining.Contains((tile, material))) return false;
            mining.Add((tile, material));
            return true;
        }

        public bool RemoveSurvey(int tile) => survey.Remove(tile);
        public bool RemoveMining(int tile, Material material) => mining.Remove((tile, material));

        /// <summary>Copies, for loops that remove orders while walking.</summary>
        public int[] SurveySnapshot() => survey.ToArray();
        public (int Tile, Material Material)[] MiningSnapshot() => mining.ToArray();

        public void Clear()
        {
            survey.Clear();
            mining.Clear();
        }
    }
}

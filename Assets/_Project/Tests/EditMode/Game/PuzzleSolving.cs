using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests
{
    /// <summary>Since stage F a survey ends in a puzzle; tests that only need the tile surveyed solve it with the right answers.</summary>
    public static class PuzzleSolving
    {
        public static void SolveSurvey(IPlanetSimulation sim, int tile)
        {
            foreach (SurveyPuzzle p in sim.SurveyPuzzles)
            {
                if (p.Tile != tile) continue;
                var answers = new List<double>();
                for (int m = 0; m < p.Materials.Count; m++) answers.Add(p.Answer(m));
                sim.AnswerSurveyPuzzle(tile, answers, out LatitudePuzzle.Grade g);
                Assert.AreEqual(LatitudePuzzle.Grade.Precise, g, "the cores' own averages are the precise answer");
                return;
            }
        }
    }
}

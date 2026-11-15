using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// The ground-survey puzzle (docs/design/solar-measurements.md, stage F). A robot drills cores across a tile and
    /// assays each one: a list of (material, grade) per core, with some cores missing the material altogether. The
    /// player estimates each material's grade over the tile as the average over all cores (a core without the
    /// material counts as 0). A close estimate reveals the tile's ground; a rough one reveals it too, but marked ±.
    /// The estimate never replaces the real grade: mining pays by the deposit, not by the guess.
    /// </summary>
    [Serializable]
    public sealed class SurveyPuzzle
    {
        /// <summary>Cores drilled per survey. TBD.</summary>
        public const int Cores = 5;
        /// <summary>Within this share of the answer: precise; within the rough share: rough. TBD.</summary>
        public const double PreciseShare = 0.10, RoughShare = 0.25;

        public int Tile;
        /// <summary>The materials the cores found, in the order asked about.</summary>
        public List<Material> Materials = new List<Material>();
        /// <summary>One row per core: that core's assay for each material in <see cref="Materials"/> (0 = not found).</summary>
        public List<CoreRow> Rows = new List<CoreRow>();
        public int Attempts;
        /// <summary>Materials already worked out, by index (director 2026-11-15: answer one at a time). Version 30.</summary>
        public List<int> Solved = new List<int>();
        /// <summary>Of those, the ones answered only roughly.</summary>
        public List<int> RoughSolved = new List<int>();

        public bool IsSolved(int materialIndex) => Solved != null && Solved.Contains(materialIndex);
        public bool AllSolved => Materials.Count > 0 && Solved != null && Solved.Count >= Materials.Count;

        [Serializable]
        public sealed class CoreRow
        {
            public double DepthM;
            public double[] Grades = new double[0];
        }

        /// <summary>Spread of the cores around the deposit's grade: core i gets grade × (1 + Pattern[i]). Averages to 1.</summary>
        static readonly double[] Pattern = { 1.35, 0.60, 1.10, 0.0, 1.95 };

        /// <summary>The cores a robot brings back from a tile whose ground is this list of deposits.</summary>
        public static SurveyPuzzle Make(int tile, IReadOnlyList<Deposit> ground)
        {
            var p = new SurveyPuzzle { Tile = tile };
            foreach (Deposit d in ground)
                if (d.Material != Material.Rock)   // rock is everywhere and always known
                    p.Materials.Add(d.Material);
            for (int i = 0; i < Cores; i++)
            {
                var row = new CoreRow { DepthM = 2.0 + 1.5 * i, Grades = new double[p.Materials.Count] };
                for (int m = 0; m < p.Materials.Count; m++)
                {
                    double grade = 0.0;
                    foreach (Deposit d in ground)
                        if (d.Material == p.Materials[m]) grade = d.Grade;
                    // Each material follows the pattern from a different core, so columns don't look alike.
                    row.Grades[m] = Math.Round(grade * Pattern[(i + m) % Pattern.Length], 2);
                }
                p.Rows.Add(row);
            }
            return p;
        }

        /// <summary>The grade the cores give for a material: the average over all cores.</summary>
        public double Answer(int materialIndex)
        {
            double sum = 0.0;
            foreach (CoreRow r in Rows)
                sum += r.Grades[materialIndex];
            return Rows.Count > 0 ? sum / Rows.Count : 0.0;
        }

        /// <summary>Grade one material's estimate.</summary>
        public LatitudePuzzle.Grade CheckOne(int materialIndex, double estimate)
        {
            if (materialIndex < 0 || materialIndex >= Materials.Count)
                return LatitudePuzzle.Grade.Wrong;
            double answer = Answer(materialIndex);
            double error = Math.Abs(estimate - answer) / Math.Max(1e-9, Math.Abs(answer));
            return error <= PreciseShare ? LatitudePuzzle.Grade.Precise
                 : error <= RoughShare ? LatitudePuzzle.Grade.Rough : LatitudePuzzle.Grade.Wrong;
        }

        /// <summary>Grade every material's estimate; the worst one is the puzzle's grade.</summary>
        public LatitudePuzzle.Grade Check(IReadOnlyList<double> estimates)
        {
            if (estimates == null || estimates.Count != Materials.Count)
                return LatitudePuzzle.Grade.Wrong;
            var grade = LatitudePuzzle.Grade.Precise;
            for (int m = 0; m < Materials.Count; m++)
            {
                double answer = Answer(m);
                double error = Math.Abs(estimates[m] - answer) / Math.Max(1e-9, Math.Abs(answer));
                var g = error <= PreciseShare ? LatitudePuzzle.Grade.Precise
                      : error <= RoughShare ? LatitudePuzzle.Grade.Rough : LatitudePuzzle.Grade.Wrong;
                if (g < grade) grade = g;
            }
            return grade;
        }
    }
}

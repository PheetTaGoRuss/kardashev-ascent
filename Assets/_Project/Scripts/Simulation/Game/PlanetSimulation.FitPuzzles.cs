using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The "fit the data" puzzle at the end of every experiment (RS-13, decided 2026-11-23, director delegated). Finishing an experiment
    /// makes its result known with the full original range; solving the puzzle narrows that range to the instrument's precision.
    /// There is no pass or fail. See <see cref="FitPuzzle"/>.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>Game rule, TBD: the molar mass of the air for the barometric scale height (Earth air, 28.97 g/mol).</summary>
        const double AirMolarMassKg = 0.02897;
        const double GasConstant = 8.314462618;

        readonly Dictionary<ExperimentKind, FitPuzzle> puzzleCache = new Dictionary<ExperimentKind, FitPuzzle>();

        /// <summary>Experiments whose puzzle has not been solved yet.</summary>
        public IReadOnlyList<ExperimentKind> PendingPuzzles => research.Puzzles;

        /// <summary>The share of an experiment's original range the colony still has: 1 right after the experiment, down to 0.1 after a good fit; 1 when not done; 0 for a result with no puzzle (exact).</summary>
        public double ExperimentRangeShare(ExperimentKind kind)
        {
            if (!IsExperimentKnown(kind)) return 1.0;
            return research.Shares.TryGetValue(kind, out double s) ? s : 0.0;
        }

        FitPuzzle.Truth PuzzleTruth()
        {
            int tile = KnowledgeTile;
            var (lo, hi) = SurfaceTemperatureRange(tile);
            double meanC = (lo + hi) / 2.0;
            double g = SurfaceGravityMs2;
            double best = 0.0;
            for (int t = 0; t < TileCount; t++)
                if (GetTile(t).Terrain == TerrainType.ShallowOcean) best = Math.Max(best, MeanWaveHeightAtM(t));
            double wavePeriod = Waves.EnergyPeriodShare * Waves.PeakPeriodS(Waves.WindMs(Math.PI / 4.0), g);
            return new FitPuzzle.Truth
            {
                PressurePa = PressurePaAt(tile),
                ScaleHeightM = GasConstant * (meanC + 273.15) / (AirMolarMassKg * g),
                TemperatureMeanC = meanC,
                TemperatureAmpK = DiurnalAmplitudeK(tile),   // the day-night swing (Diurnal.cs)
                DayHours = DayLengthHours,
                ReliefMaxM = Relief.MaxM,
                TideRangeM = Tides.EquilibriumRangeM(Moon, planetRadiusM),
                TidePeriodHours = TidePeriodHours,
                GravityMs2 = g,
                WaveHeightM = best > 0.0 ? best : 0.5,
                WavePeriodS = wavePeriod,
                BasinDepthM = 30.0,   // game rule, TBD
                OreGrade = 0.3,
            };
        }

        /// <summary>The puzzle for an experiment (the same data every time for this planet).</summary>
        public FitPuzzle PuzzleFor(ExperimentKind kind)
        {
            if (!puzzleCache.TryGetValue(kind, out FitPuzzle p))
                puzzleCache[kind] = p = FitPuzzle.Make(kind, PuzzleTruth(), unchecked(EstimateKey(100 + (int)kind) + TerrainSeed));
            return p;
        }

        /// <summary>The player's fit: narrows the range by the quality of the handles. Returns what was found.</summary>
        public string AnswerFitPuzzle(ExperimentKind kind, double[] handles)
        {
            if (!research.Puzzles.Contains(kind)) return "Nothing waiting to be fitted.";
            FitPuzzle p = PuzzleFor(kind);
            double share = p.Share(handles);
            if (p.ExtraHours != null)
            {
                // the ore assay: every sample takes robot time at the station; the range narrows when the sampling is done
                int tile = FinishedStationTile(BaseTileId);
                if (tile < 0) return "Build a Research Station first: samples are taken there.";
                double hours = p.ExtraHours(handles);
                research.Puzzles.Remove(kind);
                research.Sampling[kind] = (hours, share);
                research.Orders.Add((kind, tile));
                Settle(null);
                return $"Sampling: {hours:0.#} h of robot time. The range narrows when it is done.";
            }
            return Finish(kind, share, share <= 0.2 ? "A good fit: the range is down to the instrument's precision." : share <= 0.6 ? "A fair fit: the range is narrower." : "A poor fit: the range is still wide. Try again, or let the computer fit.");
        }

        /// <summary>"Fit for me": the least-squares answer, with a wider range than a good hand fit.</summary>
        public string AutoFitPuzzle(ExperimentKind kind)
        {
            if (!research.Puzzles.Contains(kind)) return "Nothing waiting to be fitted.";
            FitPuzzle p = PuzzleFor(kind);
            double share = Math.Max(FitPuzzle.AutoFitShare, p.Share(p.Best));
            return Finish(kind, share, "Fitted by the computer: the range is narrower, but a careful hand fit does better.");
        }

        string Finish(ExperimentKind kind, double share, string text)
        {
            research.Puzzles.Remove(kind);
            research.Shares[kind] = Math.Min(research.Shares.TryGetValue(kind, out double old) ? old : 1.0, share);
            Settle(null);
            return text;
        }

        void OnExperimentFinished(ExperimentKind kind)
        {
            Log("Research Station", "Experiment done", 0.0, "", Experiments.Name(kind), $"{Experiments.Name(kind)} measured: {ExperimentResult(kind)}");
            if (research.Sampling.TryGetValue(kind, out var s))
            {
                research.Sampling.Remove(kind);
                research.Shares[kind] = Math.Min(research.Shares.TryGetValue(kind, out double old) ? old : 1.0, s.Share);
                return;
            }
            if (!research.Puzzles.Contains(kind)) research.Puzzles.Add(kind);
            research.Shares[kind] = 1.0;
        }

        void CaptureFitPuzzles(SimulationState state)
        {
            foreach (ExperimentKind k in research.Puzzles) state.ExperimentPuzzles.Add((int)k);
            foreach (var kv in research.Sampling) state.ExperimentSampling.Add(new ExperimentSamplingRecord { Kind = kv.Key, Hours = kv.Value.Hours, Share = kv.Value.Share });
            foreach (var kv in research.Shares) state.ExperimentShares.Add(new ExperimentShareRecord { Kind = kv.Key, Share = kv.Value });
        }

        void RestoreFitPuzzles(SimulationState state)
        {
            research.Puzzles.Clear();
            research.Shares.Clear();
            puzzleCache.Clear();
            if (state.ExperimentPuzzles != null)   // version 51
                foreach (int k in state.ExperimentPuzzles)
                    if (Enum.IsDefined(typeof(ExperimentKind), k) && research.Known.Contains((ExperimentKind)k) && !research.Puzzles.Contains((ExperimentKind)k))
                        research.Puzzles.Add((ExperimentKind)k);
            research.Sampling.Clear();
            if (state.ExperimentSampling != null)   // version 52
                foreach (ExperimentSamplingRecord r in state.ExperimentSampling)
                    if (research.Known.Contains(r.Kind)) research.Sampling[r.Kind] = (Math.Max(0.0, r.Hours), Math.Max(0.0, Math.Min(1.0, r.Share)));
            if (state.ExperimentShares != null)
                foreach (ExperimentShareRecord r in state.ExperimentShares)
                    if (research.Known.Contains(r.Kind)) research.Shares[r.Kind] = Math.Max(0.0, Math.Min(1.0, r.Share));
        }
    }
}

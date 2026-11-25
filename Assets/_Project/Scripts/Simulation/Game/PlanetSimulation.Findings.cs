using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Workforce v2 stage E, second part: findings wait for sync, and colony computing.
    /// A survey or measurement finished by a robot that is cut off from the network stays with that robot until it
    /// reconnects, or until it comes near a connected robot (nearby exchange). Colony computing is a grid load for the
    /// robots' processors; it is the first load cut in a shortage, and robots then fall back to AI level 1.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        internal readonly List<HeldFinding> heldFindings = new List<HeldFinding>();

        /// <summary>Findings waiting on cut-off robots.</summary>
        public IReadOnlyList<HeldFinding> HeldFindings => heldFindings;

        /// <summary>Colony computing asked for and whether it runs now (stage E).</summary>
        public double ComputingRequestW => Workforce.ComputingRequestW;
        public bool ComputingOn { get; private set; } = true;

        /// <summary>Grid battery share below which computing is cut when there is no spare power (game rule, TBD).</summary>
        public const double ComputingBatteryReserve = 0.30;

        /// <summary>
        /// Computing runs unless power is short: not in a brownout, and either spare power covers it or the grid
        /// battery is above the reserve. Decided from the last grid step, before this one draws.
        /// </summary>
        double ComputingLoadW()
        {
            double request = Workforce.ComputingRequestW;
            ComputingOn = request <= 0.0
                || (!Grid.IsBrownout && (Grid.Last.CurtailedW >= request || Grid.Battery.StateOfCharge >= ComputingBatteryReserve));
            Workforce.ComputingAvailable = ComputingOn;
            return ComputingOn ? request : 0.0;
        }

        /// <summary>The robot that did a field task, if it can't hand the result in now: it keeps it.</summary>
        bool HoldIfCutOff(WorkTask task, HeldFinding finding)
        {
            Robot worker = Workforce.WorkerOf(task);
            if (worker != null) finding.RobotId = worker.Id;
            if (worker == null || Workforce.CanSync(worker))
                return false;
            finding.RobotId = worker.Id;
            heldFindings.Add(finding);
            worker.UnsyncedFindings++;
            KnowHeldFinding(finding);
            return true;
        }

        /// <summary>Hand in what robots that can sync now are carrying.</summary>
        void DeliverHeldFindings()
        {
            for (int i = 0; i < heldFindings.Count; i++)
            {
                HeldFinding f = heldFindings[i];
                Robot r = Workforce.GetRobot(f.RobotId);
                if (r != null && !Workforce.CanSync(r))
                    continue;
                heldFindings.RemoveAt(i--);
                if (r != null)
                    r.UnsyncedFindings = Math.Max(0, r.UnsyncedFindings - 1);
                KnowDeliveredFinding(f, true);
                Deliver(f);
            }
        }

        /// <summary>What a finished survey or measurement does once the colony has it.</summary>
        void Deliver(HeldFinding f)
        {
            switch (f.Kind)
            {
                case FindingKind.SurveyRockOnly:
                    Deposits.MarkSurveyed(f.Tile);
                    TileSurveyed?.Invoke(f.Tile);
                    break;
                case FindingKind.SurveyCores:
                    surveyPuzzles.RemoveAll(q => q.Tile == f.Tile);
                    surveyPuzzles.Add(f.Survey);
                    PuzzleReady?.Invoke(SolarParameter.Latitude);   // "a puzzle is waiting"; the kind is in SurveyPuzzles
                    break;
                case FindingKind.LightReadings:
                    parameterPuzzles.RemoveAll(q => q.Parameter == f.Parameter);
                    parameterPuzzles.Add(f.Light);
                    PuzzleReady?.Invoke(f.Parameter);
                    break;
                case FindingKind.ShadowReadings:
                    latitudePuzzles.Add(f.Latitude);
                    PuzzleReady?.Invoke(SolarParameter.Latitude);
                    break;
                case FindingKind.SurveyWorkedOut:
                    Deposits.MarkSurveyed(f.Tile);
                    roughSurveys.Remove(f.Tile);
                    TileSurveyed?.Invoke(f.Tile);
                    break;
                case FindingKind.ValueWorkedOut:
                    if (f.Parameter == SolarParameter.Latitude)
                        Notebook.RecordLatitude(f.Tile, Sphere.Latitude(f.Tile) * 180.0 / Math.PI, "noon shadow, worked out by the robot");
                    else
                        Parameters.MarkMeasured(f.Parameter);
                    ParameterMeasured?.Invoke(f.Parameter);
                    break;
            }
        }

        /// <summary>
        /// Share of survey and measurement results that come back as a puzzle for the player (director 2026-11-14:
        /// puzzles are occasional; the rest the robot works out itself). Game rule, TBD. Research will always have its
        /// own unique puzzle (future).
        /// </summary>
        public const double DefaultPuzzleChance = 0.25;

        /// <summary>This colony's puzzle chance (tests set 1 to get a puzzle every time, 0 for none).</summary>
        public double PuzzleChance { get; set; } = DefaultPuzzleChance;

        /// <summary>
        /// Does this result come back as a puzzle? A stable roll from the tile, what was measured and the game hour, so a
        /// replay or a reloaded game rolls the same.
        /// </summary>
        bool RollPuzzle(int tile, int what)
        {
            if (PuzzleChance >= 1.0) return true;
            if (PuzzleChance <= 0.0) return false;
            unchecked
            {
                uint h = (uint)tile * 2654435761u ^ (uint)what * 40503u ^ (uint)Math.Floor(TimeHours) * 2246822519u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h % 10000u) / 10000.0 < PuzzleChance;
            }
        }
    }

    /// <summary>What a held finding is.</summary>
    /// <summary>Saved as an integer: add new kinds at the end only.</summary>
    public enum FindingKind
    {
        SurveyRockOnly, SurveyCores, LightReadings, ShadowReadings,
        /// <summary>The robot worked the grades out itself (no puzzle this time).</summary>
        SurveyWorkedOut,
        /// <summary>The robot worked the measured value out itself (no puzzle this time).</summary>
        ValueWorkedOut,
    }

    /// <summary>A survey or measurement result a cut-off robot is carrying (stage E; save version 25).</summary>
    [Serializable]
    public sealed class HeldFinding
    {
        public int RobotId;
        public FindingKind Kind;
        public int Tile;
        public SolarParameter Parameter;
        // Only the field that matches Kind is used (JsonUtility fills the others with empty objects on load).
        public SurveyPuzzle Survey;
        public ParameterPuzzle Light;
        public LatitudePuzzle Latitude;
    }
}

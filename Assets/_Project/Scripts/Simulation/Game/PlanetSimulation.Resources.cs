using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        /// <summary>What lies in each tile's ground, and which tiles are surveyed.</summary>
        public DepositMap Deposits { get; private set; }

        /// <summary>Materials on hand, kg.</summary>
        public MaterialStock Materials { get; } = new MaterialStock();

        readonly List<int> surveyOrders = new List<int>();
        readonly List<(int Tile, Material Material)> miningOrders = new List<(int, Material)>();
        /// <summary>Hours of work done on survey tasks and the current mining batch.</summary>
        readonly Dictionary<WorkTask, double> fieldWork = new Dictionary<WorkTask, double>();

        public event Action<int> TileSurveyed;

        /// <summary>A Workshop job finished (a new robot, an upgrade or a refined batch).</summary>
        public event Action<ManufacturingJob> JobFinished;

        static WorkTask SurveyTask(int tile) => new WorkTask(TaskKind.Survey, tile, -1);
        static WorkTask MineTask(int tile, Material m) => new WorkTask(TaskKind.Mine, tile, (int)m);

        public bool OrderSurvey(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater || Deposits.IsSurveyed(tileId) || surveyOrders.Contains(tileId)
                || surveyPuzzles.Exists(q => q.Tile == tileId))
                return false;
            surveyOrders.Add(tileId);
            Settle(null);
            return true;
        }

        public bool IsSurveyOrdered(int tileId) => surveyOrders.Contains(tileId);

        public bool CancelSurvey(int tileId)
        {
            if (!surveyOrders.Remove(tileId))
                return false;
            Workforce.Tasks.MarkCancelled(SurveyTask(tileId));
            fieldWork.Remove(SurveyTask(tileId));
            Settle(null);
            return true;
        }

        /// <summary>How much of a tile's survey is done, 0–1 (1 once surveyed).</summary>
        public double SurveyProgress(int tileId) =>
            Deposits.IsSurveyed(tileId) ? 1.0
            : fieldWork.TryGetValue(SurveyTask(tileId), out double h) ? Math.Min(1.0, h / RobotSpecs.SurveyWorkHours) : 0.0;

        /// <summary>
        /// Keep robots mining a material on a tile (in batches, so they still take more urgent work between them).
        /// The material must be known there: rock anywhere on land, the rest after a survey.
        /// </summary>
        public bool OrderMining(int tileId, Material material)
        {
            if (tileId < 0 || tileId >= TileCount || !Deposits.IsKnown(tileId, material) || Deposits.GradeOf(tileId, material) <= 0.0
                || miningOrders.Contains((tileId, material)))
                return false;
            miningOrders.Add((tileId, material));
            Settle(null);
            return true;
        }

        public bool IsMiningOrdered(int tileId, Material material) => miningOrders.Contains((tileId, material));

        public bool CancelMining(int tileId, Material material)
        {
            if (!miningOrders.Remove((tileId, material)))
                return false;
            Workforce.Tasks.MarkCancelled(MineTask(tileId, material));
            fieldWork.Remove(MineTask(tileId, material));
            Settle(null);
            return true;
        }

        public int MiningOrderCount => miningOrders.Count;

        /// <summary>Robots on survey and mining tasks add work; finished surveys reveal tiles, finished batches add material.</summary>
        void AdvanceFieldWork(double dt)
        {
            // Interruption rule (D15): a survey (a measurement) restarts if no robot is on it; building, salvage and
            // mining keep their progress, which lives on the site or the batch.
            foreach (int tile in surveyOrders)
            {
                WorkTask st = SurveyTask(tile);
                if (!TaskPolicy.For(TaskKind.Survey).KeepsProgress && fieldWork.ContainsKey(st) && !Workforce.IsWorkedOnSite(st))
                    fieldWork.Remove(st);
            }
            foreach (var mo in measureOrders)
            {
                WorkTask mt = MeasureTask(mo.Parameter, mo.Tile);
                if (fieldWork.ContainsKey(mt) && !Workforce.IsWorkedOnSite(mt))
                    fieldWork.Remove(mt);   // a measurement restarts if the robot leaves
            }
            // Each task's crew on site works together, with diminishing returns (spec v2 §5).
            var crews = new Dictionary<WorkTask, List<double>>();
            foreach (Robot r in Workforce.Robots)
            {
                if (!r.Task.HasValue || r.Activity != RobotActivity.None || r.IsShutDown)
                    continue;
                WorkTask t = r.Task.Value;
                if (MovementEnabled && (r.IsTravelling || r.Tile != t.Tile))
                    continue;   // still driving there
                if (!crews.TryGetValue(t, out List<double> crew))
                    crews[t] = crew = new List<double>();
                crew.Add(RobotCatalog.Productivity(r, t.Kind, MilestonesReached));
            }
            foreach (var kv in crews)
            {
                WorkTask t = kv.Key;
                double speed = FacilityStaffing.Rate(kv.Value);
                if (t.Kind == TaskKind.Manufacture)
                {
                    int index = FinishedWorkshops().IndexOf((t.Tile, t.Slot));
                    if (index >= 0 && index < jobs.Count)
                    {
                        double before = jobs[index].DoneHours;
                        jobs[index].DoneHours += dt * speed;
                        // Long jobs go in rounds, like mining batches, so the robot still takes more urgent work between them.
                        if (Math.Floor(jobs[index].DoneHours / RobotSpecs.MiningBatchHours + 1e-9) > Math.Floor(before / RobotSpecs.MiningBatchHours + 1e-9))
                            Workforce.Release(t);
                    }
                    continue;
                }
                if (t.Kind != TaskKind.Survey && t.Kind != TaskKind.Mine && t.Kind != TaskKind.Measure)
                    continue;
                fieldWork[t] = (fieldWork.TryGetValue(t, out double h) ? h : 0.0) + dt * speed;
            }
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                ManufacturingJob j = jobs[i];
                if (j.DoneHours < j.Recipe.WorkHours - 1e-9)
                    continue;
                jobs.RemoveAt(i);
                FinishJob(j);
                JobFinished?.Invoke(j);
            }
            foreach (int tile in surveyOrders.ToArray())
            {
                WorkTask t = SurveyTask(tile);
                if (fieldWork.TryGetValue(t, out double h) && h >= RobotSpecs.SurveyWorkHours - 1e-9)
                {
                    fieldWork.Remove(t);
                    surveyOrders.Remove(tile);
                    SurveyPuzzle puzzle = SurveyPuzzle.Make(tile, Deposits.At(tile));
                    if (puzzle.Materials.Count == 0)
                    {
                        // Only rock here: nothing to estimate, the survey is simply done.
                        Deposits.MarkSurveyed(tile);
                        TileSurveyed?.Invoke(tile);
                        continue;
                    }
                    surveyPuzzles.RemoveAll(q => q.Tile == tile);
                    surveyPuzzles.Add(puzzle);
                    PuzzleReady?.Invoke(SolarParameter.Latitude);   // "a puzzle is waiting"; the kind is in SurveyPuzzles
                }
            }
            foreach (var mo in measureOrders.ToArray())
            {
                WorkTask t = MeasureTask(mo.Parameter, mo.Tile);
                if (!fieldWork.TryGetValue(t, out double h) || h < SolarUnknowns.MeasureHours - 1e-9)
                    continue;
                fieldWork.Remove(t);
                measureOrders.Remove(mo);
                Workforce.Release(t);
                if (HasPuzzle(mo.Parameter))
                {
                    parameterPuzzles.RemoveAll(q => q.Parameter == mo.Parameter);
                    parameterPuzzles.Add(MakeLightPuzzle(mo.Parameter, mo.Tile));
                    PuzzleReady?.Invoke(mo.Parameter);
                    continue;
                }
                if (mo.Parameter == SolarParameter.Latitude)
                {
                    // The readings are in; the player works out the latitude (stage C).
                    double lat = Sphere.Latitude(mo.Tile) * 180.0 / Math.PI;
                    latitudePuzzles.Add(LatitudePuzzle.Make(mo.Tile, lat, DescribeSky(mo.Tile, 0.0, 0.0).DeclinationDeg));
                    PuzzleReady?.Invoke(mo.Parameter);
                    continue;
                }
                Parameters.MarkMeasured(mo.Parameter);
                ParameterMeasured?.Invoke(mo.Parameter);
            }
            foreach (var order in miningOrders.ToArray())
            {
                WorkTask t = MineTask(order.Tile, order.Material);
                if (!fieldWork.TryGetValue(t, out double h) || h < RobotSpecs.MiningBatchHours - 1e-9)
                    continue;
                fieldWork[t] = h - RobotSpecs.MiningBatchHours;
                Materials.Add(order.Material, RobotSpecs.MiningKgPerHour * RobotSpecs.MiningBatchHours * Deposits.GradeOf(order.Tile, order.Material));
                Workforce.Release(t);
            }
        }
    }
}

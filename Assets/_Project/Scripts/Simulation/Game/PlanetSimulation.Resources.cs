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

        /// <summary>Grid power the Workshop's jobs drew in the last step, W (their recipes' energy over their work hours).</summary>
        public double WorkshopPowerW { get; private set; }

        readonly List<int> surveyOrders = new List<int>();
        readonly List<(int Tile, Material Material)> miningOrders = new List<(int, Material)>();
        /// <summary>Hours of work done on survey tasks and the current mining batch.</summary>
        readonly Dictionary<WorkTask, double> fieldWork = new Dictionary<WorkTask, double>();

        public event Action<int> TileSurveyed;

        /// <summary>A deposit was mined out (tile, material); its mining order ended.</summary>
        public event Action<int, Material> DepositExhausted;

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
                || Deposits.IsExhausted(tileId, material) || miningOrders.Contains((tileId, material)) || !IsOpenGround(tileId))
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
            double workshopW = 0.0;
            foreach (var kv in crews)
            {
                WorkTask t = kv.Key;
                double speed = FacilityStaffing.Rate(kv.Value);
                if (t.Kind == TaskKind.Manufacture)
                {
                    int index = FinishedWorkshops().IndexOf((t.Tile, t.Slot));
                    // A job draws its recipe's energy from the grid, spread over its work hours, as a priority load
                    // (it doesn't stop in a brownout yet: TBD).
                    if (index >= 0 && index < jobs.Count)
                    {
                        if (jobs[index].Recipe.WorkHours > 0.0)
                            workshopW += speed * jobs[index].Recipe.EnergyWh / jobs[index].Recipe.WorkHours;
                        double before = jobs[index].DoneHours;
                        jobs[index].DoneHours += dt * speed;
                        // Long jobs go in rounds, like mining batches, so the robot still takes more urgent work between them.
                        if (Math.Floor(jobs[index].DoneHours / RobotSpecs.MiningBatchHours + 1e-9) > Math.Floor(before / RobotSpecs.MiningBatchHours + 1e-9))
                            Workforce.Release(t);
                    }
                    continue;
                }
                // Construction takes time (director 2026-11-14): the crew's hours go into the site.
                if (t.Kind == TaskKind.Build)
                {
                    Build.Work(t.Tile, t.Slot, dt * speed);
                    continue;
                }
                if (t.Kind != TaskKind.Survey && t.Kind != TaskKind.Mine && t.Kind != TaskKind.Measure && t.Kind != TaskKind.Experiment
                    && t.Kind != TaskKind.Excavate)
                    continue;
                fieldWork[t] = (fieldWork.TryGetValue(t, out double h) ? h : 0.0) + dt * speed;
            }
            AdvanceRefineries(dt);
            // Refinery modules draw from the grid as part of the industrial load.
            WorkshopPowerW = workshopW + RefineryPowerW;
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
                    // Only rock here: nothing to estimate, the survey is simply done (once the colony has it).
                    // Otherwise the cores are a puzzle only now and then; usually the robot works the grades out itself.
                    var finding = puzzle.Materials.Count == 0
                        ? new HeldFinding { Kind = FindingKind.SurveyRockOnly, Tile = tile }
                        : RollPuzzle(tile, 1)
                        ? new HeldFinding { Kind = FindingKind.SurveyCores, Tile = tile, Survey = puzzle }
                        : new HeldFinding { Kind = FindingKind.SurveyWorkedOut, Tile = tile };
                    if (!HoldIfCutOff(t, finding))
                        Deliver(finding);
                }
            }
            foreach (var mo in measureOrders.ToArray())
            {
                WorkTask t = MeasureTask(mo.Parameter, mo.Tile);
                if (!fieldWork.TryGetValue(t, out double h) || h < SolarUnknowns.MeasureHours - 1e-9)
                    continue;
                fieldWork.Remove(t);
                measureOrders.Remove(mo);
                HeldFinding readings = null;
                if (!RollPuzzle(mo.Tile, 2 + (int)mo.Parameter))
                    // No puzzle this time: the robot works the value out itself.
                    readings = new HeldFinding { Kind = FindingKind.ValueWorkedOut, Tile = mo.Tile, Parameter = mo.Parameter };
                else if (HasPuzzle(mo.Parameter))
                    readings = new HeldFinding { Kind = FindingKind.LightReadings, Tile = mo.Tile, Parameter = mo.Parameter,
                                                 Light = MakeLightPuzzle(mo.Parameter, mo.Tile) };
                else if (mo.Parameter == SolarParameter.Latitude)
                {
                    // The readings are in; the player works out the latitude (stage C).
                    double lat = Sphere.Latitude(mo.Tile) * 180.0 / Math.PI;
                    readings = new HeldFinding { Kind = FindingKind.ShadowReadings, Tile = mo.Tile, Parameter = mo.Parameter,
                                                 Latitude = LatitudePuzzle.Make(mo.Tile, lat, DescribeSky(mo.Tile, 0.0, 0.0).DeclinationDeg) };
                }
                if (readings != null)
                {
                    // A cut-off robot keeps the readings until it can sync (stage E); the worker is known before release.
                    if (!HoldIfCutOff(t, readings))
                        Deliver(readings);
                    Workforce.Release(t);
                    continue;
                }
                Workforce.Release(t);
                Parameters.MarkMeasured(mo.Parameter);
                ParameterMeasured?.Invoke(mo.Parameter);
            }
            AdvanceExperiments();
            AdvancePassages();
            foreach (var order in miningOrders.ToArray())
            {
                WorkTask t = MineTask(order.Tile, order.Material);
                if (!fieldWork.TryGetValue(t, out double h) || h < RobotSpecs.MiningBatchHours - 1e-9)
                    continue;
                fieldWork[t] = h - RobotSpecs.MiningBatchHours;
                // What comes out is taken from the ground: a deposit runs out (director 2026-11-15; KI-12).
                double want = RobotSpecs.MiningKgPerHour * RobotSpecs.MiningBatchHours * Deposits.GradeOf(order.Tile, order.Material);
                Materials.Add(order.Material, Deposits.Extract(order.Tile, order.Material, want));
                Workforce.Release(t);
                if (Deposits.IsExhausted(order.Tile, order.Material))
                {
                    miningOrders.Remove(order);
                    Workforce.Tasks.MarkCancelled(t);
                    fieldWork.Remove(t);
                    industrialReports.Add(TimeHours, "Mining", IndustrialBlocker.None,
                        $"The {Resources.Materials.Name(order.Material).ToLowerInvariant()} on tile {order.Tile} is used up. Survey further out for more.");
                    DepositExhausted?.Invoke(order.Tile, order.Material);
                }
            }
        }
    }
}

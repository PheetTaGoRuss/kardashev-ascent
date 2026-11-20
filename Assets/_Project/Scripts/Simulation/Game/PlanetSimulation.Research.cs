using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The Research Station and what the colony knows about its planet (director 2026-11-14):
    /// <list type="bullet">
    /// <item>Every measurement and experiment is done by a robot at a finished Research Station, and takes time.</item>
    /// <item>Experiments test what is safe and doable here: air pressure, water's boiling point, surface temperature, gravity.</item>
    /// <item>Knowledge gates buildings (floating solar needs to know water stays liquid) and sets safety margins: until a
    /// value is known, buildings are designed for the cautious end (game rules; margins PROPOSED, TBD).</item>
    /// </list>
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly ResearchBook research = new ResearchBook();
        public event Action<ExperimentKind> ExperimentDone;

        /// <summary>Earth's standard gravity, m/s² (CGPM 1901: 9.806 65), for planets with no recorded gravity.</summary>
        public const double StandardGravityMs2 = 9.80665;
        /// <summary>Newton's gravitational constant, m³/(kg s²) (CODATA 2018: 6.674 30 × 10⁻¹¹).</summary>
        public const double GravitationalConstant = 6.67430e-11;
        /// <summary>Surface gravity, m/s² (from planets.json; Earth's standard gravity otherwise).</summary>
        public double SurfaceGravityMs2 { get; private set; } = StandardGravityMs2;

        /// <summary>Usable share of battery capacity until the surface temperature range is known (game rule, TBD):
        /// cells are kept away from their full and empty ends while nobody knows how cold or hot they get.</summary>
        public const double BatteryUnknownTemperatureShare = 0.8;
        /// <summary>The solar safety margin never cuts output below this share (game rule, TBD).</summary>
        public const double MinimumDesignShare = 0.5;

        // ---- the station ----

        public bool PlaceResearchStation(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.ResearchStation, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.ResearchStation), BuildTimes.Hours(BuildingType.ResearchStation));
            research.Stations.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.ResearchStation, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        /// <summary>True once at least one Research Station is finished.</summary>
        public bool HasResearchStation => FinishedStationTile(-1) >= 0;

        /// <summary>A tile with a finished Research Station that has been surveyed (experiments run there); -1 if none.</summary>
        public int SurveyedStationTile(int preferTile)
        {
            int any = -1;
            foreach (var s in research.Stations)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(s.Tile, s.Slot)) continue;
                if (!Deposits.IsSurveyed(s.Tile)) continue;
                if (s.Tile == preferTile) return s.Tile;
                if (any < 0 || s.Tile < any) any = s.Tile;
            }
            return any;
        }

        /// <summary>A tile with a finished Research Station: <paramref name="preferTile"/> when it has one, else the first; -1 when none.</summary>
        public int FinishedStationTile(int preferTile)
        {
            int any = -1;
            foreach (var s in research.Stations)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(s.Tile, s.Slot))
                    continue;
                if (s.Tile == preferTile)
                    return s.Tile;
                if (any < 0 || s.Tile < any)
                    any = s.Tile;
            }
            return any;
        }

        /// <summary>True when the tile has a finished Research Station.</summary>
        public bool HasResearchStationOn(int tileId) => tileId >= 0 && FinishedStationTile(tileId) == tileId;

        /// <summary>Why a measurement can't be ordered for this tile, or null when it can.</summary>
        public string MeasureBlocker(SolarParameter parameter, int tileId)
        {
            if (!HasResearchStation)
                return "Build a Research Station first: measurements are done there.";
            if (parameter == SolarParameter.Latitude && !HasResearchStationOn(tileId))
                return "A latitude is measured where a Research Station stands: build one on this tile, or use the sky view (V).";
            return null;
        }

        /// <summary>The tile a measurement is done at: the station's tile.</summary>
        int MeasureTile(SolarParameter parameter, int tileId) =>
            parameter == SolarParameter.Latitude ? (HasResearchStationOn(tileId) ? tileId : -1) : FinishedStationTile(tileId);

        // ---- experiments ----

        public bool IsExperimentKnown(ExperimentKind kind) => research.Known.Contains(kind);
        public bool IsExperimentOrdered(ExperimentKind kind) => research.Orders.Exists(o => o.Kind == kind);

        /// <summary>Why an experiment can't be ordered now, or null when it can.</summary>
        public string ExperimentBlocker(ExperimentKind kind)
        {
            if (IsExperimentKnown(kind))
                return "Done.";
            if (!HasResearchStation)
                return "Build a Research Station first.";
            if (SurveyedStationTile(-1) < 0)
                return "Survey the Research Station's tile first.";
            ExperimentKind? first = Experiments.Needs(kind);
            if (first.HasValue && !IsExperimentKnown(first.Value))
                return $"Needs {Experiments.Name(first.Value).ToLowerInvariant()} first.";
            return null;
        }

        public bool OrderExperiment(ExperimentKind kind, int preferTile = -1)
        {
            if (ExperimentBlocker(kind) != null || IsExperimentOrdered(kind))
                return false;
            research.Orders.Add((kind, SurveyedStationTile(preferTile < 0 ? BaseTileId : preferTile)));
            Settle(null);
            return true;
        }

        public bool CancelExperiment(ExperimentKind kind)
        {
            int i = research.Orders.FindIndex(o => o.Kind == kind);
            if (i < 0)
                return false;
            WorkTask t = ExperimentTask(kind, research.Orders[i].Tile);
            research.Orders.RemoveAt(i);
            Workforce.Tasks.MarkCancelled(t);
            fieldWork.Remove(t);
            Settle(null);
            return true;
        }

        public double ExperimentProgress(ExperimentKind kind)
        {
            if (IsExperimentKnown(kind))
                return 1.0;
            int i = research.Orders.FindIndex(o => o.Kind == kind);
            return i >= 0 && fieldWork.TryGetValue(ExperimentTask(kind, research.Orders[i].Tile), out double h)
                ? Math.Min(1.0, h / Experiments.WorkHours(kind)) : 0.0;
        }

        static WorkTask ExperimentTask(ExperimentKind kind, int tile) => new WorkTask(TaskKind.Experiment, tile, (int)kind);

        /// <summary>Hours of robot work on experiments: finished ones become knowledge; a robot that leaves restarts it.</summary>
        void AdvanceExperiments()
        {
            foreach (var o in research.Orders)
            {
                WorkTask t = ExperimentTask(o.Kind, o.Tile);
                if (fieldWork.ContainsKey(t) && !Workforce.IsWorkedOnSite(t))
                    fieldWork.Remove(t);
            }
            foreach (var o in research.Orders.ToArray())
            {
                WorkTask t = ExperimentTask(o.Kind, o.Tile);
                if (!fieldWork.TryGetValue(t, out double h) || h < Experiments.WorkHours(o.Kind) - 1e-9)
                    continue;
                fieldWork.Remove(t);
                research.Orders.Remove(o);
                Workforce.Release(t);
                research.Known.Add(o.Kind);
                if (o.Kind == ExperimentKind.SeismicSurvey) Deposits.DeepScanned = true;
                ExperimentDone?.Invoke(o.Kind);
            }
        }

        // ---- what the experiments find (the planet's real values) ----

        /// <summary>Air pressure at a tile, Pa (the planet's surface pressure, thinner on high ground).</summary>
        public double PressurePaAt(int tileId) =>
            Experiments.PressurePa(planet.SurfacePressureEarth * (tileId >= 0 && tileId < TileCount ? MakePanel(tileId, 0.0, 0.0, 1.0, 1.0).PressureRatio : 1.0));

        /// <summary>The coldest and warmest monthly mean air temperature over a year at a tile, °C.</summary>
        public (double Coldest, double Warmest) SurfaceTemperatureRange(int tileId)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            const int Months = 12;
            for (int m = 0; m < Months; m++)
            {
                double t = AirTemperatureAt(tileId, MonthMeanAnomaly(m, Months));
                lo = Math.Min(lo, t);
                hi = Math.Max(hi, t);
            }
            return (lo, hi);
        }

        /// <summary>The station tile an experiment was (or will be) done at; the base when none.</summary>
        int KnowledgeTile => HasResearchStation ? FinishedStationTile(BaseTileId) : BaseTileId;

        /// <summary>What an experiment found, as a sentence for the player, or null when it hasn't been done.</summary>
        public string ExperimentResult(ExperimentKind kind)
        {
            if (!IsExperimentKnown(kind))
                return null;
            int tile = KnowledgeTile;
            switch (kind)
            {
                case ExperimentKind.AirPressure:
                {
                    double pa = PressurePaAt(tile);
                    return $"{pa / 1000.0:0.##} kPa ({pa / Experiments.StandardPressurePa:0.###} of Earth's).";
                }
                case ExperimentKind.BoilingPoint:
                {
                    double boil = Experiments.WaterBoilingPointC(PressurePaAt(tile));
                    return double.IsNaN(boil)
                        ? "Water can't be liquid here: the air is below water's triple point (0.61 kPa), so ice turns straight to vapour."
                        : $"Water boils at {boil:0.#} °C here.";
                }
                case ExperimentKind.BasinSurvey:
                {
                    int bays = 0;
                    double bestRange = 0.0;
                    for (int t = 0; t < TileCount; t++)
                        if (GetTile(t).Terrain == TerrainType.ShallowOcean && BarrageBlocker(t) == null) { bays++; bestRange = Math.Max(bestRange, TidalRangeBaseM(t)); }
                    if (bays == 0) return $"No bay here has the {Tides.BarrageMinRangeM:0} m of tide a barrage needs.";
                    double gwh = Tides.BarrageEnergyPerTideJ(Tides.BarrageBasinM2, bestRange, SurfaceGravityMs2) * 365.25 * 24.0 / TidePeriodHours / 3.6e12;
                    return $"{bays} bay{(bays == 1 ? "" : "s")} can hold a barrage. The best has {bestRange:0.#} m of tide: about {gwh:0} GWh a year. The dam weakens the tide behind it by {Tides.BarrageRangeDrop * 100.0:0} % and stops boats crossing.";
                }
                case ExperimentKind.WaveBuoy:
                {
                    double bestH = 0.0;
                    for (int t = 0; t < TileCount; t++)
                        if (GetTile(t).Terrain == TerrainType.ShallowOcean) bestH = Math.Max(bestH, MeanWaveHeightAtM(t));
                    if (bestH <= 0.0) return "No waves: this world has no air to blow over the sea.";
                    double period = Waves.EnergyPeriodShare * Waves.PeakPeriodS(Waves.WindMs(Math.PI / 4.0), SurfaceGravityMs2);
                    double kw = Waves.LinePowerW(bestH, period, SurfaceGravityMs2) / 1000.0;
                    return $"The roughest shallow sea averages {bestH:0.#} m waves; a converter line makes about {kw:0} kW there (a line needs {Waves.CutInHeightM:0.#} m and shuts down above {Waves.SurvivalHeightM:0} m).";
                }
                case ExperimentKind.TideGauge:
                {
                    double best = 0.0;
                    for (int t = 0; t < TileCount; t++)
                        if (GetTile(t).Terrain == TerrainType.ShallowOcean) best = Math.Max(best, TidalRangeAtM(t));
                    double open = Tides.EquilibriumRangeM(Moon, planetRadiusM);
                    return open <= 0.0 ? "No tide: this world has no moon."
                        : $"Open-sea tide {open:0.##} m, high tide every {TidePeriodHours:0.#} h. The coast that funnels it most reaches {best:0.#} m (a rotor needs {Tides.CutInMs / Tides.PeakMsPerMetreOfRange:0.#} m or more).";
                }
                case ExperimentKind.SeismicSurvey:
                {
                    int deep = 0;
                    foreach (int t in Deposits.Surveyed)
                        if (Deposits.GradeOf(t, Resources.Material.RareEarth) > 0.0) deep++;
                    return deep > 0 ? $"Deep ore shows on surveyed tiles: rare earths on {deep} so far. Survey mountains, canyons and caves for more."
                                    : "Deep ore now shows on surveyed tiles. None yet: survey mountains, canyons and caves.";
                }
                case ExperimentKind.SurfaceTemperature:
                {
                    var (lo, hi) = SurfaceTemperatureRange(tile);
                    string verdict = OrbitalBrief.Say(OrbitalBrief.Robots((lo + hi) / 2.0));
                    return $"Coldest month {lo:0.#} °C, warmest {hi:0.#} °C. {verdict}";
                }
                default:
                {
                    double period = Experiments.PendulumPeriodS(Experiments.PendulumLengthM, SurfaceGravityMs2);
                    return $"A 1 m pendulum swings in {period:0.###} s: g = {SurfaceGravityMs2:0.##} m/s² ({SurfaceGravityMs2 / StandardGravityMs2:0.##} of Earth's).";
                }
            }
        }

        /// <summary>Whether liquid water lasts here, as far as the colony knows: null until both experiments are done.</summary>
        public bool? KnownWaterStaysLiquid
        {
            get
            {
                if (!IsExperimentKnown(ExperimentKind.BoilingPoint) || !IsExperimentKnown(ExperimentKind.SurfaceTemperature))
                    return null;
                var (lo, hi) = SurfaceTemperatureRange(KnowledgeTile);
                double boil = Experiments.WaterBoilingPointC(PressurePaAt(KnowledgeTile));
                return !double.IsNaN(boil) && hi < boil;   // ice in winter is fine for pontoons; boiling away is not
            }
        }

        // ---- knowledge gates buildings ----

        /// <summary>Why the colony may not build this yet (what it still has to find out), or null.</summary>
        public string KnowledgeBlocker(BuildingType type)
        {
            if (Debug.UnlockAll)
                return null;
            if (type == BuildingType.HighVoltageLine && UnlockLevel < HighVoltageMilestone)
                return $"High-voltage lines open at milestone {HighVoltageMilestone}.";
            if (type == BuildingType.TidalBarrage)
            {
                if (!IsExperimentKnown(ExperimentKind.BasinSurvey))
                    return "A tidal barrage needs the basin survey experiment: sound a bay first.";
                if (UnlockLevel < Tides.BarrageMilestone)
                    return $"A tidal barrage is a big build: it opens at milestone {Tides.BarrageMilestone}.";
            }
            if (type == BuildingType.WaveConverter && !IsExperimentKnown(ExperimentKind.WaveBuoy))
                return "Wave converters need to know the sea state here: run the wave buoy experiment.";
            if (type == BuildingType.TidalTurbine && !IsExperimentKnown(ExperimentKind.TideGauge))
                return "Tidal turbines need to know the tide here: run the tide gauge experiment.";
            if (type == BuildingType.FloatingSolar)
            {
                bool? liquid = KnownWaterStaysLiquid;
                if (liquid == null)
                    return "Floating solar needs to know water stays liquid here: run the water boiling point and surface temperature experiments.";
                if (liquid == false)
                    return "Water boils away here: pontoons can't float.";
            }
            return null;
        }

        // ---- safety margins until values are known ----

        (int Tile, int Signature) designMarginKey = (-1, -1);
        double designMarginShare = 1.0;

        /// <summary>
        /// The share of their real output solar panels deliver while the colony is still unsure of the light here: they
        /// are designed for the cautious end of every unknown value. 1 − the combined spread of the unknown values for a
        /// typical panel at the base (the "±" in the Research window), never below <see cref="MinimumDesignShare"/>.
        /// </summary>
        public double SolarDesignShare
        {
            get
            {
                if (!SafetyMarginsEnabled || BaseTileId < 0 || BaseTileId >= TileCount)
                    return 1.0;
                int signature = 0, bit = 0;
                foreach (SolarParameter p in (SolarParameter[])Enum.GetValues(typeof(SolarParameter)))
                {
                    if (IsParameterKnown(p, BaseTileId)) signature |= 1 << bit;
                    if (Parameters.IsRough(p)) signature |= 1 << (bit + 8);
                    bit++;
                }
                if (designMarginKey.Tile == BaseTileId && designMarginKey.Signature == signature)
                    return designMarginShare;
                designMarginKey = (BaseTileId, signature);
                double lat = Sphere.Latitude(BaseTileId);
                List<ParameterEffect> effects = ParameterEffects(BaseTileId, 30.0 * Math.PI / 180.0, lat >= 0.0 ? Math.PI : 0.0);
                double baseWh = 0.0, sq = 0.0;
                foreach (ParameterEffect e in effects)
                {
                    baseWh = Math.Max(baseWh, (e.LowWh + e.HighWh) / 2.0);
                    sq += e.HalfRangeWh * e.HalfRangeWh;
                }
                double spread = baseWh > 0.0 ? Math.Sqrt(sq) / baseWh : 0.0;
                designMarginShare = Math.Max(MinimumDesignShare, Math.Min(1.0, 1.0 - spread));
                return designMarginShare;
            }
        }

        /// <summary>Share of battery capacity in use: less until the surface temperature range is known.</summary>
        public double BatteryUsableShare =>
            !SafetyMarginsEnabled || IsExperimentKnown(ExperimentKind.SurfaceTemperature) ? 1.0 : BatteryUnknownTemperatureShare;

        /// <summary>Off in tests that check other physics with full output (on in the game).</summary>
        public bool SafetyMarginsEnabled { get; set; } = true;

        // ---- for tests ----

        /// <summary>A finished Research Station on a free slot of the tile, at no cost (tests about measuring).</summary>
        internal void AddFinishedResearchStationForTests(int tileId)
        {
            for (int s = 0; s < SlotCount(tileId); s++)
            {
                if (!CanPlace(BuildingType.ResearchStation, tileId, s))
                    continue;
                research.Stations.Add((tileId, s));
                Occupy(tileId, FootprintFor(BuildingType.ResearchStation, tileId, s));
                return;
            }
        }

        internal void LearnExperimentForTests(ExperimentKind kind)
        {
            research.Known.Add(kind);
            if (kind == ExperimentKind.SeismicSurvey) Deposits.DeepScanned = true;
        }

        // ---- save ----

        void CaptureResearch(SimulationState state)
        {
            foreach (ExperimentKind k in research.Known)
                state.KnownExperiments.Add((int)k);
            state.KnownExperiments.Sort();
            foreach (var o in research.Orders)
                state.ExperimentOrders.Add(new ExperimentOrderRecord { Kind = o.Kind, Tile = o.Tile });
        }

        void RestoreResearch(SimulationState state)
        {
            research.Known.Clear();
            research.Orders.Clear();
            if (state.KnownExperiments != null)   // version 29
                foreach (int k in state.KnownExperiments)
                    if (Enum.IsDefined(typeof(ExperimentKind), k))
                        research.Known.Add((ExperimentKind)k);
            if (state.ExperimentOrders != null)
                foreach (ExperimentOrderRecord o in state.ExperimentOrders)
                    if (o.Tile >= 0 && o.Tile < TileCount && !research.Known.Contains(o.Kind) && !IsExperimentOrdered(o.Kind))
                        research.Orders.Add((o.Kind, o.Tile));
            Deposits.DeepScanned = research.Known.Contains(ExperimentKind.SeismicSurvey);
        }
    }
}

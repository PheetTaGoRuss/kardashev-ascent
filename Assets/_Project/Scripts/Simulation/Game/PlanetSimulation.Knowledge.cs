using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Knowledge;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The colony's knowledge (K2, K5; decided 2026-11-25): everything that already measures writes immutable records into the store, with no new mechanics; reports follow
    /// the changes. Estimates (the existing KnowledgeEstimate properties) are written as derived records and as measured ones once exact. Weather at the lander, a Research Station and
    /// every tile a robot stands on is one record a day per site for T, p, RH and wind. Findings a cut-off robot holds are records with no known-at time until it synchronises.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The store (named so as not to clash with the namespace).</summary>
        public KnowledgeBase KnowledgeStore { get; } = new KnowledgeBase();

        readonly Dictionary<(Subject, Quantity), (double Value, double Unc, SourceKind Kind)> knowledgeWritten = new Dictionary<(Subject, Quantity), (double, double, SourceKind)>();
        long knowledgeDay = long.MinValue;

        /// <summary>The knowledge store on or off (on for new missions; older saves derive their first records from the state they have).</summary>
        public bool KnowledgeEnabled { get; set; } = true;

        /// <summary>The most recent discrepancies, newest last (for the Why? chain and the Knowledge window).</summary>
        public List<KnowledgeEvent> RecentDiscrepancies { get; } = new List<KnowledgeEvent>();

        void Know(Subject subject, Quantity q, double value, double unc, Confidence confidence, Source source, bool known = true)
        {
            if (!KnowledgeEnabled) return;
            KnowledgeStore.Add(new Observation(subject, q, value, unc, confidence, source, TimeHours, known ? TimeHours : -1.0), TimeHours);
            knowledgeWritten[(subject, q)] = (value, unc, source.Kind);
        }

        /// <summary>Write a record only when the value or its range changed since the last one written for this subject and quantity.</summary>
        void KnowIfChanged(Subject subject, Quantity q, double value, double unc, Confidence confidence, Source source)
        {
            if (knowledgeWritten.TryGetValue((subject, q), out var last) && Math.Abs(last.Value - value) < 1e-9 * Math.Max(1.0, Math.Abs(value)) && Math.Abs(last.Unc - unc) < 1e-9 * Math.Max(1.0, Math.Abs(unc)) && last.Kind == source.Kind) return;
            Know(subject, q, value, unc, confidence, source);
        }

        Source EstimateSource(KnowledgeEstimate e) => e.IsExact ? new Source(SourceKind.MeasuredByColony) : new Source(SourceKind.Derived);
        Confidence EstimateConfidence(KnowledgeEstimate e) => e.IsExact ? Confidence.High : Confidence.Low;

        /// <summary>The planet's properties the colony knows or estimates, from the existing estimate and experiment state.</summary>
        void RecordEstimates()
        {
            var p = Subject.Planet;
            // an experiment done makes the record a measurement (a range until its fit puzzle is answered, exact after); until then it is a derived estimate
            void Est(Quantity q, KnowledgeEstimate e, ExperimentKind kind)
            {
                bool measured = IsExperimentKnown(kind);
                KnowIfChanged(p, q, e.Value, e.HalfRange, measured ? (e.IsExact ? Confidence.High : Confidence.Medium) : Confidence.Low, measured ? new Source(SourceKind.MeasuredByColony) : new Source(SourceKind.Derived));
            }
            Est(Quantity.AirPressure, PressureEstimate, ExperimentKind.AirPressure);
            Est(Quantity.SurfaceTemperature, TemperatureEstimate, ExperimentKind.SurfaceTemperature);
            Est(Quantity.ReliefMax, HighestPointEstimate, ExperimentKind.ReliefSurvey);
            Est(Quantity.TidalRange, TideRangeEstimate, ExperimentKind.TideGauge);
            if (IsExperimentKnown(ExperimentKind.Gravity)) KnowIfChanged(p, Quantity.SurfaceGravity, SurfaceGravityMs2, SurfaceGravityMs2 * 0.01 * ExperimentRangeShare(ExperimentKind.Gravity), Confidence.High, new Source(SourceKind.MeasuredByColony));
            else KnowIfChanged(p, Quantity.SurfaceGravity, SurfaceGravityMs2, SurfaceGravityMs2 * 0.5, Confidence.Low, new Source(SourceKind.Derived));
            if (IsExperimentKnown(ExperimentKind.BoilingPoint)) { double b = Experiments.WaterBoilingPointC(PressurePaAt(KnowledgeTile)); if (!double.IsNaN(b)) KnowIfChanged(p, Quantity.WaterBoilingPoint, b, 0.3 * ExperimentRangeShare(ExperimentKind.BoilingPoint), Confidence.High, new Source(SourceKind.MeasuredByColony)); }
            if (IsExperimentKnown(ExperimentKind.WindMast) && WeatherModel.HasWeather)
            {
                var s = WeatherModel.SpreadAt(Sphere.Latitude(KnowledgeTile), YearFractionNow);
                KnowIfChanged(Subject.Tile(KnowledgeTile), Quantity.HubWindSpeed, WindPower.HubWindMs(s.WindMean), WindPower.HubWindMs(s.WindSd) * ExperimentRangeShare(ExperimentKind.WindMast), Confidence.Medium, new Source(SourceKind.MeasuredByColony));
            }
            if (IsExperimentKnown(ExperimentKind.WaveBuoy))
            {
                double best = 0.0; int bt = -1;
                for (int t = 0; t < TileCount; t++) if (GetTile(t).Terrain == TerrainType.ShallowOcean && MeanWaveHeightAtM(t) > best) { best = MeanWaveHeightAtM(t); bt = t; }
                if (bt >= 0) KnowIfChanged(Subject.Tile(bt), Quantity.WaveHeight, best, best * 0.2 * ExperimentRangeShare(ExperimentKind.WaveBuoy), Confidence.Medium, new Source(SourceKind.MeasuredByColony));
            }
            // ore grades at surveyed tiles: the existing assay estimate (a range until the assay, exact after)
            foreach (int tile in Deposits.Surveyed)
            {
                Material best = Material.Rock; double bg = 0.0;
                foreach (Deposit d in Deposits.Known(tile)) if (d.Material != Material.Rock && d.Grade > bg) { bg = d.Grade; best = d.Material; }
                if (bg <= 0.0) continue;
                var e = GradeEstimate(tile, best);
                bool assayed = IsExperimentKnown(ExperimentKind.OreAssay);
                KnowIfChanged(Subject.Tile(tile), Quantity.OreGrade, e.Value, e.HalfRange, assayed ? (e.IsExact ? Confidence.High : Confidence.Medium) : Confidence.Low, assayed ? new Source(SourceKind.MeasuredByColony) : new Source(SourceKind.Derived));
            }
        }

        /// <summary>One record a day per measured site for T, p, RH and wind; a robot that cannot sync keeps its own records unsynced.</summary>
        void RecordWeatherDay()
        {
            if (!HasWeather) return;
            var sites = new SortedSet<int> { BaseTileId };
            foreach (Robot r in Workforce.Robots) sites.Add(r.Tile);
            foreach (int t in ExtraMeasuredTiles()) sites.Add(t);
            if (HasResearchStation) sites.Add(FinishedStationTile(BaseTileId));
            foreach (int tile in sites)
            {
                if (!WeatherMeasuredAt(tile)) continue;
                Robot standing = Workforce.Robots.FirstOrDefault(x => x.Tile == tile);
                bool known = tile == BaseTileId || HasResearchStationOn(tile) || standing == null || Workforce.CanSync(standing);
                Source src = known && standing == null ? new Source(SourceKind.Instrument) : standing != null ? new Source(SourceKind.Robot, standing.Id) : new Source(SourceKind.Instrument);
                WeatherSample w = WeatherAt(tile);
                var s = Subject.Tile(tile);
                Know(s, Quantity.AirTemperature, LiveAirTemperatureAt(tile), 0.2, Confidence.High, src, known);
                Know(s, Quantity.RelativeHumidity, w.RhPercent, 2.0, Confidence.Medium, src, known);
                Know(s, Quantity.WindSpeed, w.WindMs, 0.3, Confidence.Medium, src, known);
            }
        }

        /// <summary>Meter readings (the existing LineReadout): a record a day for every tile with a measured line.</summary>
        void RecordLineReadings()
        {
            if (!WiredGrid) return;
            for (int t = 0; t < TileCount; t++)
            {
                if (!HasLineOn(t, true)) continue;
                LineReadout r = LineReadout(t);
                if (r.Measured) Know(Subject.Tile(t), Quantity.LineFlow, r.MeasuredFlowW, 0.0, Confidence.High, new Source(SourceKind.Instrument), true);
            }
        }

        long telemetryHour = long.MinValue;

        void TelemetryIfChanged(Subject s, Quantity q, double value, double minChange)
        {
            if (knowledgeWritten.TryGetValue((s, q), out var last) && Math.Abs(last.Value - value) < minChange) return;
            Know(s, q, value, 0.0, Confidence.High, new Source(SourceKind.Telemetry));
        }

        /// <summary>
        /// Telemetry (R4, OQ-1): the colony reads its own machines exactly, so robots' charge and condition, the grid battery, the power made and each wind turbine are records with
        /// source telemetry. Capped: at most one record per entity per game hour, and only when the value moved (1 point, 1 %, 1 kW).
        /// </summary>
        void RecordTelemetry()
        {
            long hour = (long)Math.Floor(TimeHours);
            if (hour == telemetryHour) return;
            telemetryHour = hour;
            foreach (Robot r in Workforce.Robots)
            {
                TelemetryIfChanged(Subject.Robot(r.Id), Quantity.RobotCharge, Math.Round(r.Battery.StateOfCharge * 100.0), 1.0);
                TelemetryIfChanged(Subject.Robot(r.Id), Quantity.RobotCondition, Math.Round(r.Condition * 100.0), 1.0);
            }
            var colony = new Subject(SubjectKind.Colony);
            if (BatteryCapacityWh > 0.0) TelemetryIfChanged(colony, Quantity.GridBatteryCharge, Math.Round(BatteryStoredWh / BatteryCapacityWh * 100.0), 1.0);
            TelemetryIfChanged(colony, Quantity.GridGeneration, Math.Round(GenerationW / 1000.0) * 1000.0, 1000.0);
            foreach (int t in WindTurbineTiles()) TelemetryIfChanged(Subject.Building(t, 0), Quantity.TurbineOutput, Math.Round(WindTurbinePowerAtW(t) / 1000.0) * 1000.0, 1000.0);
        }

        /// <summary>The Knowledge step: daily records, estimate changes, stale values, and the reports (K5).</summary>
        void StepKnowledge()
        {
            if (!KnowledgeEnabled) return;
            long day = (long)Math.Floor(TimeHours / 24.0);
            RecordTelemetry();
            bool newDay = day != knowledgeDay;
            if (newDay)
            {
                knowledgeDay = day;
                RecordEstimates();
                RecordWeatherDay();
                RecordLineReadings();
                foreach (Observation o in KnowledgeStore.NewlyStale(TimeHours))
                    Log("Colony", "Value stale", KnowledgeBase.AgeHours(o, TimeHours) / 24.0, "d", "Knowledge", $"{Quantities.Name(o.Quantity)} of {o.Subject} was last measured {Quantities.Age(KnowledgeBase.AgeHours(o, TimeHours))}: ◐ it is stale; measure it again.");
            }
            if (KnowledgeStore.PendingEvents.Count > 0) ReportKnowledge(KnowledgeStore.TakeEvents());
        }

        static string Show(Quantity q, double v, double u) => u > 0.0 ? $"{v:0.###} ±{u:0.###} {Quantities.Unit(q)}" : $"{v:0.###} {Quantities.Unit(q)}";

        void ReportKnowledge(List<KnowledgeEvent> events)
        {
            foreach (KnowledgeEvent e in events)
            {
                if (Quantities.IsTelemetry(e.Quantity)) continue;   // the colony reading its own machines is not news
                string what = $"{Quantities.Name(e.Quantity)} ({e.Subject})";
                switch (e.Kind)
                {
                    case KnowledgeEventKind.FirstMeasured:
                        if (e.New.Source.Kind == SourceKind.Derived || e.New.Source.Kind == SourceKind.Assumed || e.New.Source.Kind == SourceKind.Dataset) break;   // the first estimates are not news
                        if (e.Quantity == Quantity.AirTemperature || e.Quantity == Quantity.RelativeHumidity || e.Quantity == Quantity.WindSpeed || e.Quantity == Quantity.LineFlow) break;
                        Log("Colony", "Measured", e.New.Value, Quantities.Unit(e.Quantity), "Knowledge",
                            $"{what} measured {Show(e.Quantity, e.New.Value, e.New.Uncertainty)}" + (double.IsNaN(e.OldValue) ? "." : $" (was estimated {Show(e.Quantity, e.OldValue - e.OldUncertainty, 0.0).Replace(" ±0", "")} to {e.OldValue + e.OldUncertainty:0.###})."));
                        break;
                    case KnowledgeEventKind.RangeNarrowed:
                        Log("Colony", "Range narrowed", e.New.Uncertainty, Quantities.Unit(e.Quantity), "Knowledge", $"{what} range narrowed: {Show(e.Quantity, e.New.Value, e.New.Uncertainty)} (was ±{e.OldUncertainty:0.###}).");
                        break;
                    case KnowledgeEventKind.Discrepancy:
                        RecentDiscrepancies.Add(e); if (RecentDiscrepancies.Count > 8) RecentDiscrepancies.RemoveAt(0);
                        Log("Colony", "Discrepancy", e.New.Value, Quantities.Unit(e.Quantity), "Knowledge",
                            $"Discrepancy: {what} read {Show(e.Quantity, e.New.Value, e.New.Uncertainty)} ({e.New.Source}), but the colony showed {Show(e.Quantity, e.OldValue, e.OldUncertainty)}. Both are kept; the display uses the most recent of the highest confidence.");
                        break;
                }
            }
        }

        // ---- findings: the unsynced records of a cut-off robot ----

        static Quantity QuantityOf(HeldFinding f)
        {
            switch (f.Kind)
            {
                case FindingKind.LightReadings:
                case FindingKind.ShadowReadings:
                case FindingKind.ValueWorkedOut:
                    switch (f.Parameter)
                    {
                        case Solar.SolarParameter.Latitude: return Quantity.LatitudeFix;
                        case Solar.SolarParameter.OpticalDepth: return Quantity.OpticalDepth;
                        case Solar.SolarParameter.DiffuseShare: return Quantity.DiffuseShare;
                        case Solar.SolarParameter.Clouds: return Quantity.CloudFactor;
                        case Solar.SolarParameter.Albedo: return Quantity.Albedo;
                        case Solar.SolarParameter.AirTemperature: return Quantity.AirTemperature;
                        case Solar.SolarParameter.Dust: return Quantity.DustOpticalDepth;
                        default: return Quantity.SpectrumFit;
                    }
                default: return Quantity.SurveyCompleted;
            }
        }

        /// <summary>The value a finding reports: the true value of the parameter at its tile (the simulation knows it; the colony only does once this is synchronised).</summary>
        double FindingValue(HeldFinding f)
        {
            switch (QuantityOf(f))
            {
                case Quantity.LatitudeFix: return Sphere.Latitude(f.Tile) * 180.0 / Math.PI;
                case Quantity.OpticalDepth:
                case Quantity.DustOpticalDepth: return AnnualOpticalDepth(f.Tile);
                case Quantity.DiffuseShare: return planet.DiffuseK;
                case Quantity.CloudFactor: return CloudFactor(f.Tile, CurrentMeanAnomaly());
                case Quantity.Albedo: return Terrains.Albedo(GetTile(f.Tile).Terrain);
                case Quantity.AirTemperature: return AnnualAirTemperature(f.Tile);
                case Quantity.SurveyCompleted: return 1.0;
                default: return 0.0;
            }
        }

        /// <summary>A robot holds a finding it cannot hand in: an unsynced record (no known-at time).</summary>
        void KnowHeldFinding(HeldFinding f)
        {
            if (!KnowledgeEnabled) return;
            Know(Subject.Tile(f.Tile), QuantityOf(f), FindingValue(f), 0.0, Confidence.Medium, new Source(SourceKind.Robot, f.RobotId), false);
        }

        /// <summary>A finding reached the colony: a known record, or the robot's unsynced one becomes known.</summary>
        void KnowDeliveredFinding(HeldFinding f, bool wasHeld)
        {
            if (!KnowledgeEnabled) return;
            if (wasHeld) { KnowledgeStore.Sync(f.RobotId, TimeHours); return; }
            Know(Subject.Tile(f.Tile), QuantityOf(f), FindingValue(f), 0.0, Confidence.Medium, new Source(SourceKind.Robot, f.RobotId));
        }

        void CaptureKnowledge(SimulationState state) { state.KnowledgeOn = KnowledgeEnabled; state.Observations = KnowledgeStore.All(); }

        void RestoreKnowledge(SimulationState state)
        {
            knowledgeWritten.Clear(); RecentDiscrepancies.Clear(); knowledgeDay = long.MinValue;
            KnowledgeEnabled = state.Version >= 61 ? state.KnowledgeOn : true;   // an older save derives its first records from the state it has (RecordEstimates on the first day)
            KnowledgeStore.Restore(state.Version >= 61 && state.Observations != null ? state.Observations : new List<Observation>());
            foreach (var key in KnowledgeStore.Keys()) { var s = KnowledgeStore.Shown(key.Subject, key.Quantity); if (s.HasValue) knowledgeWritten[(key.Subject, key.Quantity)] = (s.Value.Value, s.Value.Uncertainty, s.Value.Source.Kind); }
        }
    }
}

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
        public ParameterBook Parameters { get; } = new ParameterBook();
        readonly List<(SolarParameter Parameter, int Tile)> measureOrders = new List<(SolarParameter, int)>();
        public event Action<SolarParameter> ParameterMeasured;
        public event Action<SolarParameter> PuzzleReady;
        readonly List<LatitudePuzzle> latitudePuzzles = new List<LatitudePuzzle>();
        public IReadOnlyList<LatitudePuzzle> LatitudePuzzles => latitudePuzzles;
        readonly List<ParameterPuzzle> parameterPuzzles = new List<ParameterPuzzle>();
        readonly List<SurveyPuzzle> surveyPuzzles = new List<SurveyPuzzle>();
        readonly HashSet<int> roughSurveys = new HashSet<int>();
        public IReadOnlyList<SurveyPuzzle> SurveyPuzzles => surveyPuzzles;
        public bool IsRoughSurvey(int tileId) => roughSurveys.Contains(tileId);

        public string AnswerSurveyPuzzle(int tileId, IReadOnlyList<double> estimates, out LatitudePuzzle.Grade grade)
        {
            grade = LatitudePuzzle.Grade.Wrong;
            SurveyPuzzle p = surveyPuzzles.Find(q => q.Tile == tileId);
            if (p == null)
                return "No core samples waiting for that tile.";
            grade = p.Check(estimates);
            if (grade == LatitudePuzzle.Grade.Wrong)
            {
                p.Attempts++;
                return "Those grades don't fit the cores. Average every core, counting a core without the material as 0.";
            }
            surveyPuzzles.Remove(p);
            Deposits.MarkSurveyed(tileId);
            if (grade == LatitudePuzzle.Grade.Rough)
                roughSurveys.Add(tileId);
            else
                roughSurveys.Remove(tileId);
            TileSurveyed?.Invoke(tileId);
            return grade == LatitudePuzzle.Grade.Precise
                ? $"Tile #{tileId} surveyed: the assays match your estimates."
                : $"Tile #{tileId} surveyed roughly: your estimates are within {SurveyPuzzle.RoughShare * 100.0:0} %, so its grades show as ±.";
        }
        public IReadOnlyList<ParameterPuzzle> ParameterPuzzles => parameterPuzzles;

        /// <summary>Parameters whose readings end in a puzzle (stages C and D); the rest are known when measured.</summary>
        static bool HasPuzzle(SolarParameter p) => p != SolarParameter.Latitude;

        public string AnswerParameterPuzzle(SolarParameter parameter, double value, out LatitudePuzzle.Grade grade)
        {
            grade = LatitudePuzzle.Grade.Wrong;
            ParameterPuzzle p = parameterPuzzles.Find(q => q.Parameter == parameter);
            if (p == null)
                return "No readings waiting for that.";
            grade = p.Check(value);
            if (grade == LatitudePuzzle.Grade.Wrong)
            {
                p.Attempts++;
                return $"{value:0.###} doesn't fit the readings. Check the formula and the units.";
            }
            parameterPuzzles.Remove(p);
            if (grade == LatitudePuzzle.Grade.Precise)
            {
                Parameters.MarkMeasured(parameter);
                ParameterMeasured?.Invoke(parameter);
                return $"{SolarUnknowns.Name(parameter)} = {value:0.###}: known now, its spread is gone.";
            }
            Parameters.MarkRough(parameter);
            ParameterMeasured?.Invoke(parameter);
            return $"{SolarUnknowns.Name(parameter)} ≈ {value:0.###}: close, half its spread is left (measure again to pin it down).";
        }

        /// <summary>The readings a robot brings back for a light puzzle, from this planet's own values.</summary>
        ParameterPuzzle MakeLightPuzzle(SolarParameter p, int tile)
        {
            double g0 = Orbit.ExtraterrestrialIrradiance(planet.Luminosity, planet.SemiMajorAxis);
            double pressure = MakePanel(tile, 0.0, 0.0, 1.0, 1.0).PressureRatio;
            switch (p)
            {
                case SolarParameter.OpticalDepth:
                    return ParameterPuzzle.Haze(tile, g0, planet.OpticalDepth, pressure);
                case SolarParameter.DiffuseShare:
                    return ParameterPuzzle.Diffuse(tile, g0, planet.OpticalDepth, planet.DiffuseK, pressure);
                case SolarParameter.Albedo:
                    return ParameterPuzzle.Albedo(tile, g0, MakePanel(tile, 0.0, 0.0, 1.0, 1.0).Albedo);
                case SolarParameter.AirTemperature:
                    return ParameterPuzzle.AirTemperature(tile, AnnualAirTemperature(tile));
                case SolarParameter.Dust:
                    return ParameterPuzzle.Dust(tile, g0, ModelDustShare);
                case SolarParameter.Spectrum:
                    return ParameterPuzzle.Spectrum(tile, StarTemperatureK > 0.0 ? StarTemperatureK : 5778.0);
                default:
                {
                    // Clear-sky light on flat ground per day: a flat 1 m² panel's year without clouds, per unit efficiency.
                    PanelParams flat = MakePanel(tile, 0.0, 0.0, 1.0, 1.0);
                    double days = YearLengthHours > 0.0 && DayLengthHours > 0.0 ? YearLengthHours / DayLengthHours : 365.25;
                    double kwhPerDay = AnnualYield.Calculate(planet, flat).EnergyWh / PanelEfficiency / 1000.0 / days;
                    return ParameterPuzzle.Clouds(tile, kwhPerDay, CloudFactor(tile, CurrentMeanAnomaly()));
                }
            }
        }

        public string AnswerLatitudePuzzle(int tileId, double latitudeDeg, out LatitudePuzzle.Grade grade)
        {
            grade = LatitudePuzzle.Grade.Wrong;
            LatitudePuzzle p = latitudePuzzles.Find(q => q.Tile == tileId);
            if (p == null)
                return "No shadow readings for that tile.";
            grade = p.Check(latitudeDeg);
            if (grade == LatitudePuzzle.Grade.Wrong)
            {
                p.Attempts++;
                return $"φ = {latitudeDeg:0.0}° doesn't fit the readings. Check which way the shadow points, and the sign of δ.";
            }
            latitudePuzzles.Remove(p);
            string method = grade == LatitudePuzzle.Grade.Precise
                ? "noon shadow, worked out"
                : $"noon shadow, worked out roughly (±{LatitudePuzzle.RoughDeg:0}°)";
            return Notebook.RecordLatitude(tileId, latitudeDeg, method);
        }

        static WorkTask MeasureTask(SolarParameter p, int tile) => new WorkTask(TaskKind.Measure, tile, (int)p);

        public bool IsParameterKnown(SolarParameter parameter, int tileId) =>
            parameter == SolarParameter.Latitude ? Notebook.KnowsLatitude(tileId)
            : parameter == SolarParameter.Clouds && Clouds == CloudModel.None || Parameters.IsMeasured(parameter);

        public bool OrderMeasure(SolarParameter parameter, int tileId)
        {
            if (parameter == SolarParameter.Latitude && latitudePuzzles.Exists(q => q.Tile == tileId))
                return false;   // the readings are in: solve the puzzle
            if (parameterPuzzles.Exists(q => q.Parameter == parameter))
                return false;   // the readings are in: solve the puzzle
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater
                || IsParameterKnown(parameter, tileId) || IsMeasureOrdered(parameter))
                return false;
            measureOrders.Add((parameter, tileId));
            Settle(null);
            return true;
        }

        public bool IsMeasureOrdered(SolarParameter parameter) => measureOrders.Exists(o => o.Parameter == parameter);

        public bool CancelMeasure(SolarParameter parameter)
        {
            int i = measureOrders.FindIndex(o => o.Parameter == parameter);
            if (i < 0)
                return false;
            WorkTask t = MeasureTask(parameter, measureOrders[i].Tile);
            measureOrders.RemoveAt(i);
            Workforce.Tasks.MarkCancelled(t);
            fieldWork.Remove(t);
            Settle(null);
            return true;
        }

        public double MeasureProgress(SolarParameter parameter)
        {
            if (Parameters.IsMeasured(parameter))
                return 1.0;
            int i = measureOrders.FindIndex(o => o.Parameter == parameter);
            return i >= 0 && fieldWork.TryGetValue(MeasureTask(parameter, measureOrders[i].Tile), out double h)
                ? Math.Min(1.0, h / SolarUnknowns.MeasureHours) : 0.0;
        }

        /// <summary>A copy of the planet's solar values with the atmosphere and air changed.</summary>
        static PlanetSolarParams WithAir(in PlanetSolarParams p, double opticalDepth, double diffuseK, double airTemperatureC) =>
            new PlanetSolarParams(p.Luminosity, p.SemiMajorAxis, p.Eccentricity, p.Obliquity, p.PerihelionLongitude,
                                  p.DayLengthHours, p.YearLengthHours, opticalDepth, diffuseK, airTemperatureC,
                                  p.TidallyLocked, p.SurfacePressureEarth);

        static PanelParams WithPanel(in PanelParams p, double latitude, double albedo, double fSpec, double fDust) =>
            new PanelParams(latitude, p.Tilt, p.Azimuth, p.Area, p.Efficiency, p.Noct, p.TempCoefficient, albedo,
                            p.PressureRatio, fSpec, fDust, p.FShade, p.Longitude);

        /// <summary>A panel's yearly energy, Earth years, Wh: the preview's sum, with clouds month by month.</summary>
        double YearEnergyWh(int tileId, in PlanetSolarParams pl, in PanelParams panel)
        {
            double energy = AnnualYield.Calculate(pl, panel).EnergyWh;
            if (Clouds == CloudModel.EarthClimatology && energy > 0.0)
            {
                double[] months = MonthlyYield.Calculate(pl, panel);
                double clear = 0.0, cloudy = 0.0;
                for (int m = 0; m < months.Length; m++)
                {
                    clear += months[m];
                    cloudy += months[m] * CloudFactor(tileId, MonthMeanAnomaly(m, months.Length));
                }
                energy *= clear > 0.0 ? cloudy / clear : 1.0;
            }
            if (YearLengthHours > 0.0)
                energy *= SolarConstants.EarthYearHours / YearLengthHours;
            return energy;
        }

        public List<ParameterEffect> ParameterEffects(int tileId, double tilt, double azimuth)
        {
            var list = new List<ParameterEffect>();
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater)
                return list;
            PanelParams panel = MakePanel(tileId, tilt, azimuth, 1.0, NewPanelAreaM2(tileId));
            double baseWh = YearEnergyWh(tileId, PlanetAt(tileId), panel);
            const double Deg = Math.PI / 180.0;
            foreach (SolarParameter p in (SolarParameter[])Enum.GetValues(typeof(SolarParameter)))
            {
                bool known = IsParameterKnown(p, tileId);
                double lo = baseWh, hi = baseWh;
                if (!known)
                {
                    switch (p)
                    {
                        case SolarParameter.Latitude:
                            lo = YearEnergyWh(tileId, PlanetAt(tileId), WithPanel(panel, panel.Latitude - SolarUnknowns.LatitudeDeg * Deg, panel.Albedo, panel.FSpec, panel.FDust));
                            hi = YearEnergyWh(tileId, PlanetAt(tileId), WithPanel(panel, panel.Latitude + SolarUnknowns.LatitudeDeg * Deg, panel.Albedo, panel.FSpec, panel.FDust));
                            break;
                        case SolarParameter.OpticalDepth:
                            lo = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth * (1.0 + SolarUnknowns.OpticalDepthShare), planet.DiffuseK, AnnualAirTemperature(tileId)), panel);
                            hi = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth * (1.0 - SolarUnknowns.OpticalDepthShare), planet.DiffuseK, AnnualAirTemperature(tileId)), panel);
                            break;
                        case SolarParameter.DiffuseShare:
                            lo = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, Math.Min(1.0, planet.DiffuseK * (1.0 + SolarUnknowns.DiffuseShare)), AnnualAirTemperature(tileId)), panel);
                            hi = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, planet.DiffuseK * (1.0 - SolarUnknowns.DiffuseShare), AnnualAirTemperature(tileId)), panel);
                            break;
                        case SolarParameter.Clouds:
                            lo = baseWh * (1.0 - SolarUnknowns.CloudShare);
                            hi = baseWh * (1.0 + SolarUnknowns.CloudShare);
                            break;
                        case SolarParameter.Albedo:
                            lo = YearEnergyWh(tileId, PlanetAt(tileId), WithPanel(panel, panel.Latitude, Math.Max(0.0, panel.Albedo - SolarUnknowns.Albedo), panel.FSpec, panel.FDust));
                            hi = YearEnergyWh(tileId, PlanetAt(tileId), WithPanel(panel, panel.Latitude, Math.Min(1.0, panel.Albedo + SolarUnknowns.Albedo), panel.FSpec, panel.FDust));
                            break;
                        case SolarParameter.AirTemperature:
                            lo = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, planet.DiffuseK, AnnualAirTemperature(tileId) + SolarUnknowns.AirTemperatureC), panel);
                            hi = YearEnergyWh(tileId, WithAir(planet, planet.OpticalDepth, planet.DiffuseK, AnnualAirTemperature(tileId) - SolarUnknowns.AirTemperatureC), panel);
                            break;
                        case SolarParameter.Dust:
                            lo = YearEnergyWh(tileId, PlanetAt(tileId), WithPanel(panel, panel.Latitude, panel.Albedo, panel.FSpec, Math.Min(1.0, panel.FDust + SolarUnknowns.Dust)));
                            break;
                        case SolarParameter.Spectrum:
                            lo = YearEnergyWh(tileId, PlanetAt(tileId), WithPanel(panel, panel.Latitude, panel.Albedo, panel.FSpec * (1.0 - SolarUnknowns.Spectrum), panel.FDust));
                            hi = YearEnergyWh(tileId, PlanetAt(tileId), WithPanel(panel, panel.Latitude, panel.Albedo, Math.Min(1.0, panel.FSpec * (1.0 + SolarUnknowns.Spectrum)), panel.FDust));
                            break;
                    }
                }
                bool rough = !known && Parameters.IsRough(p);
                if (rough)
                {
                    lo = baseWh - (baseWh - lo) * ParameterBook.RoughRangeLeft;
                    hi = baseWh + (hi - baseWh) * ParameterBook.RoughRangeLeft;
                }
                list.Add(new ParameterEffect(p, Math.Min(lo, hi), Math.Max(lo, hi), known, rough));
            }
            return list;
        }
    }
}

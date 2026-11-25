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
        /// <summary>Output of a floating panel is this much higher than a land panel's: the water keeps the cells cooler (game rule; about 10 °C cooler at -0.35 %/°C). TBD.</summary>
        public const double FloatingCoolingGain = 0.04;

        /// <summary>Pontoons, moorings and cables: a floating panel costs this many times a land panel's build parts (game rule, decided 2026-11-13).</summary>
        public const double FloatingCostFactor = 1.5;

        public PanelPreview PreviewPanel(int tileId, double tilt, double azimuth) => PreviewPanelCore(tileId, tilt, azimuth, false);

        public PanelPreview PreviewFloatingPanel(int tileId, double tilt, double azimuth) => PreviewPanelCore(tileId, tilt, azimuth, true);

        PanelPreview PreviewPanelCore(int tileId, double tilt, double azimuth, bool floating)
        {
            if (tileId < 0 || tileId >= TileCount)
                return new PanelPreview(false, "No such tile.", 0, 0, 0, 0);
            TerrainType ground = GetTile(tileId).Terrain;
            if (floating)
            {
                if (!Terrains.AllowsFloating(ground))
                    return new PanelPreview(false, "Floating solar needs shallow sea or wetland.", 0, 0, 0, 0);
                string unknown = restoringState ? null : KnowledgeBlocker(BuildingType.FloatingSolar);
                if (unknown != null)
                    return new PanelPreview(false, unknown, 0, 0, 0, 0);
            }
            // Saved colonies keep what they built, even on ground that is wetland now.
            else if (restoringState ? GetTile(tileId).IsWater : !GetTile(tileId).IsBuildableLand)
                return new PanelPreview(false, "Land panels cannot go on water or wetland (floating solar can).", 0, 0, 0, 0);
            if (!restoringState && !IsOpenGround(tileId))
                return new PanelPreview(false, ClosedGroundReason(tileId), 0, 0, 0, 0);
            double area = NewPanelAreaM2(tileId);
            PanelParams panel = MakePanel(tileId, tilt, azimuth, 1.0, area);
            AnnualYieldResult year = AnnualYield.Calculate(PlanetAt(tileId), panel);
            double energy = year.EnergyWh, capacityFactor = year.CapacityFactor;
            if (Clouds == CloudModel.EarthClimatology && energy > 0.0)
            {
                // Clouds as a multiplier on output, month by month (weighted by when the panel makes its energy).
                double[] months = MonthlyYield.Calculate(PlanetAt(tileId), panel);
                double clear = 0.0, cloudy = 0.0;
                for (int m = 0; m < months.Length; m++)
                {
                    clear += months[m];
                    cloudy += months[m] * CloudFactor(tileId, MonthMeanAnomaly(m, months.Length));
                }
                double share = clear > 0.0 ? cloudy / clear : 1.0;
                energy *= share;
                capacityFactor *= share;
            }
            // AnnualYield covers one orbit of this planet; the preview reports Earth years so lifetime, payback and
            // EROI mean the same on every planet (Proxima b's year is 11 days).
            if (YearLengthHours > 0.0)
                energy *= SolarConstants.EarthYearHours / YearLengthHours;
            if (floating)
                energy *= 1.0 + FloatingCoolingGain;
            double embodied = EmbodiedWhPerM2 * area * (floating ? FloatingCostFactor : 1.0);
            double eroi = energy > 0 ? Economics.Eroi(energy * PanelLifetimeYears, embodied) : 0.0;
            double payback = energy > 0 ? Economics.PaybackYears(embodied, energy) : double.PositiveInfinity;
            return new PanelPreview(true, "", energy, capacityFactor, eroi, payback);
        }

        public double[] MonthlyEnergyWh(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater)
                return null;
            double[] months = MonthlyYield.Calculate(PlanetAt(tileId), MakePanel(tileId, tilt, azimuth, 1.0, NewPanelAreaM2(tileId)));
            if (Clouds == CloudModel.EarthClimatology)
                for (int m = 0; m < months.Length; m++)
                    months[m] *= CloudFactor(tileId, MonthMeanAnomaly(m, months.Length));
            return months;
        }

        void UpdateStar()
        {
            double meanAnomaly = YearLengthHours > 0 ? 2.0 * Math.PI * TimeHours / YearLengthHours : 0.0;
            double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
            double declination = SunPosition.Declination(planet.Obliquity, nu + planet.PerihelionLongitude);
            double spin = DayLengthHours > 0 ? 2.0 * Math.PI * TimeHours / DayLengthHours : 0.0;
            StarDirection = PlanetFrame.StarDirection(declination, subStellarLongitudeAtStart - spin);
            currentIrradiance = Orbit.ExtraterrestrialIrradiance(planet.Luminosity,
                Orbit.Distance(planet.SemiMajorAxis, planet.Eccentricity, nu));
        }

        double ComputeGeneration() => ComputeFlow();   // raw power per tile by source, then the flow to the lander tile (Wiring)

        /// <summary>One panel's power under the star right now (sun up), with the light and temperature along the way.</summary>
        double PanelPowerNow(in PanelParams p, Double3 sun, double airTemperatureC, double opticalDepth,
                             out double dni, out double dhi, out double poa, out double cellT)
        {
            double airMass = Atmosphere.AirMassKastenYoung(SunPosition.ZenithDegrees(sun), p.PressureRatio);
            dni = Atmosphere.DirectNormal(currentIrradiance, opticalDepth, airMass);
            dhi = Atmosphere.DiffuseHorizontal(currentIrradiance, sun.Up, opticalDepth, airMass, planet.DiffuseK);
            double ghi = Atmosphere.GlobalHorizontal(dni, dhi, sun.Up);
            double cosTheta = PanelModel.CosIncidence(PanelModel.Normal(p.Tilt, p.Azimuth), sun);
            poa = PanelModel.PlaneOfArray(dni, dhi, ghi, cosTheta, p.Tilt, p.Albedo, p.FShade);
            cellT = PanelModel.CellTemperature(airTemperatureC, poa, p.Noct,
                                               planet.SurfacePressureEarth * p.PressureRatio, p.Efficiency);
            return PanelModel.Power(poa, p.Area, p.Efficiency, cellT, p.TempCoefficient, p.FSpec, p.FDust);
        }

        /// <summary>The sky over a tile right now and what a new panel at these angles would make (radians in).</summary>
        public SkyReport DescribeSky(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount)
                return default;
            const double deg = 180.0 / Math.PI;
            double meanAnomaly = CurrentMeanAnomaly();
            double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
            double lambda = (nu + planet.PerihelionLongitude) * deg % 360.0;
            if (lambda < 0.0) lambda += 360.0;
            double declination = Math.Asin(Math.Max(-1f, Math.Min(1f, StarDirection.Y))) * deg;
            double hourAngle = (Sphere.Longitude(tileId) - PlanetFrame.Longitude(StarDirection)) * deg;
            hourAngle = (hourAngle + 540.0) % 360.0 - 180.0;
            Double3 sun = SunAt(tileId);
            WeatherSample weather = WeatherAt(tileId);   // the sky right now includes the day's weather
            double cloud = weather.CloudFactor;
            PanelParams p = InSeason(MakePanel(tileId, tilt, azimuth, cloud, NewPanelAreaM2(tileId)), tileId, meanAnomaly);
            double air = weather.AirC;
            double dni = 0.0, dhi = 0.0, poa = 0.0, cellT = air, power = 0.0;
            if (sun.Up > 0.0)
                power = PanelPowerNow(p, sun, air, weather.DustTau, out dni, out dhi, out poa, out cellT);
            return new SkyReport(planet.TidallyLocked, Sphere.Latitude(tileId) * deg, planet.Obliquity * deg, lambda,
                                 declination, hourAngle, planet.TidallyLocked ? double.NaN : SimClock.LocalTime24(hourAngle / deg),
                                 Math.Asin(Math.Max(-1.0, Math.Min(1.0, sun.Up))) * deg, SunPosition.AzimuthDegrees(sun),
                                 tilt * deg, azimuth * deg, PanelModel.Normal(tilt, azimuth).Dot(sun),
                                 currentIrradiance, dni, dhi, poa, cellT, power, SpectralFactor, cloud);
        }

        double CurrentMeanAnomaly() => YearLengthHours > 0 ? 2.0 * Math.PI * TimeHours / YearLengthHours : 0.0;

        static double MonthMeanAnomaly(int month, int months) => 2.0 * Math.PI * (month + 0.5) / months;

        /// <summary>
        /// Air temperature over a tile at this point of the orbit, °C: on Earth-like planets NASA POWER's monthly
        /// climatology by latitude (<see cref="Climate.EarthAirTemperature"/>), elsewhere the planet's one value.
        /// </summary>
        public double AirTemperatureAt(int tileId, double meanAnomaly) =>
            Clouds == CloudModel.EarthClimatology && tileId >= 0 && tileId < TileCount
                ? Climate.EarthAirTemperature.Celsius(Sphere.Latitude(tileId), Climate.CloudClimate.YearFractionFromMeanAnomaly(meanAnomaly)) + EarthTemperatureOffsetK
                : planet.AirTemperatureC;

        /// <summary>The tile's air temperature over a whole year, °C (Earth-like planets: NASA POWER; else the planet's value).</summary>
        public double AnnualAirTemperature(int tileId) =>
            Clouds == CloudModel.EarthClimatology && tileId >= 0 && tileId < TileCount
                ? Climate.EarthAirTemperature.AnnualMean(Sphere.Latitude(tileId)) + EarthTemperatureOffsetK
                : planet.AirTemperatureC;

        double earthOffsetK = double.NaN;

        /// <summary>
        /// A uniform offset (game rule, assumption, TBD) moving NASA's Earth temperatures to this planet's own mean: the planet's air temperature minus the area-weighted
        /// global annual mean of the T2M table. An Earth-like planet at Earth's own mean is unchanged (within 0.5 K). Clouds are not touched.
        /// </summary>
        double EarthTemperatureOffsetK
        {
            get
            {
                if (double.IsNaN(earthOffsetK))
                {
                    double sum = 0.0, w = 0.0;
                    for (double lat = -89.0; lat <= 89.0; lat += 2.0) { double c = Math.Cos(lat * Math.PI / 180.0); sum += c * Climate.EarthAirTemperature.AnnualMean(lat * Math.PI / 180.0); w += c; }
                    earthOffsetK = planet.AirTemperatureC - sum / w;
                    if (Math.Abs(earthOffsetK) < 1.0) earthOffsetK = 0.0;   // Earth at its own mean (the table mean is about 14.4 C): keep NASA as it is
                }
                return earthOffsetK;
            }
        }

        /// <summary>Smallest optical depth a NASA haze change may leave (clean air still scatters: Rayleigh). Game rule.</summary>
        const double MinOpticalDepth = 0.05;

        bool EarthClimate(int tileId) => Clouds == CloudModel.EarthClimatology && tileId >= 0 && tileId < TileCount;

        /// <summary>
        /// Optical depth τ over a tile now: on Earth-like planets the planet's τ moved by NASA's aerosols for this
        /// place and season (<see cref="Climate.EarthGroundAndHaze"/>), elsewhere the planet's τ.
        /// </summary>
        public double OpticalDepthAt(int tileId, double meanAnomaly) =>
            EarthClimate(tileId)
                ? Math.Max(MinOpticalDepth, planet.OpticalDepth + Climate.EarthGroundAndHaze.HazeChange(Sphere.Latitude(tileId),
                      Climate.CloudClimate.YearFractionFromMeanAnomaly(meanAnomaly)))
                : planet.OpticalDepth;

        /// <summary>The tile's optical depth over a whole year.</summary>
        public double AnnualOpticalDepth(int tileId) =>
            EarthClimate(tileId)
                ? Math.Max(MinOpticalDepth, planet.OpticalDepth + Climate.EarthGroundAndHaze.HazeChangeYear(Sphere.Latitude(tileId)))
                : planet.OpticalDepth;

        /// <summary>How much brighter or darker the ground is this season than over the year (NASA; 0 elsewhere).</summary>
        public double GroundReflectanceChange(int tileId, double meanAnomaly) =>
            EarthClimate(tileId)
                ? Climate.EarthGroundAndHaze.AlbedoChange(Sphere.Latitude(tileId), Climate.CloudClimate.YearFractionFromMeanAnomaly(meanAnomaly))
                : 0.0;

        /// <summary>A panel with this season's ground reflectance (the terrain's own plus NASA's seasonal change).</summary>
        PanelParams InSeason(in PanelParams p, int tileId, double meanAnomaly)
        {
            double change = GroundReflectanceChange(tileId, meanAnomaly);
            return change == 0.0 ? p : WithPanel(p, p.Latitude, Math.Max(0.0, Math.Min(1.0, p.Albedo + change)), p.FSpec, p.FDust);
        }

        /// <summary>The planet as seen from one tile: its own yearly air temperature and haze on Earth-like planets.</summary>
        PlanetSolarParams PlanetAt(int tileId) =>
            Clouds == CloudModel.EarthClimatology
                ? WithAir(planet, AnnualOpticalDepth(tileId), planet.DiffuseK, AnnualAirTemperature(tileId)) : planet;

        /// <summary>Share of clear-sky light reaching panels on a tile at this point of the orbit (1 without clouds).</summary>
        public double CloudFactor(int tileId, double meanAnomaly) =>
            Clouds == CloudModel.EarthClimatology
                ? Climate.CloudClimate.Factor(Sphere.Latitude(tileId), Climate.CloudClimate.YearFractionFromMeanAnomaly(meanAnomaly))
                : 1.0;
    }
}

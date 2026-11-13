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
        public PanelPreview PreviewPanel(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount)
                return new PanelPreview(false, "No such tile.", 0, 0, 0, 0);
            if (GetTile(tileId).IsWater)
                return new PanelPreview(false, "Panels cannot go on water yet.", 0, 0, 0, 0);
            double area = NewPanelAreaM2(tileId);
            PanelParams panel = MakePanel(tileId, tilt, azimuth, 1.0, area);
            AnnualYieldResult year = AnnualYield.Calculate(planet, panel);
            double energy = year.EnergyWh, capacityFactor = year.CapacityFactor;
            if (Clouds == CloudModel.EarthClimatology && energy > 0.0)
            {
                // Clouds as a multiplier on output, month by month (weighted by when the panel makes its energy).
                double[] months = MonthlyYield.Calculate(planet, panel);
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
            double embodied = EmbodiedWhPerM2 * area;
            double eroi = energy > 0 ? Economics.Eroi(energy * PanelLifetimeYears, embodied) : 0.0;
            double payback = energy > 0 ? Economics.PaybackYears(embodied, energy) : double.PositiveInfinity;
            return new PanelPreview(true, "", energy, capacityFactor, eroi, payback);
        }

        public double[] MonthlyEnergyWh(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).IsWater)
                return null;
            double[] months = MonthlyYield.Calculate(planet, MakePanel(tileId, tilt, azimuth, 1.0, NewPanelAreaM2(tileId)));
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

        double ComputeGeneration()
        {
            double total = 0.0;
            double meanAnomaly = CurrentMeanAnomaly();
            foreach (var pair in panels)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(pair.Key.Tile, pair.Key.Slot))
                    continue;   // a site makes nothing until it is finished
                Double3 sun = SunAt(pair.Key.Tile);
                if (sun.Up <= 0.0)
                    continue;
                PanelParams p = MakePanel(pair.Key.Tile, pair.Value.Tilt, pair.Value.Azimuth,
                                          CloudFactor(pair.Key.Tile, meanAnomaly), pair.Value.Area);
                total += PanelPowerNow(p, sun, out _, out _, out _, out _);
            }
            return total;
        }

        /// <summary>One panel's power under the star right now (sun up), with the light and temperature along the way.</summary>
        double PanelPowerNow(in PanelParams p, Double3 sun, out double dni, out double dhi, out double poa, out double cellT)
        {
            double airMass = Atmosphere.AirMassKastenYoung(SunPosition.ZenithDegrees(sun), p.PressureRatio);
            dni = Atmosphere.DirectNormal(currentIrradiance, planet.OpticalDepth, airMass);
            dhi = Atmosphere.DiffuseHorizontal(currentIrradiance, sun.Up, planet.OpticalDepth, airMass, planet.DiffuseK);
            double ghi = Atmosphere.GlobalHorizontal(dni, dhi, sun.Up);
            double cosTheta = PanelModel.CosIncidence(PanelModel.Normal(p.Tilt, p.Azimuth), sun);
            poa = PanelModel.PlaneOfArray(dni, dhi, ghi, cosTheta, p.Tilt, p.Albedo, p.FShade);
            cellT = PanelModel.CellTemperature(planet.AirTemperatureC, poa, p.Noct,
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
            double cloud = CloudFactor(tileId, meanAnomaly);
            PanelParams p = MakePanel(tileId, tilt, azimuth, cloud, NewPanelAreaM2(tileId));
            double dni = 0.0, dhi = 0.0, poa = 0.0, cellT = planet.AirTemperatureC, power = 0.0;
            if (sun.Up > 0.0)
                power = PanelPowerNow(p, sun, out dni, out dhi, out poa, out cellT);
            return new SkyReport(planet.TidallyLocked, Sphere.Latitude(tileId) * deg, planet.Obliquity * deg, lambda,
                                 declination, hourAngle, planet.TidallyLocked ? double.NaN : SimClock.LocalTime24(hourAngle / deg),
                                 Math.Asin(Math.Max(-1.0, Math.Min(1.0, sun.Up))) * deg, SunPosition.AzimuthDegrees(sun),
                                 tilt * deg, azimuth * deg, PanelModel.Normal(tilt, azimuth).Dot(sun),
                                 currentIrradiance, dni, dhi, poa, cellT, power, SpectralFactor, cloud);
        }

        double CurrentMeanAnomaly() => YearLengthHours > 0 ? 2.0 * Math.PI * TimeHours / YearLengthHours : 0.0;

        static double MonthMeanAnomaly(int month, int months) => 2.0 * Math.PI * (month + 0.5) / months;

        /// <summary>Share of clear-sky light reaching panels on a tile at this point of the orbit (1 without clouds).</summary>
        public double CloudFactor(int tileId, double meanAnomaly) =>
            Clouds == CloudModel.EarthClimatology
                ? Climate.CloudClimate.Factor(Sphere.Latitude(tileId), Climate.CloudClimate.YearFractionFromMeanAnomaly(meanAnomaly))
                : 1.0;
    }
}

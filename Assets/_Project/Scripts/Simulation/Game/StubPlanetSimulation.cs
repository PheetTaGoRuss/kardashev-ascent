using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// A working stand-in for the real simulation so Presentation can be built against <see cref="IPlanetSimulation"/> now.
    /// The star's motion, panel output and previews use the real solar model, and power flows through the
    /// colony grid (<see cref="PowerGrid"/>: battery, lander RTGs, base demand). K accounting is a placeholder
    /// until the K meter (#10).
    /// </summary>
    public sealed class StubPlanetSimulation : IPlanetSimulation
    {
        const double EarthRadiusM = 6.371e6;
        const double EarthIrradiance = 1361.0;
        const double MaxStepHours = 0.25;

        /// <summary>Placeholder farm size for one placed panel, m².</summary>
        public double PanelAreaM2 { get; set; } = 10_000.0;
        public double PanelEfficiency { get; set; } = 0.20;
        /// <summary>Energy to build 1 m² of panel, Wh (≈ 500 kWh/m² for silicon).</summary>
        public double EmbodiedWhPerM2 { get; set; } = 500_000.0;
        public double PanelLifetimeYears { get; set; } = 25.0;
        /// <summary>Placeholder colony demand, W.</summary>
        public double BaseDemandW { get => Grid.DemandW; set => Grid.DemandW = value; }

        readonly PlanetSurface surface;
        readonly PlanetSolarParams planet;
        readonly double planetRadiusM;
        readonly double tileAreaM2;
        readonly double subStellarLongitudeAtStart;
        readonly Dictionary<(int Tile, int Slot), (double Tilt, double Azimuth)> panels = new Dictionary<(int, int), (double, double)>();
        readonly Dictionary<int, int> panelsPerTile = new Dictionary<int, int>();
        bool targetReached;

        /// <summary>The colony's grid: battery, RTGs and demand (tune it here). Demand is constant for now.</summary>
        public PowerGrid Grid { get; } = PowerGrid.Lander();

        public StubPlanetSimulation(HexSphere sphere, PlanetSurface surface, PlanetSolarParams planet,
                                    double planetRadiusM = EarthRadiusM, int baseTileId = -1)
        {
            Sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            this.planet = planet;
            this.planetRadiusM = planetRadiusM;
            tileAreaM2 = 4.0 * Math.PI * planetRadiusM * planetRadiusM / sphere.TileCount;
            BaseTileId = baseTileId >= 0 ? baseTileId : PickBaseTile();

            // Start mid-morning at the base: hour angle −0.4π.
            subStellarLongitudeAtStart = Sphere.Longitude(BaseTileId) + 0.4 * Math.PI;
            Grid.BrownoutChanged += on => BrownoutChanged?.Invoke(on);

            double g0 = Orbit.ExtraterrestrialIrradiance(planet.Luminosity, planet.SemiMajorAxis);
            TypeITargetW = 1e16 * (g0 / EarthIrradiance) * Math.Pow(planetRadiusM / EarthRadiusM, 2.0);

            UpdateStar();
            IsDayAtBase = SunAt(BaseTileId).Up > 0.0;
            GenerationW = ComputeGeneration() + Grid.RtgPowerW(0.0);
            RtgW = Grid.RtgPowerW(0.0);
            DemandW = BaseDemandW;
            KardashevK = ToK(Math.Min(GenerationW, DemandW));
        }

        public HexSphere Sphere { get; }
        public int TileCount => Sphere.TileCount;
        public int BaseTileId { get; }
        public double TimeHours { get; private set; }
        public double DayLengthHours => planet.DayLengthHours;
        public double YearLengthHours => planet.YearLengthHours;
        public Vector3 StarDirection { get; private set; }
        public bool IsDayAtBase { get; private set; }
        public double GenerationW { get; private set; }
        public double DemandW { get; private set; }
        public double RtgW { get; private set; }
        public double BatteryStoredWh => Grid.Battery.StoredWh;
        public double BatteryCapacityWh => Grid.Battery.CapacityWh;
        public double BatteryPowerW { get; private set; }
        public double CurtailedW { get; private set; }
        public bool IsBrownout => Grid.IsBrownout;
        public double KardashevK { get; private set; }
        public double TypeITargetW { get; }

        public event Action<bool> BrownoutChanged;
        public event Action<bool> DayNightChanged;
        public event Action TargetReached;

        public TileInfo GetTile(int tileId) =>
            new TileInfo(tileId, Sphere.Latitude(tileId), Sphere.Longitude(tileId),
                         surface.Terrain[tileId], surface.Elevation[tileId], tileAreaM2);

        public void Tick(double hours)
        {
            if (hours <= 0.0)
                return;
            while (hours > 0.0)
            {
                double dt = Math.Min(hours, MaxStepHours);
                Step(dt);
                hours -= dt;
            }
        }

        void Step(double dt)
        {
            TimeHours += dt;
            UpdateStar();

            GridStep grid = Grid.Step(ComputeGeneration(), dt, TimeHours / Rtg.HoursPerYear);
            GenerationW = grid.GenerationW;
            RtgW = grid.RtgW;
            DemandW = grid.DemandW;
            BatteryPowerW = grid.ChargeW - grid.DischargeW;
            CurtailedW = grid.CurtailedW;
            // Used power: what reached the loads plus what went into the battery; curtailed power is not used.
            KardashevK = ToK(grid.ServedW + grid.ChargeW);

            bool day = SunAt(BaseTileId).Up > 0.0;
            if (day != IsDayAtBase)
            {
                IsDayAtBase = day;
                DayNightChanged?.Invoke(day);
            }

            if (!targetReached && GenerationW >= TypeITargetW)
            {
                targetReached = true;
                TargetReached?.Invoke();
            }
        }

        public PanelPreview PreviewPanel(int tileId, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount)
                return new PanelPreview(false, "No such tile.", 0, 0, 0, 0);
            if (GetTile(tileId).IsWater)
                return new PanelPreview(false, "Panels cannot go on water yet.", 0, 0, 0, 0);
            if (planet.DayLengthHours <= 0.0)
                return new PanelPreview(false, "Tidally locked planets are not supported yet.", 0, 0, 0, 0);

            AnnualYieldResult year = AnnualYield.Calculate(planet, MakePanel(tileId, tilt, azimuth));
            double embodied = EmbodiedWhPerM2 * PanelAreaM2;
            double eroi = year.EnergyWh > 0 ? Economics.Eroi(year.EnergyWh * PanelLifetimeYears, embodied) : 0.0;
            double payback = year.EnergyWh > 0 ? Economics.PaybackYears(embodied, year.EnergyWh) : double.PositiveInfinity;
            return new PanelPreview(true, "", year.EnergyWh, year.CapacityFactor, eroi, payback);
        }

        public int SiteRings { get; set; } = TileSite.DefaultRings;

        public bool PlacePanel(int tileId, int slot, double tilt, double azimuth)
        {
            if (slot < 0 || slot >= TileSite.SlotCountFor(SiteRings) || HasPanel(tileId, slot)
                || !PreviewPanel(tileId, tilt, azimuth).CanPlace)
                return false;
            panels[(tileId, slot)] = (tilt, azimuth);
            panelsPerTile[tileId] = PanelCount(tileId) + 1;
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        public bool RemovePanel(int tileId, int slot)
        {
            if (!panels.Remove((tileId, slot)))
                return false;
            panelsPerTile[tileId] = PanelCount(tileId) - 1;
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        public bool HasPanel(int tileId, int slot) => panels.ContainsKey((tileId, slot));

        public int PanelCount(int tileId) => panelsPerTile.TryGetValue(tileId, out int n) ? n : 0;

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

        double currentIrradiance;

        Double3 SunAt(int tileId) => PlanetFrame.ToLocal(StarDirection, Sphere.Latitude(tileId), Sphere.Longitude(tileId));

        double ComputeGeneration()
        {
            double total = 0.0;
            foreach (var pair in panels)
            {
                Double3 sun = SunAt(pair.Key.Tile);
                if (sun.Up <= 0.0)
                    continue;
                PanelParams p = MakePanel(pair.Key.Tile, pair.Value.Tilt, pair.Value.Azimuth);
                double airMass = Atmosphere.AirMassKastenYoung(SunPosition.ZenithDegrees(sun), p.PressureRatio);
                double dni = Atmosphere.DirectNormal(currentIrradiance, planet.OpticalDepth, airMass);
                double dhi = Atmosphere.DiffuseHorizontal(currentIrradiance, sun.Up, planet.OpticalDepth, airMass, planet.DiffuseK);
                double ghi = Atmosphere.GlobalHorizontal(dni, dhi, sun.Up);
                double cosTheta = PanelModel.CosIncidence(PanelModel.Normal(p.Tilt, p.Azimuth), sun);
                double poa = PanelModel.PlaneOfArray(dni, dhi, ghi, cosTheta, p.Tilt, p.Albedo, p.FShade);
                double cellT = PanelModel.CellTemperature(planet.AirTemperatureC, poa, p.Noct);
                total += PanelModel.Power(poa, p.Area, p.Efficiency, cellT, p.TempCoefficient, p.FSpec, p.FDust);
            }
            return total;
        }

        PanelParams MakePanel(int tileId, double tilt, double azimuth)
        {
            float elevation = Math.Max(0f, surface.Elevation[tileId]);
            double pressureRatio = Math.Exp(-elevation * 4000.0 / 8400.0); // elevation 1 ≈ 4 km, scale height 8.4 km
            double albedo = surface.Terrain[tileId] == TerrainType.Ice ? 0.8 : 0.2;
            return new PanelParams(Sphere.Latitude(tileId), tilt, azimuth, PanelAreaM2, PanelEfficiency,
                                   45.0, -0.004, albedo, pressureRatio, 1.0, 0.0, 1.0);
        }

        int PickBaseTile()
        {
            // Lowland tile closest to 20° N, longitude 0: a temperate, sunny start.
            Vector3 goal = PlanetFrame.FromLatLon(20.0 * Math.PI / 180.0, 0.0);
            int best = 0;
            float bestDot = float.NegativeInfinity;
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                if (surface.Terrain[t] != TerrainType.Lowland)
                    continue;
                float d = Vector3.Dot(Sphere.Centers[t], goal);
                if (d > bestDot) { bestDot = d; best = t; }
            }
            return best;
        }

        static double ToK(double watts) => (Math.Log10(Math.Max(watts, 1.0)) - 6.0) / 10.0;
    }
}

using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// Wind balance (T-balance, report): for each planet type, two game years of one turbine against the same build mass spent on solar panels at the same mid-latitude tile:
    /// kWh a year per kg built, and the turbine's capacity factor. The numbers are reported, not tuned.
    /// </summary>
    public class WindBalanceTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Params(double meanC, double pressureEarth, double au = 1.0, double yearH = 365.256 * 24.0, double dayH = 24.0) =>
            new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit * au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, dayH, yearH, 0.32, 0.3, meanC, false, pressureEarth);
        static PlanetSolarParams Mars => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);

        [Test]
        public void TurbineAgainstSolar_PerKgBuilt_AtEveryPlanetType()
        {
            var types = new (string Name, PlanetSolarParams P, CloudModel C)[]
            {
                ("Earth-like 1 atm", Params(15.0, 1.0), CloudModel.EarthClimatology),
                ("Cold Earth-like (-25 C)", Params(-25.0, 1.0), CloudModel.EarthClimatology),
                ("Mars-like 0.006 atm", Mars, CloudModel.None),
                ("Hot thick air 2 atm (60 C)", Params(60.0, 2.0), CloudModel.None),
                ("Airless", Params(-50.0, 0.0), CloudModel.None),
            };
            double targetLat = 45.0 * Math.PI / 180.0;
            var rows = new System.Text.StringBuilder("T-balance (2 game years, one turbine vs the same build mass of solar at a 45 N land tile): planet | turbine kWh/yr | capacity factor | solar kWh/yr same mass | turbine kWh/kg | solar kWh/kg | turbine/solar\n");
            foreach (var t in types)
            {
                var sim = new PlanetSimulation(Sphere, Surface, t.P, Kardashev.EarthRadiusM) { WiredGrid = false, Clouds = t.C };
                sim.Debug.UnlockAll = true; sim.Debug.FreeBuilding = true;
                int tile = Enumerable.Range(0, sim.TileCount).Where(i => i != sim.BaseTileId && !sim.GetTile(i).IsWater && sim.IsOpenGround(i)).OrderBy(i => Math.Abs(Sphere.Latitude(i) - targetLat)).First();
                double sum = 0.0; int days = (int)(2 * sim.YearLengthHours / 24.0);
                for (int d = 0; d < days; d++) { sim.Tick(24.0); sum += sim.WindTurbinePowerAtW(tile) * 24.0; }
                double hours = days * 24.0;
                double earthYearH = SolarConstants.EarthYearHours;
                double turbineKwh = sum / 1000.0 / hours * earthYearH;   // per Earth year, as the panel preview is
                double cf = turbineKwh / (WindPower.RatedW / 1000.0 * earthYearH);
                // the same mass of panels at this tile (south-facing, tilted at the latitude)
                double area1 = sim.NewPanelAreaM2(tile);
                double kg1 = BuildCosts.PanelModuleKgPerM2 * area1;   // the real mass of the module, as the turbine mass is real mass
                double solarPerPanelKwh = sim.PreviewPanel(tile, Math.Abs(Sphere.Latitude(tile)), Math.PI).EnergyPerYearWh / 1000.0;
                double nPanels = WindPower.MassKg / Math.Max(1e-9, kg1);
                double solarKwh = solarPerPanelKwh * nPanels;
                rows.Append($"{t.Name} | {turbineKwh:0} | {cf * 100.0:0.0} % | {solarKwh:0} | {turbineKwh / WindPower.MassKg:0.00} | {solarKwh / WindPower.MassKg:0.00} | {(solarKwh > 0 ? turbineKwh / solarKwh : 0.0):0.00}\n");
                if (t.Name == "Airless") Assert.AreEqual(0.0, turbineKwh, 1e-9, "no air, no wind power");
            }
            TestContext.WriteLine(rows.ToString());
        }
    }
}

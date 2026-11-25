using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>Rescue balance (report): in the worst season of two planet types, one robot is forced dry 2 tiles out, three weather seeds, each method and the planner's pick.</summary>
    public class RescueBalanceTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams MarsParams => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);
        static PlanetSolarParams EarthParams => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);

        static (int Requested, int Succeeded, int ByJump, int ByTow, double Wh, int Lost, string Note) Run(PlanetSolarParams p, CloudModel clouds, double latDeg, bool southernWinter, int seed, RescueMethod? method, int hops = 2, bool noRescue = false)
        {
            double wantLat = latDeg * Math.PI / 180.0;
            int baseTile = Enumerable.Range(0, Sphere.TileCount).Where(t => !Surface.Terrain[t].ToString().Contains("Ocean")).OrderBy(t => Math.Abs(Sphere.Latitude(t) - wantLat)).First();
            var sim = new PlanetSimulation(Sphere, Surface, p, Kardashev.EarthRadiusM, baseTile) { ConstructionTimeEnabled = false, LogisticsEnabled = true, Clouds = clouds, TerrainSeed = 7 + seed * 101, RescueMethodOverride = method, RescueEnabled = !noRescue };
            sim.Workforce.Reset(sim.BaseTileId, 0);
            sim.Debug.UnlockAll = true; sim.Debug.FreeBuilding = true;
            for (int sl = 0, placed = 0; sl < sim.SlotCount(sim.BaseTileId) && placed < 40; sl++) if (sim.PlacePanel(sim.BaseTileId, sl, 0.35, Math.PI)) placed++;
            for (int sl = 0; sl < sim.SlotCount(sim.BaseTileId); sl++) if (sim.PlaceChargingStation(sim.BaseTileId, sl)) break;
            sim.Debug.FreeBuilding = false;
            sim.Workforce.AddRobot(RobotKind.General, sim.BaseTileId);
            double targetLs = southernWinter ? 90.0 : 270.0;
            for (int h = 0; h < 24 * 700 && Math.Abs(((sim.SolarLongitudeDegAt(sim.TimeHours) - targetLs + 540.0) % 360.0) - 180.0) > 6.0; h++) sim.Tick(1.0);
            // let the colony settle (everyone charged), then one robot is forced dry two tiles out
            for (int h = 0; h < 12; h++) sim.Tick(1.0);
            var near = Sphere.Neighbors[baseTile].Where(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n)).ToList();
            int two = hops == 1 ? (near.Count > 0 ? near[0] : 0) : near.SelectMany(n => Sphere.Neighbors[n]).Where(t => t != baseTile && !Sphere.Neighbors[baseTile].Contains(t) && !sim.GetTile(t).IsWater && sim.IsOpenGround(t)).OrderBy(t => t).FirstOrDefault();
            if (two == 0) return (0, 0, 0, 0, 0.0, 0, "no open tile " + hops + " out");
            var s = sim.Workforce.AddRobot(RobotKind.General, two);
            s.Battery.Draw(latDeg == 45.0 ? 1e12 : s.Battery.UsableWh - 0.25 * s.Battery.CapacityWh * s.Battery.ColdFactor, 1.0);   // the summer case is run truly empty   // low, not empty: it cannot get home on 25 % (the reserve for the way back is larger)
            for (int h = 0; h < 72; h++) sim.Tick(0.5);
            string ladder = sim.Workforce.GetRobot(s.Id) == null ? "lost" : sim.LadderOf(s.Id).Text;
            if (false) TestContext.WriteLine("LOG seed " + seed + " " + hops + " " + string.Join(" || ", sim.OperationsLog.Where(o => o.Quantity.StartsWith("Rescue") || o.Quantity == "Robot destroyed").Select(o => o.ObservedAtHours.ToString("0") + " " + o.Text)));
            return (sim.RescuesRequested, sim.RescuesSucceeded, sim.RescuesByJump, sim.RescuesByTow, sim.RescueEnergyWh, sim.RobotsDestroyed, ladder);
        }

        [Test, Explicit("balance report (about 3 minutes): run by hand")]
        public void ForcedDryTwoTilesOut_RescuesAttemptedSucceededMethodEnergyAndLosses()
        {
            var rows = new System.Text.StringBuilder("Rescue balance (one robot forced dry 2 tiles out, 36 h, 3 seeds): scenario | distance | method | rescues requested | succeeded | by jump | by tow | energy Wh | robots lost | final state\n");
            var scenarios = new (string Name, PlanetSolarParams P, CloudModel C, double Lat, bool South)[]
            {
                ("Earth-like 60 N winter", EarthParams, CloudModel.EarthClimatology, 60.0, false),
                ("Mars southern winter", MarsParams, CloudModel.None, -10.0, true),
                ("Earth-like 45 N summer", EarthParams, CloudModel.EarthClimatology, 45.0, true),
            };
            foreach (var sc in scenarios)
                foreach (int hops in new[] { 1, 2 })
                foreach (RescueMethod? method in new RescueMethod?[] { null, RescueMethod.Jump, RescueMethod.Tow, (RescueMethod)(-1) })
                {
                    int req = 0, ok = 0, jump = 0, tow = 0, lost = 0; double wh = 0; string last = "";
                    for (int seed = 0; seed < 3; seed++) { var r = Run(sc.P, sc.C, sc.Lat, sc.South, seed, method == (RescueMethod)(-1) ? (RescueMethod?)null : method, hops, method == (RescueMethod)(-1)); req += r.Requested; ok += r.Succeeded; jump += r.ByJump; tow += r.ByTow; wh += r.Wh; lost += r.Lost; last = r.Note; }
                    rows.Append($"{sc.Name} | {hops} tile(s) | {(method == (RescueMethod)(-1) ? "no rescue (baseline)" : method.HasValue ? method.ToString() : "planner's pick")} | {req} | {ok} | {jump} | {tow} | {wh / 3.0:0} | {lost} | {last}\n");
                    if (!method.HasValue) TestContext.WriteLine("planner pick ok=" + ok + " for " + sc.Name);
                }
            TestContext.WriteLine(rows.ToString());
        }

        [Test]
        public void Quick_ARobotForcedDryOneTileOutInWinter_NeverCostsAHelper()
        {
            var r = Run(EarthParams, CloudModel.EarthClimatology, 60.0, false, 0, null, 1);
            var baseline = Run(EarthParams, CloudModel.EarthClimatology, 60.0, false, 0, null, 1, noRescue: true);
            TestContext.WriteLine($"with rescue: requested {r.Requested}, succeeded {r.Succeeded}, lost {r.Lost}; without: lost {baseline.Lost}");
            Assert.LessOrEqual(r.Lost, baseline.Lost, "rescue never makes the losses worse");
        }
    }
}

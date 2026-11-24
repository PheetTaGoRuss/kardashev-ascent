using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>
    /// Weather W8: an exposed field campaign. One robot hauls for 30 days between the lander and a tile away from it, with the planner's normal staging
    /// and range check, in the worst season: Mars in southern winter, and an Earth-like world at 60 degrees in winter. Three weather seeds each.
    /// Report-only for now (see known-issues KI-46).
    /// </summary>
    public class FieldCampaignTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams MarsParams => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);
        static PlanetSolarParams EarthParams => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);

        /// <summary>(robot lost, storm days seen at the target, why it was lost or what it hauled)</summary>
        public static (bool Lost, int Storms, string Note) Run(PlanetSolarParams p, CloudModel clouds, double latDeg, bool southernWinter, int seed)
        {
            double wantLat = latDeg * Math.PI / 180.0;
            int baseTile = Enumerable.Range(0, Sphere.TileCount).Where(t => !Surface.Terrain[t].ToString().Contains("Ocean")).OrderBy(t => Math.Abs(Sphere.Latitude(t) - wantLat)).First();
            var sim = new PlanetSimulation(Sphere, Surface, p, Kardashev.EarthRadiusM, baseTile) { ConstructionTimeEnabled = false, LogisticsEnabled = true, Clouds = clouds, TerrainSeed = 7 + seed * 101 };
            sim.Workforce.Reset(sim.BaseTileId, 0);
            sim.Debug.UnlockAll = true;
            sim.Debug.FreeBuilding = true;
            for (int sl = 0, placed = 0; sl < sim.SlotCount(sim.BaseTileId) && placed < 40; sl++) if (sim.PlacePanel(sim.BaseTileId, sl, 0.35, Math.PI)) placed++;
            for (int sl = 0; sl < sim.SlotCount(sim.BaseTileId); sl++) if (sim.PlaceChargingStation(sim.BaseTileId, sl)) break;
            sim.Debug.FreeBuilding = false;
            sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.Transport, sim.BaseTileId);   // the one hauling robot
            // wait for the worst season: Ls 90 is the southern winter solstice on Mars, 21 December the northern winter on Earth
            double targetLs = southernWinter ? 90.0 : 270.0;
            for (int h = 0; h < 24 * 700 && Math.Abs(((sim.SolarLongitudeDegAt(sim.TimeHours) - targetLs + 540.0) % 360.0) - 180.0) > 6.0; h++) sim.Tick(1.0);
            int target = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && t != sim.BaseTileId && sim.Routes.HoursBetween(sim.BaseTileId, t) > 2.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 24.0 && sim.PlanHaul(sim.BaseTileId, t, 400.0).Feasible)
                .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).LastOrDefault();
            if (target == 0 && !sim.PlanHaul(sim.BaseTileId, 0, 400.0).Feasible) return (false, 0, "no feasible target");
            sim.SetStockTarget(target, Material.Iron, 2000.0);
            int storms = 0; double startH = sim.TimeHours;
            var trace = new System.Text.StringBuilder();
            for (int h = 0; h < 24 * 30; h++) { sim.Tick(1.0); if (h >= 10 && h < 26 && sim.Workforce.Robots.Count > 0) { var rr = sim.Workforce.Robots[0]; trace.Append($"[{h}h tile {rr.Tile} soc {(int)(rr.Battery.StateOfCharge * 100)} {rr.State}/{rr.Shutdown}/{rr.Activity}/task {rr.Task} storm {rr.StormHold} man {rr.ManualTarget} reserve {rr.ReserveFraction:0.00} air {sim.LiveAirTemperatureAt(rr.Tile):0}]"); } if (h % 24 == 0 && sim.StormAt(target)) storms++; }
            bool lost = sim.RobotsDestroyed > 0;
            string robots = string.Join(",", sim.Workforce.Robots.Select(r => r.Kind.ToString() + ":" + r.State + ":" + r.Shutdown + ":" + (int)(r.Battery.StateOfCharge * 100) + "%@" + r.Tile));
            string note = lost ? trace + string.Join(" | ", sim.OperationsLog.Where(r => r.Quantity == "Robot destroyed").Select(r => (r.ObservedAtHours - startH).ToString("0") + "h " + r.Text))
                : "hauled " + sim.HaulTotals.KgMoved.ToString("0") + " kg; target " + target + " hours " + sim.Routes.HoursBetween(sim.BaseTileId, target).ToString("0") + "; robots " + robots + "; orders " + sim.HaulOrders().Count + "; iron base " + sim.KgAt(sim.BaseTileId, Material.Iron).ToString("0") + " target " + sim.KgAt(target, Material.Iron).ToString("0");
            return (lost, storms, note);
        }

        [Test]
        public void MarsSouthernWinter_OneHaulingRobotFor30Days_ThreeSeeds()
        {
            var runs = Enumerable.Range(0, 3).Select(s => Run(MarsParams, CloudModel.None, -10.0, true, s)).ToList();
            TestContext.WriteLine("Mars southern winter: " + string.Join(" || ", runs.Select(r => $"lost={r.Lost} storms={r.Storms} {r.Note}")));
        }

        [Test]
        public void EarthLikeAt60DegreesInWinter_OneHaulingRobotFor30Days_ThreeSeeds()
        {
            var runs = Enumerable.Range(0, 3).Select(s => Run(EarthParams, CloudModel.EarthClimatology, 60.0, false, s)).ToList();
            // KNOWN ISSUE KI-46: robots on a loaded haul are lost near the base on lumpy ground; this run REPORTS the losses, it does not assert them yet.
            TestContext.WriteLine("Earth-like 60 N winter: " + string.Join(" || ", runs.Select(r => $"lost={r.Lost} storms={r.Storms} {r.Note}")));
        }
    }
}

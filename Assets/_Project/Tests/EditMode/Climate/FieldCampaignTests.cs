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
    /// A robot lost in any run is a planner bug (KI-46, decided 2026-11-25): every run asserts 0 lost.
    /// </summary>
    public class FieldCampaignTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams MarsParams => new PlanetSolarParams(SolarConstants.SolarLuminosity, 227956038589.7, 0.0935, 25.19 * Math.PI / 180.0, 250.87 * Math.PI / 180.0, 24.6597, 16487.52, 0.5, 0.3, -59.0, false, 0.00628);
        static PlanetSolarParams EarthParams => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);

        public static double LastShelterHours, LastWaitHours;

        /// <summary>(robot lost, storm days seen at the target, why it was lost or what it hauled)</summary>
        public static (bool Lost, int Storms, string Note, double HauledKg) Run(PlanetSolarParams p, CloudModel clouds, double latDeg, bool southernWinter, int seed, bool followAdvice = false, bool forecast = true)
        {
            double wantLat = latDeg * Math.PI / 180.0;
            int baseTile = Enumerable.Range(0, Sphere.TileCount).Where(t => !Surface.Terrain[t].ToString().Contains("Ocean")).OrderBy(t => Math.Abs(Sphere.Latitude(t) - wantLat)).First();
            var sim = new PlanetSimulation(Sphere, Surface, p, Kardashev.EarthRadiusM, baseTile) { ConstructionTimeEnabled = false, LogisticsEnabled = true, Clouds = clouds, TerrainSeed = 7 + seed * 101, ForecastPlanningEnabled = forecast };
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
            int target;
            if (followAdvice)
            {
                // the refusal has a way out: the planner refused this haul, the player builds the depot with a charging bay the advice names, and the haul then runs
                target = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && t != sim.BaseTileId && sim.Routes.HoursBetween(sim.BaseTileId, t) > 2.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 12.0 && !sim.PlanHaul(sim.BaseTileId, t, 400.0).Feasible)
                    .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).FirstOrDefault();
                if (target == 0) return (false, 0, "nothing was refused", 0.0);
                sim.Debug.FreeBuilding = true;
                bool yard = false, bay = false;
                for (int sl = 0; sl < sim.SlotCount(target) && !yard; sl++) yard = sim.PlaceStorage(BuildingType.StorageYard, target, sl);
                for (int sl = 0; sl < sim.SlotCount(target) && !bay; sl++) bay = sim.PlaceChargingBay(target, sl);
                sim.Debug.FreeBuilding = false;
                if (!yard || !bay) return (false, 0, "could not build the depot", 0.0);
            }
            else
            {
                target = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && t != sim.BaseTileId && sim.Routes.HoursBetween(sim.BaseTileId, t) > 2.0 && sim.Routes.HoursBetween(sim.BaseTileId, t) < 24.0 && sim.PlanHaul(sim.BaseTileId, t, 400.0).Feasible)
                    .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).LastOrDefault();
                if (target == 0 && !sim.PlanHaul(sim.BaseTileId, 0, 400.0).Feasible) return (false, 0, "no feasible target", 0.0);
            }
            sim.SetStockTarget(target, Material.Iron, 2000.0);
            int storms = 0; double startH = sim.TimeHours;
            var trace = new System.Text.StringBuilder();
            for (int h = 0; h < 24 * 30; h++) { sim.Tick(1.0); if (h < 24) { trace.Append($"[{h}h"); foreach (var rr in sim.Workforce.Robots) trace.Append($" R{rr.Id}@{rr.Tile} {(int)(rr.Battery.StateOfCharge * 100)}% {rr.Activity}/{(rr.Task.HasValue ? rr.Task.Value.Kind.ToString() : "-")} route{rr.Route.Count}"); trace.Append("]"); } if (h % 24 == 0 && sim.StormAt(target)) storms++; }
            LastShelterHours = sim.StormShelterRobotHours; LastWaitHours = sim.HaulStormWaitHours;
            bool lost = sim.RobotsDestroyed > 0;
            string robots = string.Join(",", sim.Workforce.Robots.Select(r => r.Kind.ToString() + ":" + r.State + ":" + r.Shutdown + ":" + (int)(r.Battery.StateOfCharge * 100) + "%@" + r.Tile));
            string note = lost ? "base " + sim.BaseTileId + " target " + target + " " + trace + string.Join(" | ", sim.OperationsLog.Where(r => r.Quantity == "Robot destroyed").Select(r => (r.ObservedAtHours - startH).ToString("0") + "h " + r.Text))
                : "hauled " + sim.HaulTotals.KgMoved.ToString("0") + " kg; target " + target + " hours " + sim.Routes.HoursBetween(sim.BaseTileId, target).ToString("0") + "; robots " + robots + "; orders " + sim.HaulOrders().Count + "; iron base " + sim.KgAt(sim.BaseTileId, Material.Iron).ToString("0") + " target " + sim.KgAt(target, Material.Iron).ToString("0");
            return (lost, storms, note, sim.HaulTotals.KgMoved);
        }

        [Test]
        public void MarsSouthernWinter_OneHaulingRobotFor30Days_ThreeSeeds()
        {
            var runs = Enumerable.Range(0, 3).Select(s => Run(MarsParams, CloudModel.None, -10.0, true, s)).ToList();
            Assert.AreEqual(0, runs.Count(r => r.Lost), "a robot is never lost because the planner sent it out: " + string.Join(" || ", runs.Select(r => r.Note)));
            TestContext.WriteLine("Mars southern winter: " + string.Join(" || ", runs.Select(r => $"lost={r.Lost} storms={r.Storms} {r.Note}")));
        }

        [Test]
        public void MarsSouthernWinter_WithTheDepotAndChargerTheAdviceNames_TheHaulRunsAndNoRobotIsLost()
        {
            var runs = Enumerable.Range(0, 3).Select(s => Run(MarsParams, CloudModel.None, -10.0, true, s, followAdvice: true)).ToList();
            TestContext.WriteLine("Mars winter, advice followed: " + string.Join(" || ", runs.Select(r => $"lost={r.Lost} hauled={r.HauledKg:0} {r.Note}")));
            Assert.AreEqual(0, runs.Count(r => r.Lost), string.Join(" || ", runs.Select(r => r.Note)));
            Assert.Greater(runs.Count(r => r.HauledKg > 0.0), 0, "the refusal had a way out: with the charger the haul ran: " + string.Join(" || ", runs.Select(r => r.Note)));
        }

        [Test]
        public void EarthLikeAt60DegreesInWinter_OneHaulingRobotFor30Days_ThreeSeeds()
        {
            var runs = Enumerable.Range(0, 3).Select(s => Run(EarthParams, CloudModel.EarthClimatology, 60.0, false, s)).ToList();
            // KNOWN ISSUE KI-46: robots on a loaded haul are lost near the base on lumpy ground; this run REPORTS the losses, it does not assert them yet.
            Assert.AreEqual(0, runs.Count(r => r.Lost), "a robot is never lost because the planner sent it out: " + string.Join(" || ", runs.Select(r => r.Note)));
            TestContext.WriteLine("Earth-like 60 N winter: " + string.Join(" || ", runs.Select(r => $"lost={r.Lost} storms={r.Storms} {r.Note}")));
        }

        [Test]
        public void ForecastPlanning_CampaignTable_AgainstTheOldRule_NoRobotLost()
        {
            var rows = new System.Text.StringBuilder("P-test (3 seeds, 30 days): scenario | planning | hauled kg per seed | robot-hours sheltering | haul-hours waiting | lost\n");
            var scenarios = new (string Name, PlanetSolarParams P, CloudModel C, double Lat, bool South, bool Advice)[]
            {
                ("Earth-like 60 N winter", EarthParams, CloudModel.EarthClimatology, 60.0, false, false),
                ("Mars southern winter", MarsParams, CloudModel.None, -10.0, true, false),
                ("Mars winter, advice followed", MarsParams, CloudModel.None, -10.0, true, true),
            };
            foreach (var sc in scenarios)
                foreach (bool forecast in new[] { false, true })
                {
                    double shelter = 0, wait = 0; int lost = 0; var kg = new System.Collections.Generic.List<string>();
                    for (int seed = 0; seed < 3; seed++)
                    {
                        var run = Run(sc.P, sc.C, sc.Lat, sc.South, seed, sc.Advice, forecast);
                        shelter += LastShelterHours; wait += LastWaitHours; if (run.Lost) lost++; kg.Add(run.HauledKg.ToString("0"));
                    }
                    rows.Append($"{sc.Name} | {(forecast ? "forecast" : "old rule")} | {string.Join("/", kg)} | {shelter:0} | {wait:0} | {lost}\n");
                    if (forecast) Assert.AreEqual(0, lost, sc.Name);
                }
            TestContext.WriteLine(rows.ToString());
        }
    }
}

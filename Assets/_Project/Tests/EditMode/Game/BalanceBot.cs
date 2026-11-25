using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// A scripted player for balance passes (docs/development/balance-findings.md). It lands the way the tutorial does, mines,
    /// builds forward chargers toward copper and lithium, orders robots, and adds panels and batteries as the colony grows,
    /// then prints a timeline. Not a regression test: explicit, run it by name when tuning numbers:
    /// <c>dotnet test tools/simtests/SimTests.csproj --filter "Name=Timeline" -- NUnit.Where="" --logger "console;verbosity=detailed"</c>
    /// (the timeline is the failure message, so a plain run shows it too).
    /// </summary>
    public class BalanceBot
    {
        static readonly Material[] Mined = { Material.Rock, Material.Iron, Material.Copper, Material.Lithium };

        [Test, Explicit("Balance tool: prints a timeline of a scripted colony")]
        public void Timeline()
        {
            Assert.Fail("\n" + Run("Earth-like", new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
                                               282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 700, true));
        }

        /// <summary>B4: a two-year run on three planet types; reports deposits used up, rock moved and the energy share mining takes. Report, not tuned.</summary>
        [Test, Explicit("Balance tool: depletion and mining energy across planet types")]
        public void DepletionByPlanetType()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            var report = new System.Text.StringBuilder();
            report.AppendLine(Run("Earth-like", new PlanetSolarParams(lum, au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 730, false));
            report.AppendLine(Run("Mars-like (cold, far)", new PlanetSolarParams(lum, 1.524 * au, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0), 730, false));
            report.AppendLine(Run("Hot (close)", new PlanetSolarParams(lum, 0.8 * au, 0.0167, 10.0 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0 * 0.51, 0.3, 0.3, 45.0), 730, false));
            Assert.Fail("\n" + report);
        }

        static string Run(string name, PlanetSolarParams planet, int days, bool full)
        {
            var sphere = HexSphere.Create(12);
            var surface = PlanetSurface.Generate(sphere, new SurfaceSettings(7));
            var sim = new PlanetSimulation(sphere, surface, planet);
            int exhausted = 0; var exhaustedWhat = new List<string>();
            sim.DepositExhausted += (tile, m) => { exhausted++; exhaustedWhat.Add($"{m}@d{(int)(sim.TimeHours / 24)}"); };
            double generatedWh = 0.0;
            int firstShortDay = -1; int milestone2Day = -1;
            int home = sim.BaseTileId;
            var log = new System.Text.StringBuilder();

            // a sensible player orders robots before spending the cargo on anything else
            int robotsOrdered = 3;   // the lander brings three
            for (int i = 0; i < 6; i++) if (sim.OrderRobot(RobotKind.General) == null) robotsOrdered++;
            // landing: the tutorial's essentials, then enough panels for the base demand (what Skip does)
            sim.PlaceChargingStation(home, 0);
            sim.PlaceBattery(home, 4);
            sim.PlaceWorkshop(home, 8);
            sim.PlaceResearchStation(home, 20);
            var preview = sim.PreviewPanel(home, 0.35, Math.PI);
            int first = Math.Max(1, (int)Math.Ceiling(1.3 * 100_000.0 / Math.Max(1.0, preview.EnergyPerYearWh / 8766.0)));
            for (int s = 30, placed = 0; s < sim.SlotCount(home) && placed < first; s++)
                if (sim.PlacePanel(home, s, 0.35, Math.PI)) placed++;
            sim.DebugSurveyAll();   // the survey puzzles are assumed solved

            var land = LandByDistance(sim, sphere, home);
            var target = new Dictionary<Material, int>();
            int chargerHops = 0;
            int last = -1;
            string robotNote = "";
            for (int hour = 0; hour < 24 * days; hour += 3)
            {
                sim.Tick(3.0);
                generatedWh += sim.GenerationW * 3.0;
                if (milestone2Day < 0 && sim.MilestonesReached >= 2) milestone2Day = hour / 24;
                if (firstShortDay < 0 && milestone2Day < 0 && hour > 24 * 20 && (sim.Materials[Material.Iron] < 1.0 || sim.Materials[Material.Rock] < 1.0)) firstShortDay = hour / 24;
                var inRange = land.Where(t => sim.WorkReachProblem(t) == null).ToList();

                // mining: base tile for rock and iron; the nearest tile with the ore for copper and lithium
                foreach (Material m in Mined)
                {
                    if (!target.TryGetValue(m, out int tile) || sim.Deposits.IsExhausted(tile, m))
                    {
                        tile = land.FirstOrDefault(t => sim.Deposits.GradeOf(t, m) > 0.0 && !sim.Deposits.IsExhausted(t, m) && (m == Material.Copper || m == Material.Lithium || sim.WorkReachProblem(t) == null));
                        target[m] = tile;
                    }
                    // a player stops mining what the stock already covers, so the robots go where the shortage is
                    double enough = m == Material.Rock ? double.MaxValue : m == Material.Iron ? 20_000.0 : 1_500.0;
                    bool wanted = sim.Materials[m] < enough;
                    if (!wanted && sim.IsMiningOrdered(tile, m)) sim.CancelMining(tile, m);
                    else if (wanted && sim.WorkReachProblem(tile) == null && !sim.IsMiningOrdered(tile, m)) sim.OrderMining(tile, m);
                }

                // forward chargers: when an ore tile is out of range, put a charger on the in-range tile nearest to it
                foreach (Material m in new[] { Material.Copper, Material.Lithium })
                {
                    int tile = target[m];
                    if (sim.WorkReachProblem(tile) == null || sim.ConstructionQueueCount > 6) continue;
                    int step = inRange.OrderBy(t => Hops(sphere, t, tile)).FirstOrDefault();
                    if (step >= 0 && sim.GetBuilding(step, 0) == BuildingType.None && sim.PlaceChargingStation(step, 0)) chargerHops++;
                }

                // robots, up to a crew of 12
                if (robotsOrdered < 16)
                {
                    robotNote = sim.OrderRobot(RobotKind.General) ?? "ordered";
                    if (robotNote == "ordered") robotsOrdered++;
                }

                // panels and batteries as the demand target rises
                // robots first: while the crew is short and the last robot order is waiting on materials, keep the stock for it
                bool saveForRobot = robotsOrdered < 16 && robotNote.StartsWith("Missing");
                if (!saveForRobot && sim.ConstructionQueueCount < 3 + sim.Workforce.Robots.Count)
                {
                    int slots = 0;
                    if (sim.GenerationW < 2.0 * Math.Max(sim.DemandW, 2e5))
                        foreach (int t in inRange.Take(12))
                        {
                            for (int s = 0; s < sim.SlotCount(t) && slots < 1; s++)
                                if (sim.CanPlace(BuildingType.SolarPanel, t, s) && sim.PlacePanel(t, s, 0.35, Math.PI)) slots++;
                            if (slots > 0) break;
                        }
                    if (sim.BatteryCapacityWh < Math.Max(sim.DemandW, 0.5 * sim.DemandTargetW) * 14.0)
                        foreach (int t in inRange.Skip(1).Take(12))
                            if (Enumerable.Range(0, sim.SlotCount(t)).Any(s => sim.PlaceBattery(t, s))) break;
                }

                int day = hour / 24;
                if (day != last && day % 10 == 0)
                {
                    last = day;
                    log.AppendLine($"d{day,3} M={sim.MilestonesReached} demand={sim.DemandW / 1e6,6:0.00}MW gen={sim.GenerationW / 1e6,6:0.0}MW batt={sim.BatteryCapacityWh / 1e6,4:0}MWh " +
                                   $"robots={sim.Workforce.Robots.Count,2} q={sim.ConstructionQueueCount,2} chargers+{chargerHops} " +
                                   $"rock={sim.Materials[Material.Rock] / 1e3:0}t fe={sim.Materials[Material.Iron] / 1e3:0}t cu={sim.Materials[Material.Copper]:0} li={sim.Materials[Material.Lithium]:0} | robot: {robotNote}");
                }
            }
            double miningWh = sim.MiningWorkHours * RobotSpecs.MinePowerW;
            double share = generatedWh > 0.0 ? miningWh / generatedWh : 0.0;
            string verdict = (share > 0.5 ? " ABSURD: mining takes over half of generation." : "") + (firstShortDay >= 0 ? $" ABSURD: ran out of iron or rock on day {firstShortDay} before milestone 2." : "");
            string summary = $"{name}: {days} d, milestones {sim.MilestonesReached} (M2 day {(milestone2Day < 0 ? "never" : milestone2Day.ToString())}), deposits used up {exhausted} [{string.Join(", ", exhaustedWhat)}], rock moved {sim.RockMovedKg / 1000.0:0} t, tailings {sim.TailingsKg / 1000.0:0} t, mining energy {miningWh / 1e6:0.0} MWh of {generatedWh / 1e6:0} MWh generated = {share * 100.0:0.0} %.{verdict}";
            return (full ? log + "\n" : "") + summary;
        }

        static List<int> LandByDistance(PlanetSimulation sim, HexSphere sphere, int home)
        {
            var order = new List<int>();
            var seen = new HashSet<int> { home };
            var queue = new Queue<int>();
            queue.Enqueue(home);
            while (queue.Count > 0)
            {
                int a = queue.Dequeue();
                if (!sim.GetTile(a).IsWater) order.Add(a);
                foreach (int nb in sphere.Neighbors[a]) if (seen.Add(nb)) queue.Enqueue(nb);
            }
            return order;
        }

        /// <summary>Tile-steps between two tiles over the sphere (breadth first; fine for a few hundred tiles).</summary>
        static int Hops(HexSphere sphere, int from, int to)
        {
            if (from == to) return 0;
            var seen = new HashSet<int> { from };
            var frontier = new List<int> { from };
            for (int d = 1; d < 60; d++)
            {
                var next = new List<int>();
                foreach (int a in frontier)
                    foreach (int nb in sphere.Neighbors[a])
                    {
                        if (nb == to) return d;
                        if (seen.Add(nb)) next.Add(nb);
                    }
                frontier = next;
            }
            return 99;
        }
    }
}

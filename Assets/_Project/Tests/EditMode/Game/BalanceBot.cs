using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
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

        /// <summary>
        /// The progression bot (B6): the scripted colony must reach milestone 1 and then milestone 2 within two game years on an Earth-like and a Mars-like world.
        /// Explicit while it is slow. It fails today: the report names the milestone days or the first blockers. Do not loosen physics or cited figures to make it pass.
        /// </summary>
        [Test, Explicit("Progression regression: milestones 1 and 2 within two game years, Earth-like and Mars-like")]
        public void ProgressionReachesMilestoneTwo()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            string earth = Run("Viridia (Earth-like)", new PlanetSolarParams(lum, au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 730, false);
            string mars = Run("Mars-like", new PlanetSolarParams(lum, 1.524 * au, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0), 730, false);
            bool ok = earth.Contains("M2 day ") && !earth.Contains("M2 day never") && mars.Contains("M2 day ") && !mars.Contains("M2 day never");
            Assert.IsTrue(ok, "\n" + earth + "\n" + mars);
        }

        /// <summary>No robot is stuck "Travelling": over 60 days of the scripted colony none travels continuously for more than 96 hours (a leg is 4 to 8 hours; a haul crosses a few).</summary>
        [Test]
        public void NoRobotStaysTravellingLongerThanAPlannedTripPlusAMargin()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            string v = Run("Viridia", new PlanetSolarParams(lum, au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 60, false);
            var m = System.Text.RegularExpressions.Regex.Match(v, @"longest continuous travel (\d+) h");
            Assert.IsTrue(m.Success, v);
            Assert.Less(int.Parse(m.Groups[1].Value), 97, v);
        }

        /// <summary>KI-46 for every outdoor task (found by the progression bot): on a -60 C world no robot may set out on a build or mining trip its cold battery and heater cannot bring it back from.</summary>
        [Test]
        public void ColdWorld_RobotsAreNotSentOnTripsTheyCannotReturnFrom()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            string mars = Run("Mars-like", new PlanetSolarParams(lum, 1.524 * au, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0), 20, false);
            StringAssert.DoesNotContain("Deaths:", mars);
            StringAssert.DoesNotContain("LAST SEEN BEFORE LOSS", mars);
        }

        [Test, Explicit("Balance tool: 60 days of the Earth-like colony with the diagnostics")]
        public void ViridiaShort()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            Assert.Fail("\n" + Run("Viridia", new PlanetSolarParams(lum, au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 60, true));
        }

        [Test, Explicit("Balance tool: the Mars-like colony's timeline")]
        public void MarsTimeline()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            Assert.Fail("\n" + Run("Mars-like", new PlanetSolarParams(lum, 1.524 * au, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0), 120, true));
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
            var sim = new PlanetSimulation(sphere, surface, planet) { WiredGrid = true, LogisticsEnabled = true };   // the new-game defaults (SimulationRunner)
            int exhausted = 0; var exhaustedWhat = new List<string>();
            sim.DepositExhausted += (tile, m) => { exhausted++; exhaustedWhat.Add($"{m}@d{(int)(sim.TimeHours / 24)}"); };
            var outpostNotes = new List<string>();
            var travelHours = new Dictionary<int, double>(); double longestTravel = 0.0;
            double generatedWh = 0.0; var lostNotes = new List<string>();
            int firstShortDay = -1; int milestone2Day = -1; int milestone1Day = -1;
            int home = sim.BaseTileId;
            var log = new System.Text.StringBuilder();

            // a sensible player orders robots before spending the cargo on anything else
            int robotsOrdered = 3;   // the lander brings three
            // (no extra robots yet: the starting copper pays for the forward chargers that make copper mining possible)
            // landing: the tutorial's essentials, then enough panels for the base demand (what Skip does)
            // (no charger on the landing tile: its power is the lander RTGs only, about 440 W for every robot at once)
            if (planet.AirTemperatureC < 0.0) sim.PlaceChargingStation(home, 0);   // cold world: robots need a charger at the lander to stay warm
            sim.PlaceBattery(home, 4);
            sim.PlaceWorkshop(home, 8);
            sim.PlaceResearchStation(home, 20);
            var preview = sim.PreviewPanel(home, 0.35, Math.PI);
            int first = Math.Max(1, (int)Math.Ceiling(1.3 * 100_000.0 / Math.Max(1.0, preview.EnergyPerYearWh / 8766.0)));
            for (int s = 30, placed = 0; s < sim.SlotCount(home) && placed < first; s++)
                if (sim.PlacePanel(home, s, 0.35, Math.PI)) placed++;
            sim.WireAllBuildings();   // what Skip does at landing
            sim.DebugSurveyAll();   // the survey puzzles are assumed solved

            var land = LandByDistance(sim, sphere, home);
            var target = new Dictionary<Material, int>();
            int chargerHops = 0; var chargerTiles = new HashSet<int>();
            int last = -1;
            string robotNote = "";
            for (int hour = 0; hour < 24 * days; hour += 3)
            {
                var snap = sim.Workforce.Robots.ToDictionary(q => q.Id, q => $"R{q.Id} task={(q.Task.HasValue ? q.Task.Value.Kind + "@" + q.Task.Value.Tile : "none")} tile={q.Tile} act={q.Activity} wh={q.Battery.UsableWh:0} soc={q.Battery.StateOfCharge:0.00} airC={sim.LiveAirTemperatureAt(q.Tile):0} sheltered={q.Sheltered} trav={q.IsTravelling} fac={(q.Facility.HasValue ? q.Facility.Value.Tile + "." + q.Facility.Value.Slot : "-")} stranded={q.IsStranded} heaterW={q.HeaterW:0} hrsTo471={(sim.Routes.HoursBetween(q.Tile, 471)):0.0} lastChg={q.LastChargeW:0} warm={q.WarmingUp} bt={q.BatteryTempC:0} hist={q.ExposedEmptyHours:0} t={sim.TimeHours:0}");
                int robotsBefore = sim.Workforce.Robots.Count;
                sim.Tick(3.0);
                if (full && sim.TimeHours > 600.0 && sim.TimeHours < 720.0) foreach (var q in sim.Workforce.Robots.Where(q => q.Task.HasValue && q.Task.Value.Kind == TaskKind.Mine && q.Task.Value.Tile == 484)) log.AppendLine($"TRACE t={sim.TimeHours:0} R{q.Id} tile={q.Tile} trav={q.IsTravelling} act={q.Activity} soc={q.Battery.StateOfCharge:0.00} tripCharge={q.TripCharge:0.00} need={q.NeedsCharge} {sim.Workforce.RangeReport(q, 484)}");
                if (sim.Workforce.Robots.Count < robotsBefore)
                    foreach (var id in snap.Keys.Where(k => !sim.Workforce.Robots.Any(q => q.Id == k))) lostNotes.Add(snap[id]);
                generatedWh += sim.GenerationW * 3.0;
                foreach (var tq in sim.Workforce.Robots) { travelHours[tq.Id] = tq.IsTravelling ? (travelHours.TryGetValue(tq.Id, out double th) ? th : 0.0) + 3.0 : 0.0; longestTravel = Math.Max(longestTravel, travelHours[tq.Id]); if (travelHours[tq.Id] == 33.0) lostNotes.Add($"LONGTRAVEL R{tq.Id} task={(tq.Task.HasValue ? tq.Task.Value.Kind + "@" + tq.Task.Value.Tile : "none")} tile={tq.Tile} act={tq.Activity} soc={tq.Battery.StateOfCharge:0.00} manual={tq.ManualTarget} fac={(tq.Facility.HasValue ? tq.Facility.Value.Tile : -1)} t={sim.TimeHours:0}"); }
                if (milestone1Day < 0 && sim.MilestonesReached >= 1) milestone1Day = hour / 24;
                if (milestone2Day < 0 && sim.MilestonesReached >= 2) milestone2Day = hour / 24;
                if (firstShortDay < 0 && milestone2Day < 0 && hour > 24 * 20 && (sim.Materials[Material.Iron] < 1.0 || sim.Materials[Material.Rock] < 1.0)) firstShortDay = hour / 24;
                var inRange = land.Where(t => sim.WorkReachProblem(t) == null).ToList();
                foreach (var ub in sim.UnwiredBuildings().ToList()) sim.PlaceBuildingWire(ub.Tile, ub.Slot);   // nothing is wired for the player (EN-22)
                bool kitPending = chargerTiles.Any(ct => Enumerable.Range(0, sim.SlotCount(ct)).Any(sl => sim.GetBuilding(ct, sl) != BuildingType.None && sim.IsUnderConstruction(ct, sl)));
                foreach (Material hm in new[] { Material.Iron, Material.Copper, Material.Lithium }) sim.SetStockTarget(home, hm, kitPending ? 0.0 : (hm == Material.Iron ? 20_000.0 : 1_500.0));   // the base keeps nothing back while an outpost kit is being hauled out   // the game's advice: keep a stock at the base; hauls bring the ore

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

                // forward chargers where the task list says "too far": a charger two tiles toward the ore from the nearest charger (the game's own advice)
                foreach (Material m in new[] { Material.Copper, Material.Lithium })
                {
                    int tile = target[m];
                    var rec = sim.Workforce.Tasks.Open.FirstOrDefault(q => q.Task.Kind == TaskKind.Mine && q.Task.Tile == tile && q.Task.Slot == (int)m);
                    bool stuck = rec != null && (rec.Reason.StartsWith("too far") || (hour > 24 * 10 && sim.Deposits.MinedKg(tile, m) < 1.0));   // too far, or ordered for days and nothing mined
                    if (!stuck || sim.ConstructionQueueCount > 6) continue;
                    var seeds = new List<int> { home }; seeds.AddRange(chargerTiles);
                    int from = seeds.OrderBy(t => Hops(sphere, t, tile)).First();
                    // a leapfrog outpost: panels, a charger and a battery on a tile up to three tiles toward the ore; the game decides whether a robot may go one way
                    var candidates = land.Where(t => Hops(sphere, from, t) <= 3 && !chargerTiles.Contains(t) && t != home && !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && Hops(sphere, t, tile) < Hops(sphere, from, tile)).OrderBy(t => Hops(sphere, t, tile)).ThenBy(t => t).Take(14).ToList();
                    bool busy = chargerTiles.Any(ct => Enumerable.Range(0, sim.SlotCount(ct)).Any(sl => sim.GetBuilding(ct, sl) != BuildingType.None && sim.IsUnderConstruction(ct, sl)));   // one outpost at a time
                    foreach (int step in busy || hour % 24 != 0 ? new List<int>() : candidates)
                    {
                        // order the outpost (sites, wiring, kit hauls); the game says whether the builder may go one way; if not, take the order back and try a nearer tile
                        string err = sim.OrderOutpost(step);
                        if (err != null) { outpostNotes.Add($"tile {step}: {err}"); continue; }
                        string outpostVerdict = sim.OutpostTextAtFullCharge(step);
                        if (outpostVerdict.StartsWith("one-way")) { chargerTiles.Add(step); chargerHops++; break; }
                        for (int sl = 0; sl < sim.SlotCount(step); sl++) if (sim.GetBuilding(step, sl) != BuildingType.None && sim.IsUnderConstruction(step, sl)) sim.OrderSalvage(step, sl);
                        foreach (Material km in Materials.All) sim.SetStockTarget(step, km, 0.0);
                        outpostNotes.Add($"tile {step}: {outpostVerdict}");
                    }
                }

                // lithium is a sealed-store material: with logistics on, mined ore needs a sealed store on the mine tile ("No room for lithium on tile N: build a sealed store there")
                if (target.TryGetValue(Material.Lithium, out int liT) && liT > 0 && sim.MiningBlocker(liT, Material.Lithium) != null && hour > 24 * 5
                    && !Enumerable.Range(0, sim.SlotCount(liT)).Any(sl => sim.GetBuilding(liT, sl) == BuildingType.SealedStore))
                    for (int sl = 30; sl < 45; sl++) if (sim.PlaceStorage(BuildingType.SealedStore, liT, sl)) break;
                // an outpost gets its charger once its panels are wired (the island has a source then)
                foreach (int ot in chargerTiles.ToList())
                    if (!Enumerable.Range(0, sim.SlotCount(ot)).Any(sl => sim.GetBuilding(ot, sl) == BuildingType.ChargingStation) && sim.IslandHasSource(ot, true))
                        for (int sl = 12; sl < 20; sl++) if (sim.PlaceChargingStation(ot, sl)) break;
                // (no base chargers on cabled tiles: a power line costs 112 kg of copper a tile; chargers stand on outposts instead)

                // robots, up to a crew of 12
                if (robotsOrdered < 16 && sim.Materials[Material.Copper] > 120.0)
                {
                    robotNote = sim.OrderRobot(RobotKind.General) ?? "ordered";
                    if (robotNote == "ordered") robotsOrdered++;
                }

                // panels and batteries as the demand target rises
                // robots first: while the crew is short and the last robot order is waiting on materials, keep the stock for it
                bool saveForRobot = robotsOrdered < 6 && robotNote.StartsWith("Missing");
                // the starting copper is kept for the chargers that make copper mining possible; growth waits until copper is coming in
                bool copperComing = target.TryGetValue(Material.Copper, out int cuTile) && sim.Deposits.MinedKg(cuTile, Material.Copper) > 100.0;
                if (!saveForRobot && (copperComing || sim.Materials[Material.Copper] > 150.0) && sim.ConstructionQueueCount < 3 + sim.Workforce.Robots.Count)
                {
                    int slots = 0;
                    if (sim.GenerationW < 2.0 * Math.Max(sim.DemandW, 2e5))
                        foreach (int t in new[] { home }.Concat(inRange.Take(12)).Where(q => sim.IsPowered(q)))
                        {
                            for (int s = 0; s < sim.SlotCount(t) && slots < 1; s++)
                                if (sim.CanPlace(BuildingType.SolarPanel, t, s) && sim.PlacePanel(t, s, 0.35, Math.PI)) slots++;
                            if (slots > 0) break;
                        }
                    if (sim.BatteryCapacityWh < Math.Max(sim.DemandW, sim.DemandTargetW) * 16.0)
                        foreach (int t in new[] { home }.Concat(inRange.Skip(1).Take(12)).Where(q => sim.IsPowered(q)))
                            if (Enumerable.Range(0, sim.SlotCount(t)).Any(s => sim.PlaceBattery(t, s))) break;
                }

                if ((hour == 24 * 100 || hour == 24 * 200) && full)
                {
                    int lt = target[Material.Lithium];
                    log.AppendLine($"LI tile={lt} minedKg={sim.Deposits.MinedKg(lt, Material.Lithium):0} pileKg={sim.KgAt(lt, Material.Lithium):0} baseKg={sim.KgAt(home, Material.Lithium):0} ordered={sim.IsMiningOrdered(lt, Material.Lithium)} hauls={string.Join(" | ", sim.HaulOrders().Select(h => h.Id + ":" + h.Material + " " + h.Kg.ToString("0") + " " + h.From + ">" + h.To + " " + (sim.HaulBlockedReason(h.Id) ?? "ok")))} targets={string.Join(",", sim.StockTargets().Select(q => q.Tile + ":" + q.Material + q.Kg))}");
                }
                if ((hour % (24 * 100) == 0) && full) log.AppendLine($"GROW d{hour / 24} target={sim.DemandTargetW:0} demand={sim.DemandW:0} gen={sim.GenerationW:0} battCap={sim.BatteryCapacityWh:0} brown={sim.IsBrownout} queue={sim.ConstructionQueueCount} robots={sim.Workforce.Robots.Count} saveForRobot={robotNote} cuComing={copperComing}");
                if (hour == 24 * 50 && full)
                    foreach (int ot in chargerTiles) log.AppendLine($"ROOM tile={ot} iron={sim.RoomAt(ot, Material.Iron):0} copper={sim.RoomAt(ot, Material.Copper):0} baseIron={sim.KgAt(home, Material.Iron):0} baseCu={sim.KgAt(home, Material.Copper):0} cost={sim.HaulCostText(home, ot, 100.0)} reach={sim.WorkReachProblem(ot)}");
                    log.AppendLine("KIT targets=" + string.Join(",", sim.StockTargets().Select(q => q.Tile + ":" + q.Material + q.Kg.ToString("0"))) + " hauls=" + string.Join(" | ", sim.HaulOrders().Select(h => h.Id + ":" + h.Material + " " + h.Kg.ToString("0") + " " + h.From + ">" + h.To + " " + (sim.HaulBlockedReason(h.Id) ?? "ok"))));
                if (hour == 24 * 50 && full)
                {
                    var counts = new Dictionary<string, int>();
                    for (int qt = 0; qt < sim.TileCount; qt++)
                        for (int qs = 0; qs < sim.SlotCount(qt); qs++)
                            if (sim.GetBuilding(qt, qs) != BuildingType.None && sim.IsUnderConstruction(qt, qs)) { string key = sim.GetBuilding(qt, qs) + (qt == home ? "@home" : "@" + qt); counts[key] = counts.TryGetValue(key, out int cc) ? cc + 1 : 1; }
                    log.AppendLine("QUEUE " + string.Join(", ", counts.Select(kv => kv.Key + " x" + kv.Value)));
                }
                if (hour == 24 * 50 && full) log.AppendLine("SHORTFALL " + string.Join(", ", Materials.All.Where(m => sim.ConstructionShortfall[m] > 0.01).Select(m => m + " " + sim.ConstructionShortfall[m].ToString("0"))) + " | remaining " + string.Join(", ", Materials.All.Where(m => sim.ConstructionRemaining[m] > 0.01).Select(m => m + " " + sim.ConstructionRemaining[m].ToString("0"))));
                if (hour == 24 * 50 && full)
                    foreach (var q in sim.Workforce.Tasks.Open) log.AppendLine($"TASK {q.Task.Kind}@{q.Task.Tile}.{q.Task.Slot} {q.State} workers={q.Workers} {q.Reason}");
                if ((hour == 24 * 50 || hour == 24 * 50 + 12) && full)
                    foreach (Material dm in new[] { Material.Copper, Material.Lithium })
                        log.AppendLine($"DIAG {dm}: task={(sim.Workforce.Tasks.Open.FirstOrDefault(q => q.Task.Kind == TaskKind.Mine && q.Task.Tile == target[dm] && q.Task.Slot == (int)dm)?.State.ToString() + "/" + sim.Workforce.Tasks.Open.FirstOrDefault(q => q.Task.Kind == TaskKind.Mine && q.Task.Tile == target[dm] && q.Task.Slot == (int)dm)?.Reason)} tile {target[dm]} reach={sim.WorkReachProblem(target[dm]) ?? "ok"} ordered={sim.IsMiningOrdered(target[dm], dm)} hops={Hops(sphere, home, target[dm])} land={land.Count} minedKg={sim.Deposits.MinedKg(target[dm], dm):0} grade={sim.Deposits.GradeOf(target[dm], dm):0.00} logistics={sim.LogisticsEnabled} chargeW={sim.RobotChargingW:0} gen={sim.GenerationW:0} dem={sim.DemandW:0} hour={hour % 24} lastW={string.Join(",", sim.Workforce.Robots.Select(q => q.LastChargeW.ToString("0")))} robots={string.Join(",", sim.Workforce.Robots.Select(r => r.Tile + ":" + (r.Task.HasValue ? r.Task.Value.Kind.ToString() : "idle") + "/" + r.Activity + "/" + r.Battery.UsableWh.ToString("0") + "Wh/cond" + r.Condition.ToString("0.00") + "/sd" + r.IsShutDown))}");
                int day = hour / 24;
                if (day != last && (day % 10 == 0 || (full && day < 8)))
                {
                    last = day;
                    log.AppendLine($"d{day,3} M={sim.MilestonesReached} demand={sim.DemandW / 1e6,6:0.00}MW gen={sim.GenerationW / 1e6,6:0.0}MW batt={sim.BatteryCapacityWh / 1e6,4:0}MWh " +
                                   $"robots={sim.Workforce.Robots.Count,2}(chg {sim.Workforce.Robots.Count(r => r.Activity == RobotActivity.Charging)}, {sim.Workforce.Robots.Sum(r => r.Battery.UsableWh) / 1000.0:0.0}kWh) q={sim.ConstructionQueueCount,2} chargers+{chargerHops} " +
                                   $"rock={sim.Materials[Material.Rock] / 1e3:0}t fe={sim.Materials[Material.Iron] / 1e3:0}t cu={sim.Materials[Material.Copper]:0} li={sim.Materials[Material.Lithium]:0} | robot: {robotNote}");
                }
            }
            double miningWh = sim.MiningWorkHours * RobotSpecs.MinePowerW + sim.ComminutionWh;
            double share = generatedWh > 0.0 ? miningWh / generatedWh : 0.0;
            string verdict = (share > 0.5 ? " ABSURD: mining takes over half of generation." : "") + (firstShortDay >= 0 ? $" ABSURD: ran out of iron or rock on day {firstShortDay} before milestone 2." : "");
            string summary = $"{name} (longest continuous travel {longestTravel:0} h): {days} d, milestones {sim.MilestonesReached} (M1 day {(milestone1Day < 0 ? "never" : milestone1Day.ToString())}, M2 day {(milestone2Day < 0 ? "never" : milestone2Day.ToString())}), deposits used up {exhausted} [{string.Join(", ", exhaustedWhat)}], rock moved {sim.RockMovedKg / 1000.0:0} t, tailings {sim.TailingsKg / 1000.0:0} t, mining energy {miningWh / 1e6:0.0} MWh (crushing {sim.ComminutionWh / 1e6:0.0}) of {generatedWh / 1e6:0} MWh generated = {share * 100.0:0.0} %.{verdict}";
            foreach (int ot in new HashSet<int>(sim.Workforce.Tasks.Open.Where(q => q.Task.Kind == TaskKind.Build).Select(q => q.Task.Tile))) { string txt = sim.OutpostText(ot); if (txt.Length > 0) summary += $" Outpost {ot}: {txt}."; }
            if (outpostNotes.Count > 0) summary += " Outpost candidates tried: " + string.Join(" | ", outpostNotes.Take(4)) + ".";
            var blockers = sim.Workforce.Tasks.Open.Where(q => q.State == TaskState.AssistanceNeeded || q.State == TaskState.Blocked).Take(4).Select(q => $"{q.Task.Kind}@{q.Task.Tile}: {q.Reason}");
            var deaths = sim.OperationsLog.Where(o => o.Quantity == "Robot destroyed").Take(2).Select(o => o.Text);
            if (lostNotes.Count > 0) summary += " LAST SEEN BEFORE LOSS: " + string.Join(" || ", lostNotes.Take(3)) + ".";
            if (deaths.Any()) summary += " Deaths: " + string.Join(" | ", deaths) + ".";
            summary += " First blockers: " + (blockers.Any() ? string.Join(" | ", blockers) : "none") + $". Final state: demand {sim.DemandW / 1e6:0.0} MW (target {sim.DemandTargetW / 1e6:0.0}), generation now {sim.GenerationW / 1e6:0.0} MW, day-average power {sim.DayAveragePowerW / 1e6:0.00} MW (M1 needs {Kardashev.PowerForMilestone(1, sim.TypeITargetW) / 1e6:0.0} MW, M2 {Kardashev.PowerForMilestone(2, sim.TypeITargetW) / 1e6:0.0} MW), battery {sim.BatteryCapacityWh / 1e6:0} MWh, robots {sim.Workforce.Robots.Count}, stock Fe {sim.Materials[Material.Iron] / 1e3:0.0} t Cu {sim.Materials[Material.Copper]:0} kg Li {sim.Materials[Material.Lithium]:0} kg rock {sim.Materials[Material.Rock] / 1e3:0} t, construction queue {sim.ConstructionQueueCount}, shortfall {string.Join(", ", Materials.All.Where(m => sim.ConstructionShortfall[m] > 0.01).Select(m => m + " " + sim.ConstructionShortfall[m].ToString("0")))}.";
            return (full ? log + "\n" : "") + summary;
        }

        /// <summary>True when the tile is on the lander grid or an island with a source; otherwise lays the next power line toward the grid (one per call) and returns false.</summary>
        static bool Powered(PlanetSimulation sim, HexSphere sphere, int tile)
        {
            if (sim.IsPowered(tile) || sim.IslandHasSource(tile)) return true;
            var seen = new HashSet<int> { tile };
            var prev = new Dictionary<int, int>();
            var queue = new Queue<int>(); queue.Enqueue(tile);
            int goal = -1;
            while (queue.Count > 0 && goal < 0)
            {
                int a = queue.Dequeue();
                foreach (int nb in sphere.Neighbors[a])
                    if (!sim.GetTile(nb).IsWater && seen.Add(nb)) { prev[nb] = a; if (sim.IsPowered(nb)) { goal = nb; break; } queue.Enqueue(nb); }
            }
            if (goal < 0) return false;
            int link = goal; int cur = prev[goal];
            while (true)
            {
                bool hasLine = Enumerable.Range(0, sim.SlotCount(cur)).Any(sl => sim.GetBuilding(cur, sl) == BuildingType.PowerLine);
                if (!hasLine)
                {
                    for (int sl = 0; sl < sim.SlotCount(cur); sl++) if (sim.PlacePowerLine(cur, sl, link)) return false;
                    return false;
                }
                if (cur == tile) return sim.IsPowered(tile);
                link = cur; cur = prev[cur];
            }
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

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
        }

        /// <summary>Milestone 2 on Viridia over five game years (rule: the budget for the second milestone), with panel fields laid out by cable where copper allows.</summary>
        [Test, Explicit("Balance tool: five game years on the Earth-like world, for milestone 2")]
        public void FiveYearsToMilestoneTwo()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            Assert.Fail("\n" + Run("Viridia, 5 years", new PlanetSolarParams(lum, au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 1826, false));
        }

        [Test, Explicit("Balance tool: the Earth-like colony for 140 days, to see the high-voltage trunk order get built")]
        public void ViridiaHvShort()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            Assert.Fail("\n" + Run("Viridia", new PlanetSolarParams(lum, au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 140, false));
        }

        [Test, Explicit("Balance tool: 60 days of the Earth-like colony with the diagnostics")]
        public void ViridiaShort()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            Assert.Fail("\n" + Run("Viridia", new PlanetSolarParams(lum, au, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0), 400, false));
        }

        [Test, Explicit("Balance tool: trace of robots 2 and 3 on the Mars-like world for 12 days")]
        public void MarsDeathTrace()
        {
            double au = SolarConstants.AstronomicalUnit, lum = SolarConstants.SolarLuminosity;
            Assert.Fail("\n" + Run("Mars-trace", new PlanetSolarParams(lum, 1.524 * au, 0.0934, 25.19 * Math.PI / 180.0, 336.0 * Math.PI / 180.0, 24.66, 686.98 * 24.0, 0.25, 0.3, -60.0), 12, false));
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
            var stuckNotes = new List<string>(); var travelHours = new Dictionary<int, double>(); double longestTravel = 0.0;
            int hvPlaced = 0; double peakSolar = 0.0; string peakNote = ""; int accN = 0; double accBase = 0, accTop = 0, accDock = 0, accPri = 0, accServed = 0, accChg = 0, accGen = 0, accCurt = 0, accDemand = 0, accKard = 0, accBattChg = 0, accRobot = 0, accShop = 0; var dawnSoc = new List<double>(); bool wasDay = false;
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
                if (full || name.StartsWith("Mars-trace")) foreach (var dq in sim.Workforce.Robots.Where(q => (q.Id == 2 || q.Id == 3) && sim.TimeHours > 120.0 && sim.TimeHours < 270.0))
                    stuckNotes.Add($"TR t={sim.TimeHours:0} R{dq.Id} tile={dq.Tile} task={(dq.Task.HasValue ? dq.Task.Value.Kind + "@" + dq.Task.Value.Tile : "none")} act={dq.Activity} route={dq.Route.Count} wh={dq.Battery.UsableWh:0} cap={dq.Battery.CapacityWh * dq.Battery.ColdFactor:0} body={dq.BodyTempC:0} heater={dq.HeaterW:0} air={sim.LiveAirTemperatureAt(dq.Tile):0} sheltered={dq.Sheltered} cargo={sim.Workforce.Tasks.Open.Count(o => o.Task.Kind == TaskKind.Haul && o.RobotId == dq.Id)}");
                int robotsBefore = sim.Workforce.Robots.Count;
                sim.Tick(3.0);
                if (full && sim.TimeHours > 600.0 && sim.TimeHours < 720.0) foreach (var q in sim.Workforce.Robots.Where(q => q.Task.HasValue && q.Task.Value.Kind == TaskKind.Mine && q.Task.Value.Tile == 484)) log.AppendLine($"TRACE t={sim.TimeHours:0} R{q.Id} tile={q.Tile} trav={q.IsTravelling} act={q.Activity} soc={q.Battery.StateOfCharge:0.00} tripCharge={q.TripCharge:0.00} need={q.NeedsCharge} {sim.Workforce.RangeReport(q, 484)}");
                if (sim.Workforce.Robots.Count < robotsBefore)
                    foreach (var id in snap.Keys.Where(k => !sim.Workforce.Robots.Any(q => q.Id == k))) lostNotes.Add(snap[id]);
                generatedWh += sim.GenerationW * 3.0;
                if (hour >= 24 * (days - 30))
                {
                    accN++; accGen += sim.GenerationW; accCurt += sim.CurtailedW; accDemand += sim.DemandW; accKard += sim.KardashevPowerW; accBattChg += Math.Max(0.0, sim.BatteryPowerW); accRobot += sim.RobotChargingW; accShop += sim.WorkshopPowerW; var gl = sim.Grid.Last; accBase += gl.BaselineServedW; accTop += gl.TopUpServedW; accDock += gl.DockServedW; accPri += gl.PriorityServedW; accServed += gl.ServedW; accChg += gl.ChargeW;
                    if (sim.IsDayAtBase && !wasDay) { dawnSoc.Add(sim.BatteryStoredWh / Math.Max(1.0, sim.BatteryCapacityWh)); }
                    wasDay = sim.IsDayAtBase;
                    if (gl.SolarW > peakSolar)
                    {
                        peakSolar = gl.SolarW;
                        int all = 0, on = 0; var perTile = new List<string>();
                        for (int q = 0; q < sim.TileCount; q++) { int n = sim.PanelCount(q); if (n <= 0) continue; all += n; bool pw = sim.IsPowered(q); if (pw) on += n; perTile.Add($"{q}:{n}{(pw ? "" : "(off)")}"); }
                        double one = sim.DescribeSky(sim.BaseTileId, 0.35, Math.PI).PanelPowerW;
                        peakNote = $"peak solar {gl.SolarW / 1e6:0.0} MW at day {sim.TimeHours / 24.0:0}: {all} panels ({on} on powered tiles); a new panel at the base would make {one:0} W, so {on} such panels = {on * one / 1e6:0.0} MW expected before wiring; delivered to the grid {gl.SolarW / 1e6:0.0} MW (line loss {sim.TransmissionLossW / 1e6:0.00} MW, lines too thin {sim.OverloadLossW / 1e6:0.00} MW, wind {sim.WindPowerW() / 1e6:0.00} MW, RTG {gl.RtgW / 1e3:0} kW); panels per tile {string.Join(" ", perTile)}";
                    }
                }
                foreach (var tq in sim.Workforce.Robots) { travelHours[tq.Id] = tq.IsTravelling ? (travelHours.TryGetValue(tq.Id, out double th) ? th : 0.0) + 3.0 : 0.0; longestTravel = Math.Max(longestTravel, travelHours[tq.Id]); if (travelHours[tq.Id] == 192.0 && Workforce.DebugWatch < 0) { Workforce.DebugWatch = tq.Id; Workforce.DebugTrace = e => { if (stuckNotes.Count < 40) stuckNotes.Add("EV t=" + sim.TimeHours.ToString("0.0") + " " + e); }; }
                    if (travelHours[tq.Id] == 201.0) stuckNotes.Add($"STUCK R{tq.Id} task={(tq.Task.HasValue ? tq.Task.Value.Kind + "@" + tq.Task.Value.Tile : "none")} tile={tq.Tile} act={tq.Activity} soc={tq.Battery.StateOfCharge:0.00} wh={tq.Battery.UsableWh:0} shut={tq.IsShutDown} stranded={tq.IsStranded} routeLeft={tq.Route.Count} hop={tq.HopProgress:0.00} manual={tq.ManualTarget} fac={(tq.Facility.HasValue ? tq.Facility.Value.Tile : -1)} reserve={tq.ReserveFraction:0.00} trip={tq.TripCharge:0.00} t={sim.TimeHours:0}");
                    if (travelHours[tq.Id] >= 204.0 && travelHours[tq.Id] <= 222.0 && stuckNotes.Count < 12) stuckNotes.Add($"TRACE R{tq.Id} t={sim.TimeHours:0} tile={tq.Tile} next={tq.NextTile} hop={tq.HopProgress:0.000} hopHours={(tq.NextTile.HasValue ? sim.Routes.HopHoursAt(tq.NextTile.Value) : 0.0):0.0} wh={tq.Battery.UsableWh:0} act={tq.Activity} task={(tq.Task.HasValue ? tq.Task.Value.Kind.ToString() : "none")} manual={tq.ManualTarget} paused={tq.StormHold} sheltered={tq.Sheltered} body={tq.BodyTempC:0} lastW={tq.LastChargeW:0}");
                    if (travelHours[tq.Id] == 33.0) lostNotes.Add($"LONGTRAVEL R{tq.Id} task={(tq.Task.HasValue ? tq.Task.Value.Kind + "@" + tq.Task.Value.Tile : "none")} tile={tq.Tile} act={tq.Activity} soc={tq.Battery.StateOfCharge:0.00} manual={tq.ManualTarget} fac={(tq.Facility.HasValue ? tq.Facility.Value.Tile : -1)} t={sim.TimeHours:0}"); }
                if (milestone1Day < 0 && sim.MilestonesReached >= 1) milestone1Day = hour / 24;
                if (milestone2Day < 0 && sim.MilestonesReached >= 2) milestone2Day = hour / 24;
                if (firstShortDay < 0 && milestone2Day < 0 && hour > 24 * 20 && (sim.Materials[Material.Iron] < 1.0 || sim.Materials[Material.Rock] < 1.0)) firstShortDay = hour / 24;
                var inRange = land.Where(t => sim.WorkReachProblem(t) == null).ToList();
                foreach (var ub in sim.UnwiredBuildings().ToList()) sim.PlaceBuildingWire(ub.Tile, ub.Slot);   // nothing is wired for the player (EN-22)
                bool kitPending = sim.ConstructionQueueCount > 0 || chargerTiles.Any(ct => Enumerable.Range(0, sim.SlotCount(ct)).Any(sl => sim.GetBuilding(ct, sl) != BuildingType.None && sim.IsUnderConstruction(ct, sl)));
                foreach (Material hm in new[] { Material.Iron, Material.Copper, Material.Lithium }) sim.SetStockTarget(home, hm, (hm == Material.Iron ? 20_000.0 : 1_500.0));   // targets are buffers: they never keep stock back from a waiting site   // the base keeps nothing back while an outpost kit is being hauled out   // the game's advice: keep a stock at the base; hauls bring the ore

                // from milestone 1 the trunk lines that are too thin become high-voltage lines, the most loaded first (the Guide's advice for the High-voltage line)
                if (UpgradeTrunk && sim.MilestonesReached >= 1 && hour % 24 == 0 && sim.WireLinks().Count(q => q.HighVoltage && !q.Built) < 2) hvPlaced += UpgradeThinLines(sim, home, 2);
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
                    bool wanted = sim.Materials[m] < enough && (tile <= 0 || tile == home || sim.KgAt(tile, m) < 2000.0);   // stop mining what is piling up at the mine faster than hauls bring it home
                    if (!wanted && sim.IsMiningOrdered(tile, m)) sim.CancelMining(tile, m);
                    else if (wanted && sim.WorkReachProblem(tile) == null && !sim.IsMiningOrdered(tile, m) && sim.MiningWarning(tile, m) == null) sim.OrderMining(tile, m);   // the bot never orders ore it cannot haul home
                }

                // forward chargers where the task list says "too far": a charger two tiles toward the ore from the nearest charger (the game's own advice)
                foreach (Material m in new[] { Material.Copper, Material.Lithium })
                {
                    int tile = target[m];
                    var rec = sim.Workforce.Tasks.Open.FirstOrDefault(q => q.Task.Kind == TaskKind.Mine && q.Task.Tile == tile && q.Task.Slot == (int)m);
                    bool stuck = (rec != null && (rec.Reason.StartsWith("too far") || (hour > 24 * 10 && sim.Deposits.MinedKg(tile, m) < 1.0))) || (!sim.IsMiningOrdered(tile, m) && tile > 0 && sim.MiningWarning(tile, m) != null);   // no haul route home: extend the charger chain toward the ore   // too far, or ordered for days and nothing mined
                    if (!stuck || sim.ConstructionQueueCount > 6) continue;
                    var seeds = new List<int> { home }; seeds.AddRange(chargerTiles);
                    int from = seeds.OrderBy(t => Hops(sphere, t, tile)).First();
                    // a leapfrog outpost: panels, a charger and a battery on a tile up to three tiles toward the ore; the game decides whether a robot may go one way
                    var candidates = land.Where(t => Hops(sphere, from, t) <= 3 && !chargerTiles.Contains(t) && t != home && !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && Hops(sphere, t, tile) < Hops(sphere, from, tile)).OrderBy(t => Hops(sphere, t, tile)).ThenBy(t => t).Take(14).ToList();
                    // one outpost at a time, two when copper is the blocker (the copper mine is still out of reach): leapfrog on both sides of the route toward the ore
                    int building = chargerTiles.Count(ct => Enumerable.Range(0, sim.SlotCount(ct)).Any(sl => sim.GetBuilding(ct, sl) != BuildingType.None && sim.IsUnderConstruction(ct, sl)));
                    bool busy = building >= (m == Material.Copper && sim.Materials[Material.Copper] < 200.0 ? 2 : 1);
                    foreach (int step in busy || hour % 24 != 0 ? new List<int>() : candidates)
                    {
                        // order the outpost (sites, wiring, kit hauls); the game says whether the builder may go one way; if not, take the order back and try a nearer tile
                        string err = sim.OrderOutpost(step, planet.AirTemperatureC < 0.0 ? 1 : 2);
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
                // payload rule ([rule] TBD): while the best load per trip from an ore tile is under 100 kg, split the longest charger-to-charger leg of its haul route with an outpost
                if (hour % 24 == 0 && sim.ConstructionQueueCount <= 6)
                    foreach (Material pm in new[] { Material.Copper, Material.Lithium })
                    {
                        if (!target.TryGetValue(pm, out int pt) || pt <= 0 || pt == home) continue;
                        double payload = sim.FeasiblePayloadKg(pt, home, PlanetSimulation.BasePayloadKg);
                        if (payload <= 0.0 || payload >= 100.0) continue;
                        var pplan = sim.PlanHaul(pt, home, payload);
                        if (!pplan.Feasible) continue;
                        var (la, lb, _) = sim.LongestLeg(pt, home, pplan.Stops, payload);
                        var route = sim.Routes.Route(la, lb);
                        if (route == null || route.Count < 2) continue;
                        bool kitPendingNow = chargerTiles.Any(ct => Enumerable.Range(0, sim.SlotCount(ct)).Any(sl => sim.GetBuilding(ct, sl) != BuildingType.None && sim.IsUnderConstruction(ct, sl)));
                        if (kitPendingNow) continue;
                        foreach (int mid in new[] { route[route.Count / 2 - 1 < 0 ? 0 : route.Count / 2 - 1], route[Math.Min(route.Count - 1, route.Count / 2)] })
                        {
                            if (mid == home || chargerTiles.Contains(mid) || sim.GetTile(mid).IsWater || sim.IsPowered(mid)) continue;
                            if (sim.OrderOutpost(mid, planet.AirTemperatureC < 0.0 ? 1 : 2) == null) { chargerTiles.Add(mid); outpostNotes.Add($"payload {payload:0} kg for {pm}: outpost at tile {mid} splits the leg {la}-{lb}"); break; }
                        }
                    }
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
                bool copperComing = target.TryGetValue(Material.Copper, out int cuTileEnd) && sim.Deposits.MinedKg(cuTileEnd, Material.Copper) > 100.0;
                // copper bootstrap: while the copper deposit is not on the haul chain and what copper there is (the aluminium stand-in counts, 0.494 kg per kg) cannot cover the next outpost on the way, home panels wait:
                // the outposts toward the deposit come first
                bool copperHold = target.TryGetValue(Material.Copper, out int cuT0) && cuT0 > 0 && sim.MiningWarning(cuT0, Material.Copper) != null
                                  && sim.Materials[Material.Copper] + sim.Materials[Material.Aluminium] / KardashevAscent.Simulation.Resources.StandIns.AluminiumPerCopperKg < 130.0;
                if (!saveForRobot && !copperHold && (copperComing || sim.Materials[Material.Copper] > 150.0) && sim.ConstructionQueueCount < 3 + sim.Workforce.Robots.Count)
                {
                    int slots = 0;
                    if (sim.GenerationW < 2.0 * Math.Max(sim.DemandW, 2e5))
                        foreach (int t in new[] { home }.Concat(inRange.Take(12)).Where(q => sim.IsPowered(q)))
                        {
                            for (int s = 0; s < sim.SlotCount(t) && slots < 1; s++)
                                if (sim.CanPlace(BuildingType.SolarPanel, t, s) && sim.PlacePanel(t, s, 0.35, Math.PI)) slots++;
                            if (slots > 0) break;
                        }
                    // no room left on the powered tiles: lay a line toward the nearest open tile (112 kg of copper a tile) so the next panels can stand there
                    if (slots == 0 && sim.GenerationW < 2.0 * Math.Max(sim.DemandW, 2e5))
                    {
                        int farm = land.Where(q => !sim.IsPowered(q) && !chargerTiles.Contains(q) && !sim.GetTile(q).IsWater && sim.IsOpenGround(q)).OrderBy(q => Hops(sphere, home, q)).ThenBy(q => q).FirstOrDefault();
                        if (farm > 0) Powered(sim, sphere, farm);
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
                    foreach (var hq in sim.HaulOrders().Take(1)) log.AppendLine($"HAULDBG kg={hq.Kg:0} planFeasible={sim.PlanHaul(hq.From, hq.To, hq.Kg).Feasible} payloadAtFull={sim.FeasiblePayloadKg(hq.From, hq.To, 490.0):0.0} cost={sim.HaulCostText(hq.From, hq.To, hq.Kg)} legBudget={sim.LegBudgetWh:0}");
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
            {
                var stuck = new List<string>();
                for (int qt = 0; qt < sim.TileCount; qt++) for (int qs = 0; qs < sim.SlotCount(qt); qs++) if (sim.GetBuilding(qt, qs) != BuildingType.None && sim.IsUnderConstruction(qt, qs)) stuck.Add($"{sim.GetBuilding(qt, qs)}@{qt}.{qs} {sim.ConstructionProgress(qt, qs) * 100.0:0}%");
                int panelCount = 0, powered = sim.PoweredTileCount;
                for (int qt = 0; qt < sim.TileCount; qt++) for (int qs = 0; qs < sim.SlotCount(qt); qs++) if (sim.GetBuilding(qt, qs) == BuildingType.SolarPanel && !sim.IsUnderConstruction(qt, qs)) panelCount++;
                summary += " Build tasks: " + string.Join(" | ", sim.Workforce.Tasks.Open.Where(q => q.Task.Kind == TaskKind.Build).Take(4).Select(q => $"@{q.Task.Tile} {q.State} {q.Reason}")) + ". Hauls: " + string.Join(" | ", sim.HaulOrders().Take(4).Select(h => $"{h.Material} {h.Kg:0} {h.From}>{h.To} {(sim.HaulBlockedReason(h.Id) ?? "ok")}")) + ". Stock at 472: Cu " + sim.KgAt(472, Material.Copper).ToString("0") + " Fe " + sim.KgAt(472, Material.Iron).ToString("0") + ".";
                foreach (int ht in Enumerable.Range(0, sim.TileCount))
                    for (int hs = 0; hs < sim.SlotCount(ht); hs++)
                        if (sim.GetBuilding(ht, hs) == BuildingType.HighVoltageLine && sim.IsUnderConstruction(ht, hs))
                        {
                            var rec = sim.Workforce.Tasks.Open.FirstOrDefault(q => q.Task.Kind == TaskKind.Build && q.Task.Tile == ht);
                            summary += $" HV SITE @{ht}.{hs} progress {sim.ConstructionProgress(ht, hs):0.00}, task {(rec == null ? "NONE open" : rec.State + " " + rec.Reason)}, reach problem '{sim.WorkReachProblem(ht)}', copper {sim.Materials[Material.Copper]:0} kg, aluminium {sim.Materials[Material.Aluminium]:0} kg, ALL HAULS: {string.Join(" | ", sim.HaulOrders().Select(o => $"#{o.Id} {o.Material} {o.Kg:0}kg {o.From}>{o.To} {(o.Loaded ? "loaded" : "waiting")} R{o.RobotId}"))}; ROBOTS: {string.Join(" | ", sim.Workforce.Robots.Select(q => $"R{q.Id} {q.Kind} tile {q.Tile} {(q.Task.HasValue ? q.Task.Value.Kind + "@" + q.Task.Value.Tile : "no task")} {q.Activity}"))}; payload base->site {sim.FeasiblePayloadKg(sim.BaseTileId, ht, 490.0):0} kg, mine 496->site {sim.FeasiblePayloadKg(496, ht, 490.0):0} kg; MINE->SITE: {sim.HaulCostText(496, ht, 44.0)}; copper at 496 {sim.KgAt(496, Material.Copper):0} kg, rock at 496 {sim.KgAt(496, Material.Rock):0} kg; BASE->SITE: {sim.HaulCostText(sim.BaseTileId, ht, 44.0)}; sites under construction at the base tile: {string.Join(" ", Enumerable.Range(0, sim.SlotCount(sim.BaseTileId)).Where(q => sim.GetBuilding(sim.BaseTileId, q) != BuildingType.None && sim.IsUnderConstruction(sim.BaseTileId, q)).Select(q => sim.GetBuilding(sim.BaseTileId, q) + "." + q))}; copper held for hauls out of the base {sim.HaulOrders().Where(o => o.From == sim.BaseTileId && !o.Loaded && o.Material == Material.Copper).Sum(o => o.Kg):0} kg; room for copper at the site tile {sim.RoomAt(ht, Material.Copper):0} kg, yards there {sim.StorageCount(ht, BuildingType.StorageYard)}; stock at the site tile: Cu {sim.KgAt(ht, Material.Copper):0} Fe {sim.KgAt(ht, Material.Iron):0} Rock {sim.KgAt(ht, Material.Rock):0}, at base {sim.BaseTileId}: Cu {sim.KgAt(sim.BaseTileId, Material.Copper):0} Fe {sim.KgAt(sim.BaseTileId, Material.Iron):0}; bill {sim.BillToBuild(BuildingType.HighVoltageLine)}; all open build tasks: {string.Join(" | ", sim.Workforce.Tasks.Open.Where(q => q.Task.Kind == TaskKind.Build).Take(8).Select(q => "@" + q.Task.Tile + "." + q.Task.Slot + " " + q.State + " " + q.Reason))}.";
                        }
                summary += $" Panels built {panelCount}, powered tiles {powered}, sites waiting: {(stuck.Count == 0 ? "none" : string.Join(", ", stuck.Take(6)))}.";
            }
            if (stuckNotes.Count > 0) summary += " " + string.Join(" || ", stuckNotes.Take(200));
            summary += " Ore ledger (crushing kWh | mined kg | left at the mine tile | at base): " + string.Join("; ", sim.ComminutionWhByOre.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key.Material}@{kv.Key.Tile} {kv.Value / 1000.0:0} | {sim.MinedByOreTile.GetValueOrDefault(kv.Key):0} | {sim.KgAt(kv.Key.Tile, kv.Key.Material):0} | {sim.KgAt(home, kv.Key.Material):0}")) + ".";
            if (target.TryGetValue(Material.Copper, out int cuTile) && cuTile > 0) summary += $" Copper tile {cuTile}: payload per trip {sim.FeasiblePayloadKg(cuTile, home, PlanetSimulation.BasePayloadKg):0} kg, mined {sim.Deposits.MinedKg(cuTile, Material.Copper) / days:0.0} kg a day on average, {sim.HaulTotals.KgMoved / 1000.0:0} t hauled in all, {sim.HaulTotals.Wh / 1e6:0.0} MWh spent hauling.";
            { int hv = sim.WireLinks().Count(q => q.HighVoltage && q.Built); summary += $" HV TRUNK: {hvPlaced} ordered ({(hvProblem.Length > 0 ? "last refusal: " + hvProblem : "none refused")}; thin tile {sim.ThinLinesTile}, thin share {sim.ThinLinesShare:0.00}), {hv} built (each one tile of conductor: {KardashevAscent.Simulation.Resources.BuildCosts.CableCopperKg:0} kg copper or {KardashevAscent.Simulation.Resources.BuildCosts.CableCopperKg * KardashevAscent.Simulation.Resources.StandIns.AluminiumPerCopperKg:0} kg aluminium, plus iron and rock); copper in stock {sim.Materials[Material.Copper]:0} kg, aluminium {sim.Materials[Material.Aluminium]:0} kg."; }
            if (peakNote.Length > 0) summary += " GENERATION CHECK: " + peakNote + ".";
            if (accN > 0) summary += $" GRID SPLIT (MW): served colony {accServed / accN / 1e6:0.0} of which baseline {accBase / accN / 1e6:0.0}; dock/critical {accDock / accN / 1e6:0.00}; priority {accPri / accN / 1e6:0.00}; top-up {accTop / accN / 1e6:0.00}; battery charge {accChg / accN / 1e6:0.0}."
                + $" DEMAND TABLE (last 30 days, means, MW): generation {accGen / accN / 1e6:0.0}, curtailed {accCurt / accN / 1e6:0.0}, colony demand {accDemand / accN / 1e6:0.0} (baseline {sim.BaseDemandW / 1e6:0.00} now; target {sim.DemandTargetW / 1e6:0.0}), battery charging {accBattChg / accN / 1e6:0.0}, robot charging {accRobot / accN / 1e6:0.00}, workshop+refinery+crushing {accShop / accN / 1e6:0.00}, counted as used by the milestone {accKard / accN / 1e6:0.0}; battery at dawn {(dawnSoc.Count > 0 ? dawnSoc.Average() * 100.0 : double.NaN):0} % of {sim.BatteryCapacityWh / 1e6:0} MWh.";
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

        /// <summary>Set false to run the bot without the trunk upgrade (the old behaviour).</summary>
    public static bool UpgradeTrunk = true;
    static string hvProblem = "";

    /// <summary>Put a high-voltage line next to each overloaded plain line on the way to the lander, most loaded first. Returns how many were ordered.</summary>
    internal static int UpgradeThinLines(PlanetSimulation sim, int home, int most)
    {
        var links = sim.WireLinks().ToList();
        var dist = new Dictionary<int, int> { [home] = 0 };
        var queue = new Queue<int>(); queue.Enqueue(home);
        while (queue.Count > 0)
        {
            int a = queue.Dequeue();
            foreach (var l in links)
            {
                int b = l.From == a ? l.To : l.To == a ? l.From : -1;
                if (b >= 0 && !dist.ContainsKey(b)) { dist[b] = dist[a] + 1; queue.Enqueue(b); }
            }
        }
        int ordered = 0;
        foreach (int t in dist.Keys.Where(q => sim.IsLineOverloaded(q) || (q == sim.ThinLinesTile && sim.ThinLinesShare > 0.02)).OrderByDescending(q => q == sim.ThinLinesTile ? double.MaxValue : sim.LineFlowW(q)).ToList())
        {
            foreach (var l in links.Where(q => !q.HighVoltage && q.Built && (q.From == t || q.To == t)))
            {
                int u = l.From == t ? l.To : l.From;
                if (!dist.ContainsKey(u)) continue;   // the flagged tile may be either end of the thin line (at the lander it is the child side)
                if (links.Any(q => q.HighVoltage && ((q.From == t && q.To == u) || (q.From == u && q.To == t)))) continue;
                if (sim.PlaceWire(t, u, true)) { ordered++; if (ordered >= most) break; continue; }
                hvProblem = sim.WireProblem;
            }
            if (ordered >= most) break;
        }
        return ordered;
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

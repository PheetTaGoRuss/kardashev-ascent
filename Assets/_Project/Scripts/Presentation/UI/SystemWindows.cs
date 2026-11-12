using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>Manufacturing (U): Workshops, the job queue, new robots and refining. Moved out of the robots window.</summary>
    public sealed class ManufacturingWindow : SystemWindow
    {
        public override string Id => "manufacturing";
        public override string Title => "Manufacturing";
        public override KeyCode Key => KeyCode.U;
        public override int Order => 30;

        static readonly RobotKind[] Buildable =
            { RobotKind.General, RobotKind.Construction, RobotKind.Mining, RobotKind.Research, RobotKind.Maintenance };

        Label status, stock, message, reports;
        VisualElement jobs, orders;
        readonly List<Label> jobLabels = new List<Label>();
        string shownShape;

        protected override void BuildBody()
        {
            status = Text();
            Heading("MATERIALS");
            stock = Text();
            Heading("JOBS");
            jobs = new VisualElement();
            Body.Add(jobs);
            Heading("ORDER");
            orders = new VisualElement();
            Body.Add(orders);
            message = Text("caption");
            message.AddToClassList("message");
            Heading("REPORTS");
            reports = Text();
            Text("caption").text = "Each Workshop works on one job at a time, in queue order. Jobs use materials from mining " +
                                   "(Robots window, or Mine in the tile panel) and build parts. Recipes, times and unlock milestones " +
                                   "are placeholder values.";
        }

        public override string LockedReason(IPlanetSimulation sim) =>
            sim != null && sim.WorkshopCount == 0 ? "build a Robot Workshop on a site first (build bar → Workshop)." : null;

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            status.text = $"{sim.WorkshopCount} Workshop{(sim.WorkshopCount == 1 ? "" : "s")} · milestone {sim.MilestonesReached} · " +
                          $"build parts {Format.Energy(sim.BuildStockWh)}";
            stock.text = Stock(sim);
            string shape = sim.MilestonesReached + "|" + sim.WorkshopCount + "|" + sim.Jobs.Count;
            if (shape != shownShape)
            {
                Rebuild(sim);
                shownShape = shape;
            }
            for (int i = 0; i < jobLabels.Count && i < sim.Jobs.Count; i++)
                jobLabels[i].text = JobText(sim, i);
            reports.text = Reports(sim);
        }

        /// <summary>The last few industry reports, newest first, each with the day it was made.</summary>
        static string Reports(IPlanetSimulation sim)
        {
            var list = sim.IndustrialReports;
            if (list.Count == 0)
                return "No reports yet.";
            string s = "";
            for (int i = list.Count - 1; i >= 0 && i >= list.Count - 4; i--)
                s += (s.Length > 0 ? "\n" : "") + $"Day {(int)(list[i].TimeHours / 24.0) + 1} · {list[i].Message}";
            return s;
        }

        static string Stock(IPlanetSimulation sim)
        {
            string s = "";
            foreach (Material m in Materials.All)
                if (sim.Materials[m] > 0.0)
                    s += (s.Length > 0 ? ", " : "") + $"{Materials.Name(m).ToLowerInvariant()} {sim.Materials[m]:0} kg";
            return s.Length > 0 ? s : "none yet (survey and mine from the tile panel)";
        }

        static string JobText(IPlanetSimulation sim, int i)
        {
            ManufacturingJob j = sim.Jobs[i];
            string where = i < sim.WorkshopCount ? $"{j.Progress * 100.0:0} % of {j.Recipe.WorkHours:0} h of work" : "waiting for a free Workshop";
            return $"{i + 1}. {j.Describe()} · {where}";
        }

        void Rebuild(IPlanetSimulation sim)
        {
            jobs.Clear();
            jobLabels.Clear();
            if (sim.Jobs.Count == 0)
                jobs.Add(Label("No jobs queued.", "caption"));
            for (int i = 0; i < sim.Jobs.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("job");
                Label text = Label(JobText(sim, i), "job-text");
                jobLabels.Add(text);
                row.Add(text);
                row.Add(MakeButton("Cancel", () => { sim.CancelJob(index); Changed(sim); }));
                jobs.Add(row);
            }

            orders.Clear();
            var robots = new VisualElement();
            robots.AddToClassList("button-row");
            robots.Add(Label("New robot:", "priority-label"));
            foreach (RobotKind kind in Buildable)
            {
                RobotKind k = kind;
                int unlock = RobotTech.UnlockMilestone(kind);
                Button b = MakeButton(kind.ToString(), () => PlaceOrder(sim, sim.OrderRobot(k), $"{k} robot ordered."));
                b.EnableInClassList("locked", sim.MilestonesReached < unlock);
                b.tooltip = $"Iron 400 kg, rock 200 kg, copper (or aluminium) 40 kg, lithium 2 kg (or sodium 6 kg), 50 kWh of parts, 24 h of work. Unlocks at milestone {unlock}.";
                robots.Add(b);
            }
            orders.Add(robots);

            var refine = new VisualElement();
            refine.AddToClassList("button-row");
            refine.Add(Label("Refine from rock:", "priority-label"));
            foreach (Material m in new[] { Material.Aluminium, Material.Sodium })
            {
                Material material = m;
                Button b = MakeButton($"{RobotTech.RefineBatchKg(m):0} kg {Materials.Name(m).ToLowerInvariant()}",
                                      () => PlaceOrder(sim, sim.OrderRefine(material), $"{Materials.Name(material)} batch ordered."));
                b.EnableInClassList("locked", sim.MilestonesReached < RobotTech.UnlockMilestone(m));
                b.tooltip = Materials.Origin(m) + $" Unlocks at milestone {RobotTech.UnlockMilestone(m)}.";
                refine.Add(b);
            }
            orders.Add(refine);
        }

        void PlaceOrder(IPlanetSimulation sim, string why, string ok)
        {
            message.text = why ?? ok;
            Changed(sim);
        }

        void Changed(IPlanetSimulation sim)
        {
            sim.RefreshWork();
            shownShape = null;
        }
    }

    /// <summary>Power (O): what makes power now, where it goes, the battery and the grid's buildings.</summary>
    public sealed class PowerWindow : SystemWindow
    {
        public override string Id => "power";
        public override string Title => "Power";
        public override KeyCode Key => KeyCode.O;
        public override int Order => 10;

        Label now, sources, uses, battery, buildings;

        protected override void BuildBody()
        {
            now = Text();
            Heading("MADE");
            sources = Text();
            Heading("USED");
            uses = Text();
            Heading("BATTERY");
            battery = Text();
            Heading("POWER BUILDINGS");
            buildings = Text();
            Text("caption").text = "The week chart of power made and needed is in Overview → Power (C).";
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            double solar = sim.GenerationW - sim.RtgW;
            now.text = (sim.IsBrownout ? "BROWNOUT: demand is not met.\n" : "") +
                       $"Making {Format.Power(sim.GenerationW)} · needed {Format.Power(sim.DemandW)} " +
                       $"(colony target {Format.Power(sim.DemandTargetW)})";
            now.EnableInClassList("message", sim.IsBrownout);
            sources.text = $"Solar panels {Format.Power(System.Math.Max(0.0, solar))}\nRTGs (lander) {Format.Power(sim.RtgW)}\n" +
                           $"Wasted (nowhere to put it) {Format.Power(sim.CurtailedW)}";
            uses.text = $"Colony {Format.Power(sim.DemandW)}\nConstruction {Format.Power(sim.BuildPowerW)}\n" +
                        $"Robot charging {Format.Power(sim.RobotChargingW)}";
            double share = sim.BatteryCapacityWh > 0.0 ? sim.BatteryStoredWh / sim.BatteryCapacityWh : 0.0;
            string flow = sim.BatteryPowerW > 1.0 ? $"charging {Format.Power(sim.BatteryPowerW)}"
                        : sim.BatteryPowerW < -1.0 ? $"discharging {Format.Power(-sim.BatteryPowerW)}" : "idle";
            battery.text = $"{share * 100.0:0} % of {Format.Energy(sim.BatteryCapacityWh)} · {flow}";

            int panels = 0, yards = 0;
            for (int t = 0; t < sim.TileCount; t++)
            {
                panels += sim.PanelCount(t);
                yards += sim.BatteryCount(t);
            }
            buildings.text = $"{panels} solar panel{(panels == 1 ? "" : "s")} · {yards} battery yard{(yards == 1 ? "" : "s")}";
        }
    }

    /// <summary>
    /// Research (T): the solar parameters (docs/design/solar-measurements.md). For the selected tile (or the base),
    /// each parameter's status and how much its uncertainty can change a panel's yearly energy; order a robot to measure.
    /// </summary>
    public sealed class ResearchWindow : SystemWindow
    {
        public override string Id => "research";
        public override string Title => "Research";
        public override KeyCode Key => KeyCode.T;
        public override int Order => 40;

        Label where, total, puzzleHead, solvedText;
        VisualElement puzzles;
        int shownPuzzles = -1;
        VisualElement rows;
        readonly Dictionary<SolarParameter, Label> status = new Dictionary<SolarParameter, Label>();
        readonly Dictionary<SolarParameter, Button> buttons = new Dictionary<SolarParameter, Button>();
        List<ParameterEffect> effects;
        int effectsTile = -2;
        float nextEffects;
        KardashevAscent.Presentation.Planet.TilePicker picker;

        protected override void BuildBody()
        {
            puzzleHead = Heading("PUZZLES");
            puzzles = new VisualElement();
            Body.Add(puzzles);
            solvedText = Text();
            solvedText.AddToClassList("good");
            where = Text();
            total = Text();
            Heading("WHAT A PANEL HERE DEPENDS ON");
            rows = new VisualElement();
            Body.Add(rows);
            foreach (SolarParameter p in (SolarParameter[])System.Enum.GetValues(typeof(SolarParameter)))
            {
                SolarParameter param = p;
                var row = new VisualElement();
                row.AddToClassList("job");
                var text = new VisualElement();
                text.style.flexGrow = 1;
                text.style.flexShrink = 1;
                text.Add(Label(SolarUnknowns.Name(p), "robot-name"));
                Label st = Label("", "job-text");
                text.Add(st);
                row.Add(text);
                Button b = MakeButton("Measure", () => Toggle(param));
                row.Add(b);
                rows.Add(row);
                status[p] = st;
                buttons[p] = b;
            }
            Text("caption").text =
                "Spread = how far the yearly energy of a 30°-tilted panel facing the equator could be off while the value is " +
                "only an estimate (the model run at both ends of the range). A robot measures one value at a time; research " +
                "robots are twice as quick as General ones. Every value ends with a puzzle: work the value " +
                "out from the robot's readings (within 5 %: known; within 15 %: half the spread is left). Ground surveys " +
                "end here too: the robot's cores wait for your grade estimates. Ranges and hours are placeholder values.";
        }

        IPlanetSimulation lastSim;
        int Tile(IPlanetSimulation sim)
        {
            if (picker == null) picker = Object.FindFirstObjectByType<KardashevAscent.Presentation.Planet.TilePicker>();
            int t = picker != null ? picker.SelectedTile : -1;
            return t >= 0 && !sim.GetTile(t).IsWater ? t : sim.BaseTileId;
        }

        static bool HasLightPuzzle(IPlanetSimulation sim, SolarParameter p)
        {
            foreach (ParameterPuzzle q in sim.ParameterPuzzles)
                if (q.Parameter == p) return true;
            return false;
        }

        static bool HasPuzzle(IPlanetSimulation sim, int tile)
        {
            foreach (LatitudePuzzle q in sim.LatitudePuzzles)
                if (q.Tile == tile) return true;
            return false;
        }

        void Toggle(SolarParameter p)
        {
            if (lastSim == null) return;
            if (lastSim.IsMeasureOrdered(p)) lastSim.CancelMeasure(p);
            else lastSim.OrderMeasure(p, Tile(lastSim));
            effectsTile = -2;
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            lastSim = sim;
            int puzzleShape = sim.LatitudePuzzles.Count + sim.ParameterPuzzles.Count * 100 + sim.SurveyPuzzles.Count * 10000;
            if (puzzleShape != shownPuzzles)
            {
                shownPuzzles = puzzleShape;
                puzzles.Clear();
                foreach (LatitudePuzzle p in sim.LatitudePuzzles)
                    puzzles.Add(new LatitudePuzzleView(sim, p, result => { solvedText.text = result; effectsTile = -2; }));
                foreach (ParameterPuzzle p in sim.ParameterPuzzles)
                    puzzles.Add(new LightPuzzleView(sim, p, result => { solvedText.text = result; effectsTile = -2; }));
                foreach (SurveyPuzzle p in sim.SurveyPuzzles)
                    puzzles.Add(new SurveyPuzzleView(sim, p, result => { solvedText.text = result; effectsTile = -2; }));
                puzzleHead.style.display = shownPuzzles > 0 || solvedText.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            int tile = Tile(sim);
            double lat = sim.Sphere.Latitude(tile);
            if (tile != effectsTile || Time.unscaledTime >= nextEffects)
            {
                // A 30° panel facing the equator: a typical panel, so the spreads compare across parameters.
                effects = sim.ParameterEffects(tile, 30.0 * Mathf.Deg2Rad, lat >= 0.0 ? Mathf.PI : 0.0);
                effectsTile = tile;
                nextEffects = Time.unscaledTime + 3f;
            }
            where.text = $"Tile #{tile}{(tile == sim.BaseTileId ? " (base)" : "")} · select another tile on the map to see it there.";
            double baseWh = 0.0, sq = 0.0;
            foreach (ParameterEffect e in effects)
            {
                baseWh = System.Math.Max(baseWh, (e.LowWh + e.HighWh) / 2.0);
                sq += e.HalfRangeWh * e.HalfRangeWh;
            }
            double spread = System.Math.Sqrt(sq);
            total.text = baseWh > 0.0
                ? $"One panel here: about {Format.Energy(baseWh)} a year, ± {Format.Energy(spread)} ({spread / baseWh * 100.0:0.0} %) from what is still unknown."
                : "No sunlight model for this tile.";
            foreach (ParameterEffect e in effects)
            {
                SolarParameter p = e.Parameter;
                bool ordered = sim.IsMeasureOrdered(p);
                string share = baseWh > 0.0 ? $"± {e.HalfRangeWh / baseWh * 100.0:0.0} %" : "";
                bool waitingPuzzle = p == SolarParameter.Latitude ? HasPuzzle(sim, tile) : HasLightPuzzle(sim, p);
                status[p].text = e.Known
                    ? (p == SolarParameter.Clouds && !sim.Parameters.IsMeasured(p) ? "No clouds on this planet's model." : "Measured.")
                    : ordered ? $"Measuring with {SolarUnknowns.Instrument(p)}: {sim.MeasureProgress(p) * 100.0:0} %"
                    : waitingPuzzle ? "Readings are in: work it out in PUZZLES above."
                    : e.Rough ? $"Measured roughly · spread {share} left · measure again to pin it down"
                    : $"Estimate · spread {share} · measured with {SolarUnknowns.Instrument(p)}";
                Button b = buttons[p];
                b.text = ordered ? "Cancel" : waitingPuzzle ? "Solve above" : e.Rough ? "Again" : "Measure";
                b.SetEnabled(!e.Known && !waitingPuzzle);
            }
        }
    }

    /// <summary>Network (N): workforce v2 stage E. Shows what it will do until it is built.</summary>
    public sealed class NetworkWindow : SystemWindow
    {
        public override string Id => "network";
        public override string Title => "Network";
        public override KeyCode Key => KeyCode.N;
        public override int Order => 50;

        Label status, coverage, power, robots;

        protected override void BuildBody()
        {
            status = Text();
            Heading("COVERAGE");
            coverage = Text();
            Heading("POWER");
            power = Text();
            Heading("ROBOTS");
            robots = Text();
            Text("caption").text =
                "The lander is the network hub. A robot within range of the hub or a powered relay is connected: it hears " +
                "new orders, rule changes and the colony's needs, and hands in what it found. A robot out of range keeps " +
                "working on the program and priorities it last synced, and can't react to the colony's needs until it is " +
                "back in range. The hub and relays need grid power: in a brownout the whole network is down. " +
                "Build relays from the build bar to extend coverage. Range and power draws are placeholder values.";
        }

        protected override void RefreshBody(IPlanetSimulation sim)
        {
            var net = sim.Network;
            var w = sim.Workforce;
            int off = w.DisconnectedCount;
            status.text = net.Powered
                ? (off == 0 ? "Network up · every robot connected." : $"Network up · {off} robot{(off == 1 ? "" : "s")} out of range.")
                : "NETWORK DOWN: the grid is in brownout, so the hub and relays are unpowered. Robots work on cached rules.";
            status.EnableInClassList("message", !net.Powered || off > 0);
            int relays = net.Relays.Count;
            coverage.text = $"{net.Coverage.Count} tiles covered (range {KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles} tiles from each node) · " +
                            $"hub at tile #{net.HubTile} · {relays} relay{(relays == 1 ? "" : "s")}" +
                            (relays == 0 ? ".\nNo relays yet: robots working more than " + KardashevAscent.Simulation.Robots.ColonyNetwork.RangeTiles + " tiles from the lander drop off the network." : ".");
            power.text = $"Hub {Format.Power(KardashevAscent.Simulation.Robots.ColonyNetwork.HubPowerW)} · relays {Format.Power(relays * KardashevAscent.Simulation.Robots.ColonyNetwork.RelayPowerW)} · " +
                         $"total {Format.Power(net.PowerW)}, served before the colony's other loads.";
            var sb = new System.Text.StringBuilder();
            foreach (var r in w.Robots)
            {
                sb.Append(r.Name).Append(": ");
                if (r.Connected) sb.Append("connected");
                else sb.Append($"out of range for {r.HoursOffline:0.0} h on tile #{r.Tile} · running cached rules ({r.CachedProgram})");
                sb.Append('\n');
            }
            robots.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "No robots.";
        }
    }
}

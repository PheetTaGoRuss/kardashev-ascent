using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
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

        Label status, stock, message;
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

    /// <summary>Research (T): not designed yet; the tab is there so the window has its place.</summary>
    public sealed class ResearchWindow : SystemWindow
    {
        public override string Id => "research";
        public override string Title => "Research";
        public override KeyCode Key => KeyCode.T;
        public override int Order => 40;

        public override string LockedReason(IPlanetSimulation sim) => "not built yet.";

        protected override void BuildBody() =>
            Text().text = "Research will live here: what the colony is studying and what each discovery unlocks. " +
                          "Today, upgrades unlock at Kardashev milestones (see Overview) and Research robots can be built " +
                          "in Manufacturing, but they have no research to do yet.";

        protected override void RefreshBody(IPlanetSimulation sim) { }
    }

    /// <summary>Network (N): workforce v2 stage E. Shows what it will do until it is built.</summary>
    public sealed class NetworkWindow : SystemWindow
    {
        public override string Id => "network";
        public override string Title => "Network";
        public override KeyCode Key => KeyCode.N;
        public override int Order => 50;

        public override string LockedReason(IPlanetSimulation sim) => "coming in workforce stage E.";

        protected override void BuildBody() =>
            Text().text = "The robot network will live here: relays, their range and the hub's power. A robot out of " +
                          "relay range, or cut off when the hub has no power, works on its own with simpler rules. " +
                          "Today every robot is always connected.";

        protected override void RefreshBody(IPlanetSimulation sim) { }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        public event Action<int, int> BuildingSalvaged;

        /// <summary>Open work: construction sites in queue order, then salvage orders in the order asked.</summary>
        List<WorkTask> OpenTasks()
        {
            ReconsiderStandIns();
            var tasks = new List<WorkTask>(Build.QueueCount + salvageOrders.Count);
            int i = 0;
            // Floating panels are built by robots with a boat module (director, 2026-11-14; before, raft crews built them).
            // A site is work only while the stock has some of every material it still needs: otherwise a robot would
            // drive out and back with nothing to bring (it waits for mining or refining instead).
            foreach (var site in Build.Sites())
            {
                if (!CanAdvance(site.Bill, site.Share, StockAt(site.Tile)))
                    continue;
                tasks.Add(new WorkTask(TaskKind.Build, site.Tile, site.Slot, i++));
            }
            for (int k = 0; k < salvageOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Salvage, salvageOrders[k].Tile, salvageOrders[k].Slot, k));
            for (int k = 0; k < adjustOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Adjust, adjustOrders[k].Tile, adjustOrders[k].Slot, k));
            for (int k = 0; k < orders.Survey.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Survey, orders.Survey[k], -1, k));
            for (int k = 0; k < measureOrders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Measure, measureOrders[k].Tile, (int)measureOrders[k].Parameter, k));
            for (int k = 0; k < research.Orders.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Experiment, research.Orders[k].Tile, (int)research.Orders[k].Kind, k));
            for (int k = 0; k < orders.Mining.Count; k++)
                tasks.Add(new WorkTask(TaskKind.Mine, orders.Mining[k].Tile, (int)orders.Mining[k].Material, k));
            AddPassageTasks(tasks);
            List<(int Tile, int Slot)> shops = FinishedWorkshops();
            for (int k = 0; k < Math.Min(jobs.Count, shops.Count); k++)
                tasks.Add(new WorkTask(TaskKind.Manufacture, shops[k].Tile, shops[k].Slot, k));
            return tasks;
        }

        /// <summary>True when the stock holds some of every material a site still needs.</summary>
        bool CanAdvance(MaterialBill bill, double share, MaterialStock stock)
        {
            if (share >= 1.0) return true;
            foreach (var (m, kg) in bill.Items())
                if (kg > 1e-9 && stock[m] <= 1e-9) return false;
            return true;
        }

        /// <summary>
        /// Hand out work, let worked sites take parts from stock and worked salvage orders take their building down,
        /// and repeat while anything finished (a freed robot takes the next task at once).
        /// </summary>
        void Settle((int Tile, int Slot)? silent)
        {
            while (true)
            {
                Workforce.Movement = CurrentMovement();
                Workforce.Milestones = UnlockLevel;
                Workforce.NowHours = TimeHours;
                Workforce.Assign(OpenTasks(), Needs());
                bool any = false;
                // A site takes materials from stock while a robot works on it.
                foreach (var done in Build.Pay(key => StockAt(key.Tile), key => Workforce.IsWorkedOnSite(new WorkTask(TaskKind.Build, key.Tile, key.Slot))))
                {
                    any = true;
                    Finish(done.Tile, done.Slot, !(silent.HasValue && silent.Value == done));
                }
                foreach (var order in salvageOrders.ToArray())
                {
                    if (!Workforce.IsWorkedOnSite(new WorkTask(TaskKind.Salvage, order.Tile, order.Slot)))
                        continue;
                    any = true;
                    RemoveBuilding(order.Tile, order.Slot);
                    BuildingSalvaged?.Invoke(order.Tile, order.Slot);
                }
                foreach (var order in adjustOrders.ToArray())
                {
                    if (!Workforce.IsWorkedOnSite(new WorkTask(TaskKind.Adjust, order.Tile, order.Slot)))
                        continue;
                    any = true;
                    adjustOrders.Remove(order);
                    SetPanelAngles(order.Tile, order.Slot, order.Tilt, order.Azimuth);
                    PanelAdjusted?.Invoke(order.Tile, order.Slot);
                }
                if (!any)
                    return;
            }
        }

        public void RefreshWork() => Settle(null);

        /// <summary>
        /// What the colony needs now, for robots on the Demand program (D16): a brownout, or the grid battery under
        /// 30 % at night (TBD), and sites waiting for a robot.
        /// </summary>
        ColonyNeeds Needs() =>
            new ColonyNeeds(Grid.IsBrownout || (!IsDayAtBase && Grid.Battery.StateOfCharge < 0.30), Build.QueueCount > 0);

        /// <summary>Every open task now (sites, salvage and survey orders, mining, Workshop jobs), for the UI.</summary>
        public IReadOnlyList<WorkTask> OpenWork() => OpenTasks();

        public bool OrderSalvage(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (GetBuilding(tileId, slot) == BuildingType.None)
                return false;
            if (Build.IsQueued(tileId, slot))
                return RemoveBuilding(tileId, slot);   // a site is simply cancelled (its materials come back)
            if (salvageOrders.Contains((tileId, slot)))
                return false;
            salvageOrders.Add((tileId, slot));
            Settle(null);
            return true;
        }

        public bool IsSalvageOrdered(int tileId, int slot) => salvageOrders.Contains((tileId, A(tileId, slot)));

        public bool CancelSalvage(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (!salvageOrders.Remove((tileId, slot)))
                return false;
            Workforce.Tasks.MarkCancelled(new WorkTask(TaskKind.Salvage, tileId, slot));
            Settle(null);
            return true;
        }

        readonly Dictionary<int, TileSite> sites = new Dictionary<int, TileSite>();

        public double DemandTargetW => Colony.TargetW;
        public bool IsColonyGrowing => Colony.IsGrowing;
        public double HoursSinceBrownout => Colony.HoursSinceBrownout;

        double currentIrradiance;

        /// <summary>
        /// One panel on a tile. <paramref name="cloudFactor"/> (share of clear-sky light that gets through) goes into
        /// the panel's light-loss slot f_dust as 1 − factor: the game has no dust yet, and CloudClimate is meant to be
        /// applied to the panel's light as a whole. Longitude is measured from the sub-stellar point, which only
        /// matters on a tidally locked planet.
        /// </summary>
        PanelParams MakePanel(int tileId, double tilt, double azimuth, double cloudFactor, double area)
        {
            float elevation = Math.Max(0f, surface.Elevation[tileId]);
            double pressureRatio = Math.Exp(-elevation * 4000.0 / 8400.0); // elevation 1 ≈ 4 km, scale height 8.4 km
            double albedo = Terrains.Albedo(surface.Terrain[tileId]);
            return new PanelParams(Sphere.Latitude(tileId), tilt, azimuth, area, PanelEfficiency,
                                   45.0, -0.004, albedo, pressureRatio, SpectralFactor, 1.0 - cloudFactor, 1.0,
                                   Sphere.Longitude(tileId) - subStellarLongitudeAtStart);
        }
    }
}

using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What buildings cost (game rules on top of real embodied-energy numbers).
    /// The colony builds from <em>build parts</em>, counted in Wh of the energy that went into making them. Only the
    /// on-site share of a building's embodied energy is paid on the planet (assembly, site work, local processing);
    /// the refined materials (wafers, cells, glass) come with the lander's cargo. That share is a game rule.
    /// </summary>
    public static class BuildCosts
    {
        /// <summary>Share of a building's embodied energy paid on site (game rule).</summary>
        public const double OnSiteShare = 0.01;

        /// <summary>
        /// Embodied energy of Li-ion storage per Wh of capacity, electricity-equivalent (about 100; cradle-to-gate
        /// studies give roughly 300 Wh of primary energy per Wh, about a third of that as electricity).
        /// </summary>
        public const double LiIonEmbodiedWhPerWh = 100.0;

        /// <summary>Build parts the lander brings: enough for the first few buildings, Wh.</summary>
        public const double LanderStockWh = 200_000_000.0;

        /// <summary>Share of a finished building's cost that comes back as parts when it is taken down (game rule).</summary>
        public const double SalvageShare = 0.5;

        /// <summary>On-site cost of a panel of <paramref name="areaM2"/>, Wh.</summary>
        public static double Panel(double embodiedWhPerM2, double areaM2) => OnSiteShare * embodiedWhPerM2 * areaM2;

        /// <summary>On-site cost of a battery building of <paramref name="capacityWh"/>, Wh.</summary>
        public static double Battery(double capacityWh) => OnSiteShare * LiIonEmbodiedWhPerWh * capacityWh;
    }

    /// <summary>
    /// The colony's build parts and its construction queue (game rule).
    /// Spare power (what is left once the colony and the battery have taken theirs) is turned into parts. A new building
    /// takes what it costs from the parts in stock; if there are not enough, it waits as a construction site and the
    /// sites are finished first come, first served as new parts are made. A site does nothing until it is finished.
    /// With robots (design D2) the simulation only lets a site take parts while a robot works on it (<see cref="Pay"/>).
    /// Cancelling a site gives back what was put into it; taking down a finished building gives back
    /// <see cref="BuildCosts.SalvageShare"/> of its cost.
    /// </summary>
    public sealed class Construction
    {
        sealed class Site
        {
            public (int Tile, int Slot) Key;
            public double CostWh;
            public double PaidWh;
        }

        readonly List<Site> queue = new List<Site>();

        public Construction(double stockWh = BuildCosts.LanderStockWh)
        {
            if (stockWh < 0.0) throw new ArgumentOutOfRangeException(nameof(stockWh));
            StockWh = stockWh;
        }

        /// <summary>Build parts on hand, Wh.</summary>
        public double StockWh { get; private set; }

        /// <summary>Construction sites still waiting for parts.</summary>
        public int QueueCount => queue.Count;

        /// <summary>Parts still needed to finish every site, Wh.</summary>
        public double RemainingWh
        {
            get
            {
                double sum = 0.0;
                foreach (Site s in queue)
                    sum += s.CostWh - s.PaidWh;
                return sum;
            }
        }

        public bool IsQueued(int tile, int slot) => Find((tile, slot)) >= 0;

        /// <summary>How much of a site is paid for, 0–1; 1 when it is not a site (finished, or nothing there).</summary>
        public double Progress(int tile, int slot)
        {
            int i = Find((tile, slot));
            if (i < 0)
                return 1.0;
            Site s = queue[i];
            return s.CostWh > 0.0 ? s.PaidWh / s.CostWh : 1.0;
        }

        /// <summary>
        /// Start a building that costs <paramref name="costWh"/>. Returns true if the parts in stock paid for all of it
        /// (it is finished now), false if it joins the queue as a site.
        /// </summary>
        public bool Start(int tile, int slot, double costWh, double paidWh = 0.0) => Start(tile, slot, costWh, paidWh, true);

        /// <summary>
        /// Start a building; with <paramref name="payFromStock"/> false it takes nothing from stock now and waits as a
        /// site (unless it costs nothing), to be paid by <see cref="Pay"/> once a robot works on it.
        /// </summary>
        public bool Start(int tile, int slot, double costWh, double paidWh, bool payFromStock)
        {
            if (costWh < 0.0) throw new ArgumentOutOfRangeException(nameof(costWh));
            if (IsQueued(tile, slot)) throw new InvalidOperationException($"Slot {tile}.{slot} is already a construction site.");
            double take = payFromStock ? Math.Min(StockWh, Math.Max(0.0, costWh - paidWh)) : 0.0;
            StockWh -= take;
            paidWh += take;
            if (paidWh >= costWh - Tolerance(costWh))
                return true;
            queue.Add(new Site { Key = (tile, slot), CostWh = costWh, PaidWh = paidWh });
            return false;
        }

        /// <summary>Put back a site exactly as it was (when a game is loaded); it goes to the end of the queue.</summary>
        public void RestoreSite(int tile, int slot, double costWh, double paidWh)
        {
            if (IsQueued(tile, slot)) throw new InvalidOperationException($"Slot {tile}.{slot} is already a construction site.");
            queue.Add(new Site { Key = (tile, slot), CostWh = costWh, PaidWh = Math.Max(0.0, Math.Min(paidWh, costWh)) });
        }

        /// <summary>Remove a site and give back the parts already in it. Returns those parts, Wh (0 if it was no site).</summary>
        public double Cancel(int tile, int slot)
        {
            int i = Find((tile, slot));
            if (i < 0)
                return 0.0;
            double refund = queue[i].PaidWh;
            queue.RemoveAt(i);
            StockWh += refund;
            return refund;
        }

        /// <summary>A finished building costing <paramref name="costWh"/> was taken down: salvage part of it. Returns the parts, Wh.</summary>
        public double Salvage(double costWh)
        {
            double back = BuildCosts.SalvageShare * Math.Max(0.0, costWh);
            StockWh += back;
            return back;
        }

        /// <summary>
        /// Turn <paramref name="spareW"/> of spare power into parts for <paramref name="hours"/>, then pay the sites in
        /// order. Returns the sites finished in this step (in the order they finished).
        /// </summary>
        public List<(int Tile, int Slot)> Step(double spareW, double hours)
        {
            AddParts(spareW, hours);
            return Pay(null);
        }

        /// <summary>Turn <paramref name="spareW"/> of spare power into parts for <paramref name="hours"/>.</summary>
        public void AddParts(double spareW, double hours)
        {
            if (hours > 0.0 && spareW > 0.0)
                StockWh += spareW * hours;
        }

        /// <summary>
        /// Pay sites from stock in queue order, skipping those <paramref name="canTake"/> refuses (null: every site;
        /// with robots, only sites a robot is working on). Returns the sites finished, in the order they finished.
        /// </summary>
        public List<(int Tile, int Slot)> Pay(Func<(int Tile, int Slot), bool> canTake)
        {
            var finished = new List<(int Tile, int Slot)>();
            for (int i = 0; i < queue.Count && StockWh > 0.0;)
            {
                Site s = queue[i];
                if (canTake != null && !canTake(s.Key))
                {
                    i++;
                    continue;
                }
                double take = Math.Min(StockWh, s.CostWh - s.PaidWh);
                s.PaidWh += take;
                StockWh -= take;
                if (s.PaidWh < s.CostWh - Tolerance(s.CostWh))
                    break;
                queue.RemoveAt(i);
                finished.Add(s.Key);
            }
            return finished;
        }

        /// <summary>The sites in queue order, with what each costs and what is paid, Wh (for saving).</summary>
        public IEnumerable<(int Tile, int Slot, double CostWh, double PaidWh)> Sites()
        {
            foreach (Site s in queue)
                yield return (s.Key.Tile, s.Key.Slot, s.CostWh, s.PaidWh);
        }

        /// <summary>Drop every site without refunds and set the stock (when a game is loaded).</summary>
        public void Reset(double stockWh)
        {
            queue.Clear();
            StockWh = Math.Max(0.0, stockWh);
        }

        int Find((int, int) key)
        {
            for (int i = 0; i < queue.Count; i++)
                if (queue[i].Key == key)
                    return i;
            return -1;
        }

        static double Tolerance(double costWh) => Math.Max(1e-6, costWh * 1e-12);
    }
}

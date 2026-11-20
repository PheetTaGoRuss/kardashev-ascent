using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Resources;
using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The colony's construction sites (director, 2026-11-14: buildings are built from real materials, not power).
    /// A new building waits as a site with its bill of materials (<see cref="BuildCosts"/>). While a robot works on it,
    /// it takes materials from the colony's stock (<see cref="Pay"/>): each time, the same share of every material on
    /// its bill, as much as the scarcest one allows, so a site is always some share paid. Robots on the site also put in
    /// work (<see cref="Work"/>, <see cref="BuildTimes.Hours(BuildingType, double)"/>), never more than the materials delivered allow. It is
    /// finished when all its materials are in and the work is done; a site does nothing until then. Cancelling a site gives back what was put into it; taking down a
    /// finished building gives back <see cref="BuildCosts.SalvageShare"/> of its bill.
    /// </summary>
    public sealed class Construction
    {
        sealed class Site
        {
            public (int Tile, int Slot) Key;
            public MaterialBill Bill;
            public double Share;
            public double WorkHours;
            public double WorkDone;

            /// <summary>Building progress 0–1: the work done (the work can't run ahead of the materials).</summary>
            public double Progress => WorkHours > 0.0 ? Math.Min(1.0, WorkDone / WorkHours) : Share;
        }

        readonly List<Site> queue = new List<Site>();

        /// <summary>False: a site is finished as soon as its materials are in (no work time; for tests about other things).</summary>
        public bool TimeEnabled { get; set; } = true;

        /// <summary>Construction sites still waiting for materials or a robot.</summary>
        public int QueueCount => queue.Count;

        /// <summary>Materials still needed to finish every site.</summary>
        public MaterialBill Remaining
        {
            get
            {
                var sum = new MaterialBill();
                foreach (Site s in queue)
                    sum = sum.Plus(s.Bill.Times(1.0 - s.Share));
                return sum;
            }
        }

        public bool IsQueued(int tile, int slot) => Find((tile, slot)) >= 0;

        /// <summary>How far a site is built, 0–1; 1 when it is not a site (finished, or nothing there).</summary>
        public double Progress(int tile, int slot)
        {
            int i = Find((tile, slot));
            return i < 0 ? 1.0 : queue[i].Progress;
        }

        /// <summary>Share of a site's materials delivered, 0–1 (1 when it is no site).</summary>
        public double MaterialShare(int tile, int slot)
        {
            int i = Find((tile, slot));
            return i < 0 ? 1.0 : queue[i].Share;
        }

        /// <summary>Work done and work needed on a site, hours (0, 0 when it is no site).</summary>
        public (double Done, double Total) WorkAt(int tile, int slot)
        {
            int i = Find((tile, slot));
            return i < 0 ? (0.0, 0.0) : (queue[i].WorkDone, queue[i].WorkHours);
        }

        /// <summary>
        /// Robots put <paramref name="hours"/> of work into a site (a crew's hours at its rate), up to what the materials
        /// delivered so far allow. Returns the hours that went in.
        /// </summary>
        public double Work(int tile, int slot, double hours)
        {
            int i = Find((tile, slot));
            if (i < 0 || hours <= 0.0)
                return 0.0;
            Site s = queue[i];
            double cap = s.Share * s.WorkHours;
            double add = Math.Max(0.0, Math.Min(hours, cap - s.WorkDone));
            s.WorkDone += add;
            return add;
        }

        /// <summary>What a site still needs (empty when it is no site).</summary>
        public MaterialBill RemainingAt(int tile, int slot)
        {
            int i = Find((tile, slot));
            return i < 0 ? new MaterialBill() : queue[i].Bill.Times(1.0 - queue[i].Share);
        }

        /// <summary>
        /// Start a building with this bill. Returns true when it needs nothing (finished now); otherwise it waits as a
        /// site until a robot brings the materials.
        /// </summary>
        public bool Start(int tile, int slot, MaterialBill bill, double workHours = 0.0)
        {
            if (bill == null) throw new ArgumentNullException(nameof(bill));
            if (IsQueued(tile, slot)) throw new InvalidOperationException($"Slot {tile}.{slot} is already a construction site.");
            if (bill.IsEmpty)
                return true;
            queue.Add(new Site { Key = (tile, slot), Bill = bill, Share = 0.0, WorkHours = TimeEnabled ? Math.Max(0.0, workHours) : 0.0 });
            return false;
        }

        /// <summary>
        /// Put back a site exactly as it was (when a game is loaded); it goes to the end of the queue.
        /// <paramref name="workDone"/> null: a save from before construction time (version 27): work as far as its materials.
        /// </summary>
        public void RestoreSite(int tile, int slot, MaterialBill bill, double share, double? workDone = null, double workHours = 0.0)
        {
            if (IsQueued(tile, slot)) throw new InvalidOperationException($"Slot {tile}.{slot} is already a construction site.");
            bill = bill ?? new MaterialBill();
            share = Math.Max(0.0, Math.Min(share, 1.0));
            double hours = TimeEnabled ? Math.Max(0.0, workHours) : 0.0;
            queue.Add(new Site { Key = (tile, slot), Bill = bill, Share = share, WorkHours = hours,
                                 WorkDone = Math.Max(0.0, Math.Min(workDone ?? share * hours, share * hours)) });
        }

        /// <summary>
        /// Give a site nothing has gone into yet a new bill (a stand-in metal). Returns false, changing nothing, if it is
        /// no site or materials or work are already in it.
        /// </summary>
        public bool Rebill(int tile, int slot, MaterialBill bill)
        {
            int i = Find((tile, slot));
            if (i < 0 || bill == null || queue[i].Share > 0.0 || queue[i].WorkDone > 0.0)
                return false;
            queue[i].Bill = bill;   // the same work: only the metal changes
            return true;
        }

        /// <summary>Remove a site and give back the materials already in it. Returns them (empty if it was no site).</summary>
        public MaterialBill Cancel(int tile, int slot, MaterialStock stock)
        {
            int i = Find((tile, slot));
            if (i < 0)
                return new MaterialBill();
            MaterialBill refund = queue[i].Bill.Times(queue[i].Share);
            queue.RemoveAt(i);
            AddTo(stock, refund);
            return refund;
        }

        /// <summary>A finished building with this bill was taken down: salvage part of it. Returns what came back.</summary>
        public MaterialBill Salvage(MaterialBill bill, MaterialStock stock)
        {
            MaterialBill back = (bill ?? new MaterialBill()).Times(BuildCosts.SalvageShare);
            AddTo(stock, back);
            return back;
        }

        /// <summary>
        /// Let sites take materials from <paramref name="stock"/> in queue order, skipping those <paramref name="canTake"/>
        /// refuses (null: every site; in the game, only sites a robot is working on). Returns the sites finished, in order.
        /// </summary>
        public List<(int Tile, int Slot)> Pay(MaterialStock stock, Func<(int Tile, int Slot), bool> canTake) =>
            Pay(_ => stock, canTake);

        /// <summary>The same, each site drawing from its own stock (its tile's depot when the colony keeps stock per place).</summary>
        public List<(int Tile, int Slot)> Pay(Func<(int Tile, int Slot), MaterialStock> stockFor, Func<(int Tile, int Slot), bool> canTake)
        {
            var finished = new List<(int Tile, int Slot)>();
            if (stockFor == null)
                return finished;
            for (int i = 0; i < queue.Count;)
            {
                Site s = queue[i];
                MaterialStock stock = stockFor(s.Key);
                if (stock == null || (canTake != null && !canTake(s.Key)))
                {
                    i++;
                    continue;
                }
                // The same share of everything still needed, as much as the scarcest material allows.
                double left = 1.0 - s.Share;
                double f = 1.0;
                foreach (var (m, kg) in s.Bill.Items())
                {
                    double need = kg * left;
                    if (need > 1e-12)
                        f = Math.Min(f, stock[m] / need);
                }
                if (f > 1e-12)
                {
                    f = Math.Min(1.0, f);
                    foreach (var (m, kg) in s.Bill.Items())
                        stock.Take(m, kg * left * f);
                    s.Share = f >= 1.0 - 1e-12 ? 1.0 : s.Share + left * f;
                }
                if (s.Share < 1.0 - 1e-9 || s.WorkDone < s.WorkHours - 1e-9)
                {
                    i++;
                    continue;
                }
                queue.RemoveAt(i);
                finished.Add(s.Key);
            }
            return finished;
        }

        /// <summary>The sites in queue order, with each bill, the share paid and the work done, hours (for saving).</summary>
        public IEnumerable<(int Tile, int Slot, MaterialBill Bill, double Share, double WorkDone)> Sites()
        {
            foreach (Site s in queue)
                yield return (s.Key.Tile, s.Key.Slot, s.Bill, s.Share, s.WorkDone);
        }

        /// <summary>Drop every site without refunds (when a game is loaded).</summary>
        public void Reset() => queue.Clear();

        static void AddTo(MaterialStock stock, MaterialBill bill)
        {
            if (stock == null) return;
            foreach (var (m, kg) in bill.Items())
                stock.Add(m, kg);
        }

        int Find((int, int) key)
        {
            for (int i = 0; i < queue.Count; i++)
                if (queue[i].Key == key)
                    return i;
            return -1;
        }
    }
}

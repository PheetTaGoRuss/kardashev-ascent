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
    /// its bill, as much as the scarcest one allows, so a site is always some share paid. It is finished when the share
    /// reaches 1; a site does nothing until then. Cancelling a site gives back what was put into it; taking down a
    /// finished building gives back <see cref="BuildCosts.SalvageShare"/> of its bill.
    /// </summary>
    public sealed class Construction
    {
        sealed class Site
        {
            public (int Tile, int Slot) Key;
            public MaterialBill Bill;
            public double Share;
        }

        readonly List<Site> queue = new List<Site>();

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

        /// <summary>How much of a site is paid for, 0–1; 1 when it is not a site (finished, or nothing there).</summary>
        public double Progress(int tile, int slot)
        {
            int i = Find((tile, slot));
            return i < 0 ? 1.0 : queue[i].Share;
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
        public bool Start(int tile, int slot, MaterialBill bill)
        {
            if (bill == null) throw new ArgumentNullException(nameof(bill));
            if (IsQueued(tile, slot)) throw new InvalidOperationException($"Slot {tile}.{slot} is already a construction site.");
            if (bill.IsEmpty)
                return true;
            queue.Add(new Site { Key = (tile, slot), Bill = bill, Share = 0.0 });
            return false;
        }

        /// <summary>Put back a site exactly as it was (when a game is loaded); it goes to the end of the queue.</summary>
        public void RestoreSite(int tile, int slot, MaterialBill bill, double share)
        {
            if (IsQueued(tile, slot)) throw new InvalidOperationException($"Slot {tile}.{slot} is already a construction site.");
            queue.Add(new Site { Key = (tile, slot), Bill = bill ?? new MaterialBill(), Share = Math.Max(0.0, Math.Min(share, 1.0)) });
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
        public List<(int Tile, int Slot)> Pay(MaterialStock stock, Func<(int Tile, int Slot), bool> canTake)
        {
            var finished = new List<(int Tile, int Slot)>();
            if (stock == null)
                return finished;
            for (int i = 0; i < queue.Count;)
            {
                Site s = queue[i];
                if (canTake != null && !canTake(s.Key))
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
                if (s.Share < 1.0 - 1e-9)
                {
                    i++;
                    continue;
                }
                queue.RemoveAt(i);
                finished.Add(s.Key);
            }
            return finished;
        }

        /// <summary>The sites in queue order, with each bill and the share paid (for saving).</summary>
        public IEnumerable<(int Tile, int Slot, MaterialBill Bill, double Share)> Sites()
        {
            foreach (Site s in queue)
                yield return (s.Key.Tile, s.Key.Slot, s.Bill, s.Share);
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

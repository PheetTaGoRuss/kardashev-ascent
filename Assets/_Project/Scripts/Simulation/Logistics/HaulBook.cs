using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Logistics
{
    /// <summary>One haul: load some kg of a material at a tile's depot, drive, unload at another tile's depot (mission operations 3.2).</summary>
    public sealed class HaulOrder
    {
        public int Id;
        public int FromTile;
        public int ToTile;
        public Material Material;
        /// <summary>Kg planned; after loading, kg aboard.</summary>
        public double Kg;
        /// <summary>0: a robot is to load it. 1: it is loaded and the carrier is to unload it.</summary>
        public int Stage;
        /// <summary>The robot carrying it once loaded (-1 before).</summary>
        public int RobotId = -1;
    }

    /// <summary>A wish to hold at least some kg of a material at a depot: hauls are generated until it is met.</summary>
    public readonly struct StockTarget
    {
        public readonly int Tile;
        public readonly Material Material;
        public readonly double Kg;

        public StockTarget(int tile, Material material, double kg)
        {
            Tile = tile;
            Material = material;
            Kg = kg;
        }
    }

    /// <summary>
    /// The hauls under way and the stock targets set. Pure bookkeeping (architecture step M1); the simulation decides
    /// what to haul and the robots do the work. Order ids rise and are never reused.
    /// </summary>
    public sealed class HaulBook
    {
        readonly List<HaulOrder> orders = new List<HaulOrder>();
        readonly Dictionary<(int Tile, Material Material), double> targets = new Dictionary<(int, Material), double>();
        int nextId = 1;

        public IReadOnlyList<HaulOrder> Orders => orders;
        public int NextId => nextId;

        public HaulOrder Add(int from, int to, Material material, double kg)
        {
            var o = new HaulOrder { Id = nextId++, FromTile = from, ToTile = to, Material = material, Kg = kg };
            orders.Add(o);
            return o;
        }

        public HaulOrder Find(int id)
        {
            foreach (HaulOrder o in orders)
                if (o.Id == id) return o;
            return null;
        }

        public bool Remove(int id)
        {
            int i = orders.FindIndex(o => o.Id == id);
            if (i < 0) return false;
            orders.RemoveAt(i);
            return true;
        }

        /// <summary>Kg of a material on its way to a tile (planned or loaded).</summary>
        public double InFlightTo(int tile, Material m)
        {
            double t = 0.0;
            foreach (HaulOrder o in orders)
                if (o.ToTile == tile && o.Material == m) t += o.Kg;
            return t;
        }

        /// <summary>Kg of a material still to be loaded at a tile (promised, not yet taken from the depot).</summary>
        public double ReservedAt(int tile, Material m)
        {
            double t = 0.0;
            foreach (HaulOrder o in orders)
                if (o.Stage == 0 && o.FromTile == tile && o.Material == m) t += o.Kg;
            return t;
        }

        /// <summary>Set the wish for a depot; zero or less removes it.</summary>
        public void SetTarget(int tile, Material m, double kg)
        {
            if (kg <= 0.0) targets.Remove((tile, m));
            else targets[(tile, m)] = kg;
        }

        public double TargetAt(int tile, Material m) => targets.TryGetValue((tile, m), out double kg) ? kg : 0.0;

        /// <summary>The targets in a fixed order (tile, then material), so plans do not depend on insertion order.</summary>
        public List<StockTarget> Targets()
        {
            var list = new List<StockTarget>();
            foreach (var kv in targets) list.Add(new StockTarget(kv.Key.Tile, kv.Key.Material, kv.Value));
            list.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : ((int)a.Material).CompareTo((int)b.Material));
            return list;
        }

        public void Clear()
        {
            orders.Clear();
            targets.Clear();
            nextId = 1;
        }

        /// <summary>Put back what a save held.</summary>
        public void Restore(IEnumerable<HaulOrder> saved, IEnumerable<StockTarget> savedTargets, int next)
        {
            Clear();
            foreach (HaulOrder o in saved) orders.Add(o);
            foreach (StockTarget t in savedTargets) SetTarget(t.Tile, t.Material, t.Kg);
            int top = 0;
            foreach (HaulOrder o in orders) top = Math.Max(top, o.Id);
            nextId = Math.Max(next, top + 1);
        }
    }

    /// <summary>A material a tile lacks: kg wanted there (a site's shortfall, or a stock target not yet met).</summary>
    public readonly struct HaulDemand
    {
        public readonly int Tile;
        public readonly Material Material;
        public readonly double Kg;

        public HaulDemand(int tile, Material material, double kg)
        {
            Tile = tile;
            Material = material;
            Kg = kg;
        }
    }

    /// <summary>One haul the planner chose.</summary>
    public readonly struct PlannedHaul
    {
        public readonly int From;
        public readonly int To;
        public readonly Material Material;
        public readonly double Kg;

        public PlannedHaul(int from, int to, Material material, double kg)
        {
            From = from;
            To = to;
            Material = material;
            Kg = kg;
        }
    }

    /// <summary>
    /// Matches each demand with the nearest known holding (mission operations 3.2): nearest by route hours, then by
    /// tile id, so the same inputs always give the same plan. Pure: the simulation supplies what is where.
    /// </summary>
    public static class HaulPlanner
    {
        /// <param name="demands">What tiles lack, in the order to serve them.</param>
        /// <param name="sourceTiles">Tiles with a depot, in id order.</param>
        /// <param name="availableKg">Kg of a material a tile can give away (its stock less what it keeps and has promised).</param>
        /// <param name="hoursBetween">Route hours from one tile to another (infinity: no way).</param>
        /// <param name="payloadKg">The most one haul carries.</param>
        /// <param name="maxPerDemand">At most this many hauls are planned for one demand in one pass.</param>
        public static List<PlannedHaul> Plan(IEnumerable<HaulDemand> demands, IReadOnlyList<int> sourceTiles,
                                             Func<int, Material, double> availableKg, Func<int, int, double> hoursBetween,
                                             double payloadKg, int maxPerDemand = 4)
        {
            var plan = new List<PlannedHaul>();
            var taken = new Dictionary<(int, Material), double>();
            if (payloadKg <= 0.0) return plan;
            foreach (HaulDemand d in demands)
            {
                double remaining = d.Kg;
                for (int k = 0; k < maxPerDemand && remaining > 1e-6; k++)
                {
                    int best = -1;
                    double bestHours = double.PositiveInfinity, bestAvail = 0.0;
                    foreach (int s in sourceTiles)
                    {
                        if (s == d.Tile) continue;
                        double avail = availableKg(s, d.Material) - (taken.TryGetValue((s, d.Material), out double t) ? t : 0.0);
                        if (avail <= 1e-6) continue;
                        double h = hoursBetween(s, d.Tile);
                        if (double.IsInfinity(h) || double.IsNaN(h)) continue;
                        if (h < bestHours - 1e-12) { best = s; bestHours = h; bestAvail = avail; }
                    }
                    if (best < 0) break;
                    double kg = Math.Min(Math.Min(remaining, payloadKg), bestAvail);
                    plan.Add(new PlannedHaul(best, d.Tile, d.Material, kg));
                    taken[(best, d.Material)] = (taken.TryGetValue((best, d.Material), out double t0) ? t0 : 0.0) + kg;
                    remaining -= kg;
                }
            }
            return plan;
        }
    }
}

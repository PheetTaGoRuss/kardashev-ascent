using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Logistics
{
    /// <summary>Where a stock lies: a depot on a tile, a plant's input or output buffer, or the cargo a robot carries.</summary>
    public enum PlaceKind { Depot, BufferIn, BufferOut, Cargo, Wreck }

    /// <summary>A typed place id (mission operations spec 3.3): every kilogram the colony holds is in exactly one place.</summary>
    public readonly struct PlaceId : IEquatable<PlaceId>
    {
        public readonly PlaceKind Kind;
        public readonly int Id;

        public PlaceId(PlaceKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public static PlaceId Depot(int tile) => new PlaceId(PlaceKind.Depot, tile);
        public static PlaceId Cargo(int robotId) => new PlaceId(PlaceKind.Cargo, robotId);
        public static PlaceId Wreck(int wreckId) => new PlaceId(PlaceKind.Wreck, wreckId);

        public bool Equals(PlaceId other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object obj) => obj is PlaceId p && Equals(p);
        public override int GetHashCode() => (int)Kind * 7919 ^ Id;
        public override string ToString() => $"{Kind} {Id}";
    }

    /// <summary>
    /// What lies in each place, kg. Pure bookkeeping (architecture step M1): the simulation decides what moves and when.
    /// Each place is a <see cref="MaterialStock"/>. <see cref="Capacity"/> limits what a place holds (null: no limit).
    /// </summary>
    public sealed class InventoryBook
    {
        readonly Dictionary<PlaceId, MaterialStock> stocks = new Dictionary<PlaceId, MaterialStock>();

        /// <summary>Total kg a place can hold, or null for no limit.</summary>
        public Func<PlaceId, double?> Capacity { get; set; }

        /// <summary>Kg a place can hold of one storage class (bulk solid, sealed liquid, gas, hazardous), or null for no limit: piles, yards, stores and tanks decide this.</summary>
        public Func<PlaceId, StorageClass, double?> ClassCapacity { get; set; }

        /// <summary>Use an existing stock as this place (the lander depot is the colony's old single stock).</summary>
        public MaterialStock Adopt(PlaceId place, MaterialStock stock)
        {
            stocks[place] = stock ?? throw new ArgumentNullException(nameof(stock));
            return stock;
        }

        /// <summary>The stock of a place (made empty when there is none yet).</summary>
        public MaterialStock StockOf(PlaceId place)
        {
            if (!stocks.TryGetValue(place, out MaterialStock s))
                stocks[place] = s = new MaterialStock();
            return s;
        }

        public bool Has(PlaceId place) => stocks.ContainsKey(place);

        public IEnumerable<PlaceId> Places => stocks.Keys;

        /// <summary>Forget a place (a robot is gone, a building taken down); its stock should be empty or moved first.</summary>
        public bool Remove(PlaceId place) => stocks.Remove(place);

        /// <summary>Every place's holding of one material, kg.</summary>
        public double Total(Material m)
        {
            double t = 0.0;
            foreach (MaterialStock s in stocks.Values) t += s[m];
            return t;
        }

        /// <summary>A copy of the sum over every place: the colony-wide view (the HUD's facade).</summary>
        public MaterialStock Totals()
        {
            var sum = new MaterialStock();
            foreach (Material m in Materials.All)
            {
                double t = Total(m);
                if (t > 0.0) sum.Add(m, t);
            }
            return sum;
        }

        /// <summary>Kg a place holds of every material together.</summary>
        public double MassAt(PlaceId place)
        {
            if (!stocks.TryGetValue(place, out MaterialStock s)) return 0.0;
            double t = 0.0;
            foreach (Material m in Materials.All) t += s[m];
            return t;
        }

        /// <summary>Kg still free in a place (infinite when it has no capacity limit).</summary>
        public double FreeAt(PlaceId place)
        {
            double? cap = Capacity?.Invoke(place);
            return cap.HasValue ? Math.Max(0.0, cap.Value - MassAt(place)) : double.PositiveInfinity;
        }

        /// <summary>Kg of the materials of one storage class a place holds.</summary>
        public double MassAt(PlaceId place, StorageClass storageClass)
        {
            if (!stocks.TryGetValue(place, out MaterialStock s)) return 0.0;
            double t = 0.0;
            foreach (Material m in Materials.All)
                if (MaterialCatalog.PropertiesOf(m).StorageClass == storageClass) t += s[m];
            return t;
        }

        /// <summary>Kg of this material a place has room for: the total limit and the limit of its storage class both count.</summary>
        public double FreeFor(PlaceId place, Material m)
        {
            double free = FreeAt(place);
            StorageClass cls = MaterialCatalog.PropertiesOf(m).StorageClass;
            double? cap = ClassCapacity?.Invoke(place, cls);
            if (cap.HasValue) free = Math.Min(free, Math.Max(0.0, cap.Value - MassAt(place, cls)));
            return free;
        }

        /// <summary>Add to a place, up to what is free there. Returns what went in; the rest is not added.</summary>
        public double Add(PlaceId place, Material m, double kg)
        {
            if (kg < 0.0) throw new ArgumentOutOfRangeException(nameof(kg));
            double put = Math.Min(kg, FreeFor(place, m));
            if (put > 0.0) StockOf(place).Add(m, put);
            return put;
        }

        /// <summary>Move up to <paramref name="kg"/> between places: no more than the source holds or the target has room for. Returns what moved.</summary>
        public double Move(PlaceId from, PlaceId to, Material m, double kg)
        {
            if (!stocks.TryGetValue(from, out MaterialStock src)) return 0.0;
            double moved = Math.Min(Math.Min(kg, src[m]), FreeFor(to, m));
            if (moved <= 0.0) return 0.0;
            src.Take(m, moved);
            StockOf(to).Add(m, moved);
            return moved;
        }

        /// <summary>Forget every place except <paramref name="keep"/> (for a load).</summary>
        public void ClearExcept(PlaceId keep)
        {
            var drop = new List<PlaceId>();
            foreach (PlaceId p in stocks.Keys)
                if (!p.Equals(keep)) drop.Add(p);
            foreach (PlaceId p in drop) stocks.Remove(p);
        }
    }

    /// <summary>
    /// What hauling cost, per material: kg moved, kg-km, and the Wh spent driving it (mission operations spec 3.3).
    /// It feeds the energy ledger so energy conservation still closes.
    /// </summary>
    public sealed class LogisticsLedger
    {
        readonly double[] kgMoved = new double[Materials.All.Length];
        readonly double[] kgKm = new double[Materials.All.Length];
        double haulWh;

        public double KgMoved(Material m) => kgMoved[(int)m];
        public double KgKm(Material m) => kgKm[(int)m];

        /// <summary>Energy the colony has spent hauling, Wh.</summary>
        public double HaulWh => haulWh;

        public void RecordMove(Material m, double kg, double km)
        {
            kgMoved[(int)m] += kg;
            kgKm[(int)m] += kg * km;
        }

        public void RecordEnergy(double wh) => haulWh += Math.Max(0.0, wh);

        public void Restore(double wh, IReadOnlyList<(Material Material, double Kg, double KgKm)> rows)
        {
            Array.Clear(kgMoved, 0, kgMoved.Length);
            Array.Clear(kgKm, 0, kgKm.Length);
            haulWh = wh;
            foreach (var r in rows)
            {
                kgMoved[(int)r.Material] = r.Kg;
                kgKm[(int)r.Material] = r.KgKm;
            }
        }
    }
}

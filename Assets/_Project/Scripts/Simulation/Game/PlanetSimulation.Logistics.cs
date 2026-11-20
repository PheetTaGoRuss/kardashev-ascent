using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Stock kept per place (mission operations spec 3.3, MO-02). The lander depot is the colony's old single stock
    /// (<see cref="Materials"/>); with <see cref="LogisticsEnabled"/> on, mined ore lies in a pile on its mine tile and a
    /// construction site draws only from its own tile's depot. Hauls that bring materials where they are needed come
    /// with the haul task. With it off (old saves, the simulation tests) everything behaves as before.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly InventoryBook inventories = new InventoryBook();
        readonly LogisticsLedger haulLedger = new LogisticsLedger();

        /// <summary>
        /// How much bulk solid a tile's open pile holds, kg: no limit yet. Game rule, TBD (open question Q1). Storage
        /// yards, sealed stores and gas tanks add their own room, class by class (<see cref="StorageCapacityKg"/>).
        /// </summary>
        public double OpenPileCapacityKg { get; set; } = double.PositiveInfinity;

        /// <summary>Stock per place instead of one colony stock. New missions turn it on once hauls exist; old saves keep it off.</summary>
        public bool LogisticsEnabled { get; set; }

        /// <summary>The stock that serves a tile: the lander depot when logistics is off, else the depot on that tile.</summary>
        MaterialStock StockAt(int tileId) =>
            !LogisticsEnabled || tileId == BaseTileId ? Materials : inventories.StockOf(PlaceId.Depot(tileId));

        public double KgAt(int tileId, Material material) => StockAt(tileId)[material];

        public double TotalKg(Material material) => LogisticsEnabled ? inventories.Total(material) : Materials[material];

        /// <summary>Put materials into the depot of a tile (what a haul unloads; used by tests and the debug panel).</summary>
        public void PutMaterials(int tileId, Material material, double kg)
        {
            if (kg <= 0.0) return;
            StockAt(tileId).Add(material, kg);
            Settle(null);   // a site that was waiting for it can start
        }

        /// <summary>The colony-wide view the HUD reads: the sum over every place when logistics is on.</summary>
        MaterialStock IResourceCommands.Materials => LogisticsEnabled ? inventories.Totals() : Materials;

        public (double KgMoved, double KgKm, double Wh) HaulTotals
        {
            get
            {
                double kg = 0.0, kgKm = 0.0;
                foreach (Material m in Resources.Materials.All) { kg += haulLedger.KgMoved(m); kgKm += haulLedger.KgKm(m); }
                return (kg, kgKm, haulLedger.HaulWh);
            }
        }

        /// <summary>Every place's holding, for tests and the read models (kind, id, material, kg).</summary>
        public IEnumerable<(PlaceKind Kind, int Id, Material Material, double Kg)> Holdings()
        {
            foreach (PlaceId p in inventories.Places)
            {
                MaterialStock s = inventories.StockOf(p);
                foreach (Material m in Resources.Materials.All)
                    if (s[m] > 0.0) yield return (p.Kind, p.Id, m, s[m]);
            }
        }

        void CaptureLogistics(SimulationState state)
        {
            state.LogisticsEnabled = LogisticsEnabled;
            PlaceId hub = PlaceId.Depot(BaseTileId);
            foreach (PlaceId p in inventories.Places)
            {
                if (p.Equals(hub)) continue;
                MaterialStock s = inventories.StockOf(p);
                var rec = new InventoryRecord { Kind = p.Kind, Id = p.Id };
                foreach (Material m in Resources.Materials.All)
                    if (s[m] > 0.0) rec.Stock.Add(new MaterialRecord { Material = m, Kg = s[m] });
                if (rec.Stock.Count > 0) state.Inventories.Add(rec);
            }
            state.HaulWh = haulLedger.HaulWh;
            foreach (Material m in Resources.Materials.All)
                if (haulLedger.KgMoved(m) > 0.0)
                    state.HaulLedger.Add(new HaulLedgerRecord { Material = m, Kg = haulLedger.KgMoved(m), KgKm = haulLedger.KgKm(m) });
        }

        void RestoreLogistics(SimulationState state)
        {
            // A save from before version 38 keeps its one stock in the lander depot and logistics off.
            LogisticsEnabled = state.Version >= 38 && state.LogisticsEnabled;
            if (state.Inventories != null)
                foreach (InventoryRecord rec in state.Inventories)
                {
                    MaterialStock s = inventories.StockOf(new PlaceId(rec.Kind, rec.Id));
                    foreach (MaterialRecord m in rec.Stock) s.Add(m.Material, Math.Max(0.0, m.Kg));
                }
            var rows = new List<(Material, double, double)>();
            if (state.HaulLedger != null)
                foreach (HaulLedgerRecord r in state.HaulLedger) rows.Add((r.Material, r.Kg, r.KgKm));
            haulLedger.Restore(state.Version >= 38 ? state.HaulWh : 0.0, rows);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>What the stored materials have taken from and given to the environment so far (reporting; not saved): the ledger of the chemistry step.</summary>
    public sealed class ChemistryLedger
    {
        public double OxygenFromAirKg, WaterFromAirKg, HydrogenVentedKg, WaterSublimatedKg;
        public readonly Dictionary<Material, double> DrainedKg = new Dictionary<Material, double>();
        /// <summary>The mass of every stock changed by the chemistry: the sum of all changes. With the exchange it must close.</summary>
        public double StockChangeKg;
        public double TotalDrainedKg { get { double t = 0; foreach (var kv in DrainedKg) t += kv.Value; return t; } }
        /// <summary>Change of stock + drained + vented + sublimated - taken from the air: zero when the books close.</summary>
        public double ImbalanceKg => StockChangeKg + TotalDrainedKg + HydrogenVentedKg + WaterSublimatedKg - OxygenFromAirKg - WaterFromAirKg;
        public readonly Dictionary<Material, double> LostKg = new Dictionary<Material, double>();
    }

    public sealed partial class PlanetSimulation
    {
        /// <summary>Chemistry step length, hours.</summary>
        const double ChemistryStepHours = 6.0;
        double chemistryClock;
        readonly HashSet<(int Place, Material)> chemistryWarned = new HashSet<(int, Material)>();

        /// <summary>The environment ledger of the stored materials' reactions (not saved).</summary>
        public ChemistryLedger Chemistry { get; } = new ChemistryLedger();

        /// <summary>The chemistry of stored materials on or off (on for new missions; saves before version 60 keep their materials as they were).</summary>
        public bool ChemistryEnabled { get; set; } = true;

        bool CoastTile(int tile) { foreach (int n in Sphere.Neighbors[tile]) if (GetTile(n).IsWater) return true; return false; }

        ChemEnvironment EnvironmentAt(int tile) => new ChemEnvironment(LiveAirTemperatureAt(tile), HumidityAt(tile), PressurePaAt(tile), OxygenFraction, CoastTile(tile), SurfaceGravityMs2, AirMolarMassKg);

        /// <summary>How a stock of this material is kept on a tile (M3): the lander is sealed; elsewhere by the building the material's storage class needs, else an open pile.</summary>
        public Shelter ShelterOf(int tile, Material m)
        {
            StorageClass cls = MaterialCatalog.PropertiesOf(m).StorageClass;
            if (tile == BaseTileId && cls != StorageClass.PressurizedGas) return cls == StorageClass.Hazardous ? Shelter.Inert : Shelter.Sealed;   // the lander keeps hazardous solids in inert containers   // the lander holds every solid and liquid; gas needs a tank
            switch (MaterialCatalog.PropertiesOf(m).StorageClass)
            {
                case StorageClass.PressurizedGas: return StorageCount(tile, BuildingType.GasTank) > 0 ? Shelter.Sealed : Shelter.OpenPile;
                case StorageClass.Hazardous: return StorageCount(tile, BuildingType.SealedStore) > 0 ? Shelter.Inert : Shelter.OpenPile;   // the sealed store for hazardous solids is filled with argon or oil
                case StorageClass.SealedLiquid: return StorageCount(tile, BuildingType.SealedStore) > 0 ? Shelter.Sealed : Shelter.OpenPile;
                default: return StorageCount(tile, BuildingType.StorageYard) > 0 ? Shelter.Yard : Shelter.OpenPile;
            }
        }

        void StepChemistry(double dt)
        {
            if (!ChemistryEnabled || inventories == null) return;
            chemistryClock += dt;
            if (chemistryClock < ChemistryStepHours) return;
            double days = chemistryClock / 24.0; chemistryClock = 0.0;
            var places = inventories.Places.Where(p => p.Kind == PlaceKind.Depot || p.Kind == PlaceKind.Wreck).ToList();
            var envCache = new Dictionary<int, (ChemEnvironment Env, double Wet)>();
            foreach (PlaceId place in places)
            {
                int tile = place.Kind == PlaceKind.Wreck ? (wrecks.FirstOrDefault(w => w.Id == place.Id)?.Tile ?? -1) : place.Id;
                if (tile < 0) continue;
                if (!envCache.TryGetValue(tile, out var e))
                {
                    var (mean, sd) = HumidityClimateAt(tile);
                    envCache[tile] = e = (EnvironmentAt(tile), Chemistry_WetShare(tile, mean, sd));
                }
                MaterialStock stock = inventories.StockOf(place);
                foreach (Material m in Resources.Materials.All)
                {
                    double kg = stock[m];
                    if (kg <= 0.0) continue;
                    Shelter shelter = place.Kind == PlaceKind.Wreck ? Shelter.OpenPile : ShelterOf(tile, m);
                    ChemResult r = Resources.Chemistry.Step(m, kg, shelter, e.Env, e.Wet, days);
                    if (r.Delta.Count == 0) continue;
                    double change = 0.0;
                    foreach (var kv in r.Delta)
                    {
                        if (kv.Value < 0.0) { double taken = stock.Take(kv.Key, -kv.Value); change -= taken; Chemistry.LostKg[kv.Key] = (Chemistry.LostKg.TryGetValue(kv.Key, out double l) ? l : 0.0) + taken; }
                        else { stock.Add(kv.Key, kv.Value); change += kv.Value; }
                    }
                    Chemistry.StockChangeKg += change;
                    Chemistry.OxygenFromAirKg += r.OxygenFromAir; Chemistry.WaterFromAirKg += r.WaterFromAir; Chemistry.HydrogenVentedKg += r.HydrogenVented; Chemistry.WaterSublimatedKg += r.WaterSublimated;
                    foreach (var kv in r.Drained) Chemistry.DrainedKg[kv.Key] = (Chemistry.DrainedKg.TryGetValue(kv.Key, out double d) ? d : 0.0) + kv.Value;
                    WarnChemistry(place, tile, m, kg, r, shelter, days);
                }
            }
        }

        double Chemistry_WetShare(int tile, double mean, double sd) => Resources.Chemistry.WetShare(mean, sd, AnnualAirTemperature(tile));

        void WarnChemistry(PlaceId place, int tile, Material m, double kg, ChemResult r, Shelter shelter, double days)
        {
            double lost = 0.0; foreach (var kv in r.Delta) if (kv.Key == m && kv.Value < 0.0) lost = -kv.Value;
            if (lost <= 0.0 || days <= 0.0) return;
            double perMonth = 1.0 - Math.Pow(Math.Max(0.0, 1.0 - lost / kg), 30.0 / days);   // share lost in a month at this rate, never above 100 %
            int key = place.Kind == PlaceKind.Wreck ? -place.Id - 1 : place.Id;
            if (perMonth <= 0.01 || !chemistryWarned.Add((key, m))) return;
            string name = Resources.Materials.Name(m).ToLowerInvariant();
            if ((m == Material.Sodium || m == Material.Lithium) && shelter != Shelter.Sealed && shelter != Shelter.Inert)
                Log($"Tile {tile}", "Reactive metal exposed", perMonth * 100.0, "%/month", $"T{tile}", $"{kg:0} kg of {name} on tile {tile} is reacting with the damp air (about {perMonth * 100.0:0} % a month, ◐ humidity estimated): it needs a sealed store.", -1);
            else
                Log($"Tile {tile}", "Storage loss", perMonth * 100.0, "%/month", $"T{tile}", $"{kg:0} kg of {name} on tile {tile} is losing about {perMonth * 100.0:0.#} % a month ({Resources.Chemistry.Describe(m, shelter) ?? "the environment"}).", -1);
        }

        /// <summary>The loss rate of a material kept on a tile as it is now, share a year (zero when it is stable), for the tile panel; marked estimated while humidity is not measured.</summary>
        public double StorageLossPerYear(int tile, Material m)
        {
            Shelter s = ShelterOf(tile, m);
            var (mean, sd) = HumidityClimateAt(tile);
            ChemEnvironment env = new ChemEnvironment(AnnualAirTemperature(tile), mean, PressurePaAt(tile), OxygenFraction, CoastTile(tile), SurfaceGravityMs2, AirMolarMassKg);
            ChemResult r = Resources.Chemistry.Step(m, 1.0, s, env, Chemistry_WetShare(tile, mean, sd), 1.0);
            double lost = 0.0; foreach (var kv in r.Delta) if (kv.Key == m && kv.Value < 0.0) lost = -kv.Value;
            return 1.0 - Math.Pow(Math.Max(0.0, 1.0 - lost), 365.25);
        }

        /// <summary>Lines for the tile panel: each stored material that changes, with its rate and whether it is estimated (humidity is measured only where the weather is).</summary>
        public IReadOnlyList<string> StorageNotes(int tile)
        {
            var lines = new List<string>();
            if (!ChemistryEnabled || inventories == null || !inventories.Has(PlaceId.Depot(tile))) return lines;
            MaterialStock stock = inventories.StockOf(PlaceId.Depot(tile));
            string mark = WeatherMeasuredAt(tile) ? "●" : "◐";
            foreach (Material m in Resources.Materials.All)
            {
                if (stock[m] <= 0.0) continue;
                double loss = StorageLossPerYear(tile, m);
                Shelter s = ShelterOf(tile, m);
                string how = s == Shelter.Inert ? "inert store" : s == Shelter.Sealed ? "sealed" : s == Shelter.Yard ? "yard" : "open pile";
                string text = Resources.Chemistry.Describe(m, s);
                if (loss > 0.0005) lines.Add($"{Resources.Materials.Name(m)} {how} · losing ~{loss * 100.0:0.#} %/yr {mark}" + (mark == "◐" ? " (humidity estimated)" : ""));
                else if (text != null && text.StartsWith("stable")) lines.Add($"{Resources.Materials.Name(m)} {how} · {text}");
            }
            return lines;
        }
    }
}

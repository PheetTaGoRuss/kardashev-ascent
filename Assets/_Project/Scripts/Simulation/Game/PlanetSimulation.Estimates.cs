using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Research tied to the physics (decided 2026-11-23, director delegated; docs/design/research-station.md): until a quantity is
    /// measured the game shows an estimate with a range, and buildings run on the cautious end. The simulation always uses the true values.
    /// All ranges are game rules, TBD.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The share of relief the assumed scale can be off by: Earth twins and Mars are well mapped, other worlds scale Earth by 1/g (game rule, TBD).</summary>
        public const double ReliefSpreadWell = 0.10, ReliefSpreadAssumed = 0.30;
        /// <summary>The share a tide range seen only from orbit can be off by (game rule, TBD).</summary>
        public const double TideSpreadShare = 0.50;
        /// <summary>The share an ore grade can be off by before an assay (game rule, TBD).</summary>
        public const double GradeSpreadShare = 0.25;
        /// <summary>The cable rating never drops below this share while the air is unmeasured (game rule, TBD).</summary>
        public const double MinimumCableShare = 0.5;

        int EstimateKey(int salt)
        {
            int h = salt;
            foreach (char c in PlanetId ?? "") h = unchecked(h * 31 + c);
            return h;
        }

        /// <summary>Air pressure at the base, Pa: exact once measured, else the orbit brief's range.</summary>
        public KnowledgeEstimate PressureEstimate =>
            KnowledgeEstimate.Of(PressurePaAt(KnowledgeTile), PressurePaAt(KnowledgeTile) * OrbitalBrief.PressureSpreadShare * ExperimentRangeShare(ExperimentKind.AirPressure), false, EstimateKey(1));

        /// <summary>Mean air temperature at the base over a year, C: exact once measured, else the orbit brief's range.</summary>
        public KnowledgeEstimate TemperatureEstimate
        {
            get
            {
                var (lo, hi) = SurfaceTemperatureRange(KnowledgeTile);
                return KnowledgeEstimate.Of((lo + hi) / 2.0, OrbitalBrief.TemperatureSpreadC * ExperimentRangeShare(ExperimentKind.SurfaceTemperature), false, EstimateKey(2));
            }
        }

        /// <summary>The highest point of the planet, m: exact after the terrain relief survey, else the assumed scale with its range.</summary>
        public KnowledgeEstimate HighestPointEstimate
        {
            get
            {
                string id = (PlanetId ?? "").ToLowerInvariant();
                double spread = id == "mars" || id == "earth" || id == "viridia" ? ReliefSpreadWell : ReliefSpreadAssumed;
                double max = Relief.MaxM;
                return KnowledgeEstimate.Of(max, max * spread * ExperimentRangeShare(ExperimentKind.ReliefSurvey), false, EstimateKey(3));
            }
        }

        /// <summary>The open-sea tide range, m: exact after the tide gauge, else a range (0 and exact with no moon).</summary>
        public KnowledgeEstimate TideRangeEstimate
        {
            get
            {
                double open = Tides.EquilibriumRangeM(Moon, planetRadiusM);
                return KnowledgeEstimate.Of(open, open * TideSpreadShare * ExperimentRangeShare(ExperimentKind.TideGauge), false, EstimateKey(4));
            }
        }

        /// <summary>The grade of an ore at a surveyed tile, 0 to 1: exact after the ore assay, else a range.</summary>
        public KnowledgeEstimate GradeEstimate(int tileId, Material material)
        {
            double g = Deposits.GradeOf(tileId, material);
            if (material == Material.Rock) return new KnowledgeEstimate(g, 0.0);   // rock is everywhere on land: nothing to assay
            return KnowledgeEstimate.Of(g, g * GradeSpreadShare * ExperimentRangeShare(ExperimentKind.OreAssay), false, unchecked(EstimateKey(5) + tileId * 7 + (int)material));
        }

        /// <summary>What one mining robot digs out of a tile, kg per hour: the nominal rate times the grade, as a range until the assay.</summary>
        public KnowledgeEstimate MiningRateEstimate(int tileId, Material material)
        {
            KnowledgeEstimate g = GradeEstimate(tileId, material);
            double depletion = Deposits.GradeOf(tileId, material) > 0.0 ? Deposits.EffectiveGrade(tileId, material) / Deposits.GradeOf(tileId, material) : 1.0;
            return new KnowledgeEstimate(Robots.RobotSpecs.MiningKgPerHour * g.Value * depletion, Robots.RobotSpecs.MiningKgPerHour * g.HalfRange * depletion);
        }

        public System.Collections.Generic.IReadOnlyList<(int Tile, Material Material, double HeldKg)> ExhaustedDeposits()
        {
            var list = new System.Collections.Generic.List<(int, Material, double)>();
            for (int t = 0; t < TileCount; t++)
                foreach (Material m in Resources.Materials.Mined)
                    if (m != Material.Rock && Deposits.MinedKg(t, m) > 0.0 && Deposits.IsExhausted(t, m)) list.Add((t, m, Deposits.ReserveKg(t, m)));
            return list;
        }

        public System.Collections.Generic.IReadOnlyList<(int Tile, KnowledgeEstimate OreGrade, double ShareLeft)> BestDeposits(Material material, int count)
        {
            var list = new System.Collections.Generic.List<(int Tile, KnowledgeEstimate OreGrade, double ShareLeft)>();
            for (int t = 0; t < TileCount; t++)
                if (!GetTile(t).IsWater && Deposits.IsKnown(t, material) && Deposits.GradeOf(t, material) > 0.0 && !Deposits.IsExhausted(t, material))
                    list.Add((t, OreGradeEstimate(t, material), Deposits.RemainingShare(t, material)));
            list.Sort((a, b) => (b.OreGrade.Value * b.ShareLeft).CompareTo(a.OreGrade.Value * a.ShareLeft));
            return list.GetRange(0, Math.Min(count, list.Count));
        }

        /// <summary>Serve the chargers on each island from its own sources, shared in proportion to what each asked; the rest is curtailed (no battery on an island).</summary>
        readonly HashSet<(int Tile, int Slot)> islandBatteries = new HashSet<(int, int)>();
        readonly Dictionary<(int Tile, int Slot), double> islandBatteryWh = new Dictionary<(int, int), double>();

        void AddBatteryCapacity(int tileId, int slot)
        {
            if (WiredGrid && !restoringState && !IsPowered(tileId)) { islandBatteries.Add((tileId, slot)); islandBatteryWh[(tileId, slot)] = 0.0; }
            else Grid.Battery.AddCapacity(BatteryUnitWh);
        }

        /// <summary>Energy held in the batteries on this tile's island, Wh, and their capacity.</summary>
        public (double StoredWh, double CapacityWh) IslandBatteryOf(int tile)
        {
            int id = IslandIdOf(tile);
            double stored = 0.0, cap = 0.0;
            if (id < 0) return (0.0, 0.0);
            foreach (var k in islandBatteries)
                if (IslandIdOf(k.Tile) == id && !(Build.QueueCount > 0 && Build.IsQueued(k.Tile, k.Slot))) { cap += BatteryUnitWh; stored += islandBatteryWh.TryGetValue(k, out double w) ? w : 0.0; }
            return (stored, cap);
        }

        /// <summary>The share of an island source's power that reaches its chargers: the same line loss per tile as the lander grid (resistive, 2 % per tile of line), counted over the tiles between the nearest source and the charger.</summary>
        double IslandLineFactor(int sourceTile, int chargerTile)
        {
            if (sourceTile == chargerTile) return 1.0;
            var dist = new Dictionary<int, int> { [sourceTile] = 0 };
            var queue = new Queue<int>(); queue.Enqueue(sourceTile);
            while (queue.Count > 0)
            {
                int a = queue.Dequeue();
                if (a == chargerTile) break;
                if (islandAdjacent != null && islandAdjacent.TryGetValue(a, out List<int> nbs))
                    foreach (int nb in nbs) if (!dist.ContainsKey(nb)) { dist[nb] = dist[a] + 1; queue.Enqueue(nb); }
            }
            return dist.TryGetValue(chargerTile, out int hops) ? Math.Pow(1.0 - LineLossPerHop, hops) : 1.0;
        }

        Dictionary<int, double> ServeIslands(double dt, out double generatedW, out double servedW, out double curtailedW)
        {
            generatedW = servedW = curtailedW = 0.0;
            if (!WiredGrid) return null;
            var requests = Upkeep.TakeIslandRequests(Workforce, tile => IslandIdOf(tile) >= 0);
            var offers = new Dictionary<int, double>();
            var seenIslands = new HashSet<int>();
            foreach (var b in WireableBuildings())
            {
                if (!IsWireOutput(b.Type)) continue;
                int id = IslandIdOf(b.Tile);
                if (id < 0 || !seenIslands.Add(id)) continue;
                double gen = IslandGenerationW(b.Tile);
                generatedW += gen;
                gen *= IslandLineFactor(b.Tile, FirstChargerTile(id, b.Tile));
                var (stored, cap) = IslandBatteryOf(b.Tile);
                var here = new List<int>();
                double total = 0.0;
                foreach (var kv in requests)
                {
                    Robot r = Workforce.GetRobot(kv.Key);
                    if (r != null && r.Facility.HasValue && IslandIdOf(r.Facility.Value.Tile) == id) { here.Add(kv.Key); total += kv.Value; }
                }
                // the island battery covers a shortfall and takes the surplus (kept simple: 95 % charging efficiency, a game rule, TBD)
                double fromBattery = total > gen && stored > 0.0 ? Math.Min(total - gen, stored / dt) : 0.0;
                double share = total > 0.0 ? Math.Min(1.0, (gen + fromBattery) / total) : 0.0;
                foreach (int rid in here) { offers[rid] = requests[rid] * share; servedW += requests[rid] * share; requests.Remove(rid); }
                double surplus = Math.Max(0.0, gen - total * share);
                double toBattery = cap > 0.0 ? Math.Min(surplus, Math.Max(0.0, cap - stored) / (0.95 * dt)) : 0.0;
                SettleIslandBattery(b.Tile, -fromBattery * dt + 0.95 * toBattery * dt);
                curtailedW += surplus - toBattery;
            }
            foreach (var kv in requests) offers[kv.Key] = 0.0;   // an island with no source serves nothing
            return offers;
        }

        int FirstChargerTile(int islandId, int fallback)
        {
            foreach (var c in stations) if (IslandIdOf(c.Tile) == islandId) return c.Tile;
            return fallback;
        }

        /// <summary>Add (or take) energy to the island's batteries, spread over them in order.</summary>
        void SettleIslandBattery(int tile, double deltaWh)
        {
            int id = IslandIdOf(tile);
            foreach (var k in new List<(int Tile, int Slot)>(islandBatteries))
            {
                if (Math.Abs(deltaWh) < 1e-12) break;
                if (IslandIdOf(k.Tile) != id) continue;
                double cur = islandBatteryWh.TryGetValue(k, out double w) ? w : 0.0;
                double next = Math.Max(0.0, Math.Min(BatteryUnitWh, cur + deltaWh));
                deltaWh -= next - cur;
                islandBatteryWh[k] = next;
            }
        }

        public double DepositShareLeft(int tileId, Material material) => Deposits.RemainingShare(tileId, material);

        /// <summary>Rock moved and tailings left so far, kg (reporting; the product is in the stock).</summary>
        public double RockMovedKg { get; private set; }
        public double TailingsKg { get; private set; }
        /// <summary>Robot hours spent in mining batches so far; times the mining power it is the energy mining took (reporting, B4).</summary>
        public double MiningWorkHours { get; private set; }
        /// <summary>Crushing and grinding energy spent so far, Wh (Bond's law; reporting, B4).</summary>
        public double ComminutionWh { get; private set; }

        /// <summary>Crushing and grinding energy per tonne of product from this deposit now, kWh (zero when it needs none): a range until the ore assay.</summary>
        public KnowledgeEstimate ComminutionEstimate(int tileId, Material material)
        {
            KnowledgeEstimate og = OreGradeEstimate(tileId, material);
            double lo = Comminution.KWhPerTonneProduct(material, Math.Max(1e-9, og.Value + og.HalfRange)), hi = Comminution.KWhPerTonneProduct(material, Math.Max(1e-9, og.Value - og.HalfRange));
            double mid = Comminution.KWhPerTonneProduct(material, og.Value);
            double now = Deposits.OreGrade(tileId, material) > 0.0 ? Deposits.EffectiveGrade(tileId, material) / Deposits.GradeOf(tileId, material) : 1.0;
            return og.IsExact ? new KnowledgeEstimate(mid / now, 0.0) : new KnowledgeEstimate(mid / now, Math.Max(hi - mid, mid - lo) / now);
        }

        /// <summary>
        /// What a deposit holds, kg of product: a range until the ore assay measures it (the tonnage follows the grade, so it carries the grade's spread), exact after. The share left
        /// (1 before any mining) is always known to the colony that mines it.
        /// </summary>
        public KnowledgeEstimate ReserveEstimate(int tileId, Material material)
        {
            double reserve = Deposits.ReserveKg(tileId, material);
            double g = Deposits.GradeOf(tileId, material);
            if (reserve <= 0.0 || double.IsInfinity(reserve) || g <= 0.0) return new KnowledgeEstimate(reserve, 0.0);
            KnowledgeEstimate ge = GradeEstimate(tileId, material);
            return new KnowledgeEstimate(reserve * ge.Value / g, reserve * ge.HalfRange / g);
        }

        /// <summary>The ore grade, kg of product per kg of rock, as a range until the assay.</summary>
        public KnowledgeEstimate OreGradeEstimate(int tileId, Material material)
        {
            double t = DepositMap.TypicalOreGrade(material);
            KnowledgeEstimate ge = GradeEstimate(tileId, material);
            return new KnowledgeEstimate(t * ge.Value, t * ge.HalfRange);
        }

        /// <summary>True where Peek's law is not valid (the Paschen region, with a labelled fallback): the cable rating there is a game rule, not the law's result (UI marks, outside model).</summary>
        public bool CableOutsideModel
        {
            get
            {
                double p = planet.SurfacePressureEarth * CableEnvironment.StandardPressurePa;
                return CableEnvironment.RegimeOf(CableEnvironment.RelativeAirDensity(p, 25.0)) == CableEnvironment.Regime.Paschen;
            }
        }

        /// <summary>
        /// The share of its rating a cable is given while the air is unmeasured: the voltage derate at the cautious (thin) end of the
        /// pressure range over the derate at the true pressure, never below <see cref="MinimumCableShare"/>. 1 once the air pressure is measured.
        /// </summary>
        public double CableDesignShare
        {
            get
            {
                double rangeShare = ExperimentRangeShare(ExperimentKind.AirPressure);
                if (!SafetyMarginsEnabled || rangeShare <= 0.0) return 1.0;
                double p = planet.SurfacePressureEarth * CableEnvironment.StandardPressurePa;
                double delta = CableEnvironment.RelativeAirDensity(p, 25.0);
                double low = CableEnvironment.RelativeAirDensity(p * (1.0 - OrbitalBrief.PressureSpreadShare * rangeShare), 25.0);
                double area = 500e-6;
                double now = CableEnvironment.VoltageDerate(delta, area, out _), cautious = CableEnvironment.VoltageDerate(low, area, out _);
                return now <= 0.0 ? 1.0 : Math.Max(MinimumCableShare, Math.Min(1.0, cautious / now));
            }
        }
    }
}

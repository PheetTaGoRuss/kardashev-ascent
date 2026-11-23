using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>The kinds of line the flow knows: the two DC tiers and the two AC classes (decided 2026-11-23: 33 kV and 500 kV AC at 50 Hz).</summary>
    public enum LineKind { Plain, High, Ac33, Ac500 }

    /// <summary>
    /// AC on the planet grid (electrical redesign, KI-44). AC lines carry power at 33 kV or 500 kV between tiles; a Rectifier or an Inverter on a
    /// tile joins an AC line to the DC grid there; a Transformer joins the two AC classes. Power flows from the generators toward the lander,
    /// so moving away from the lander the first crossing from DC to AC is a Rectifier (AC power from beyond becomes DC) and from AC to DC an
    /// Inverter (the generating end sends DC power onto the AC line). A crossing loses power by the converter's efficiency (<see cref="VoltageLadder"/>,
    /// TBD); the loss is heat. A three-phase line behaves in the flow like a single conductor at its line voltage (3 I^2 R = P^2 R / V^2), with the
    /// rating counted per phase. Without AC lines or converters nothing here runs and the grid is exactly as before.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The milestone that opens AC (game rule, TBD): the next after the high-voltage line's.</summary>
        public const int AcMilestone = 2;

        readonly Dictionary<(int Tile, int Slot), BuildingType> converterBuildings = new Dictionary<(int, int), BuildingType>();
        readonly Dictionary<(int, int), LineKind> edgeKind = new Dictionary<(int, int), LineKind>();
        readonly HashSet<int> reachedTiles = new HashSet<int>();
        readonly Dictionary<int, double> tileConvEff = new Dictionary<int, double>();

        public static bool IsConverter(BuildingType t) => t == BuildingType.Transformer || t == BuildingType.Inverter || t == BuildingType.Rectifier;

        bool AcActive => wireBook.AcLines.Count > 0 || converterBuildings.Count > 0;

        // ---- line kinds ----
        public static double KindVolts(LineKind k) => k == LineKind.Plain || k == LineKind.Ac33 ? PlainLineVolts : HighLineVolts;
        public static bool IsAc(LineKind k) => k == LineKind.Ac33 || k == LineKind.Ac500;
        static CableSpec KindSpec(LineKind k) => k == LineKind.Plain || k == LineKind.Ac33 ? PlainLineSpec : HighLineSpec;
        /// <summary>The flow counts a three-phase line as one conductor at its line voltage, so its per-phase rating is worth sqrt(3) in flow current.</summary>
        static double KindRatingFactor(LineKind k) => IsAc(k) ? Math.Sqrt(3.0) : 1.0;
        /// <summary>What a line of this kind carries at its rating, W.</summary>
        public static double KindCapacityW(LineKind k) => KindVolts(k) * KindSpec(k).RatingA * KindRatingFactor(k);

        /// <summary>The converter efficiency across an AC class (game rule, TBD: the catalogue figures).</summary>
        public static double ConverterEfficiency(BuildingType type, int acMedium)
        {
            if (type == BuildingType.Transformer) return VoltageLadder.Converters.PowerTransformer(true).Efficiency;
            var spec = acMedium == 1
                ? (type == BuildingType.Inverter ? VoltageLadder.Converters.Inverter33k() : VoltageLadder.Converters.Rectifier33k())
                : (type == BuildingType.Inverter ? VoltageLadder.Converters.Inverter500k() : VoltageLadder.Converters.Rectifier500k());
            return spec.Efficiency;
        }

        // ---- placing ----
        public bool PlaceConverter(BuildingType type, int tileId, int slot)
        {
            if (!IsConverter(type) || !CanPlace(type, tileId, slot)) return false;
            StartSite(tileId, slot, BuildBill(type), BuildTimes.Hours(type));
            converterBuildings[(tileId, slot)] = type;
            Occupy(tileId, FootprintFor(type, tileId, slot));
            wiringStamp = double.NaN; flowDirty = true;
            Settle((tileId, slot));
            return true;
        }

        bool RemoveConverter(int tileId, int slot)
        {
            if (!converterBuildings.TryGetValue((tileId, slot), out BuildingType type)) return false;
            Dismantle(tileId, slot, type);
            converterBuildings.Remove((tileId, slot));
            Vacate(tileId, slot);
            wiringStamp = double.NaN; flowDirty = true;
            return true;
        }

        public bool PlaceAcLine(int tileId, int slot, int targetTile, int acClass)
        {
            if ((acClass != VoltageLadder.Ac33k && acClass != VoltageLadder.Ac500k) || !CanLinkAc(tileId, targetTile, acClass) || !CanPlace(BuildingType.AcLine, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.AcLine), BuildTimes.Hours(BuildingType.AcLine));
            wireBook.AcLines.Add((tileId, slot));
            wireBook.AcClass[(tileId, slot)] = acClass;
            linkTargets[(tileId, slot)] = targetTile;
            Occupy(tileId, FootprintFor(BuildingType.AcLine, tileId, slot));
            wiringStamp = double.NaN; flowDirty = true;
            Settle((tileId, slot));
            return true;
        }

        bool CanLinkAc(int a, int b, int acClass)
        {
            if (a < 0 || a >= TileCount || b < 0 || b >= TileCount || a == b || Array.IndexOf(Sphere.Neighbors[a], b) < 0) return false;
            bool Wirable(int t) { TerrainType g = GetTile(t).Terrain; return Terrains.IsBuildableLand(g) || g == TerrainType.ShallowOcean; }
            return Wirable(a) && Wirable(b) && !wireBook.LinkedAc(a, b, acClass);
        }

        /// <summary>Runs an AC line between two neighbouring tiles at 33 kV or 500 kV; the sea limit applies (an AC link under water is refused beyond 100 km). False: see <see cref="WireProblem"/>.</summary>
        public bool PlaceAcWire(int fromTile, int toTile, int acClass)
        {
            WireProblem = null;
            if (!CanLinkAc(fromTile, toTile, acClass)) { WireProblem = "Pick two neighbouring land or shallow-sea tiles with no AC line of this class between them."; return false; }
            string blocker = KnowledgeBlocker(BuildingType.AcLine);
            if (blocker != null) { WireProblem = blocker; return false; }
            bool sea = GetTile(fromTile).Terrain == TerrainType.ShallowOcean || GetTile(toTile).Terrain == TerrainType.ShallowOcean;
            double lengthM = ElectricalGraph.CableLengthM(Sphere.Centers[fromTile], Sphere.Centers[toTile], planetRadiusM);
            string subsea = SubseaRules.Check(NetworkType.Ac, lengthM, sea);
            if (subsea != null) { WireProblem = subsea; return false; }
            int host = IsPowered(toTile) && !IsPowered(fromTile) ? toTile : fromTile;
            int other = host == fromTile ? toTile : fromTile;
            for (int pass = 0; pass < 2; pass++)
            {
                int h = pass == 0 ? host : other, o = pass == 0 ? other : host;
                for (int s = 0; s < SlotCount(h); s++)
                    if (CanPlace(BuildingType.AcLine, h, s)) return PlaceAcLine(h, s, o, acClass);
            }
            WireProblem = "Neither tile has a free slot for the line's post.";
            return false;
        }

        /// <summary>The AC lines as (from tile, to tile, finished, class).</summary>
        public IEnumerable<(int From, int To, bool Built, int AcClass)> AcLinks()
        {
            foreach (var key in wireBook.AcLines)
                if (linkTargets.TryGetValue(key, out int to))
                    yield return (key.Tile, to, HasFinishedLine(key), wireBook.AcClass.TryGetValue(key, out int c) ? c : VoltageLadder.Ac500k);
        }

        /// <summary>The converters standing on the tile (finished or not).</summary>
        public IEnumerable<BuildingType> ConvertersOn(int tileId)
        {
            foreach (var kv in converterBuildings) if (kv.Key.Tile == tileId) yield return kv.Value;
        }

        // ---- reach through the media (DC, AC 33 kV, AC 500 kV) ----

        /// <summary>
        /// Replaces the plain breadth-first reach when the colony has AC. Nodes are (tile, medium); links join nodes of one medium; a converter
        /// on the tile crosses between media. Fills the same maps the DC reach fills (powered tiles, hops, parent) plus the reached tiles,
        /// the kind of every active link and the product of the converter efficiencies on the way to each tile.
        /// </summary>
        void ReachMedia()
        {
            var adj = new Dictionary<(int, int), List<(int Tile, LineKind Kind)>>();
            var linkList = new List<(int A, int B, int M, LineKind Kind)>();
            void AddLink(int a, int b, int m, LineKind kind)
            {
                if (!adj.TryGetValue((a, m), out var la)) adj[(a, m)] = la = new List<(int, LineKind)>();
                if (!adj.TryGetValue((b, m), out var lb)) adj[(b, m)] = lb = new List<(int, LineKind)>();
                la.Add((b, kind)); lb.Add((a, kind));
                linkList.Add((a, b, m, kind));
            }
            foreach (var key in powerLines) if (HasFinishedLine(key) && LineWorks(key) && linkTargets.TryGetValue(key, out int to)) AddLink(key.Tile, to, 0, LineKind.Plain);
            foreach (var key in highVoltageLines) if (HasFinishedLine(key) && LineWorks(key) && linkTargets.TryGetValue(key, out int to)) AddLink(key.Tile, to, 0, LineKind.High);
            foreach (var key in wireBook.AcLines)
                if (HasFinishedLine(key) && LineWorks(key) && linkTargets.TryGetValue(key, out int to))
                {
                    bool c33 = wireBook.AcClass.TryGetValue(key, out int cls) && cls == VoltageLadder.Ac33k;
                    AddLink(key.Tile, to, c33 ? 1 : 2, c33 ? LineKind.Ac33 : LineKind.Ac500);
                }
            var inverter = new HashSet<int>(); var rectifier = new HashSet<int>(); var transformer = new HashSet<int>();
            foreach (var kv in converterBuildings)
            {
                if (!HasFinishedLine(kv.Key)) continue;   // a converter under construction converts nothing
                (kv.Value == BuildingType.Inverter ? inverter : kv.Value == BuildingType.Rectifier ? rectifier : transformer).Add(kv.Key.Tile);
            }

            var seen = new Dictionary<(int, int), (int Hops, int Parent, double Eff)>();
            var queue = new Queue<(int Tile, int M)>();
            seen[(BaseTileId, 0)] = (0, -1, 1.0);
            queue.Enqueue((BaseTileId, 0));
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                var info = seen[n];
                if (adj.TryGetValue(n, out var next))
                    foreach (var nb in next)
                        if (!seen.ContainsKey((nb.Tile, n.M)))
                        {
                            seen[(nb.Tile, n.M)] = (info.Hops + 1, n.Tile, info.Eff);
                            queue.Enqueue((nb.Tile, n.M));
                        }
                void Cross(int toM, double eff)
                {
                    if (seen.ContainsKey((n.Tile, toM))) return;
                    seen[(n.Tile, toM)] = (info.Hops, info.Parent, info.Eff * eff);
                    queue.Enqueue((n.Tile, toM));
                }
                if (n.M == 0)
                {
                    // away from the lander, DC to AC is where AC power from beyond is turned into DC: a Rectifier
                    if (rectifier.Contains(n.Tile))
                        for (int ac = 1; ac <= 2; ac++)
                            if (adj.ContainsKey((n.Tile, ac))) Cross(ac, ConverterEfficiency(BuildingType.Rectifier, ac));
                }
                else
                {
                    if (inverter.Contains(n.Tile)) Cross(0, ConverterEfficiency(BuildingType.Inverter, n.M));
                    int other = 3 - n.M;
                    if (transformer.Contains(n.Tile) && adj.ContainsKey((n.Tile, other))) Cross(other, ConverterEfficiency(BuildingType.Transformer, n.M));
                }
            }

            foreach (var kv in seen)
            {
                reachedTiles.Add(kv.Key.Item1);
                if (kv.Key.Item2 != 0) continue;
                poweredTiles.Add(kv.Key.Item1);
                lineHops[kv.Key.Item1] = kv.Value.Hops;
                lineParent[kv.Key.Item1] = kv.Value.Parent;
                tileConvEff[kv.Key.Item1] = kv.Value.Eff;
            }
            foreach (var l in linkList)
            {
                if (!seen.ContainsKey((l.A, l.M)) || !seen.ContainsKey((l.B, l.M))) continue;
                var edge = (Math.Min(l.A, l.B), Math.Max(l.A, l.B));
                double cap = KindCapacityW(l.Kind);
                if (!edgeCapacity.TryGetValue(edge, out double have) || cap > have)
                {
                    edgeCapacity[edge] = cap;
                    edgeKind[edge] = l.Kind;
                }
            }
        }

        /// <summary>Conversion loss at the converters on the way from the generators, W (heat), at the last flow.</summary>
        public double ConverterLossW { get { EnsureFlow(); return converterLossW; } }
        double converterLossW;

        void CaptureConverters(SimulationState state)
        {
            foreach (var kv in converterBuildings)
                state.Buildings.Add(new BuildingRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Type = kv.Value });
        }
    }
}

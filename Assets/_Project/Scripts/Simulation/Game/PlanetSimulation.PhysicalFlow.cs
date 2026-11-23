using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Electrical;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Electrical redesign stage 3: the lander network's flow worked out from cable physics instead of a tree with 2 % a hop.
    /// Director decisions (2026-11-20): the lander bus is the slack node (D1, G1); an overloaded cable curtails the generators behind
    /// it just enough to bring the current back to its rating (G1); candidate classes approved: plain line 33 kV, high-voltage line
    /// 500 kV, one 500 mm2 copper conductor per tile, ratings 600 A and 3000 A (D2, D4). Cable length is the real distance between
    /// the tile centres times the route factor (D3).
    /// Game rule, TBD: where a plain and a high-voltage line meet, the join behaves as an ideal transformer (no loss), because
    /// converters are not built yet. In per-unit terms every cable is referred to the plain-line base voltage.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>Plain power line, nominal V (candidate, TBD).</summary>
        public const double PlainLineVolts = 33e3;
        /// <summary>High-voltage line, nominal V (candidate, TBD).</summary>
        public const double HighLineVolts = 500e3;
        /// <summary>Plain line current rating, A (candidate, TBD): 600 A at 33 kV is about today's 20 MW.</summary>
        public const double PlainLineRatingA = 600.0;
        /// <summary>High-voltage line current rating, A (candidate, TBD).</summary>
        public const double HighLineRatingA = 3000.0;
        /// <summary>One 500 mm2 copper conductor (candidate, TBD).</summary>
        static readonly CableSpec PlainLineSpec = CableSpec.FromSquareMillimetres("Copper 500 mm2, 33 kV", ConductorMaterial.Copper, 500.0, 1, PlainLineRatingA);
        static readonly CableSpec HighLineSpec = CableSpec.FromSquareMillimetres("Copper 500 mm2, 500 kV", ConductorMaterial.Copper, 500.0, 1, HighLineRatingA);

        FlowBranch[] lastBranches = Array.Empty<FlowBranch>();
        Dictionary<int, int> lastIndex = new Dictionary<int, int>();
        int lastNodeCount, lastSlack;

        /// <summary>Power used to probe the efficiency of a tile that makes nothing yet, W (game rule, TBD).</summary>
        public const double ProbePowerW = 1e6;

        /// <summary>Share of ProbePowerW that a generator on this (powered, empty) tile would deliver to the lander over the last solved lines.</summary>
        double ProbeEfficiency(int tile)
        {
            if (tile == BaseTileId) return 1.0;
            if (!lastIndex.TryGetValue(tile, out int i)) return 0.0;
            var inj = new double[lastNodeCount];
            inj[i] = ProbePowerW;
            var r = DcFlow.Solve(lastNodeCount, lastBranches, inj, lastSlack, PlainLineVolts);
            return r.Converged ? Math.Max(0.0, -r.SlackPowerW) / ProbePowerW * (tileConvEff.TryGetValue(tile, out double ce) ? ce : 1.0) : 0.0;
        }

        readonly HashSet<(int, int)> highEdges = new HashSet<(int, int)>();

        /// <summary>
        /// Conductor temperature of a link, C: the air temperature at its two ends (electrical stage 6: a cold planet makes cheaper
        /// transmission, a hot one dearer). The heating from the current itself comes with the thermal model (stage 8).
        /// </summary>
        double CableCelsius(int tileA, int tileB)
        {
            if (edgeTemperature.TryGetValue((Math.Min(tileA, tileB), Math.Max(tileA, tileB)), out double known)) return known;
            double m = CurrentMeanAnomaly();
            return 0.5 * (AirTemperatureAt(tileA, m) + AirTemperatureAt(tileB, m));
        }

        sealed class FlowEdge
        {
            public int A, B;           // tile ids
            public LineKind Kind;
            public double Volts => KindVolts(Kind);
        }

        /// <summary>
        /// Solves the lander's network for the power each generator tile (own) makes. Fills tileFactor, tileFlow and overloadedTiles;
        /// returns (delivered, loss, curtailed) in W.
        /// </summary>
        (double Delivered, double Loss, double Curtailed) SolvePhysicalFlow(Dictionary<int, double> own)
        {
            // Nodes: powered tiles, in tile order. Edges: the finished links between them, in tile order.
            // Power made beyond a converter loses the converter's share first (heat); the solver then sees what enters the lines.
            double convLoss = 0.0;
            var scaled = new Dictionary<int, double>();
            foreach (var kv in own)
            {
                double eta = tileConvEff.TryGetValue(kv.Key, out double ce) ? ce : 1.0;
                scaled[kv.Key] = kv.Value * eta;
                convLoss += kv.Value - kv.Value * eta;
            }
            own = scaled;
            converterLossW = convLoss;
            if (!AcActive) { reachedTiles.Clear(); reachedTiles.UnionWith(poweredTiles); }
            var tiles = new List<int>(reachedTiles);
            tiles.Sort();
            var index = new Dictionary<int, int>();
            for (int i = 0; i < tiles.Count; i++) index[tiles[i]] = i;
            var edges = new List<FlowEdge>();
            var keys = new List<(int, int)>(edgeCapacity.Keys);
            keys.Sort();
            foreach (var k in keys)
                if (index.ContainsKey(k.Item1) && index.ContainsKey(k.Item2))
                    edges.Add(new FlowEdge { A = k.Item1, B = k.Item2, Kind = edgeKind.TryGetValue(k, out var ek) ? ek : LineKind.Plain });
            var branches = new FlowBranch[edges.Count];
            for (int e = 0; e < edges.Count; e++)
            {
                var fe = edges[e];
                var spec = KindSpec(fe.Kind);
                double lengthM = ElectricalGraph.CableLengthM(Sphere.Centers[fe.A], Sphere.Centers[fe.B], planetRadiusM);
                double ratio = PlainLineVolts / fe.Volts;
                // Per-unit on the plain-line base: resistance scales with (Vbase/V)^2, current with V/Vbase.
                branches[e] = new FlowBranch(index[fe.A], index[fe.B], spec.ResistanceOhm(lengthM, CableCelsius(fe.A, fe.B)) * ratio * ratio, spec.RatingA * KindRatingFactor(fe.Kind) * fe.Volts / PlainLineVolts);
            }

            int slack = index[BaseTileId];
            lastBranches = branches; lastIndex = index; lastNodeCount = tiles.Count; lastSlack = slack;
            var gens = new List<int>();   // powered generator tiles other than the lander tile, in tile order
            foreach (int t in tiles) if (t != BaseTileId && poweredTiles.Contains(t) && own.TryGetValue(t, out double p) && p > 0.0) gens.Add(t);
            var f = new Dictionary<int, double>();
            foreach (int g in gens) f[g] = 1.0;
            var overEdges = new HashSet<int>();

            double[] Inject(Dictionary<int, double> factors, int only = -1)
            {
                var inj = new double[tiles.Count];
                foreach (int g in gens)
                    if (only < 0 || g == only) inj[index[g]] = own[g] * factors[g];
                return inj;
            }

            DcFlowResult total = null;
            for (int iteration = 0; iteration < 40; iteration++)
            {
                total = DcFlow.Solve(tiles.Count, branches, Inject(f), slack, PlainLineVolts);
                if (!total.Converged)
                {
                    // Beyond what the lines can carry at all: back every generator off together and try again.
                    foreach (int g in gens) f[g] *= 0.8;
                    continue;
                }
                int worst = -1; double worstRatio = 1.0;
                for (int e = 0; e < branches.Length; e++)
                {
                    if (!total.BranchOverloaded[e]) continue;
                    double ratio = branches[e].RatingA / Math.Abs(total.BranchCurrentA[e]);
                    if (ratio < worstRatio) { worstRatio = ratio; worst = e; }
                }
                if (worst < 0) break;
                overEdges.Add(worst);
                // Curtail the generators whose power flows through that cable, each by the ratio that brings it back to rating.
                foreach (int g in gens)
                {
                    var alone = DcFlow.Solve(tiles.Count, branches, Inject(f, g), slack, PlainLineVolts);
                    if (alone.Converged && Math.Abs(alone.BranchCurrentA[worst]) > 1e-9) f[g] *= worstRatio * 0.9995;
                }
            }
            if (total == null || !total.Converged)
                total = DcFlow.Solve(tiles.Count, branches, new double[tiles.Count], slack, PlainLineVolts);

            double ownBase = own.TryGetValue(BaseTileId, out double pb) ? pb : 0.0;
            double deliveredFromLines = Math.Max(0.0, -total.SlackPowerW);
            // Share each generator tile gets of what arrives: its own alone-efficiency, normalised so the shares add up to the total.
            var eff = new Dictionary<int, double>();
            double weighted = 0.0;
            foreach (int g in gens)
            {
                var alone = DcFlow.Solve(tiles.Count, branches, Inject(f, g), slack, PlainLineVolts);
                double e = alone.Converged && own[g] * f[g] > 0.0 ? Math.Max(0.0, -alone.SlackPowerW) / (own[g] * f[g]) : 0.0;
                eff[g] = e;
                weighted += own[g] * f[g] * e;
            }
            double scale = weighted > 0.0 ? deliveredFromLines / weighted : 0.0;
            double curtailed = 0.0;
            foreach (int g in gens)
            {
                tileFactor[g] = f[g] * eff[g] * scale * (tileConvEff.TryGetValue(g, out double ge) ? ge : 1.0);
                curtailed += own[g] * (1.0 - f[g]);
            }
            tileFactor[BaseTileId] = 1.0;

            // What each tile's line toward the lander carries, and which lines are at their limit.
            var edgeAt = new Dictionary<(int, int), int>();
            for (int e = 0; e < edges.Count; e++) edgeAt[(Math.Min(edges[e].A, edges[e].B), Math.Max(edges[e].A, edges[e].B))] = e;
            foreach (int t in tiles)
            {
                if (t == BaseTileId || !ParentOfTile(t, out int parent) || parent < 0) continue;
                if (!edgeAt.TryGetValue((Math.Min(t, parent), Math.Max(t, parent)), out int e)) continue;
                tileFlow[t] = Math.Abs(total.BranchCurrentA[e]) * PlainLineVolts;   // power on the plain-line base equals V x I in any class
                if (overEdges.Contains(e)) overloadedTiles.Add(t);
            }
            tileFlow[BaseTileId] = deliveredFromLines + ownBase;
            UpdateCableHealth(edges, total.BranchCurrentA);
            return (deliveredFromLines + ownBase, total.TotalLossW + convLoss, curtailed);
        }
    }
}

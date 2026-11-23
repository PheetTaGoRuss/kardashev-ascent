using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Depot siting advice (L3, decided 2026-11-24, director delegated; docs/design/logistics-routes.md). The planner only advises; the player
    /// builds a storage yard where they choose. For each mine the ore goes to the lander; a depot D turns that haul into mine-to-D plus D-to-lander.
    /// A site is worth the energy per day it saves over the direct haul (driving, climb at the high end of the relief range, heater), ranked
    /// best first. It saves energy when the direct route climbs a ridge or crosses cold ground that a route through D avoids. Game rules, TBD.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>A suggested depot site: the tile and what it saves, Wh per day, over hauling the mines' ore direct to the lander.</summary>
        public readonly struct DepotAdvice
        {
            public readonly int Tile;
            public readonly double SavedWhPerDay;
            public DepotAdvice(int tile, double saved) { Tile = tile; SavedWhPerDay = saved; }
        }

        /// <summary>How many hops from a mine route a candidate depot may lie (game rule, TBD).</summary>
        const int DepotCandidateHops = 2;

        double RoundTripWhAlong(int from, List<int> route, double cargoKg)
        {
            double outWh = LegAlong(from, route, cargoKg).TotalWh;
            // back the same way, empty
            var path = new List<int> { from }; path.AddRange(route);
            path.Reverse();
            double backWh = LegAlong(path[0], path.Skip(1).ToList(), 0.0).TotalWh;
            return outWh + backWh;
        }

        public IReadOnlyList<(int Tile, double SavedWhPerDay)> DepotSites(int max) => DepotSuggestions(max).Select(a => (a.Tile, a.SavedWhPerDay)).ToList();

        /// <summary>The best places for a depot, by the energy per day they save, best first. Empty with no mining orders.</summary>
        public IReadOnlyList<DepotAdvice> DepotSuggestions(int max = 3)
        {
            var result = new List<DepotAdvice>();
            if (Routes == null) return result;
            double payload = BasePayloadKg;
            var flows = new List<(int Tile, double KgPerDay, List<int> Direct, double DirectWhPerKg)>();
            foreach (var m in orders.MiningSnapshot())
            {
                if (m.Tile == BaseTileId) continue;
                List<int> direct = Routes.Route(m.Tile, BaseTileId);
                if (direct == null) continue;
                double kgDay = RobotSpecs.MiningKgPerHour * Deposits.GradeOf(m.Tile, m.Material) * 24.0;
                if (kgDay <= 0.0) continue;
                flows.Add((m.Tile, kgDay, direct, RoundTripWhAlong(m.Tile, direct, payload) / payload));
            }
            if (flows.Count == 0) return result;
            // candidates: open land tiles near the routes of the flows
            var cand = new HashSet<int>();
            foreach (var f in flows)
            {
                var frontier = new List<int>(f.Direct) { f.Tile };
                for (int hop = 0; hop <= DepotCandidateHops; hop++)
                {
                    var next = new List<int>();
                    foreach (int t in frontier)
                        if (cand.Add(t)) next.AddRange(Sphere.Neighbors[t]);
                    frontier = next;
                }
            }
            foreach (int d in cand.OrderBy(t => t))
            {
                if (d == BaseTileId || GetTile(d).IsWater || !IsOpenGround(d)) continue;
                double saved = 0.0;
                foreach (var f in flows)
                {
                    List<int> toD = Routes.Route(f.Tile, d);
                    List<int> fromD = Routes.Route(d, BaseTileId);
                    if (toD == null || fromD == null) continue;
                    double via = (RoundTripWhAlong(f.Tile, toD, payload) + RoundTripWhAlong(d, fromD, payload)) / payload;
                    saved += f.KgPerDay * (f.DirectWhPerKg - via);
                }
                if (saved > 1.0) result.Add(new DepotAdvice(d, saved));
            }
            return result.OrderByDescending(a => a.SavedWhPerDay).ThenBy(a => a.Tile).Take(max).ToList();
        }
    }
}

using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// Driving between tiles (spec §10, design D9): robots move tile to tile along <see cref="HexSphere.Neighbors"/>,
    /// slower on rough ground, never across open water; the time to cross a tile depends on its terrain. Driving
    /// draws <see cref="DrivePowerW"/> from the robot's battery, scaled by surface gravity (rolling resistance
    /// F = C_rr·m·g, so at a fixed speed P = F·v ∝ g; physics-verification.md, robot driving section).
    /// Hop times and the base drive power are TBD placeholders (D4): at Perseverance's top speed (~152 m/h) a
    /// ~300 km tile would take ~2,000 hours, so game hops are much faster.
    /// </summary>
    public sealed class RouteFinder
    {
        /// <summary>Hours to cross one flat tile. TBD (D4).</summary>
        public const double FlatHopHours = 4.0;

        /// <summary>Drive power on flat ground under Earth gravity, W. TBD (D4).</summary>
        public const double BaseDrivePowerW = 50.0;

        public const double EarthGravity = 9.80665;

        const int CacheSize = 64;

        readonly HexSphere sphere;
        readonly PlanetSurface surface;
        readonly Dictionary<int, (double[] Hours, int[] Previous)> cache = new Dictionary<int, (double[], int[])>();

        public RouteFinder(HexSphere sphere, PlanetSurface surface, double gravityMs2 = EarthGravity, RiverMap rivers = null)
        {
            this.sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            GravityMs2 = gravityMs2 > 0.0 ? gravityMs2 : EarthGravity;
            this.rivers = rivers;
        }

        readonly RiverMap rivers;

        /// <summary>Crossing a river tile takes this much longer: fording, or finding a crossing (game rule, TBD).</summary>
        public const double RiverCrossingFactor = 1.5;

        /// <summary>Hours to drive into this tile: its terrain, slower over a river.</summary>
        double StepHours(int tile) =>
            HopHours(surface.Terrain[tile]) * (rivers != null && rivers.IsRiver(tile) ? RiverCrossingFactor : 1.0);

        public double GravityMs2 { get; }

        /// <summary>Drive power on this planet, W: ∝ g (rolling resistance).</summary>
        public double DrivePowerW => BaseDrivePowerW * GravityMs2 / EarthGravity;

        /// <summary>Hours to cross a tile of this terrain (∞ for open water). Multipliers are TBD (D4).</summary>
        public static double HopHours(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.DeepOcean:
                case TerrainType.ShallowOcean: return double.PositiveInfinity;
                case TerrainType.Highland: return FlatHopHours * 1.5;
                case TerrainType.Mountain: return FlatHopHours * 2.5;
                case TerrainType.Wetland: return FlatHopHours * 2.0;
                case TerrainType.Forest: return FlatHopHours * 1.4;
                case TerrainType.VolcanicRock: return FlatHopHours * 1.8;
                case TerrainType.Ice:
                case TerrainType.FrozenSea: return FlatHopHours * 2.0;
                default: return FlatHopHours;
            }
        }

        public double HopHoursAt(int tile) => StepHours(tile);

        /// <summary>Driving hours from <paramref name="from"/> to every tile (∞ where unreachable).</summary>
        public double[] HoursFrom(int from) => Search(from).Hours;

        /// <summary>Driving hours from the nearest of several tiles to every tile.</summary>
        public double[] HoursFromNearest(IEnumerable<int> starts)
        {
            var list = new List<int>(starts);
            return Dijkstra(list).Hours;
        }

        public double HoursBetween(int from, int to) => from == to ? 0.0 : HoursFrom(from)[to];

        /// <summary>The tiles to drive through from <paramref name="from"/> to <paramref name="to"/> (not including the start), or null if unreachable.</summary>
        public List<int> Route(int from, int to)
        {
            if (from == to) return new List<int>();
            var (hours, previous) = Search(from);
            if (double.IsInfinity(hours[to])) return null;
            var route = new List<int>();
            for (int t = to; t != from; t = previous[t])
                route.Add(t);
            route.Reverse();
            return route;
        }

        (double[] Hours, int[] Previous) Search(int from)
        {
            if (cache.TryGetValue(from, out var hit))
                return hit;
            if (cache.Count >= CacheSize)
                cache.Clear();
            var result = Dijkstra(new List<int> { from });
            cache[from] = result;
            return result;
        }

        /// <summary>Shortest driving times; entering a tile costs that tile's hop time.</summary>
        (double[] Hours, int[] Previous) Dijkstra(List<int> starts)
        {
            int n = sphere.TileCount;
            var hours = new double[n];
            var previous = new int[n];
            for (int i = 0; i < n; i++) { hours[i] = double.PositiveInfinity; previous[i] = -1; }
            var heap = new SortedSet<(double H, int T)>();
            foreach (int s in starts)
            {
                if (s < 0 || s >= n) continue;
                hours[s] = 0.0;
                heap.Add((0.0, s));
            }
            while (heap.Count > 0)
            {
                var (h, t) = heap.Min;
                heap.Remove(heap.Min);
                if (h > hours[t]) continue;
                foreach (int nb in sphere.Neighbors[t])
                {
                    double step = StepHours(nb);
                    if (double.IsInfinity(step)) continue;
                    double nh = h + step;
                    if (nh < hours[nb])
                    {
                        if (!double.IsInfinity(hours[nb])) heap.Remove((hours[nb], nb));
                        hours[nb] = nh;
                        previous[nb] = t;
                        heap.Add((nh, nb));
                    }
                }
            }
            return (hours, previous);
        }
    }

    /// <summary>What the workforce needs to plan trips this step: routes, where chargers are, and drive power.</summary>
    public sealed class Movement
    {
        public Movement(RouteFinder routes, double[] hoursToCharger)
        {
            Routes = routes;
            HoursToCharger = hoursToCharger;
        }

        public RouteFinder Routes { get; }
        /// <summary>Driving hours from each tile to the nearest charger.</summary>
        public double[] HoursToCharger { get; }
        public double DrivePowerW => Routes.DrivePowerW;

        /// <summary>Energy to drive for <paramref name="hours"/>, Wh, taken from the battery (one-way efficiency counted by the battery).</summary>
        public double EnergyWh(double hours) => hours * DrivePowerW;
    }
}

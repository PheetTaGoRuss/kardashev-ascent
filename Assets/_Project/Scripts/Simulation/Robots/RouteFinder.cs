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
        /// <summary>Barrier tiles the colony has opened (tunnel, blasting, bridge); shared with <see cref="Amphibious"/>.</summary>
        readonly HashSet<int> opened;

        /// <param name="onWater">Routes for a robot with a boat module: open water can be crossed too.</param>
        public RouteFinder(HexSphere sphere, PlanetSurface surface, double gravityMs2 = EarthGravity, bool onWater = false)
            : this(sphere, surface, gravityMs2, onWater, new HashSet<int>()) { }

        RouteFinder(HexSphere sphere, PlanetSurface surface, double gravityMs2, bool onWater, HashSet<int> opened)
        {
            this.sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            GravityMs2 = gravityMs2 > 0.0 ? gravityMs2 : EarthGravity;
            OnWater = onWater;
            this.opened = opened;
        }

        /// <summary>
        /// True when robots can drive across this tile. A barrier (mountain, canyon: <see cref="Terrains.IsBarrier"/>)
        /// only once it is opened; robots can still drive up to one, onto its edge, to work there.
        /// </summary>
        public bool IsPassable(int tile) => !Terrains.IsBarrier(surface.Terrain[tile]) || opened.Contains(tile);

        /// <summary>Open (or close) a barrier tile for driving across it. Routes are worked out again.</summary>
        public void SetOpened(int tile, bool open)
        {
            bool changed = open ? opened.Add(tile) : opened.Remove(tile);
            if (!changed) return;
            cache.Clear();
            amphibious?.cache.Clear();
        }

        public bool IsOpened(int tile) => opened.Contains(tile);
        public IEnumerable<int> Opened => opened;

        public double GravityMs2 { get; }

        /// <summary>True for routes of robots with a boat module: they cross open water as well as land.</summary>
        public bool OnWater { get; }

        RouteFinder amphibious;

        /// <summary>The same planet's routes for a robot that can go on water (made once, then kept).</summary>
        public RouteFinder Amphibious() =>
            OnWater ? this : amphibious ?? (amphibious = new RouteFinder(sphere, surface, GravityMs2, true, opened));

        /// <summary>
        /// Hours to cross a tile of open water with a boat module (game rule, TBD): shallow sea 1.5 flat tiles, deep
        /// ocean 2 (waves and longer crossings). ∞ for land, which <see cref="HopHours"/> covers.
        /// </summary>
        public static double WaterHopHours(TerrainType t) =>
            t == TerrainType.ShallowOcean ? FlatHopHours * 1.5
            : t == TerrainType.DeepOcean ? FlatHopHours * 2.0 : double.PositiveInfinity;

        /// <summary>Hours to cross a tile of this terrain on these routes.</summary>
        public double Hop(TerrainType t)
        {
            double land = HopHours(t);
            return OnWater && double.IsInfinity(land) ? WaterHopHours(t) : land;
        }

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
                case TerrainType.Mountain: return FlatHopHours * 2.5;   // through a tunnel or a blasted pass
                case TerrainType.Canyon: return FlatHopHours * 1.5;     // across its bridge
                case TerrainType.Wetland: return FlatHopHours * 2.0;
                case TerrainType.Forest: return FlatHopHours * 1.4;
                case TerrainType.VolcanicRock: return FlatHopHours * 1.8;
                case TerrainType.Ice:
                case TerrainType.FrozenSea: return FlatHopHours * 2.0;
                default: return FlatHopHours;
            }
        }

        public double HopHoursAt(int tile) => Hop(surface.Terrain[tile]);

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
                // A closed barrier is a dead end: robots reach its edge but don't drive on across it (a start is fine:
                // a robot working there drives back out).
                if (h > 0.0 && !IsPassable(t)) continue;
                foreach (int nb in sphere.Neighbors[t])
                {
                    double step = Hop(surface.Terrain[nb]);
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
        public Movement(RouteFinder routes, double[] hoursToCharger, double[] hoursToChargerOnWater = null)
        {
            Routes = routes;
            HoursToCharger = hoursToCharger;
            HoursToChargerOnWater = hoursToChargerOnWater;
        }

        public RouteFinder Routes { get; }
        /// <summary>Driving hours from each tile to the nearest charger.</summary>
        public double[] HoursToCharger { get; }
        /// <summary>The same for a robot with a boat module (null: as on land).</summary>
        public double[] HoursToChargerOnWater { get; }

        /// <summary>The routes this robot can take: across water too with a boat module.</summary>
        public RouteFinder RoutesFor(Robot r) => r != null && r.Has(RobotModule.Boat) ? Routes.Amphibious() : Routes;

        /// <summary>Hours from each tile to the nearest charger for this robot.</summary>
        public double[] HoursToChargerFor(Robot r) =>
            r != null && r.Has(RobotModule.Boat) && HoursToChargerOnWater != null ? HoursToChargerOnWater : HoursToCharger;
        public double DrivePowerW => Routes.DrivePowerW;

        /// <summary>Energy to drive for <paramref name="hours"/>, Wh, taken from the battery (one-way efficiency counted by the battery).</summary>
        public double EnergyWh(double hours) => hours * DrivePowerW;
    }
}

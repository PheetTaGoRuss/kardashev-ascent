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
        /// <summary>Water tiles dammed by a barrage: boats can reach their edge but not cross them. Shared with <see cref="Amphibious"/>.</summary>
        readonly HashSet<int> dammed;

        /// <param name="onWater">Routes for a robot with a boat module: open water can be crossed too.</param>
        public RouteFinder(HexSphere sphere, PlanetSurface surface, double gravityMs2 = EarthGravity, bool onWater = false)
            : this(sphere, surface, gravityMs2, onWater, new HashSet<int>(), new HashSet<int>()) { }

        RouteFinder(HexSphere sphere, PlanetSurface surface, double gravityMs2, bool onWater, HashSet<int> opened, HashSet<int> dammed)
        {
            this.sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            GravityMs2 = gravityMs2 > 0.0 ? gravityMs2 : EarthGravity;
            OnWater = onWater;
            this.opened = opened;
            this.dammed = dammed;
        }

        /// <summary>
        /// True when robots can drive across this tile. A barrier (mountain, canyon: <see cref="Terrains.IsBarrier"/>)
        /// only once it is opened; robots can still drive up to one, onto its edge, to work there.
        /// </summary>
        public bool IsPassable(int tile) => !dammed.Contains(tile) && (!Terrains.IsBarrier(surface.Terrain[tile]) || opened.Contains(tile));

        /// <summary>Dam (or free) a water tile: boats stop crossing it. Routes are worked out again.</summary>
        public void SetDammed(int tile, bool on)
        {
            bool changed = on ? dammed.Add(tile) : dammed.Remove(tile);
            if (!changed) return;
            cache.Clear();
            amphibious?.cache.Clear();
        }

        public bool IsDammed(int tile) => dammed.Contains(tile);

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

        /// <summary>
        /// Contour routing (L4, decided 2026-11-24): the extra energy, Wh, of entering a tile from a neighbour (climb with no downhill
        /// regeneration, plus the heater). With a weight, the cheapest route trades time against energy; null means time only.
        /// </summary>
        public Func<int, int, double, double> ExtraWh { get; private set; }

        /// <summary>Hours of driving one Wh is worth when choosing a route: the single weight, TBD (0: time only).</summary>
        public double HoursPerWh { get; private set; }

        /// <summary>Set (or clear, with null) the energy weighing. Every cached route is worked out again.</summary>
        public void SetEnergyCost(Func<int, int, double, double> extraWh, double hoursPerWh)
        {
            ExtraWh = extraWh; HoursPerWh = hoursPerWh;
            cache.Clear();
            if (amphibious != null) amphibious.SetEnergyCost(extraWh, hoursPerWh);
        }

        /// <summary>True for routes of robots with a boat module: they cross open water as well as land.</summary>
        public bool OnWater { get; }

        RouteFinder amphibious;

        /// <summary>The same planet's routes for a robot that can go on water (made once, then kept).</summary>
        public RouteFinder Amphibious() =>
            OnWater ? this : amphibious ?? (amphibious = MakeAmphibious());

        RouteFinder MakeAmphibious()
        {
            var a = new RouteFinder(sphere, surface, GravityMs2, true, opened, dammed);
            a.ExtraWh = ExtraWh; a.HoursPerWh = HoursPerWh;
            return a;
        }

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

        /// <summary>
        /// The total ascent, m, of the cheapest route from every tile to the nearest of several tiles (chargers): every uphill step on the way counts, not only the
        /// net climb (a robot driving home over a ridge pays for the ridge). Weather W8: a field campaign lost robots that had reserved only the net climb.
        /// </summary>
        public double[] AscentToNearest(IEnumerable<int> starts, double[] heightM)
        {
            var (hours, previous) = Dijkstra(new List<int>(starts));
            int n = hours.Length;
            var order = new List<int>();
            for (int t = 0; t < n; t++) if (!double.IsInfinity(hours[t])) order.Add(t);
            order.Sort((a, b) => hours[a].CompareTo(hours[b]));
            var ascent = new double[n];
            foreach (int t in order)
            {
                int p = previous[t];
                ascent[t] = p < 0 ? 0.0 : ascent[p] + Math.Max(0.0, heightM[p] - heightM[t]);   // going from t toward the charger, the next tile is p
            }
            return ascent;
        }

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
            // cost orders the search (time, plus the energy weighing when it is on); hours is the driving time along the route found
            var hours = new double[n];
            var cost = new double[n];
            var previous = new int[n];
            for (int i = 0; i < n; i++) { hours[i] = double.PositiveInfinity; cost[i] = double.PositiveInfinity; previous[i] = -1; }
            var heap = new SortedSet<(double C, int T)>();
            foreach (int s in starts)
            {
                if (s < 0 || s >= n) continue;
                hours[s] = 0.0; cost[s] = 0.0;
                heap.Add((0.0, s));
            }
            bool weighing = ExtraWh != null && HoursPerWh > 0.0;
            while (heap.Count > 0)
            {
                var (c, t) = heap.Min;
                heap.Remove(heap.Min);
                if (c > cost[t]) continue;
                // A closed barrier is a dead end: robots reach its edge but don't drive on across it (a start is fine:
                // a robot working there drives back out).
                if (hours[t] > 0.0 && !IsPassable(t)) continue;
                foreach (int nb in sphere.Neighbors[t])
                {
                    double step = Hop(surface.Terrain[nb]);
                    if (double.IsInfinity(step)) continue;
                    double nc = c + step + (weighing ? HoursPerWh * Math.Max(0.0, ExtraWh(t, nb, step)) : 0.0);
                    if (nc < cost[nb])
                    {
                        if (!double.IsInfinity(cost[nb])) heap.Remove((cost[nb], nb));
                        cost[nb] = nc;
                        hours[nb] = hours[t] + step;
                        previous[nb] = t;
                        heap.Add((nc, nb));
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
            if (hoursToCharger != null)
                foreach (double h in hoursToCharger)
                    if (!double.IsInfinity(h)) { HasCharger = true; break; }
            HoursToChargerOnWater = hoursToChargerOnWater;
        }

        public RouteFinder Routes { get; }

        /// <summary>The height of each tile above sea level, m (null: flat ground; no climbing cost). Water tiles are at 0.</summary>
        public double[] HeightM { get; set; }

        /// <summary>The height of the nearest charger for each tile, m (null: no climb reserve).</summary>
        public double[] NearestChargerHeightM { get; set; }

        /// <summary>
        /// The climbing energy, Wh, of a trip out to a tile and home again for a robot (Weather W8): every uphill step of the route out, and the ascent of the route home,
        /// at the game's climb share. The range checks used the flat drive only, so a robot with 25 % charge set off over a ridge and was lost.
        /// </summary>
        public double TripClimbWh(Robot r, int fromTile, int toTile)
        {
            if (HeightM == null) return 0.0;
            var route = RoutesFor(r).Route(fromTile, toTile);
            double up = 0.0; int at = fromTile;
            if (route != null) foreach (int t in route) { up += Math.Max(0.0, HeightM[t] - HeightM[at]); at = t; }
            if (AscentHomeM != null) up += AscentHomeM[toTile];
            return Logistics.HaulPhysics.ClimbEnergyWh(Logistics.HaulPhysics.DefaultRobotMassKg, 0.0, GravityMs2, up, r.Attribute(RobotAttribute.EnergyEfficiency))
                   * Logistics.HaulPhysics.ClimbEffortShare * ReliefReserveScale;
        }

        /// <summary>Total ascent, m, along the cheapest route home from each tile (null: use the net climb).</summary>
        public double[] AscentHomeM { get; set; }

        /// <summary>The climb reserve is scaled by this while the relief is only an estimate: the high end of its range over its centre (1 once surveyed).</summary>
        public double ReliefReserveScale { get; set; } = 1.0;

        /// <summary>Surface gravity, m/s2, for the climb.</summary>
        public double GravityMs2 { get; set; } = Logistics.HaulPhysics.StandardGravityMs2;

        /// <summary>
        /// False while the colony has no charger (director 2026-11-15: no charger, no recharging): robots then work
        /// until their battery is empty instead of keeping charge for a way back to nowhere.
        /// </summary>
        public bool HasCharger { get; }
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

        /// <summary>Drive power with cargo aboard, W: the empty power times (robot + cargo) / robot (mission operations MO-04).</summary>
        public double DrivePowerWithCargoW(double cargoKg) =>
            DrivePowerW * Logistics.HaulPhysics.LoadFactor(Logistics.HaulPhysics.DefaultRobotMassKg, cargoKg);

        /// <summary>Energy to drive for <paramref name="hours"/>, Wh, taken from the battery (one-way efficiency counted by the battery).</summary>
        public double EnergyWh(double hours) => hours * DrivePowerW;
    }
}

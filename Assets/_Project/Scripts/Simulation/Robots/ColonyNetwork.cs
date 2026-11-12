using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// The colony network (workforce v2 stage E; Q1 decided 2026-11-08: relay range + hub power).
    /// The lander is the hub. A robot is connected while it stands within <see cref="RangeTiles"/> tiles (along
    /// <see cref="HexSphere.Neighbors"/>) of the hub or of a powered relay. Relays extend coverage; the hub and every
    /// relay need grid power, so in a brownout the whole network is down.
    /// Everything here is a configurable TBD placeholder until tuned in play.
    /// </summary>
    public sealed class ColonyNetwork
    {
        /// <summary>Tiles from a powered node (hub or relay) a robot can still reach the network. TBD.</summary>
        public const int RangeTiles = 3;
        /// <summary>The hub's draw, part of colony demand, W. TBD.</summary>
        public const double HubPowerW = 2_000.0;
        /// <summary>One relay's draw, W. TBD.</summary>
        public const double RelayPowerW = 500.0;

        readonly HexSphere sphere;
        /// <summary>Tiles of finished relays (a site under construction relays nothing).</summary>
        readonly HashSet<int> relays = new HashSet<int>();
        /// <summary>Tiles in coverage → hops to the nearest node (0 on a node). Rebuilt by <see cref="Recompute"/>.</summary>
        readonly Dictionary<int, int> coverage = new Dictionary<int, int>();

        public ColonyNetwork(HexSphere sphere, int hubTile)
        {
            this.sphere = sphere;
            HubTile = hubTile;
            Recompute();
        }

        public int HubTile { get; }

        /// <summary>False while the grid is in brownout: the hub and every relay are unpowered.</summary>
        public bool Powered { get; private set; } = true;

        public IReadOnlyCollection<int> Relays => relays;

        /// <summary>Tiles in coverage while powered (hops to the nearest node).</summary>
        public IReadOnlyDictionary<int, int> Coverage => coverage;

        /// <summary>A robot on this tile can reach the network now.</summary>
        public bool IsConnected(int tile) => Powered && coverage.ContainsKey(tile);

        /// <summary>This tile is covered when the network is powered (what a map overlay shows).</summary>
        public bool IsCovered(int tile) => coverage.ContainsKey(tile);

        /// <summary>Total network draw: the hub and the finished relays, W.</summary>
        public double PowerW => HubPowerW + relays.Count * RelayPowerW;

        public void SetPowered(bool powered) => Powered = powered;

        public void AddRelay(int tile)
        {
            if (relays.Add(tile))
                Recompute();
        }

        public void RemoveRelay(int tile)
        {
            if (relays.Remove(tile))
                Recompute();
        }

        public void SetRelays(IEnumerable<int> tiles)
        {
            relays.Clear();
            if (tiles != null)
                foreach (int t in tiles)
                    if (t >= 0 && t < sphere.TileCount)
                        relays.Add(t);
            Recompute();
        }

        /// <summary>Breadth-first from every node, out to the range.</summary>
        void Recompute()
        {
            coverage.Clear();
            var queue = new Queue<int>();
            void Seed(int t)
            {
                if (t < 0 || t >= sphere.TileCount || coverage.ContainsKey(t))
                    return;
                coverage[t] = 0;
                queue.Enqueue(t);
            }
            Seed(HubTile);
            foreach (int r in relays)
                Seed(r);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                int hops = coverage[t];
                if (hops >= RangeTiles)
                    continue;
                foreach (int n in sphere.Neighbors[t])
                    if (!coverage.ContainsKey(n))
                    {
                        coverage[n] = hops + 1;
                        queue.Enqueue(n);
                    }
            }
        }
    }
}

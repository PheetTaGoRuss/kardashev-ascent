using System.Collections.Generic;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// Rivers (decided 2026-11-14: drawn, with small rules). Game rule, not hydrology: on a planet with a liquid sea,
    /// some high land tiles are springs; from each, water runs to the lowest neighbour, tile by tile, until it reaches
    /// the sea or joins another river. A path that ends in a hollow with no lower neighbour is dropped (lakes are not
    /// modelled). Deterministic from the planet's tiles and seed, so the simulation and the view compute the same map.
    /// Placeholder shares and thresholds, TBD.
    /// </summary>
    public sealed class RiverMap
    {
        /// <summary>Lowest elevation (0–1 above sea) a spring can start at. TBD.</summary>
        public const double SpringElevation = 0.35;
        /// <summary>Share of eligible high tiles that are springs. TBD.</summary>
        public const double SpringShare = 0.08;
        /// <summary>Paths shorter than this many tiles are dropped (a trickle, not a river). TBD.</summary>
        public const int MinLength = 3;

        readonly int[] downstream;
        readonly int[] flow;

        RiverMap(int tiles)
        {
            downstream = new int[tiles];
            flow = new int[tiles];
            for (int t = 0; t < tiles; t++) downstream[t] = -1;
        }

        public int TileCount => flow.Length;

        /// <summary>A river runs through this land tile.</summary>
        public bool IsRiver(int tile) => tile >= 0 && tile < flow.Length && flow[tile] > 0;

        /// <summary>Where the river goes from this tile (a land tile or the sea tile it ends in), or −1.</summary>
        public int Downstream(int tile) => tile >= 0 && tile < downstream.Length ? downstream[tile] : -1;

        /// <summary>How many springs feed the river here (1 near a spring, more after rivers join): its size.</summary>
        public int Flow(int tile) => tile >= 0 && tile < flow.Length ? flow[tile] : 0;

        /// <summary>Tiles with a river.</summary>
        public int RiverTileCount
        {
            get
            {
                int n = 0;
                foreach (int f in flow) if (f > 0) n++;
                return n;
            }
        }

        /// <summary>No rivers (a dry or frozen planet).</summary>
        public static RiverMap None(int tiles) => new RiverMap(tiles);

        public static RiverMap Generate(HexSphere sphere, PlanetSurface surface, int seed)
        {
            int count = sphere.TileCount;
            var map = new RiverMap(count);
            bool liquidSea = false;
            for (int t = 0; t < count && !liquidSea; t++)
                liquidSea = surface.Terrain[t] == TerrainType.ShallowOcean || surface.Terrain[t] == TerrainType.DeepOcean;
            if (!liquidSea)
                return map;   // rivers need a sea to run to (and liquid water)

            for (int spring = 0; spring < count; spring++)
            {
                if (!CanFlow(surface.Terrain[spring]) || surface.Elevation[spring] < SpringElevation
                    || Hash01(seed, spring) >= SpringShare || map.flow[spring] > 0)
                    continue;
                var path = new List<int> { spring };
                int end = -1;   // sea tile or river tile reached
                int cur = spring;
                for (int step = 0; step < count; step++)
                {
                    int next = -1;
                    float lowest = surface.Elevation[cur];
                    foreach (int n in sphere.Neighbors[cur])
                    {
                        if (Terrains.IsWater(surface.Terrain[n]) && surface.Terrain[n] != TerrainType.FrozenSea)
                        {
                            next = n;   // the sea: the river ends here
                            lowest = float.MinValue;
                            break;
                        }
                        if (surface.Elevation[n] < lowest)
                        {
                            lowest = surface.Elevation[n];
                            next = n;
                        }
                    }
                    if (next < 0 || (!Terrains.IsWater(surface.Terrain[next]) && !CanFlow(surface.Terrain[next])))
                        break;   // a hollow, or ice in the way: no river
                    if (Terrains.IsWater(surface.Terrain[next]) || map.flow[next] > 0 || path.Contains(next))
                    {
                        if (!path.Contains(next))
                            end = next;
                        break;
                    }
                    path.Add(next);
                    cur = next;
                }
                if (end < 0 || path.Count < MinLength)
                    continue;
                for (int i = 0; i < path.Count; i++)
                {
                    map.downstream[path[i]] = i + 1 < path.Count ? path[i + 1] : end;
                    map.flow[path[i]]++;
                }
                // Joining another river: it grows from here to the sea.
                for (int t = end, guard = 0; t >= 0 && map.flow[t] > 0 && guard < count; t = map.downstream[t], guard++)
                    map.flow[t]++;
            }
            return map;
        }

        /// <summary>Ground a river can run over: not the sea, not glaciers or frozen sea.</summary>
        static bool CanFlow(TerrainType t) =>
            !Terrains.IsWater(t) && t != TerrainType.Ice && t != TerrainType.FrozenSea;

        static double Hash01(int seed, int tile)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)tile * 0x85EBCA77u ^ 0x51AB3D27u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}

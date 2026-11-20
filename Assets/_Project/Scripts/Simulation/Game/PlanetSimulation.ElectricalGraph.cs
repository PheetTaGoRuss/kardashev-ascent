using System.Collections.Generic;
using KardashevAscent.Simulation.Electrical;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Stage 1 of the electrical redesign (W-11): a one-way adapter that builds the electrical graph from the legacy wire book.
    /// Nothing in gameplay reads it yet; the old tree flow still decides power. The graph is derived, so nothing new is saved.
    /// Voltage class stays 0 (unspecified) because the class ladder is still an open decision (D2).
    /// </summary>
    public sealed partial class PlanetSimulation : IElectricalNetworks
    {
        ElectricalGraph legacyGraph;
        double legacyGraphStamp = double.NaN;
        int legacyGraphQueue = -1;

        /// <summary>
        /// The graph for the current wire book: a node on every tile that has a finished line end, a cable for each finished line
        /// (length from the tile centres), and a connected terminal for each wired building. Rebuilt when the wire book changed.
        /// </summary>
        ElectricalGraph LegacyGraph()
        {
            // Cheap change test: time step, queue size and the wire book's size. Lines are built or removed through the
            // methods that also move the wiring stamp, so rebuild whenever the wiring is recomputed.
            int sig = wireBook.PowerLines.Count * 7919 + wireBook.HighVoltageLines.Count * 104729 + wireBook.WiredBuildings.Count * 13 + Build.QueueCount;
            if (legacyGraph != null && legacyGraphStamp == TimeHours && legacyGraphQueue == sig) return legacyGraph;
            var g = new ElectricalGraph();
            var at = new Dictionary<int, NodeId>();
            NodeId NodeFor(int tile)
            {
                if (!at.TryGetValue(tile, out var id)) { id = g.AddNode(tile, NetworkType.Dc, 0); at[tile] = id; }
                return id;
            }
            var lines = new List<(int Tile, int Slot, int To)>();
            foreach (var key in wireBook.PowerLines) if (HasFinishedLine(key) && wireBook.LinkTargets.TryGetValue(key, out int to)) lines.Add((key.Tile, key.Slot, to));
            foreach (var key in wireBook.HighVoltageLines) if (HasFinishedLine(key) && wireBook.LinkTargets.TryGetValue(key, out int to)) lines.Add((key.Tile, key.Slot, to));
            lines.Sort((x, y) => x.Tile != y.Tile ? x.Tile.CompareTo(y.Tile) : x.Slot != y.Slot ? x.Slot.CompareTo(y.Slot) : x.To.CompareTo(y.To));
            foreach (var l in lines)
            {
                var a = NodeFor(l.Tile); var b = NodeFor(l.To);
                g.Connect(a, b, ElectricalGraph.CableLengthM(Sphere.Centers[l.Tile], Sphere.Centers[l.To], planetRadiusM));
            }
            NodeFor(BaseTileId);
            var wired = new List<(int Tile, int Slot)>(wireBook.WiredBuildings);
            wired.Sort((x, y) => x.Tile != y.Tile ? x.Tile.CompareTo(y.Tile) : x.Slot.CompareTo(y.Slot));
            var typeAt = new Dictionary<(int, int), BuildingType>();
            foreach (var b in WireableBuildings()) typeAt[(b.Tile, b.Slot)] = b.Type;
            foreach (var w in wired)
            {
                if (!typeAt.TryGetValue((w.Tile, w.Slot), out var type)) continue;   // a wire left on a building that is gone
                var role = WireRoles.TerminalRoleOf(type);
                if (role == TerminalRole.Unspecified) continue;
                var dev = g.AddDevice(w.Tile, w.Slot, (int)type);
                var term = g.AddTerminal(dev, NetworkType.Dc, 0, role);
                g.Attach(term, NodeFor(w.Tile));
            }
            legacyGraph = g;
            legacyGraphStamp = TimeHours;
            legacyGraphQueue = sig;
            return g;
        }

        /// <summary>The tiles on the same electrical network as the lander tile (the network the single power bus serves).</summary>
        public IReadOnlyCollection<int> LanderNetworkTiles()
        {
            var g = LegacyGraph();
            var net = g.NetworkOf(g.NodeOnTile(BaseTileId));
            var tiles = new List<int>();
            if (net == null) return tiles;
            foreach (var n in net.Nodes) tiles.Add(g.Node(n).Tile);
            tiles.Sort();
            return tiles;
        }

        /// <summary>How many separate electrical networks the finished lines form (the lander's plus any islands).</summary>
        public int ElectricalNetworkCount => LegacyGraph().Networks().Count;

        /// <summary>True when the tile has a node on a network that has no connection to the lander network.</summary>
        public bool IsOnElectricalIsland(int tileId)
        {
            var g = LegacyGraph();
            var node = g.NodeOnTile(tileId);
            if (node.IsNone) return false;
            return !g.SameNetwork(node, g.NodeOnTile(BaseTileId));
        }

        /// <summary>
        /// What a building's electrical terminal says now (read model). A building is on a network only through a terminal made by
        /// wiring it; a cable passing through its tile does nothing. "Receiving enough" is not reported: it needs the consumer
        /// allocation policy, which is a separate design.
        /// </summary>
        public BuildingElectricalState BuildingElectrical(int tileId, int slot)
        {
            var g = LegacyGraph();
            foreach (var d in g.Devices())
            {
                if (d.OwnerTile != tileId || d.OwnerSlot != slot) continue;
                var t = g.Terminal(d.Terminals[0]);
                bool connected = !t.Node.IsNone;
                bool main = connected && g.SameNetwork(t.Node, g.NodeOnTile(BaseTileId));
                return new BuildingElectricalState(true, t.Role, connected, true, main);
            }
            return default;
        }
    }
}

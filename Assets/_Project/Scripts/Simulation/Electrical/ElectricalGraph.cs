using System;
using System.Collections.Generic;
using System.Numerics;

namespace KardashevAscent.Simulation.Electrical
{
    /// <summary>
    /// The electrical graph (design: docs/design/electrical-system.md; workflow W-06 to W-10). Pure data and connectivity: nodes,
    /// cables, devices and terminals, with compatibility checks (a cable joins nodes of the same kind and voltage class only) and
    /// deterministic connected networks. It holds no power flow: loops, branches, independent networks and splits are all
    /// supported, and a rooted tree is not assumed anywhere. Everything iterates in id order, so results do not depend on
    /// insertion order or hashing.
    /// </summary>
    public sealed class ElectricalGraph
    {
        readonly SortedDictionary<int, ElectricalNode> nodes = new SortedDictionary<int, ElectricalNode>();
        readonly SortedDictionary<int, ElectricalConnection> connections = new SortedDictionary<int, ElectricalConnection>();
        readonly SortedDictionary<int, ElectricalTerminal> terminals = new SortedDictionary<int, ElectricalTerminal>();
        readonly SortedDictionary<int, ElectricalDevice> devices = new SortedDictionary<int, ElectricalDevice>();
        /// <summary>Node id to the connections that touch it (the adjacency index).</summary>
        readonly Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
        readonly Dictionary<int, int> nodeAtTile = new Dictionary<int, int>();

        int nextNode = 1, nextConnection = 1, nextTerminal = 1, nextDevice = 1;
        List<ElectricalNetwork> networks;
        Dictionary<int, ElectricalNetwork> networkOfNode;
        /// <summary>Raised on every change that can alter connectivity; cached networks rebuild when it moves.</summary>
        public int TopologyStamp { get; private set; }

        public int NodeCount => nodes.Count;
        public int ConnectionCount => connections.Count;

        // ---- saved counters (ids are never reused) ----
        public int NextNodeValue => nextNode;
        public int NextConnectionValue => nextConnection;
        public int NextTerminalValue => nextTerminal;
        public int NextDeviceValue => nextDevice;

        void Touch()
        {
            TopologyStamp++;
            networks = null;
            networkOfNode = null;
        }

        // ---- nodes ----
        public NodeId AddNode(int tile, NetworkType type, int voltageClass)
        {
            var n = new ElectricalNode { Id = new NodeId(nextNode++), Tile = tile, Type = type, VoltageClass = voltageClass };
            nodes[n.Id.Value] = n;
            adjacency[n.Id.Value] = new List<int>();
            if (tile >= 0 && !nodeAtTile.ContainsKey(tile)) nodeAtTile[tile] = n.Id.Value;
            Touch();
            return n.Id;
        }

        public ElectricalNode Node(NodeId id) => nodes.TryGetValue(id.Value, out var n) ? n : null;

        /// <summary>The first node standing on a tile, or none.</summary>
        public NodeId NodeOnTile(int tile) => nodeAtTile.TryGetValue(tile, out int v) ? new NodeId(v) : NodeId.None;

        public IEnumerable<ElectricalNode> Nodes() => nodes.Values;

        /// <summary>Removes a node with every cable that touches it; terminals on it are left unattached.</summary>
        public bool RemoveNode(NodeId id)
        {
            if (!nodes.TryGetValue(id.Value, out var n)) return false;
            foreach (int c in new List<int>(adjacency[id.Value])) RemoveConnection(new ConnectionId(c));
            foreach (var t in terminals.Values) if (t.Node == id) t.Node = NodeId.None;
            adjacency.Remove(id.Value);
            nodes.Remove(id.Value);
            if (n.Tile >= 0 && nodeAtTile.TryGetValue(n.Tile, out int at) && at == id.Value)
            {
                nodeAtTile.Remove(n.Tile);
                foreach (var o in nodes.Values) if (o.Tile == n.Tile) { nodeAtTile[n.Tile] = o.Id.Value; break; }
            }
            Touch();
            return true;
        }

        // ---- connections ----
        /// <summary>
        /// Joins two nodes with a cable. Refuses (with a plain reason) a missing node, a cable to itself, an AC/DC mix, or a
        /// voltage-class mismatch. Parallel cables between the same pair are allowed.
        /// </summary>
        public ConnectResult Connect(NodeId a, NodeId b, double lengthM)
        {
            if (!nodes.TryGetValue(a.Value, out var na) || !nodes.TryGetValue(b.Value, out var nb)) return ConnectResult.Fail("A cable end has no node.");
            if (a == b) return ConnectResult.Fail("A cable cannot join a node to itself.");
            string why = Compatible(na.Type, na.VoltageClass, nb.Type, nb.VoltageClass);
            if (why != null) return ConnectResult.Fail(why);
            var c = new ElectricalConnection { Id = new ConnectionId(nextConnection++), A = a, B = b, LengthM = lengthM };
            connections[c.Id.Value] = c;
            adjacency[a.Value].Add(c.Id.Value);
            adjacency[b.Value].Add(c.Id.Value);
            Touch();
            return ConnectResult.Success(c.Id.Value);
        }

        public bool RemoveConnection(ConnectionId id)
        {
            if (!connections.TryGetValue(id.Value, out var c)) return false;
            if (adjacency.TryGetValue(c.A.Value, out var la)) la.Remove(id.Value);
            if (adjacency.TryGetValue(c.B.Value, out var lb)) lb.Remove(id.Value);
            connections.Remove(id.Value);
            Touch();
            return true;
        }

        public ElectricalConnection Connection(ConnectionId id) => connections.TryGetValue(id.Value, out var c) ? c : null;
        public IEnumerable<ElectricalConnection> Connections() => connections.Values;

        /// <summary>Cables touching a node, in id order.</summary>
        public IReadOnlyList<int> ConnectionsAt(NodeId id)
        {
            if (!adjacency.TryGetValue(id.Value, out var l)) return Array.Empty<int>();
            l.Sort();
            return l;
        }

        /// <summary>Opens or closes a cable (a fault, a switch-off). An open cable joins nothing.</summary>
        public bool SetOpen(ConnectionId id, bool open)
        {
            if (!connections.TryGetValue(id.Value, out var c) || c.Open == open) return false;
            c.Open = open;
            Touch();
            return true;
        }

        public bool SetCondition(ConnectionId id, double condition)
        {
            if (!connections.TryGetValue(id.Value, out var c)) return false;
            bool was = c.Joins;
            c.Condition = Math.Max(0.0, Math.Min(1.0, condition));
            if (was != c.Joins) Touch();
            return true;
        }

        // ---- devices and terminals ----
        public DeviceId AddDevice(int tile, int slot, int kind)
        {
            var d = new ElectricalDevice { Id = new DeviceId(nextDevice++), OwnerTile = tile, OwnerSlot = slot, Kind = kind };
            devices[d.Id.Value] = d;
            return d.Id;
        }

        public ElectricalDevice Device(DeviceId id) => devices.TryGetValue(id.Value, out var d) ? d : null;
        public IEnumerable<ElectricalDevice> Devices() => devices.Values;

        public TerminalId AddTerminal(DeviceId device, NetworkType type, int voltageClass, TerminalRole role)
        {
            if (!devices.TryGetValue(device.Value, out var d)) return TerminalId.None;
            var t = new ElectricalTerminal { Id = new TerminalId(nextTerminal++), Device = device, Type = type, VoltageClass = voltageClass, Role = role };
            terminals[t.Id.Value] = t;
            d.Terminals.Add(t.Id);
            Touch();
            return t.Id;
        }

        public ElectricalTerminal Terminal(TerminalId id) => terminals.TryGetValue(id.Value, out var t) ? t : null;
        public IEnumerable<ElectricalTerminal> Terminals() => terminals.Values;

        /// <summary>
        /// Connects a terminal to a node. A building is on a network only this way: standing on the same tile as a node, or a
        /// cable passing through, connects nothing. Refused when kind or voltage class differ.
        /// </summary>
        public ConnectResult Attach(TerminalId terminal, NodeId node)
        {
            if (!terminals.TryGetValue(terminal.Value, out var t)) return ConnectResult.Fail("No such terminal.");
            if (!nodes.TryGetValue(node.Value, out var n)) return ConnectResult.Fail("No such node.");
            string why = Compatible(t.Type, t.VoltageClass, n.Type, n.VoltageClass);
            if (why != null) return ConnectResult.Fail(why);
            t.Node = node;
            Touch();
            return ConnectResult.Success(terminal.Value);
        }

        public bool Detach(TerminalId terminal)
        {
            if (!terminals.TryGetValue(terminal.Value, out var t) || t.Node.IsNone) return false;
            t.Node = NodeId.None;
            Touch();
            return true;
        }

        /// <summary>Why two ends cannot be joined, or null when they can. Unspecified kind or class (0) matches anything.</summary>
        public static string Compatible(NetworkType ta, int ca, NetworkType tb, int cb)
        {
            if (ta != NetworkType.Unspecified && tb != NetworkType.Unspecified && ta != tb)
                return "AC and DC cannot join without conversion equipment.";
            if (ca != 0 && cb != 0 && ca != cb)
                return "The voltage classes differ; a transformer or converter is needed.";
            return null;
        }

        // ---- networks ----
        /// <summary>All networks, sorted by id. Rebuilt only when the topology stamp has moved.</summary>
        public IReadOnlyList<ElectricalNetwork> Networks()
        {
            if (networks == null) Rebuild();
            return networks;
        }

        public ElectricalNetwork NetworkOf(NodeId node)
        {
            if (networks == null) Rebuild();
            return networkOfNode.TryGetValue(node.Value, out var n) ? n : null;
        }

        /// <summary>The networks a device sits on through its attached terminals, in id order.</summary>
        public List<ElectricalNetwork> NetworksOf(DeviceId device)
        {
            var list = new List<ElectricalNetwork>();
            if (!devices.TryGetValue(device.Value, out var d)) return list;
            foreach (var tid in d.Terminals)
            {
                var t = terminals[tid.Value];
                if (t.Node.IsNone) continue;
                var net = NetworkOf(t.Node);
                if (net != null && !list.Contains(net)) list.Add(net);
            }
            list.Sort((x, y) => x.Id.CompareTo(y.Id));
            return list;
        }

        /// <summary>True when the two nodes are in one network now.</summary>
        public bool SameNetwork(NodeId a, NodeId b)
        {
            var na = NetworkOf(a);
            return na != null && na == NetworkOf(b);
        }

        void Rebuild()
        {
            // Union-find with the smallest id as root, so a network's id is its smallest node id.
            var parent = new Dictionary<int, int>();
            foreach (int id in nodes.Keys) parent[id] = id;
            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            foreach (var c in connections.Values)
            {
                if (!c.Joins) continue;
                int ra = Find(c.A.Value), rb = Find(c.B.Value);
                if (ra == rb) continue;
                if (ra < rb) parent[rb] = ra; else parent[ra] = rb;
            }
            var list = new List<ElectricalNetwork>();
            var byRoot = new Dictionary<int, ElectricalNetwork>();
            networkOfNode = new Dictionary<int, ElectricalNetwork>();
            foreach (var n in nodes.Values)
            {
                int r = Find(n.Id.Value);
                if (!byRoot.TryGetValue(r, out var net))
                {
                    net = new ElectricalNetwork { Id = new NodeId(r), Type = n.Type, VoltageClass = n.VoltageClass };
                    byRoot[r] = net;
                    list.Add(net);
                }
                if (net.Type == NetworkType.Unspecified) net.Type = n.Type;
                if (net.VoltageClass == 0) net.VoltageClass = n.VoltageClass;
                net.Nodes.Add(n.Id);
                networkOfNode[n.Id.Value] = net;
            }
            foreach (var c in connections.Values)
            {
                if (!c.Joins) continue;
                byRoot[Find(c.A.Value)].Connections.Add(c.Id);
            }
            foreach (var t in terminals.Values)
            {
                if (t.Node.IsNone) continue;
                var net = byRoot[Find(t.Node.Value)];
                net.Terminals.Add(t.Id);
                if (!net.Devices.Contains(t.Device)) net.Devices.Add(t.Device);
            }
            foreach (var net in list)
            {
                net.Devices.Sort();
                // A connected graph with V nodes needs V-1 cables to be a tree; more means a loop.
                net.IsMesh = net.Connections.Count > net.Nodes.Count - 1;
            }
            networks = list;
        }

        // ---- cable length (W-07) ----
        /// <summary>Default route factor: a cable does not follow the great circle exactly (game rule, TBD).</summary>
        public const double DefaultRouteFactor = 1.1;

        /// <summary>
        /// Cable length between two tile centres: the great-circle arc of the two unit vectors times the planet radius times a
        /// route factor. Stored on the connection when it is built; a circuit wire does not use this (its length is set by hand).
        /// </summary>
        public static double CableLengthM(Vector3 a, Vector3 b, double planetRadiusM, double routeFactor = DefaultRouteFactor)
        {
            double dot = Math.Max(-1.0, Math.Min(1.0, (double)Vector3.Dot(a, b)));
            // atan2 of |cross| and dot stays accurate for very short and for opposite arcs, where acos loses precision.
            double cross = Vector3.Cross(a, b).Length();
            double arc = Math.Atan2(cross, dot);
            return arc * planetRadiusM * routeFactor;
        }
    }
}

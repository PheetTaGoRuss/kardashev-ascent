using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Electrical
{
    /// <summary>
    /// The kind of electrical network a node, a cable or a terminal belongs to (electrical system design 3.1). Ordinary cables
    /// join nodes of the same kind only; changing kind needs conversion equipment (transformer, rectifier, inverter, DC-DC
    /// converter), which later stages add. Saved as integers: append only.
    /// </summary>
    public enum NetworkType { Unspecified, Dc, Ac }

    /// <summary>What a terminal does for its device (electrical system design 4.3). Saved as integers: append only.</summary>
    public enum TerminalRole { Unspecified, Source, Load, Storage, Passive }

    /// <summary>A stable id for a node. Ids rise and are never reused, so they survive save and load and give every iteration a fixed order. Zero is none.</summary>
    public readonly struct NodeId : IEquatable<NodeId>, IComparable<NodeId>
    {
        public readonly int Value;
        public NodeId(int value) { Value = value; }
        public static readonly NodeId None = default;
        public bool IsNone => Value == 0;
        public bool Equals(NodeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NodeId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(NodeId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString();
        public static bool operator ==(NodeId a, NodeId b) => a.Value == b.Value;
        public static bool operator !=(NodeId a, NodeId b) => a.Value != b.Value;
    }

    /// <summary>A stable id for a connection. Ids rise and are never reused, so they survive save and load and give every iteration a fixed order. Zero is none.</summary>
    public readonly struct ConnectionId : IEquatable<ConnectionId>, IComparable<ConnectionId>
    {
        public readonly int Value;
        public ConnectionId(int value) { Value = value; }
        public static readonly ConnectionId None = default;
        public bool IsNone => Value == 0;
        public bool Equals(ConnectionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ConnectionId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(ConnectionId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString();
        public static bool operator ==(ConnectionId a, ConnectionId b) => a.Value == b.Value;
        public static bool operator !=(ConnectionId a, ConnectionId b) => a.Value != b.Value;
    }

    /// <summary>A stable id for a terminal. Ids rise and are never reused, so they survive save and load and give every iteration a fixed order. Zero is none.</summary>
    public readonly struct TerminalId : IEquatable<TerminalId>, IComparable<TerminalId>
    {
        public readonly int Value;
        public TerminalId(int value) { Value = value; }
        public static readonly TerminalId None = default;
        public bool IsNone => Value == 0;
        public bool Equals(TerminalId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TerminalId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(TerminalId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString();
        public static bool operator ==(TerminalId a, TerminalId b) => a.Value == b.Value;
        public static bool operator !=(TerminalId a, TerminalId b) => a.Value != b.Value;
    }

    /// <summary>A stable id for a device. Ids rise and are never reused, so they survive save and load and give every iteration a fixed order. Zero is none.</summary>
    public readonly struct DeviceId : IEquatable<DeviceId>, IComparable<DeviceId>
    {
        public readonly int Value;
        public DeviceId(int value) { Value = value; }
        public static readonly DeviceId None = default;
        public bool IsNone => Value == 0;
        public bool Equals(DeviceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is DeviceId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(DeviceId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString();
        public static bool operator ==(DeviceId a, DeviceId b) => a.Value == b.Value;
        public static bool operator !=(DeviceId a, DeviceId b) => a.Value != b.Value;
    }

    /// <summary>
    /// A point where cables meet. A tile can host one (tile 0 or more; -1 when it is not on the planet grid, as in a circuit).
    /// The electrical graph is separate from the planet's building slots: nearness alone connects nothing (design 4.1).
    /// </summary>
    public sealed class ElectricalNode
    {
        public NodeId Id;
        /// <summary>The planet tile this node stands on, or -1.</summary>
        public int Tile = -1;
        public NetworkType Type;
        /// <summary>An opaque voltage class id (0: not set). The classes and their values come with design decision D2.</summary>
        public int VoltageClass;
        /// <summary>Nominal frequency of an AC node, terminal or network, Hz; 0 for DC or not set. A network has one frequency. The values are a design decision still open.</summary>
        public double FrequencyHz;
    }

    /// <summary>
    /// A cable between two nodes (design 4.3). Stage 1 holds only what connectivity needs; resistance, rating and material
    /// arrive with stage 2. A cable that is <see cref="Open"/> (failed, cut or switched off) carries nothing and does not
    /// join its nodes.
    /// </summary>
    public sealed class ElectricalConnection
    {
        public ConnectionId Id;
        public NodeId A, B;
        /// <summary>Physical length, m (0 until set). Never derived from how it is drawn.</summary>
        public double LengthM;
        /// <summary>0 to 1; 0 means failed.</summary>
        public double Condition = 1.0;
        public bool Open;

        public bool Joins => !Open && Condition > 0.0;
    }

    /// <summary>A building's or device's electrical port. A building is on a network only through a terminal connected to a node.</summary>
    public sealed class ElectricalTerminal
    {
        public TerminalId Id;
        public DeviceId Device;
        /// <summary>The node it is connected to, or none.</summary>
        public NodeId Node;
        public NetworkType Type;
        public int VoltageClass;
        /// <summary>Nominal frequency of an AC node, terminal or network, Hz; 0 for DC or not set. A network has one frequency. The values are a design decision still open.</summary>
        public double FrequencyHz;
        public TerminalRole Role;
    }

    /// <summary>
    /// Something with electrical ports: a building, later a robot or a machine (design 4.3). <see cref="Owner"/> is an opaque
    /// key the owning system understands (the planet simulation uses tile and slot).
    /// </summary>
    public sealed class ElectricalDevice
    {
        public DeviceId Id;
        public int OwnerTile = -1;
        public int OwnerSlot = -1;
        /// <summary>A tag the owner understands (the building type as an integer).</summary>
        public int Kind;
        public readonly List<TerminalId> Terminals = new List<TerminalId>();
    }

    /// <summary>The result of asking the graph to join two things: success, or why not.</summary>
    public readonly struct ConnectResult
    {
        public readonly bool Ok;
        public readonly string Reason;
        public readonly int Id;

        public ConnectResult(bool ok, string reason, int id)
        {
            Ok = ok;
            Reason = reason;
            Id = id;
        }

        public static ConnectResult Success(int id) => new ConnectResult(true, null, id);
        public static ConnectResult Fail(string reason) => new ConnectResult(false, reason, 0);
    }

    /// <summary>
    /// One connected electrical subsystem: the nodes a closed path of cables joins, with their kind and class and what is
    /// attached (design 4.3). Its id is the smallest node id it holds, so it does not change while it only grows or loses
    /// other nodes.
    /// </summary>
    public sealed class ElectricalNetwork
    {
        public NodeId Id;
        public NetworkType Type;
        public int VoltageClass;
        /// <summary>Nominal frequency of an AC node, terminal or network, Hz; 0 for DC or not set. A network has one frequency. The values are a design decision still open.</summary>
        public double FrequencyHz;
        public readonly List<NodeId> Nodes = new List<NodeId>();
        public readonly List<ConnectionId> Connections = new List<ConnectionId>();
        /// <summary>Terminals connected to a node of this network, in id order.</summary>
        public readonly List<TerminalId> Terminals = new List<TerminalId>();
        /// <summary>Devices with at least one terminal on this network, in id order.</summary>
        public readonly List<DeviceId> Devices = new List<DeviceId>();
        /// <summary>True when the nodes form loops (more cables than a tree needs): a mesh.</summary>
        public bool IsMesh;
    }
}

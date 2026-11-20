using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Circuits
{
    /// <summary>
    /// The parts a player can place in a circuit (workflow W-34, tier 1 first). Saved as integers: append only.
    /// Each part is a real electrical model, not a logic block: a relay has a coil and contacts, a battery has a finite energy and an
    /// internal resistance, a lamp consumes power, a resistor turns it into heat.
    /// </summary>
    public enum CircuitComponentType { Junction, Battery, DcSupply, Switch, PushButton, Lamp, Resistor, Fuse, Relay, Diode }

    /// <summary>What a wire carries. Power wires carry energy; signal wires carry information (no tier-1 part has a signal port yet).</summary>
    public enum WireKind { Power, Signal }

    public enum WireMaterial { Copper, Aluminium }

    /// <summary>
    /// What the numbers on a placed component mean, by type (units in brackets):
    /// Battery: P1 voltage [V], P2 internal resistance [ohm], P3 capacity [Wh]. DcSupply: P1 voltage, P2 internal resistance, P3 current limit [A].
    /// Lamp: P1 rated voltage, P2 rated power [W]. Resistor: P1 resistance, P2 power rating [W]. Fuse: P1 rating [A]. Relay: P1 coil resistance, P2 pull-in current [A].
    /// Diode: P1 forward drop [V]. Switch, button and junction need none. Values are game data, TBD until balanced.
    /// </summary>
    [Serializable]
    public sealed class CircuitPlacement
    {
        public int Id;
        public CircuitComponentType Type;
        /// <summary>Position on the editor grid. Never used for electrical values: wire length is its own number.</summary>
        public int X, Y;
        /// <summary>Quarter turns, 0 to 3.</summary>
        public int Rotation;
        public double P1, P2, P3;
        /// <summary>Starting state of a switch or button (closed) .</summary>
        public bool Closed;
    }

    /// <summary>A wire between two pins. Its length is set by hand and is the physical length, whatever the editor draws.</summary>
    [Serializable]
    public sealed class CircuitWire
    {
        public int Id;
        public WireKind Kind;
        public int FromComponent, FromPin, ToComponent, ToPin;
        /// <summary>Physical length, m.</summary>
        public double LengthM = 0.1;
        public WireMaterial Material = WireMaterial.Copper;
        /// <summary>Cross-section, mm2.</summary>
        public double AreaMm2 = 1.5;
    }

    /// <summary>A pin the circuit offers to its host (power in, power out, a control line), by name.</summary>
    [Serializable]
    public sealed class CircuitPort
    {
        public string Name;
        public int Component, Pin;
        public WireKind Kind;
    }

    /// <summary>A reusable design (W-35). Editing it never changes an installed instance.</summary>
    [Serializable]
    public sealed class CircuitBlueprint
    {
        public string Name = "Circuit";
        public int Version = 1;
        public int GridWidth = 16, GridHeight = 12;
        public List<CircuitPlacement> Components = new List<CircuitPlacement>();
        public List<CircuitWire> Wires = new List<CircuitWire>();
        public List<CircuitPort> Ports = new List<CircuitPort>();
        public int NextId = 1;

        public CircuitPlacement Place(CircuitComponentType type, int x, int y, double p1 = 0, double p2 = 0, double p3 = 0, bool closed = false, int rotation = 0)
        {
            var c = new CircuitPlacement { Id = NextId++, Type = type, X = x, Y = y, Rotation = rotation & 3, P1 = p1, P2 = p2, P3 = p3, Closed = closed };
            Components.Add(c);
            Version++;
            return c;
        }

        public CircuitWire Connect(int fromComponent, int fromPin, int toComponent, int toPin, double lengthM = 0.1, double areaMm2 = 1.5,
                                   WireMaterial material = WireMaterial.Copper, WireKind kind = WireKind.Power)
        {
            var w = new CircuitWire { Id = NextId++, Kind = kind, FromComponent = fromComponent, FromPin = fromPin, ToComponent = toComponent, ToPin = toPin, LengthM = lengthM, AreaMm2 = areaMm2, Material = material };
            Wires.Add(w);
            Version++;
            return w;
        }

        public bool RemoveWire(int id)
        {
            int n = Wires.RemoveAll(w => w.Id == id);
            if (n > 0) Version++;
            return n > 0;
        }

        public bool RemoveComponent(int id)
        {
            int n = Components.RemoveAll(c => c.Id == id);
            if (n == 0) return false;
            Wires.RemoveAll(w => w.FromComponent == id || w.ToComponent == id);
            Ports.RemoveAll(p => p.Component == id);
            Version++;
            return true;
        }

        public CircuitPlacement Component(int id) => Components.Find(c => c.Id == id);

        /// <summary>A deep copy (an installed instance holds one, so later edits of the blueprint do not reach it).</summary>
        public CircuitBlueprint Clone()
        {
            var b = new CircuitBlueprint { Name = Name, Version = Version, GridWidth = GridWidth, GridHeight = GridHeight, NextId = NextId };
            foreach (var c in Components) b.Components.Add(new CircuitPlacement { Id = c.Id, Type = c.Type, X = c.X, Y = c.Y, Rotation = c.Rotation, P1 = c.P1, P2 = c.P2, P3 = c.P3, Closed = c.Closed });
            foreach (var w in Wires) b.Wires.Add(new CircuitWire { Id = w.Id, Kind = w.Kind, FromComponent = w.FromComponent, FromPin = w.FromPin, ToComponent = w.ToComponent, ToPin = w.ToPin, LengthM = w.LengthM, Material = w.Material, AreaMm2 = w.AreaMm2 });
            foreach (var p in Ports) b.Ports.Add(new CircuitPort { Name = p.Name, Component = p.Component, Pin = p.Pin, Kind = p.Kind });
            return b;
        }
    }

    /// <summary>The pins of each part. Pin 0 is the negative or first pin.</summary>
    public static class CircuitParts
    {
        public static int PinCount(CircuitComponentType t)
        {
            switch (t)
            {
                case CircuitComponentType.Junction: return 1;
                case CircuitComponentType.Relay: return 4;   // 0, 1 coil; 2, 3 contacts
                default: return 2;
            }
        }

        public static bool IsSource(CircuitComponentType t) => t == CircuitComponentType.Battery || t == CircuitComponentType.DcSupply;
    }

    /// <summary>What changes while a circuit runs (not the design): switch positions, relay contacts, blown fuses, battery charge.</summary>
    [Serializable]
    public sealed class CircuitComponentState
    {
        public int Id;
        /// <summary>Switch or button closed; relay contacts closed.</summary>
        public bool Closed;
        public bool Blown;
        /// <summary>Battery energy left, Wh.</summary>
        public double EnergyWh;
    }

    /// <summary>One thing that went wrong in a circuit, in words a player can use.</summary>
    [Serializable]
    public sealed class CircuitFault
    {
        public string Code;
        public int Component;
        public double AtHours;
    }

    /// <summary>What a host building or machine offers a circuit (W-39): room, and the ports it expects.</summary>
    [Serializable]
    public sealed class CircuitHostSpec
    {
        public string Name;
        public int MaxComponents;
        public int MaxGridWidth, MaxGridHeight;
        /// <summary>Port names the host needs the circuit to expose.</summary>
        public List<string> RequiredPorts = new List<string>();
    }

    /// <summary>An installed circuit (W-35): a copy of a blueprint in one host, with its own state and fault history.</summary>
    [Serializable]
    public sealed class CircuitInstance
    {
        public int Id;
        public int HostId;
        public string HostName;
        /// <summary>The blueprint version this instance was installed from.</summary>
        public int BlueprintVersion;
        public CircuitBlueprint Design;
        public List<CircuitComponentState> State = new List<CircuitComponentState>();
        public List<CircuitFault> Faults = new List<CircuitFault>();
        public double HoursRun;

        public static CircuitInstance Install(CircuitBlueprint blueprint, CircuitHostSpec host, int instanceId, int hostId, out string refusal)
        {
            refusal = CircuitCompatibility.Check(blueprint, host);
            if (refusal != null) return null;
            var inst = new CircuitInstance { Id = instanceId, HostId = hostId, HostName = host.Name, BlueprintVersion = blueprint.Version, Design = blueprint.Clone() };
            inst.ResetState();
            return inst;
        }

        public void ResetState()
        {
            State.Clear();
            foreach (var c in Design.Components)
                State.Add(new CircuitComponentState { Id = c.Id, Closed = c.Closed, EnergyWh = c.Type == CircuitComponentType.Battery ? c.P3 : 0.0 });
        }

        public CircuitComponentState StateOf(int id) => State.Find(s => s.Id == id);
    }

    /// <summary>Whether a design fits a host: separate from whether it is electrically sound and whether it does its job (W-37).</summary>
    public static class CircuitCompatibility
    {
        /// <summary>Null when the design fits; otherwise the reason it does not.</summary>
        public static string Check(CircuitBlueprint b, CircuitHostSpec host)
        {
            if (host == null) return "No host.";
            if (b.Components.Count > host.MaxComponents) return $"{host.Name} holds {host.MaxComponents} parts; this circuit has {b.Components.Count}.";
            if (b.GridWidth > host.MaxGridWidth || b.GridHeight > host.MaxGridHeight) return $"This circuit's board ({b.GridWidth} x {b.GridHeight}) is larger than {host.Name} takes ({host.MaxGridWidth} x {host.MaxGridHeight}).";
            foreach (var need in host.RequiredPorts)
                if (!b.Ports.Exists(p => p.Name == need)) return $"{host.Name} needs a port called '{need}'.";
            return null;
        }
    }
}

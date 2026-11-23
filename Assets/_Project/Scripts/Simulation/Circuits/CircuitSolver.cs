using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Electrical;

namespace KardashevAscent.Simulation.Circuits
{
    public enum DiagnosticSeverity { Info, Warning, Error }

    /// <summary>What a diagnostic is about (W-36). Strings in <see cref="CircuitDiagnostic.Message"/> are for players; the code is for tests and the editor.</summary>
    public enum DiagnosticCode
    {
        NoSource, OpenCircuit, ShortCircuit, ReversedPolarity, Overcurrent, ExcessiveVoltageDrop, ExcessiveLoss, ComponentFailure,
        DisconnectedTerminal, InvalidConnection, DidNotSettle, UndersizedWire, BatteryEmpty
    }

    public sealed class CircuitDiagnostic
    {
        public DiagnosticSeverity Severity;
        public DiagnosticCode Code;
        public int Component;   // 0 when not about one part
        public int Wire;        // 0 when not about one wire
        public string Message;
    }

    /// <summary>The numbers for one placed component.</summary>
    public sealed class ComponentResult
    {
        public int Id;
        /// <summary>Voltage across the part (pin 0 to pin 1), V.</summary>
        public double VoltageV;
        /// <summary>Current through it from pin 0 to pin 1, A (positive inside a source that delivers, because a source drives current from its pin 0 up to its positive pin 1).</summary>
        public double CurrentA;
        /// <summary>Power it takes (positive) or gives (negative), W.</summary>
        public double PowerW;
    }

    public sealed class WireResult
    {
        public int Id;
        public double ResistanceOhm;
        public double CurrentA;
        public double DropV;
        public double LossW;
    }

    public sealed class CircuitResult
    {
        public bool Solved;
        public int Iterations;
        public readonly List<ComponentResult> Components = new List<ComponentResult>();
        public readonly List<WireResult> Wires = new List<WireResult>();
        public readonly List<CircuitDiagnostic> Diagnostics = new List<CircuitDiagnostic>();
        /// <summary>Power the sources give, W.</summary>
        public double SourcePowerW;
        /// <summary>Power lost in the wires, W.</summary>
        public double WireLossW;
        /// <summary>Hours the batteries last at this load (infinity when none is drawn).</summary>
        public double BatteryHours = double.PositiveInfinity;
        readonly Dictionary<long, double> pinVoltages = new Dictionary<long, double>();

        internal void SetPinVoltage(int component, int pin, double volts) => pinVoltages[(long)component * 16 + pin] = volts;

        /// <summary>Voltage at a pin, V, measured from the reference of the pin's own island (the negative pin of its source).</summary>
        public double VoltageAt(int component, int pin) => pinVoltages.TryGetValue((long)component * 16 + pin, out double v) ? v : 0.0;

        public ComponentResult Of(int id) => Components.Find(c => c.Id == id);
        public WireResult WireOf(int id) => Wires.Find(w => w.Id == id);
        public bool Has(DiagnosticCode code) => Diagnostics.Exists(d => d.Code == code);
    }

    /// <summary>
    /// Steady-state DC circuit solver (W-36): modified nodal analysis on a dense Cholesky solve, the same core as the grid flow.
    /// Every pin is a node; every part and every wire is a conductance with an optional source current. Parts with a state (switches,
    /// relay contacts, diodes, fuses) are settled by repeating the solve until they stop changing. Capacitors, inductors and
    /// switching in time are not modelled; <see cref="IDynamicCircuitStep"/> is the seam for them.
    /// </summary>
    public static class CircuitSolver
    {
        /// <summary>Round-trip efficiency of a circuit battery: the same figure as the colony Battery (lithium-ion, 0.90; game rule, TBD).</summary>
        public const double BatteryRoundTrip = 0.90;
        /// <summary>Charge and discharge limit of a circuit battery, capacity per hour (the colony Battery default is 1; game rule, TBD).</summary>
        public const double BatteryCRatePerHour = 1.0;
        public const double SwitchOnOhm = 1e-3;
        public const double GMin = 1e-12;
        /// <summary>Game rule, TBD: a wire may carry this many amperes per mm2 (small copper wire in free air).</summary>
        public const double WireAmpsPerMm2 = 8.0;
        /// <summary>Game rule, TBD: warn when the wires lose more than this share of the source power.</summary>
        public const double LossWarningShare = 0.10;
        /// <summary>Game rule, TBD: warn when a wire drops more than this share of the source voltage.</summary>
        public const double DropWarningShare = 0.10;
        /// <summary>Game rule, TBD: external resistance below this counts as a short, ohm.</summary>
        public const double ShortOhm = 0.05;

        struct Element
        {
            public int A, B;          // pin nodes (-1 = reference)
            public double G, J;       // i(A to B) = G (VA - VB) - J
            public int Component;     // 0 for a wire
            public int Wire;
            public int Tag;           // 1 = a relay's contacts
        }

        public static CircuitResult Solve(CircuitBlueprint design, List<CircuitComponentState> state, double atHours = 0.0)
        {
            var res = new CircuitResult();
            state = state ?? new List<CircuitComponentState>();
            CircuitComponentState St(int id)
            {
                var s = state.Find(x => x.Id == id);
                if (s == null) { s = new CircuitComponentState { Id = id }; state.Add(s); }
                return s;
            }

            // Pin numbering.
            var pinBase = new Dictionary<int, int>();
            int pins = 0;
            foreach (var c in design.Components) { pinBase[c.Id] = pins; pins += CircuitParts.PinCount(c.Type); }
            var seen = new HashSet<int>();
            foreach (var c in design.Components) if (!seen.Add(c.Id)) res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.InvalidConnection, c.Id, 0, "Two parts share one id."));

            // Structure: wires must join real pins, and a power wire only to a power pin.
            var validWires = new List<CircuitWire>();
            var connected = new HashSet<int>();
            foreach (var w in design.Wires)
            {
                bool ok = PinOk(design, w.FromComponent, w.FromPin) && PinOk(design, w.ToComponent, w.ToPin);
                if (!ok) { res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.InvalidConnection, 0, w.Id, "A wire is attached to a pin that does not exist.")); continue; }
                if (w.Kind == WireKind.Signal) { res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.InvalidConnection, 0, w.Id, "These parts have no signal pins; a signal wire cannot join them.")); continue; }
                if (w.LengthM <= 0.0 || w.AreaMm2 <= 0.0) { res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.InvalidConnection, 0, w.Id, "A wire needs a length and a cross-section above zero.")); continue; }
                validWires.Add(w);
                connected.Add(pinBase[w.FromComponent] + w.FromPin);
                connected.Add(pinBase[w.ToComponent] + w.ToPin);
            }
            foreach (var c in design.Components)
                for (int p = 0; p < CircuitParts.PinCount(c.Type); p++)
                    if (!connected.Contains(pinBase[c.Id] + p))
                        res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.DisconnectedTerminal, c.Id, 0, $"Pin {p + 1} of {c.Type} #{c.Id} is not connected."));

            // The reference node: the negative pin of the lowest-numbered source.
            CircuitPlacement first = null;
            foreach (var c in design.Components) if (CircuitParts.IsSource(c.Type) && (first == null || c.Id < first.Id)) first = c;
            foreach (var c in design.Components) { St(c.Id); }
            if (first == null)
            {
                res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.NoSource, 0, 0, "The circuit has no battery or supply."));
                foreach (var c in design.Components) res.Components.Add(new ComponentResult { Id = c.Id });
                foreach (var w in validWires) res.Wires.Add(new WireResult { Id = w.Id, ResistanceOhm = WireOhm(w) });
                res.Solved = true;
                return res;
            }
            var sourcePins = new List<int>();
            foreach (var c in design.Components) if (CircuitParts.IsSource(c.Type)) sourcePins.Add(pinBase[c.Id]);

            var diodeOn = new Dictionary<int, bool>();
            foreach (var c in design.Components) if (c.Type == CircuitComponentType.Diode) diodeOn[c.Id] = true;
            double[] v = null;
            List<Element> elements = null;
            bool settled = false;
            int iter;
            for (iter = 1; iter <= 40 && !settled; iter++)
            {
                elements = Build(design, validWires, pinBase, state, diodeOn);
                v = SolveNodes(elements, pins, sourcePins);
                settled = true;
                foreach (var c in design.Components)
                {
                    var s = St(c.Id);
                    int b = pinBase[c.Id];
                    switch (c.Type)
                    {
                        case CircuitComponentType.Relay:
                        {
                            double coil = Math.Abs((V(v, b) - V(v, b + 1)) / Math.Max(c.P1, 1e-9));
                            bool want = s.Closed ? coil > 0.5 * c.P2 : coil >= c.P2;
                            if (want != s.Closed) { s.Closed = want; settled = false; }
                            break;
                        }
                        case CircuitComponentType.Fuse:
                        {
                            if (s.Blown) break;
                            double i = Math.Abs((V(v, b) - V(v, b + 1)) / SwitchOnOhm);
                            if (i > c.P1) { s.Blown = true; settled = false; res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.ComponentFailure, c.Id, 0, $"The fuse #{c.Id} blew at {i:0.0} A (rated {c.P1:0.0} A).")); }
                            break;
                        }
                        case CircuitComponentType.Diode:
                        {
                            double vd = V(v, b) - V(v, b + 1);
                            bool on = diodeOn[c.Id];
                            bool want = vd > c.P1;   // it conducts only while the forward voltage is above its drop (current runs forward)
                            if (want != on) { diodeOn[c.Id] = want; settled = false; }
                            break;
                        }
                    }
                }
            }
            res.Iterations = iter - 1;
            res.Solved = settled;
            if (!settled) res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.DidNotSettle, 0, 0, "The circuit keeps switching itself on and off (a relay or diode is chattering); it has no steady state."));

            // Results.
            foreach (var c in design.Components)
                for (int p = 0; p < CircuitParts.PinCount(c.Type); p++) res.SetPinVoltage(c.Id, p, V(v, pinBase[c.Id] + p));
            foreach (var c in design.Components) res.Components.Add(new ComponentResult { Id = c.Id });
            // Element currents by part: the first element of a part is its main branch.
            var main = new Dictionary<int, Element>();
            foreach (var e in elements) if (e.Component != 0 && !main.ContainsKey(e.Component)) main[e.Component] = e;
            foreach (var cr in res.Components)
            {
                var c = design.Component(cr.Id);
                int b = pinBase[c.Id];
                if (!main.TryGetValue(c.Id, out var e)) continue;
                double va = V(v, b), vb = V(v, b + 1);
                double i = e.G * (va - vb) - e.J;
                cr.VoltageV = va - vb;
                cr.CurrentA = i;
                cr.PowerW = i * (va - vb);
            }
            foreach (var w in validWires)
            {
                double r = WireOhm(w);
                double va = V(v, pinBase[w.FromComponent] + w.FromPin), vb = V(v, pinBase[w.ToComponent] + w.ToPin);
                double i = (va - vb) / r;
                res.Wires.Add(new WireResult { Id = w.Id, ResistanceOhm = r, CurrentA = i, DropV = va - vb, LossW = i * i * r });
                res.WireLossW += i * i * r;
            }

            // Sources and diagnostics.
            double sourceVoltage = 0.0;
            foreach (var c in design.Components)
            {
                if (!CircuitParts.IsSource(c.Type)) continue;
                var cr = res.Of(c.Id);
                double iOut = cr.CurrentA;   // positive when it delivers: current runs inside it from pin 0 to pin 1
                double pOut = iOut * (V(v, pinBase[c.Id] + 1) - V(v, pinBase[c.Id]));
                res.SourcePowerW += Math.Max(0.0, pOut);
                sourceVoltage = Math.Max(sourceVoltage, c.P1);
                var s = St(c.Id);
                if (c.Type == CircuitComponentType.Battery && s.EnergyWh <= 0.0 && c.P3 > 0.0)
                    res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.BatteryEmpty, c.Id, 0, $"Battery #{c.Id} is empty."));
                if (iOut < 1e-9 && c.P1 > 0.0 && res.Solved)
                    res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.OpenCircuit, c.Id, 0, $"No current leaves #{c.Id}: the circuit is open somewhere."));
                if (iOut > 1e-9 && c.P1 / Math.Max(iOut, 1e-12) < ShortOhm + c.P2)
                    res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.ShortCircuit, c.Id, 0, $"Short circuit: #{c.Id} sees only {c.P1 / iOut:0.000} ohm, and pushes {iOut:0.0} A."));
                if (c.Type == CircuitComponentType.DcSupply && c.P3 > 0.0 && iOut > c.P3)
                    res.Diagnostics.Add(Diag(DiagnosticSeverity.Error, DiagnosticCode.Overcurrent, c.Id, 0, $"Supply #{c.Id} gives {iOut:0.0} A but is rated {c.P3:0.0} A."));
                if (c.Type == CircuitComponentType.Battery && c.P3 > 0.0 && pOut > 0.0)
                    res.BatteryHours = Math.Min(res.BatteryHours, s.EnergyWh / pOut);
            }
            foreach (var c in design.Components)
            {
                var cr = res.Of(c.Id);
                var s = St(c.Id);
                switch (c.Type)
                {
                    case CircuitComponentType.Lamp:
                    {
                        double rated = c.P2 / Math.Max(c.P1, 1e-9);
                        if (Math.Abs(cr.CurrentA) > 1.2 * rated) res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.Overcurrent, c.Id, 0, $"Lamp #{c.Id} gets {Math.Abs(cr.CurrentA):0.00} A; it is rated {rated:0.00} A."));
                        break;
                    }
                    case CircuitComponentType.Resistor:
                        if (c.P2 > 0.0 && cr.PowerW > c.P2) res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.Overcurrent, c.Id, 0, $"Resistor #{c.Id} dissipates {cr.PowerW:0.00} W; it is rated {c.P2:0.00} W."));
                        break;
                }
            }
            foreach (var wr in res.Wires)
            {
                var w = validWires.Find(x => x.Id == wr.Id);
                double rating = w.AreaMm2 * WireAmpsPerMm2;
                if (Math.Abs(wr.CurrentA) > rating)
                    res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.UndersizedWire, 0, w.Id, $"Wire #{w.Id} carries {Math.Abs(wr.CurrentA):0.0} A; {w.AreaMm2:0.0} mm2 is good for {rating:0.0} A."));
                if (sourceVoltage > 0.0 && Math.Abs(wr.DropV) > DropWarningShare * sourceVoltage)
                    res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.ExcessiveVoltageDrop, 0, w.Id, $"Wire #{w.Id} drops {Math.Abs(wr.DropV):0.0} V of {sourceVoltage:0.0} V."));
            }
            if (res.SourcePowerW > 0.0 && res.WireLossW > LossWarningShare * res.SourcePowerW)
                res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.ExcessiveLoss, 0, 0, $"The wires waste {100.0 * res.WireLossW / res.SourcePowerW:0} % of the power."));
            if (design.Components.Exists(c => c.Type == CircuitComponentType.Diode) && res.SourcePowerW < 1e-9)
            {
                // Sources push no current although the parts are wired: tell the player when a diode is the reason.
                foreach (var c in design.Components)
                    if (c.Type == CircuitComponentType.Diode && !diodeOn[c.Id] && !res.Has(DiagnosticCode.ReversedPolarity))
                        res.Diagnostics.Add(Diag(DiagnosticSeverity.Warning, DiagnosticCode.ReversedPolarity, c.Id, 0, $"Diode #{c.Id} blocks the current; check which way it faces."));
            }
            return res;
        }

        static double WireOhm(CircuitWire w)
        {
            double rho = w.Material == WireMaterial.Copper ? ConductorMaterial.Copper.Rho20 : ConductorMaterial.Aluminium.Rho20;
            return rho * w.LengthM / (w.AreaMm2 * 1e-6);
        }

        static bool PinOk(CircuitBlueprint d, int component, int pin)
        {
            var c = d.Component(component);
            return c != null && pin >= 0 && pin < CircuitParts.PinCount(c.Type);
        }

        static double V(double[] v, int pin) => v[pin];

        static CircuitDiagnostic Diag(DiagnosticSeverity s, DiagnosticCode code, int comp, int wire, string msg) =>
            new CircuitDiagnostic { Severity = s, Code = code, Component = comp, Wire = wire, Message = msg };

        static List<Element> Build(CircuitBlueprint d, List<CircuitWire> wires, Dictionary<int, int> pinBase, List<CircuitComponentState> state,
                                   Dictionary<int, bool> diodeOn)
        {
            var list = new List<Element>();
            foreach (var c in d.Components)
            {
                int b = pinBase[c.Id];
                var s = state.Find(x => x.Id == c.Id);
                switch (c.Type)
                {
                    case CircuitComponentType.Battery:
                    case CircuitComponentType.DcSupply:
                    {
                        double volts = c.Type == CircuitComponentType.Battery && s != null && s.EnergyWh <= 0.0 && c.P3 > 0.0 ? 0.0 : c.P1;
                        double g = 1.0 / Math.Max(c.P2, 1e-6);
                        list.Add(new Element { A = b, B = b + 1, G = g, J = -g * volts, Component = c.Id });
                        break;
                    }
                    case CircuitComponentType.Switch:
                    case CircuitComponentType.PushButton:
                        list.Add(new Element { A = b, B = b + 1, G = s != null && s.Closed ? 1.0 / SwitchOnOhm : 0.0, Component = c.Id });
                        break;
                    case CircuitComponentType.Lamp:
                        list.Add(new Element { A = b, B = b + 1, G = c.P2 / Math.Max(c.P1 * c.P1, 1e-12), Component = c.Id });
                        break;
                    case CircuitComponentType.Resistor:
                        list.Add(new Element { A = b, B = b + 1, G = 1.0 / Math.Max(c.P1, 1e-9), Component = c.Id });
                        break;
                    case CircuitComponentType.Fuse:
                        list.Add(new Element { A = b, B = b + 1, G = s != null && s.Blown ? 0.0 : 1.0 / SwitchOnOhm, Component = c.Id });
                        break;
                    case CircuitComponentType.Relay:
                        list.Add(new Element { A = b, B = b + 1, G = 1.0 / Math.Max(c.P1, 1e-9), Component = c.Id });
                        list.Add(new Element { A = b + 2, B = b + 3, G = s != null && s.Closed ? 1.0 / SwitchOnOhm : 0.0, Component = c.Id, Wire = 0, Tag = 1 });
                        break;
                    case CircuitComponentType.Diode:
                    {
                        double gOn = 1.0 / 0.01;
                        list.Add(diodeOn[c.Id] ? new Element { A = b, B = b + 1, G = gOn, J = gOn * c.P1, Component = c.Id } : new Element { A = b, B = b + 1, G = 0.0, Component = c.Id });
                        break;
                    }
                }
            }
            foreach (var w in wires)
                list.Add(new Element { A = pinBase[w.FromComponent] + w.FromPin, B = pinBase[w.ToComponent] + w.ToPin, G = 1.0 / WireOhm(w), Wire = w.Id });
            return list;
        }

        static double[] SolveNodes(List<Element> elements, int pins, List<int> sourcePins)
        {
            // Every island of connected pins gets one reference pin at 0 V: the negative pin of its lowest-numbered source, or, with
            // no source, its lowest pin (nothing drives it, so it stays at 0 V).
            var parent = new int[pins];
            for (int i = 0; i < pins; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var e in elements)
                if (e.G > 1e-15) { int ra = Find(e.A), rb = Find(e.B); if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb); }
            var reference = new Dictionary<int, int>();
            foreach (int sp in sourcePins) { int r = Find(sp); if (!reference.ContainsKey(r)) reference[r] = sp; }
            var isRef = new bool[pins];
            for (int i = 0; i < pins; i++)
            {
                int r = Find(i);
                if (!reference.ContainsKey(r)) reference[r] = i;
            }
            foreach (int p in reference.Values) isRef[p] = true;
            var idx = new int[pins];
            int n = 0;
            for (int i = 0; i < pins; i++) idx[i] = isRef[i] ? -1 : n++;
            var g = new double[n, n];
            var rhs = new double[n];
            foreach (var e in elements)
            {
                int a = idx[e.A], b = idx[e.B];
                if (a >= 0) { g[a, a] += e.G; rhs[a] += e.J; }
                if (b >= 0) { g[b, b] += e.G; rhs[b] -= e.J; }
                if (a >= 0 && b >= 0) { g[a, b] -= e.G; g[b, a] -= e.G; }
            }
            var v = new double[pins];
            if (n == 0) return v;
            double[,] l = DenseSpd.Factor(g, n);
            if (l == null) return v;
            double[] x = DenseSpd.Solve(l, rhs, n);
            for (int i = 0; i < pins; i++) v[i] = idx[i] < 0 ? 0.0 : x[idx[i]];
            return v;
        }

        /// <summary>
        /// Takes the energy a battery gave in a stretch of time out of its charge (steady-state load held over the time). This is the
        /// only time-dependent part so far.
        /// </summary>
        public static void Advance(CircuitBlueprint design, List<CircuitComponentState> state, CircuitResult at, double hours)
        {
            foreach (var c in design.Components)
            {
                if (c.Type != CircuitComponentType.Battery) continue;
                var s = state.Find(x => x.Id == c.Id);
                var cr = at.Of(c.Id);
                if (s == null || cr == null) continue;
                if (cr.PowerW <= 0.0)
                {
                    double giving = -cr.PowerW;
                    s.EnergyWh = Math.Max(0.0, s.EnergyWh - giving * hours);
                }
                else
                {
                    // The solver drove current into it: it charges, like the colony Battery (one-way efficiency = sqrt of the round trip), up to its capacity.
                    double inW = Math.Min(cr.PowerW, c.P3 * BatteryCRatePerHour);   // limited by the C-rate, as in the colony Battery
                    s.EnergyWh = Math.Min(c.P3, s.EnergyWh + inW * Math.Sqrt(BatteryRoundTrip) * hours);
                }
            }
        }
    }

    /// <summary>The seam for time-dependent parts (capacitors, inductors, changing switches). Nothing implements it yet; the steady-state solver does not pretend to.</summary>
    public interface IDynamicCircuitStep
    {
        /// <summary>Moves the circuit forward by dt seconds.</summary>
        void Step(CircuitBlueprint design, List<CircuitComponentState> state, double dtSeconds);
    }
}

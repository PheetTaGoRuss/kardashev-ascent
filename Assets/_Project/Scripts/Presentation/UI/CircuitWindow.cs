using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Circuits;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Circuit editor (F2): a 2D board where the player places real parts, joins pins with wires, sets each wire's physical length
    /// and cross-section by hand, tests the circuit, saves it as a blueprint and installs it in a charging station (electrical
    /// redesign, stage 9, W-40). The window keeps only a draft of the design; every rule (solving, fit, install, what the circuit
    /// controls) lives in the simulation behind <see cref="ICircuitCommands"/>. The position of a part on the board never changes
    /// a wire's resistance: the length is its own number.
    /// </summary>
    public sealed class CircuitWindow : SystemWindow
    {
        public override string Id => "circuits";
        public override string Title => "Circuits";
        public override KeyCode Key => KeyCode.F2;
        public override int Order => 37;
        protected override float Width => 980f;

        enum Tool { Select, Wire, Port }

        CircuitBlueprint draft = new CircuitBlueprint { Name = "New circuit" };
        Tool tool = Tool.Select;
        CircuitComponentType? placing;
        (int Component, int Pin)? pendingPin;
        int selectedComponent, selectedWire;
        CircuitResult lastTest;
        IPlanetSimulation sim;

        BoardElement board;
        VisualElement properties;
        Label diagnostics, hint, installedInfo;
        TextField nameField;
        DropdownField libraryChoice, stationChoice;
        List<string> libraryNames = new List<string>();
        List<(int Tile, int Slot)> stationKeys = new List<(int, int)>();
        string shownStations = "";
        VisualElement switches;
        string shownSwitches = "";

        static readonly CircuitComponentType[] Palette =
        {
            CircuitComponentType.Battery, CircuitComponentType.DcSupply, CircuitComponentType.Switch, CircuitComponentType.PushButton,
            CircuitComponentType.Lamp, CircuitComponentType.Resistor, CircuitComponentType.Fuse, CircuitComponentType.Relay,
            CircuitComponentType.Diode, CircuitComponentType.Junction,
        };

        protected override void BuildBody()
        {
            hint = Text("caption");
            hint.text = "Pick a part, click the board to place it. Wire: click one pin, then another. A wire's length is a number you set; it does not follow the drawing.";

            Heading("PARTS");
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            foreach (CircuitComponentType type in Palette)
            {
                CircuitComponentType t = type;
                row.Add(MakeButton(t.ToString(), () => { placing = t; tool = Tool.Select; pendingPin = null; hint.text = $"Click the board to place a {t}."; }));
            }
            row.Add(MakeButton("Select", () => { placing = null; tool = Tool.Select; pendingPin = null; hint.text = "Click a part or a wire to edit it."; }));
            row.Add(MakeButton("Wire", () => { placing = null; tool = Tool.Wire; pendingPin = null; hint.text = "Click a pin, then the pin to join it to."; }));
            row.Add(MakeButton("Set enable port", () => { placing = null; tool = Tool.Port; pendingPin = null; hint.text = "Click the pin that should switch the station: it is high when this pin is at least half the supply voltage."; }));
            row.Add(MakeButton("Rotate", () => { var c = draft.Component(selectedComponent); if (c != null) { c.Rotation = (c.Rotation + 1) & 3; Changed(); } }));
            row.Add(MakeButton("Delete", DeleteSelection));
            Body.Add(row);

            var main = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            board = new BoardElement(this);
            main.Add(board);
            properties = new VisualElement { style = { marginLeft = 10, flexGrow = 1 } };
            main.Add(properties);
            Body.Add(main);

            Heading("TEST");
            var testRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            testRow.Add(MakeButton("Test run", () => { RunTest(); }));
            Body.Add(testRow);
            diagnostics = Text();

            Heading("BLUEPRINTS");
            nameField = new TextField("Name") { value = draft.Name };
            nameField.RegisterValueChangedCallback(e => draft.Name = e.newValue);
            Body.Add(nameField);
            var libRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            libRow.Add(MakeButton("Save blueprint", () =>
            {
                if (sim == null) return;
                string why = sim.CircuitFitProblem(draft);
                sim.SaveCircuitBlueprint(draft);
                hint.text = why == null ? $"Saved '{draft.Name}'. It fits a charging station." : $"Saved '{draft.Name}'. It does not fit a charging station yet: {why}";
                shownStations = "";
            }));
            libraryChoice = new DropdownField("Saved", new List<string>(), 0);
            libRow.Add(libraryChoice);
            libRow.Add(MakeButton("Open", () =>
            {
                if (sim == null) return;
                var lib = sim.CircuitBlueprints();
                int i = libraryChoice.index;
                if (i < 0 || i >= lib.Count) return;
                draft = lib[i];
                nameField.SetValueWithoutNotify(draft.Name);
                selectedComponent = selectedWire = 0;
                Changed();
            }));
            libRow.Add(MakeButton("Delete saved", () => { if (sim != null && sim.DeleteCircuitBlueprint(libraryChoice.index)) shownStations = ""; }));
            Body.Add(libRow);

            Heading("INSTALL IN A CHARGING STATION");
            var instRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            stationChoice = new DropdownField("Station", new List<string>(), 0);
            instRow.Add(stationChoice);
            instRow.Add(MakeButton("Install saved blueprint", () =>
            {
                if (sim == null || stationChoice.index < 0 || stationChoice.index >= stationKeys.Count) { hint.text = "Build a charging station first."; return; }
                if (libraryChoice.index < 0) { hint.text = "Save a blueprint first, then pick it under Saved."; return; }
                var key = stationKeys[stationChoice.index];
                string why = sim.OrderCircuitInstall(key.Tile, key.Slot, libraryChoice.index);
                hint.text = why ?? "A robot is on its way to install it.";
            }));
            instRow.Add(MakeButton("Remove circuit", () =>
            {
                if (sim == null || stationChoice.index < 0 || stationChoice.index >= stationKeys.Count) return;
                var key = stationKeys[stationChoice.index];
                hint.text = sim.RemoveStationCircuit(key.Tile, key.Slot) ? "Circuit removed; the station charges as before." : "That station has no circuit.";
            }));
            Body.Add(instRow);
            installedInfo = Text();
            switches = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            Body.Add(switches);
            Changed();
        }

        protected override void RefreshBody(IPlanetSimulation s)
        {
            sim = s;
            string libShape = string.Join("|", s.CircuitBlueprints().Select(b => b.Name));
            if (libShape != string.Join("|", libraryNames))
            {
                libraryNames = s.CircuitBlueprints().Select(b => b.Name).ToList();
                libraryChoice.choices = libraryNames;
                if (libraryChoice.index < 0 && libraryNames.Count > 0) libraryChoice.index = 0;
            }
            var stations = s.ChargingStations().ToList();
            string shape = string.Join("|", stations);
            if (shape != shownStations)
            {
                shownStations = shape;
                stationKeys = stations;
                stationChoice.choices = stations.Select(k => $"Tile {k.Tile}, slot {k.Slot}").ToList();
                if (stations.Count > 0 && stationChoice.index < 0) stationChoice.index = 0;
            }
            if (stationChoice.index >= 0 && stationChoice.index < stationKeys.Count)
            {
                var key = stationKeys[stationChoice.index];
                StationCircuitView view = s.StationCircuit(key.Tile, key.Slot);
                if (view == null)
                {
                    installedInfo.text = "No circuit in this station: it charges robots as usual.";
                    switches.Clear();
                    shownSwitches = "";
                }
                else
                {
                    installedInfo.text = $"'{view.Design.Name}' installed. The station is {(view.Enabled ? "charging" : "switched off by its circuit")}."
                        + (view.Faults.Count > 0 ? " Faults: " + string.Join(", ", view.Faults.Select(f => $"{f.Code} on #{f.Component}")) : "")
                        + (view.Result != null && view.Result.BatteryHours < 1e6 ? $"\nBattery lasts about {view.Result.BatteryHours:0.0} h at this load." : "");
                    string sw = string.Join("|", view.SwitchClosed.Select(k => k.Key + ":" + k.Value));
                    if (sw != shownSwitches)
                    {
                        shownSwitches = sw;
                        switches.Clear();
                        foreach (var kv in view.SwitchClosed)
                        {
                            int id = kv.Key; bool closed = kv.Value;
                            switches.Add(MakeButton($"Switch #{id}: {(closed ? "closed" : "open")} (click)", () => { sim.SetCircuitSwitch(key.Tile, key.Slot, id, !closed); shownSwitches = ""; }));
                        }
                    }
                }
            }
            else
            {
                installedInfo.text = "No charging station yet. Build one, then install a circuit in it.";
            }
        }

        // ---- editing ----

        void Changed()
        {
            RunTest();
            board?.Redraw();
            BuildProperties();
        }

        void RunTest()
        {
            lastTest = sim != null && draft.Components.Count > 0 ? sim.TestCircuit(draft) : null;
            if (diagnostics == null) return;
            if (lastTest == null) { diagnostics.text = draft.Components.Count == 0 ? "Empty board." : "Open the game to test."; return; }
            var lines = new List<string>
            {
                $"Source gives {lastTest.SourcePowerW:0.00} W; wires lose {lastTest.WireLossW:0.000} W.",
            };
            foreach (CircuitDiagnostic d in lastTest.Diagnostics)
                lines.Add((d.Severity == DiagnosticSeverity.Error ? "ERROR: " : "Warning: ") + d.Message);
            if (lastTest.Diagnostics.Count == 0) lines.Add("No problems found.");
            string fit = sim.CircuitFitProblem(draft);
            lines.Add(fit == null ? "Fits a charging station." : "Station: " + fit);
            diagnostics.text = string.Join("\n", lines);
        }

        void DeleteSelection()
        {
            if (selectedWire != 0) draft.RemoveWire(selectedWire);
            else if (selectedComponent != 0) draft.RemoveComponent(selectedComponent);
            selectedComponent = selectedWire = 0;
            Changed();
        }

        static string[] ParamNames(CircuitComponentType t)
        {
            switch (t)
            {
                case CircuitComponentType.Battery: return new[] { "Voltage (V)", "Internal resistance (ohm)", "Capacity (Wh)" };
                case CircuitComponentType.DcSupply: return new[] { "Voltage (V)", "Internal resistance (ohm)", "Current limit (A)" };
                case CircuitComponentType.Lamp: return new[] { "Rated voltage (V)", "Rated power (W)", null };
                case CircuitComponentType.Resistor: return new[] { "Resistance (ohm)", "Power rating (W)", null };
                case CircuitComponentType.Fuse: return new[] { "Rating (A)", null, null };
                case CircuitComponentType.Relay: return new[] { "Coil resistance (ohm)", "Pull-in current (A)", null };
                case CircuitComponentType.Diode: return new[] { "Forward drop (V)", null, null };
                default: return new string[] { null, null, null };
            }
        }

        static (double P1, double P2, double P3) Defaults(CircuitComponentType t)
        {
            switch (t)
            {
                case CircuitComponentType.Battery: return (12.0, 0.05, 50.0);
                case CircuitComponentType.DcSupply: return (12.0, 0.01, 10.0);
                case CircuitComponentType.Lamp: return (12.0, 6.0, 0.0);
                case CircuitComponentType.Resistor: return (10.0, 5.0, 0.0);
                case CircuitComponentType.Fuse: return (2.0, 0.0, 0.0);
                case CircuitComponentType.Relay: return (50.0, 0.05, 0.0);
                case CircuitComponentType.Diode: return (0.7, 0.0, 0.0);
                default: return (0.0, 0.0, 0.0);
            }
        }

        void BuildProperties()
        {
            if (properties == null) return;
            properties.Clear();
            var comp = draft.Component(selectedComponent);
            var wire = draft.Wires.Find(w => w.Id == selectedWire);
            if (comp != null)
            {
                properties.Add(Label($"{comp.Type} #{comp.Id}", "value"));
                string[] names = ParamNames(comp.Type);
                for (int i = 0; i < 3; i++)
                {
                    if (names[i] == null) continue;
                    int index = i;
                    var f = new DoubleField(names[i]) { value = index == 0 ? comp.P1 : index == 1 ? comp.P2 : comp.P3 };
                    f.RegisterValueChangedCallback(e => { if (index == 0) comp.P1 = e.newValue; else if (index == 1) comp.P2 = e.newValue; else comp.P3 = e.newValue; draft.Version++; RunTest(); board.Redraw(); });
                    properties.Add(f);
                }
                if (comp.Type == CircuitComponentType.Switch || comp.Type == CircuitComponentType.PushButton)
                {
                    var t = new Toggle("Starts closed") { value = comp.Closed };
                    t.RegisterValueChangedCallback(e => { comp.Closed = e.newValue; draft.Version++; Changed(); });
                    properties.Add(t);
                }
            }
            else if (wire != null)
            {
                properties.Add(Label($"Wire #{wire.Id}", "value"));
                var len = new DoubleField("Physical length (m)") { value = wire.LengthM };
                len.RegisterValueChangedCallback(e => { wire.LengthM = Math.Max(1e-6, e.newValue); draft.Version++; RunTest(); board.Redraw(); });
                properties.Add(len);
                var area = new DoubleField("Cross-section (mm2)") { value = wire.AreaMm2 };
                area.RegisterValueChangedCallback(e => { wire.AreaMm2 = Math.Max(1e-6, e.newValue); draft.Version++; RunTest(); board.Redraw(); });
                properties.Add(area);
                var mat = new EnumField("Metal", wire.Material);
                mat.RegisterValueChangedCallback(e => { wire.Material = (WireMaterial)e.newValue; draft.Version++; RunTest(); });
                properties.Add(mat);
                if (lastTest != null && lastTest.WireOf(wire.Id) != null)
                {
                    WireResult r = lastTest.WireOf(wire.Id);
                    properties.Add(Label($"{r.ResistanceOhm:0.0000} ohm · {Math.Abs(r.CurrentA):0.000} A · drops {Math.Abs(r.DropV):0.000} V · loses {r.LossW:0.0000} W", "caption"));
                }
            }
            else
            {
                properties.Add(Label("Nothing selected.", "caption"));
            }
            if (comp != null && lastTest != null && lastTest.Of(comp.Id) != null)
            {
                ComponentResult r = lastTest.Of(comp.Id);
                properties.Add(Label($"{Math.Abs(r.CurrentA):0.000} A · {r.VoltageV:0.000} V across · {Math.Abs(r.PowerW):0.000} W", "caption"));
            }
            var port = draft.Ports.Find(p => p.Name == "enable");
            properties.Add(Label(port == null ? "No enable port set (a charging station needs one)." : $"Enable port: part #{port.Component}, pin {port.Pin + 1}.", "caption"));
        }

        // ---- the board ----

        static Vector2 PinOffset(CircuitComponentType t, int pin)
        {
            switch (t)
            {
                case CircuitComponentType.Junction: return Vector2.zero;
                case CircuitComponentType.Relay:
                    return new Vector2(pin % 2 == 0 ? -0.38f : 0.38f, pin < 2 ? -0.22f : 0.22f);
                default: return new Vector2(pin == 0 ? -0.38f : 0.38f, 0f);
            }
        }

        sealed class BoardElement : VisualElement
        {
            const float Cell = 44f;
            readonly CircuitWindow w;

            public BoardElement(CircuitWindow window)
            {
                w = window;
                style.width = 16 * Cell; style.height = 12 * Cell;
                style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.9f);
                style.flexShrink = 0;
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(OnDown);
            }

            Vector2 CellCentre(int x, int y) => new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

            Vector2 PinPosition(CircuitPlacement c, int pin)
            {
                Vector2 o = PinOffset(c.Type, pin);
                for (int i = 0; i < c.Rotation; i++) o = new Vector2(-o.y, o.x);
                return CellCentre(c.X, c.Y) + o * Cell;
            }

            public void Redraw()
            {
                Clear();
                foreach (CircuitPlacement c in w.draft.Components)
                {
                    var l = new Label(Short(c.Type) + c.Id);
                    l.pickingMode = PickingMode.Ignore;
                    l.style.position = Position.Absolute;
                    Vector2 p = CellCentre(c.X, c.Y);
                    l.style.left = p.x - 16; l.style.top = p.y - 8;
                    l.style.fontSize = 11;
                    l.style.color = c.Id == w.selectedComponent ? new Color(1f, 0.85f, 0.3f) : new Color(0.85f, 0.88f, 0.92f);
                    Add(l);
                }
                MarkDirtyRepaint();
            }

            static string Short(CircuitComponentType t)
            {
                switch (t)
                {
                    case CircuitComponentType.Battery: return "BAT";
                    case CircuitComponentType.DcSupply: return "PSU";
                    case CircuitComponentType.Switch: return "SW";
                    case CircuitComponentType.PushButton: return "BTN";
                    case CircuitComponentType.Lamp: return "LMP";
                    case CircuitComponentType.Resistor: return "R";
                    case CircuitComponentType.Fuse: return "F";
                    case CircuitComponentType.Relay: return "RLY";
                    case CircuitComponentType.Diode: return "D";
                    default: return "J";
                }
            }

            bool HitPin(Vector2 pos, out int component, out int pin)
            {
                float best = 11f * 11f;
                component = 0; pin = 0;
                foreach (CircuitPlacement c in w.draft.Components)
                    for (int p = 0; p < CircuitParts.PinCount(c.Type); p++)
                    {
                        float d = (PinPosition(c, p) - pos).sqrMagnitude;
                        if (d < best) { best = d; component = c.Id; pin = p; }
                    }
                return component != 0;
            }

            void OnDown(PointerDownEvent e)
            {
                Vector2 pos = e.localPosition;
                if (e.button == 1) { w.selectedComponent = w.selectedWire = 0; w.pendingPin = null; w.placing = null; w.Changed(); return; }
                int cx = Mathf.Clamp((int)(pos.x / Cell), 0, 15), cy = Mathf.Clamp((int)(pos.y / Cell), 0, 11);
                if (w.placing.HasValue)
                {
                    if (w.draft.Components.Exists(c => c.X == cx && c.Y == cy)) { w.hint.text = "That square is taken."; return; }
                    var d = Defaults(w.placing.Value);
                    CircuitPlacement placed = w.draft.Place(w.placing.Value, cx, cy, d.P1, d.P2, d.P3);
                    w.selectedComponent = placed.Id; w.selectedWire = 0;
                    w.Changed();
                    return;
                }
                if (w.tool == Tool.Wire || w.tool == Tool.Port)
                {
                    if (!HitPin(pos, out int comp, out int pin)) return;
                    if (w.tool == Tool.Port)
                    {
                        w.draft.Ports.RemoveAll(p => p.Name == "enable");
                        w.draft.Ports.Add(new CircuitPort { Name = "enable", Component = comp, Pin = pin });
                        w.draft.Version++;
                        w.tool = Tool.Select;
                        w.hint.text = "Enable port set.";
                        w.Changed();
                        return;
                    }
                    if (!w.pendingPin.HasValue) { w.pendingPin = (comp, pin); w.hint.text = "Now click the pin to join it to."; MarkDirtyRepaint(); return; }
                    var a = w.pendingPin.Value;
                    w.pendingPin = null;
                    if (a.Component == comp && a.Pin == pin) return;
                    CircuitWire wire = w.draft.Connect(a.Component, a.Pin, comp, pin);
                    w.selectedWire = wire.Id; w.selectedComponent = 0;
                    w.hint.text = "Wire placed. Set its physical length in the panel on the right.";
                    w.Changed();
                    return;
                }
                // select: a part in the square, else the nearest wire
                CircuitPlacement hit = w.draft.Components.Find(c => c.X == cx && c.Y == cy);
                if (hit != null) { w.selectedComponent = hit.Id; w.selectedWire = 0; w.Changed(); return; }
                float best = 8f; int bestWire = 0;
                foreach (CircuitWire wr in w.draft.Wires)
                {
                    CircuitPlacement a = w.draft.Component(wr.FromComponent), b = w.draft.Component(wr.ToComponent);
                    if (a == null || b == null) continue;
                    float d = DistanceToSegment(pos, PinPosition(a, wr.FromPin), PinPosition(b, wr.ToPin));
                    if (d < best) { best = d; bestWire = wr.Id; }
                }
                w.selectedWire = bestWire; w.selectedComponent = 0;
                w.Changed();
            }

            static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
            {
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return (p - (a + ab * t)).magnitude;
            }

            void Draw(MeshGenerationContext mgc)
            {
                Painter2D p = mgc.painter2D;
                // grid
                p.strokeColor = new Color(1f, 1f, 1f, 0.06f);
                p.lineWidth = 1f;
                for (int x = 0; x <= 16; x++) { p.BeginPath(); p.MoveTo(new Vector2(x * Cell, 0)); p.LineTo(new Vector2(x * Cell, 12 * Cell)); p.Stroke(); }
                for (int y = 0; y <= 12; y++) { p.BeginPath(); p.MoveTo(new Vector2(0, y * Cell)); p.LineTo(new Vector2(16 * Cell, y * Cell)); p.Stroke(); }
                // wires: thicker with more current when a test has run
                foreach (CircuitWire wr in w.draft.Wires)
                {
                    CircuitPlacement a = w.draft.Component(wr.FromComponent), b = w.draft.Component(wr.ToComponent);
                    if (a == null || b == null) continue;
                    double amps = w.lastTest != null && w.lastTest.WireOf(wr.Id) != null ? Math.Abs(w.lastTest.WireOf(wr.Id).CurrentA) : 0.0;
                    bool selected = wr.Id == w.selectedWire;
                    p.strokeColor = selected ? new Color(1f, 0.85f, 0.3f) : amps > 1e-6 ? new Color(0.95f, 0.62f, 0.2f) : new Color(0.55f, 0.6f, 0.68f);
                    p.lineWidth = selected ? 4f : 2f + (float)Math.Min(3.0, amps);
                    p.BeginPath();
                    p.MoveTo(PinPosition(a, wr.FromPin));
                    p.LineTo(PinPosition(b, wr.ToPin));
                    p.Stroke();
                }
                // parts and pins
                var port = w.draft.Ports.Find(q => q.Name == "enable");
                foreach (CircuitPlacement c in w.draft.Components)
                {
                    Vector2 centre = CellCentre(c.X, c.Y);
                    bool sel = c.Id == w.selectedComponent;
                    if (c.Type != CircuitComponentType.Junction)
                    {
                        p.fillColor = sel ? new Color(0.28f, 0.34f, 0.45f) : new Color(0.17f, 0.2f, 0.26f);
                        p.strokeColor = sel ? new Color(1f, 0.85f, 0.3f) : new Color(0.5f, 0.56f, 0.66f);
                        p.lineWidth = 2f;
                        float hw = Cell * 0.26f, hh = Cell * 0.3f;
                        p.BeginPath();
                        p.MoveTo(centre + new Vector2(-hw, -hh)); p.LineTo(centre + new Vector2(hw, -hh));
                        p.LineTo(centre + new Vector2(hw, hh)); p.LineTo(centre + new Vector2(-hw, hh)); p.ClosePath();
                        p.Fill(); p.Stroke();
                    }
                    for (int pin = 0; pin < CircuitParts.PinCount(c.Type); pin++)
                    {
                        bool isPort = port != null && port.Component == c.Id && port.Pin == pin;
                        bool pending = w.pendingPin.HasValue && w.pendingPin.Value.Component == c.Id && w.pendingPin.Value.Pin == pin;
                        p.fillColor = isPort ? new Color(0.3f, 0.9f, 0.45f) : pending ? new Color(1f, 0.85f, 0.3f) : new Color(0.8f, 0.85f, 0.9f);
                        p.BeginPath();
                        p.Arc(PinPosition(c, pin), isPort || pending ? 5.5f : 4f, 0f, 360f);
                        p.Fill();
                    }
                }
            }
        }
    }
}

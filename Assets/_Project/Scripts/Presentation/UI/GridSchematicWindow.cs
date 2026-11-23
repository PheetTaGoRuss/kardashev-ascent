using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Grid schematic (F3): the colony's electrical network drawn flat, like a wiring diagram. Every tile with a line or a power building
    /// is a hub; each wire-role building on it is a row with an input or output pin (a generator gives power: OUT; a consumer takes it:
    /// IN; a battery has both). Hubs are laid out outward from the lander, lines are drawn between them, and islands that are not on the
    /// main grid are outlined. Click a pin and then its hub to wire a building; click a hub and then a neighbouring hub to run a line.
    /// It only reads the grid and calls the same commands as the X wire tool; it computes nothing. A line shows its load only when it is
    /// metered or freshly inspected (gate G3, option B).
    /// </summary>
    public sealed class GridSchematicWindow : SystemWindow
    {
        public override string Id => "gridschematic";
        public override string Title => "Grid";
        public override KeyCode Key => KeyCode.F3;
        public override int Order => 38;
        protected override float Width => 1000f;

        const float HubW = 150f, RowH = 20f, HeadH = 24f, ColGap = 90f, RowGap = 16f, Pad = 20f;

        readonly struct Part
        {
            public readonly int Tile, Slot;
            public readonly BuildingType Type;
            public Part(int tile, int slot, BuildingType type) { Tile = tile; Slot = slot; Type = type; }
            public bool Out => WireRoles.IsOutput(Type) || Type == BuildingType.Battery;
            public bool In => WireRoles.IsInput(Type);
        }

        sealed class Hub
        {
            public int Tile;
            public Rect Rect;
            public List<Part> Parts = new List<Part>();
            public bool Island;
            public bool IsLander;
        }

        IPlanetSimulation sim;
        BoardElement board;
        Label hint;
        Toggle fSources, fLoads, fStorage, fUnwired, fIslands, fHigh;
        TextField search;
        readonly List<Hub> hubs = new List<Hub>();
        readonly Dictionary<int, Hub> hubOf = new Dictionary<int, Hub>();
        List<(int From, int To, bool Built, bool High)> links = new List<(int, int, bool, bool)>();
        readonly HashSet<(int, int)> acPairs = new HashSet<(int, int)>();
        string shape = "";
        (int Tile, int Slot)? pendingPin;
        int pendingHub = -1;
        float contentW = 600f, contentH = 300f;
        Vector2 downPos, pointer;

        protected override void BuildBody()
        {
            hint = Text("caption");
            hint.style.minHeight = 34f;   // two lines kept free: a longer hint must not push the board down while a drag is in progress
            hint.text = "Click a building's pin, then its tile, to wire it. Click a tile, then a neighbouring tile, to run a line between them.";
            var chips = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            Toggle Chip(string text, bool on)
            {
                var t = new Toggle(text) { value = on };
                t.RegisterValueChangedCallback(_ => { shape = ""; });
                chips.Add(t);
                return t;
            }
            fSources = Chip("Sources", true); fLoads = Chip("Loads", true); fStorage = Chip("Storage", true);
            fUnwired = Chip("Unwired only", false); fIslands = Chip("Islands only", false);
            fHigh = new Toggle("New lines high-voltage") { value = false };
            chips.Add(fHigh);
            Body.Add(chips);
            search = new TextField("Find") { value = "" };
            search.RegisterValueChangedCallback(_ => { shape = ""; });
            Body.Add(search);
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { height = 470 } };
            board = new BoardElement(this);
            scroll.Add(board);
            Body.Add(scroll);
            var legend = Label("Gold: lander tile. Blue pin: OUT (gives power). Orange pin: IN (takes power). Red ring: not wired. Violet outline: an island, not on the main grid. Orange line: high-voltage.", "caption");
            legend.AddToClassList("wrap");
            Body.Add(legend);
        }

        protected override void RefreshBody(IPlanetSimulation s)
        {
            sim = s;
            if (!s.WiredGrid)
            {
                hint.text = "This colony does not use the wired grid, so there is nothing to wire.";
                return;
            }
            Layout();
            board.MarkDirtyRepaint();
        }

        // ---- layout ----

        bool PartShown(Part p)
        {
            bool wired = sim.IsBuildingWired(p.Tile, p.Slot);
            if (fUnwired.value && wired) return false;
            bool source = WireRoles.IsOutput(p.Type), storage = p.Type == BuildingType.Battery;
            bool load = WireRoles.IsInput(p.Type) && !storage;
            if (source && !fSources.value) return false;
            if (storage && !fStorage.value) return false;
            if (load && !fLoads.value) return false;
            string q = search.value;
            if (!string.IsNullOrWhiteSpace(q) && p.Type.ToString().IndexOf(q.Trim(), StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        void Layout()
        {
            var parts = sim.WireableBuildings().Select(b => new Part(b.Tile, b.Slot, b.Type)).Where(PartShown).ToList();
            links = sim.WireLinks().ToList();
            acPairs.Clear();
            foreach (var ac in sim.AcLinks())
            {
                links.Add((ac.From, ac.To, ac.Built, true));
                acPairs.Add((System.Math.Min(ac.From, ac.To), System.Math.Max(ac.From, ac.To)));
            }
            string sig = string.Join(";", parts.Select(p => $"{p.Tile}.{p.Slot}:{(sim.IsBuildingWired(p.Tile, p.Slot) ? 1 : 0)}")) + "|" +
                         string.Join(";", links.Select(l => $"{l.From}-{l.To}:{(l.Built ? 1 : 0)}{(l.High ? 1 : 0)}")) + "|" + fIslands.value + "|" + sim.BaseTileId;
            if (sig == shape) return;
            shape = sig;

            var tiles = new SortedSet<int> { sim.BaseTileId };
            foreach (var l in links) { tiles.Add(l.From); tiles.Add(l.To); }
            foreach (var p in parts) tiles.Add(p.Tile);
            var adj = new Dictionary<int, List<int>>();
            foreach (int t in tiles) adj[t] = new List<int>();
            foreach (var l in links) { adj[l.From].Add(l.To); adj[l.To].Add(l.From); }

            // depth from the lander, then from the lowest tile of each separate island
            var depth = new Dictionary<int, int>();
            var order = new List<int>();
            void Walk(int root)
            {
                var q = new Queue<int>();
                depth[root] = 0; q.Enqueue(root);
                while (q.Count > 0)
                {
                    int t = q.Dequeue();
                    order.Add(t);
                    foreach (int n in adj[t].OrderBy(x => x))
                        if (!depth.ContainsKey(n)) { depth[n] = depth[t] + 1; q.Enqueue(n); }
                }
            }
            Walk(sim.BaseTileId);
            int mainCount = order.Count;
            var islandTiles = new HashSet<int>();
            foreach (int t in tiles)
                if (!depth.ContainsKey(t)) { int before = order.Count; Walk(t); for (int i = before; i < order.Count; i++) islandTiles.Add(order[i]); }

            hubs.Clear(); hubOf.Clear();
            var partsOf = parts.GroupBy(p => p.Tile).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Slot).ToList());
            var yOfColumn = new Dictionary<int, float>();
            float maxX = 0f, maxY = 0f;
            float islandOffset = 0f;
            // main grid first (rows by column), islands below it
            foreach (bool island in new[] { false, true })
            {
                yOfColumn.Clear();
                float top = island ? islandOffset : 0f;
                foreach (int t in order)
                {
                    if (islandTiles.Contains(t) != island) continue;
                    if (fIslands.value && !island) continue;
                    var hub = new Hub { Tile = t, Island = island, IsLander = t == sim.BaseTileId };
                    if (partsOf.TryGetValue(t, out var ps)) hub.Parts = ps;
                    float h = HeadH + Math.Max(1, hub.Parts.Count) * RowH + 6f;
                    int col = depth[t];
                    float y = yOfColumn.TryGetValue(col, out float cy) ? cy : top;
                    hub.Rect = new Rect(Pad + col * (HubW + ColGap), Pad + y, HubW, h);
                    yOfColumn[col] = y + h + RowGap;
                    maxX = Math.Max(maxX, hub.Rect.xMax); maxY = Math.Max(maxY, hub.Rect.yMax);
                    hubs.Add(hub); hubOf[t] = hub;
                }
                islandOffset = (yOfColumn.Count == 0 ? top : yOfColumn.Values.Max()) + 30f;
            }
            contentW = Math.Max(700f, maxX + Pad); contentH = Math.Max(300f, maxY + Pad);
            board.style.width = contentW; board.style.height = contentH;
            board.Rebuild();
        }

        // ---- clicks ----

        void OnBoardClick(Vector2 pos)
        {
            if (sim == null) return;
            downPos = pos; pointer = pos;
            // pins first
            foreach (Hub h in hubs)
                for (int i = 0; i < h.Parts.Count; i++)
                {
                    Part p = h.Parts[i];
                    foreach (Vector2 pin in PinPositions(h, i, p))
                        if ((pin - pos).sqrMagnitude < 81f)
                        {
                            pendingPin = (p.Tile, p.Slot); pendingHub = -1;
                            hint.text = $"{p.Type} on tile {p.Tile}: now click tile {p.Tile} to wire it.";
                            board.MarkDirtyRepaint();
                            return;
                        }
                }
            foreach (Hub h in hubs)
            {
                if (!new Rect(h.Rect.x, h.Rect.y, h.Rect.width, HeadH).Contains(pos)) continue;
                ClickHub(h);
                return;
            }
            pendingPin = null; pendingHub = -1;
            board.MarkDirtyRepaint();
        }

        /// <summary>Letting go of a drag: the pin or tile picked up on the way down is dropped on the tile under the pointer.</summary>
        void OnBoardRelease(Vector2 pos)
        {
            if (sim == null || (pos - downPos).magnitude <= 8f) return;   // a click, already handled on the way down
            foreach (Hub h in hubs)
                if (new Rect(h.Rect.x, h.Rect.y, h.Rect.width, HeadH).Contains(pos) && (pendingPin.HasValue || pendingHub >= 0))
                {
                    ClickHub(h);
                    return;
                }
            pendingPin = null; pendingHub = -1;
            hint.text = "Dropped on nothing: cancelled.";
            board.MarkDirtyRepaint();
        }

        void ClickHub(Hub h)
        {
            {
                if (pendingPin.HasValue)
                {
                    var pp = pendingPin.Value;
                    pendingPin = null;
                    if (pp.Tile != h.Tile) { hint.text = "A building wires to the tile it stands on: click that tile."; board.MarkDirtyRepaint(); return; }
                    hint.text = sim.PlaceBuildingWire(pp.Tile, pp.Slot) ? "Wired." : (sim.WireProblem ?? "Could not wire it.");
                    shape = "";
                    return;
                }
                if (pendingHub < 0) { pendingHub = h.Tile; hint.text = $"Tile {h.Tile}: click or drag to a neighbouring tile to run a line."; board.MarkDirtyRepaint(); return; }
                int from = pendingHub; pendingHub = -1;
                if (from == h.Tile) { board.MarkDirtyRepaint(); return; }
                hint.text = sim.PlaceWire(from, h.Tile, fHigh.value) ? "Line placed." : (sim.WireProblem ?? "Could not place the line.");
                shape = "";
            }
        }

        static IEnumerable<Vector2> PinPositions(Hub h, int row, Part p)
        {
            float y = h.Rect.y + HeadH + row * RowH + RowH * 0.5f;
            if (p.In) yield return new Vector2(h.Rect.x, y);
            if (p.Out) yield return new Vector2(h.Rect.xMax, y);
        }

        // ---- the board ----

        sealed class BoardElement : VisualElement
        {
            readonly GridSchematicWindow w;

            public BoardElement(GridSchematicWindow window)
            {
                w = window;
                style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.9f);
                style.width = 700; style.height = 300;
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { this.CapturePointer(e.pointerId); w.OnBoardClick(e.localPosition); } });
                RegisterCallback<PointerMoveEvent>(e => { if (w.pendingPin.HasValue || w.pendingHub >= 0) { w.pointer = e.localPosition; MarkDirtyRepaint(); } });
                RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && this.HasPointerCapture(e.pointerId)) { this.ReleasePointer(e.pointerId); w.OnBoardRelease(e.localPosition); } });
            }

            public void Rebuild()
            {
                Clear();
                foreach (Hub h in w.hubs)
                {
                    string conv = string.Join(", ", w.sim.ConvertersOn(h.Tile).Select(c => c.ToString().ToLowerInvariant()));
                    AddLabel($"Tile {h.Tile}" + (h.IsLander ? " · lander" : h.Island ? " · island" : "") + (conv.Length > 0 ? " · " + conv : ""), h.Rect.x + 6, h.Rect.y + 3, 12, new Color(0.92f, 0.94f, 0.97f));
                    for (int i = 0; i < h.Parts.Count; i++)
                    {
                        Part p = h.Parts[i];
                        bool wired = w.sim.IsBuildingWired(p.Tile, p.Slot);
                        string io = p.In && p.Out ? "IN/OUT" : p.Out ? "OUT" : "IN";
                        AddLabel($"{Short(p.Type)} {p.Slot} · {io}" + (wired ? "" : " · unwired"), h.Rect.x + 10, h.Rect.y + HeadH + i * RowH + 3, 11,
                                 wired ? new Color(0.8f, 0.84f, 0.9f) : new Color(1f, 0.55f, 0.5f));
                    }
                    if (h.Parts.Count == 0) AddLabel("no buildings", h.Rect.x + 10, h.Rect.y + HeadH + 3, 11, new Color(0.5f, 0.55f, 0.62f));
                }
            }

            void AddLabel(string text, float x, float y, int size, Color colour)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                l.style.position = Position.Absolute;
                l.style.left = x; l.style.top = y; l.style.fontSize = size; l.style.color = colour;
                Add(l);
            }

            static string Short(BuildingType t)
            {
                switch (t)
                {
                    case BuildingType.SolarPanel: return "Panel";
                    case BuildingType.FloatingSolar: return "Float. solar";
                    case BuildingType.TidalTurbine: return "Tidal";
                    case BuildingType.WaveConverter: return "Wave";
                    case BuildingType.TidalBarrage: return "Barrage";
                    case BuildingType.ChargingStation: return "Charger";
                    case BuildingType.ResearchStation: return "Research";
                    default: return t.ToString();
                }
            }

            void Draw(MeshGenerationContext mgc)
            {
                if (w.sim == null) return;
                Painter2D p = mgc.painter2D;
                // lines
                foreach (var l in w.links)
                {
                    if (!w.hubOf.TryGetValue(l.From, out Hub a) || !w.hubOf.TryGetValue(l.To, out Hub b)) continue;
                    bool aLeft = a.Rect.x <= b.Rect.x;
                    Vector2 pa = new Vector2(aLeft ? a.Rect.xMax : a.Rect.x, a.Rect.y + HeadH * 0.5f);
                    Vector2 pb = new Vector2(aLeft ? b.Rect.x : b.Rect.xMax, b.Rect.y + HeadH * 0.5f);
                    if (Mathf.Approximately(a.Rect.x, b.Rect.x)) { pa = new Vector2(a.Rect.x, a.Rect.y + HeadH * 0.5f); pb = new Vector2(b.Rect.x, b.Rect.y + HeadH * 0.5f); }
                    LineReadout ra = w.sim.LineReadout(l.From), rb = w.sim.LineReadout(l.To);
                    bool known = ra.Measured || rb.Measured;
                    bool over = (ra.Measured && ra.Overloaded) || (rb.Measured && rb.Overloaded);
                    Color c = !l.Built ? new Color(0.5f, 0.55f, 0.62f, 0.5f) : over ? new Color(0.95f, 0.3f, 0.25f) : l.High ? new Color(1f, 0.62f, 0.25f) : new Color(0.95f, 0.8f, 0.3f);
                    if (!known && l.Built && !over) c = l.High ? new Color(1f, 0.62f, 0.25f) : new Color(0.85f, 0.75f, 0.35f);
                    if (w.acPairs.Contains((Mathf.Min(l.From, l.To), Mathf.Max(l.From, l.To))) && !over) c = l.Built ? new Color(0.4f, 0.85f, 1f) : new Color(0.4f, 0.85f, 1f, 0.4f);
                    p.strokeColor = c;
                    p.lineWidth = l.High ? 4f : 2.5f;
                    p.BeginPath();
                    p.MoveTo(pa);
                    float mid = (pa.x + pb.x) * 0.5f;
                    p.BezierCurveTo(new Vector2(mid, pa.y), new Vector2(mid, pb.y), pb);
                    p.Stroke();
                }
                // the wire being pulled
                if (w.pendingPin.HasValue || w.pendingHub >= 0)
                {
                    Vector2? from = null;
                    foreach (Hub h in w.hubs)
                    {
                        if (w.pendingHub == h.Tile) from = new Vector2(h.Rect.xMax, h.Rect.y + HeadH * 0.5f);
                        for (int i = 0; i < h.Parts.Count; i++)
                            if (w.pendingPin.HasValue && w.pendingPin.Value.Tile == h.Parts[i].Tile && w.pendingPin.Value.Slot == h.Parts[i].Slot)
                                from = PinPositions(h, i, h.Parts[i]).First();
                    }
                    if (from.HasValue)
                    {
                        p.strokeColor = new Color(1f, 0.9f, 0.4f, 0.9f);
                        p.lineWidth = 2.5f;
                        p.BeginPath(); p.MoveTo(from.Value); p.LineTo(w.pointer); p.Stroke();
                    }
                }
                // hubs
                foreach (Hub h in w.hubs)
                {
                    bool sel = w.pendingHub == h.Tile;
                    p.fillColor = h.IsLander ? new Color(0.35f, 0.3f, 0.12f) : new Color(0.15f, 0.18f, 0.24f);
                    p.strokeColor = sel ? new Color(1f, 0.9f, 0.4f) : h.Island ? new Color(0.65f, 0.45f, 0.95f) : h.IsLander ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.4f, 0.48f, 0.6f);
                    p.lineWidth = sel || h.Island ? 3f : 2f;
                    Box(p, h.Rect);
                    p.Fill(); p.Stroke();
                    for (int i = 0; i < h.Parts.Count; i++)
                    {
                        Part part = h.Parts[i];
                        bool wired = w.sim.IsBuildingWired(part.Tile, part.Slot);
                        bool pend = w.pendingPin.HasValue && w.pendingPin.Value.Tile == part.Tile && w.pendingPin.Value.Slot == part.Slot;
                        foreach (Vector2 pin in PinPositions(h, i, part))
                        {
                            bool isOut = Mathf.Approximately(pin.x, h.Rect.xMax);
                            Color pc = isOut ? new Color(0.35f, 0.65f, 1f) : new Color(1f, 0.6f, 0.2f);
                            p.fillColor = pend ? new Color(1f, 0.9f, 0.4f) : wired ? pc : new Color(0.1f, 0.1f, 0.12f);
                            p.strokeColor = wired ? pc : new Color(0.95f, 0.3f, 0.25f);
                            p.lineWidth = 2f;
                            p.BeginPath(); p.Arc(pin, 5f, 0f, 360f); p.Fill(); p.Stroke();
                        }
                    }
                }
            }

            static void Box(Painter2D p, Rect r)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(r.x, r.y)); p.LineTo(new Vector2(r.xMax, r.y));
                p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.x, r.yMax)); p.ClosePath();
            }
        }
    }
}

using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using KardashevAscent.Presentation.UI;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace KardashevAscent.PlayModeTests
{
    /// <summary>
    /// Pointer input through the real UI Toolkit panel: a synthetic mouse down, move and up is picked at a panel position and sent to
    /// whatever element is there, the way the mouse does it. Covers the F3 grid schematic (drag a pin or a tile to wire it) and the F2
    /// circuit editor (place parts, wire two pins). The geometry of the windows is read back from what they draw (their labels), not from
    /// their internals, so these tests see what a player sees.
    /// </summary>
    public class UiPointerTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        GameObject host;
        UIDocument doc;
        RenderTexture rt;

        [SetUp]
        public void SetUp()
        {
            rt = new RenderTexture(1600, 1200, 0);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.targetTexture = rt;
            host = new GameObject("ui-test");
            doc = host.AddComponent<UIDocument>();
            doc.panelSettings = settings;
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) UnityEngine.Object.Destroy(host);
            if (rt != null) rt.Release();
        }

        // ---- the synthetic mouse ----

        /// <summary>Send one mouse event at a panel position to the element found there (the same pick the real mouse gets).</summary>
        void Mouse(EventType type, Vector2 panelPos, int button = 0)
        {
            IPanel panel = doc.rootVisualElement.panel;
            VisualElement hit = panel.Pick(panelPos);
            Assert.IsNotNull(hit, $"nothing under the pointer at {panelPos}");
            var sys = new Event { type = type, mousePosition = panelPos, button = button, clickCount = 1 };
            using (EventBase e = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(sys)
                               : type == EventType.MouseUp ? (EventBase)PointerUpEvent.GetPooled(sys) : PointerMoveEvent.GetPooled(sys))
            {
                e.target = hit;
                hit.SendEvent(e);
            }
        }

        IEnumerator ClickAt(Vector2 p)
        {
            Mouse(EventType.MouseDown, p); yield return null;
            Mouse(EventType.MouseUp, p); yield return null;
        }

        /// <summary>Drag between two points that are re-read at every step: the layout may move under the pointer, as it can for a player.</summary>
        IEnumerator Drag(Func<Vector2> from, Func<Vector2> to)
        {
            Mouse(EventType.MouseDown, from()); yield return null;
            for (int i = 1; i <= 4; i++) { Mouse(EventType.MouseDrag, Vector2.Lerp(from(), to(), i / 4f)); yield return null; }
            Mouse(EventType.MouseUp, to()); yield return null;
        }

        IEnumerator Show(SystemWindow w, IPlanetSimulation sim)
        {
            VisualElement root = w.Build();
            root.style.position = Position.Absolute; root.style.left = 10; root.style.top = 10;
            doc.rootVisualElement.Add(root);
            w.SetVisible(true);
            w.Refresh(sim);
            for (int i = 0; i < 4; i++) yield return null;
            w.Refresh(sim);
            for (int i = 0; i < 4; i++) yield return null;
        }

        static VisualElement FindByTypeName(VisualElement root, string name) => root.Query<VisualElement>().ToList().First(e => e.GetType().Name == name);

        // ---- F3: the grid schematic ----

        static PlanetSimulation Colony(out int t1)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            sim.Debug.UnlockAll = true;
            int B = sim.BaseTileId;
            t1 = Sphere.Neighbors[B].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            return sim;
        }

        /// <summary>The board and the top left of a tile's hub, read from the label the window draws ("Tile N", 6 px right and 3 px down from the corner).</summary>
        static (VisualElement Board, Vector2 Corner) Hub(VisualElement root, int tile)
        {
            VisualElement board = FindByTypeName(root, "BoardElement");
            Label label = board.Query<Label>().ToList().First(l => Regex.IsMatch(l.text, $@"^Tile {tile}( ·|$)"));
            return (board, new Vector2(label.resolvedStyle.left - 6f, label.resolvedStyle.top - 3f));
        }

        static Vector2 ToPanel(VisualElement board, Vector2 local) => board.LocalToWorld(local);

        [UnityTest]
        public IEnumerator F3_DraggingABuildingsPinOntoItsTileWiresIt()
        {
            PlanetSimulation sim = Colony(out int t1);
            Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, t1, false));
            Assert.IsTrue(sim.PlacePanel(t1, 1, 0.35, Math.PI));
            Assert.IsFalse(sim.IsBuildingWired(t1, 1), "starts unwired");
            var w = new GridSchematicWindow();
            yield return Show(w, sim);

            var (board, hub) = Hub(doc.rootVisualElement, t1);
            // a panel gives power: its pin is on the right edge of the hub, in the middle of its row (header 24, row 20, hub 150 wide)
            yield return Drag(() => ToPanel(board, hub + new Vector2(150f, 24f + 10f)), () => ToPanel(board, hub + new Vector2(75f, 12f)));
            Assert.IsTrue(sim.IsBuildingWired(t1, 1), "dragging the pin onto its tile wired the panel");
        }

        [UnityTest]
        public IEnumerator F3_DroppingAPinOnNothingCancelsAndWiresNothing()
        {
            PlanetSimulation sim = Colony(out int t1);
            Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, t1, false));
            Assert.IsTrue(sim.PlacePanel(t1, 1, 0.35, Math.PI));
            var w = new GridSchematicWindow();
            yield return Show(w, sim);
            var (board, hub) = Hub(doc.rootVisualElement, t1);
            yield return Drag(() => ToPanel(board, hub + new Vector2(150f, 34f)), () => ToPanel(board, hub + new Vector2(150f + 40f, 300f)));
            Assert.IsFalse(sim.IsBuildingWired(t1, 1), "dropped on nothing: not wired");
        }

        [UnityTest]
        public IEnumerator F3_DraggingOneTileOntoANeighbourRunsALineBetweenThem()
        {
            PlanetSimulation sim = Colony(out int a);
            int b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePanel(a, 1, 0.35, Math.PI));
            Assert.IsTrue(sim.PlacePanel(b, 1, 0.35, Math.PI));
            var w = new GridSchematicWindow();
            yield return Show(w, sim);
            var (board, ha) = Hub(doc.rootVisualElement, a);
            var (_, hb) = Hub(doc.rootVisualElement, b);
            int before = sim.WireLinks().Count();
            yield return Drag(() => ToPanel(board, ha + new Vector2(75f, 12f)), () => ToPanel(board, hb + new Vector2(75f, 12f)));
            Assert.AreEqual(before + 1, sim.WireLinks().Count(), "a line was run between the two tiles");
            Assert.IsTrue(sim.WireLinks().Any(l => (l.From == a && l.To == b) || (l.From == b && l.To == a)));
        }

        [UnityTest]
        public IEnumerator F3_ClickingAPinThenItsTileAlsoWires()
        {
            PlanetSimulation sim = Colony(out int t1);
            Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, t1, false));
            Assert.IsTrue(sim.PlacePanel(t1, 1, 0.35, Math.PI));
            var w = new GridSchematicWindow();
            yield return Show(w, sim);
            var (board, hub) = Hub(doc.rootVisualElement, t1);
            yield return ClickAt(ToPanel(board, hub + new Vector2(150f, 34f)));
            yield return ClickAt(ToPanel(board, hub + new Vector2(75f, 12f)));
            Assert.IsTrue(sim.IsBuildingWired(t1, 1));
        }

        // ---- F2: the circuit editor ----

        const float Cell = 44f;

        static Vector2 CellCentre(int x, int y) => new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

        [UnityTest]
        public IEnumerator F2_PlacingABatteryAndALampAndWiringThemMakesACircuitThatRuns()
        {
            PlanetSimulation sim = Colony(out _);
            var w = new CircuitWindow();
            yield return Show(w, sim);
            VisualElement root = doc.rootVisualElement;
            VisualElement board = FindByTypeName(root, "BoardElement");
            Button Btn(string text) => root.Query<Button>().ToList().First(b => b.text == text);

            yield return ClickAt(Btn("Battery").worldBound.center);
            yield return ClickAt(board.LocalToWorld(CellCentre(3, 4)));
            yield return ClickAt(Btn("Lamp").worldBound.center);
            yield return ClickAt(board.LocalToWorld(CellCentre(8, 4)));
            yield return ClickAt(Btn("Wire").worldBound.center);
            // battery pin 1 (right, +0.38 cell) to lamp pin 0 (left), then battery pin 0 to lamp pin 1 to close the loop
            yield return ClickAt(board.LocalToWorld(CellCentre(3, 4) + new Vector2(0.38f * Cell, 0f)));
            yield return ClickAt(board.LocalToWorld(CellCentre(8, 4) + new Vector2(-0.38f * Cell, 0f)));
            yield return ClickAt(board.LocalToWorld(CellCentre(3, 4) + new Vector2(-0.38f * Cell, 0f)));
            yield return ClickAt(board.LocalToWorld(CellCentre(8, 4) + new Vector2(0.38f * Cell, 0f)));

            var labels = root.Query<Label>().ToList();
            Label diag = labels.FirstOrDefault(l => l.text.StartsWith("Source gives"));
            Assert.IsNotNull(diag, "the circuit was tested: " + string.Join(" | ", labels.Select(l => l.text).Where(t => t.Length > 0).Take(12)));
            Match m = Regex.Match(diag.text, @"Source gives ([0-9.]+) W");
            Assert.IsTrue(m.Success);
            Assert.Greater(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 0.0, "current flows round the loop: " + diag.text);
            Assert.IsTrue(labels.Any(l => l.text.StartsWith("BAT")) && labels.Any(l => l.text.StartsWith("LMP")), "both parts are drawn");
        }

        // ---- the fit puzzle ----

        static void FinishExperiment(PlanetSimulation sim, ExperimentKind kind)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            sim.GetType().GetMethod("LearnExperimentForTests", F).Invoke(sim, new object[] { kind });
            sim.GetType().GetMethod("OnExperimentFinished", F).Invoke(sim, new object[] { kind });
        }

        [UnityTest]
        public IEnumerator Fit_DraggingASliderMovesTheCurve_AndUseThisFitNarrowsTheRange()
        {
            PlanetSimulation sim = Colony(out _);
            FinishExperiment(sim, ExperimentKind.AirPressure);
            string said = null;
            var view = new FitPuzzleView(sim, ExperimentKind.AirPressure, r => said = r);
            view.style.position = Position.Absolute; view.style.left = 10; view.style.top = 10; view.style.width = 700;
            doc.rootVisualElement.Add(view);
            for (int i = 0; i < 6; i++) yield return null;

            var sliders = view.Query<Slider>().ToList();
            Assert.AreEqual(2, sliders.Count, "pressure has two handles");
            float before0 = sliders[0].value, before1 = sliders[1].value;
            // Move each handle to the least-squares place. (Unity's own Slider does not follow synthetic pointer drags, so the value is set
            // directly; the buttons below are pressed with real pointer events.)
            var puzzle = sim.PuzzleFor(ExperimentKind.AirPressure);
            for (int k = 0; k < 2; k++) sliders[k].value = (float)puzzle.Best[k];
            yield return null;
            Assert.AreNotEqual(before0, sliders[0].value, "the first handle moved");
            Assert.AreNotEqual(before1, sliders[1].value, "the second handle moved");
            Assert.AreEqual(1.0, sim.ExperimentRangeShare(ExperimentKind.AirPressure), "nothing changes until the fit is used");

            Button use = view.Query<Button>().ToList().First(b => b.text == "Use this fit");
            yield return ClickAt(use.worldBound.center);
            Assert.IsNotNull(said, "the view reported a result");
            Assert.Less(sim.ExperimentRangeShare(ExperimentKind.AirPressure), 1.0, "a better fit than the starting one narrowed the range");
            Assert.IsEmpty(sim.PendingPuzzles);
        }

        [UnityTest]
        public IEnumerator Fit_FitForMeSettlesTheRangeAtTheAutoShare()
        {
            PlanetSimulation sim = Colony(out _);
            FinishExperiment(sim, ExperimentKind.Gravity);
            var view = new FitPuzzleView(sim, ExperimentKind.Gravity, null);
            view.style.position = Position.Absolute; view.style.left = 10; view.style.top = 10; view.style.width = 700;
            doc.rootVisualElement.Add(view);
            for (int i = 0; i < 6; i++) yield return null;
            Button auto = view.Query<Button>().ToList().First(b => b.text == "Fit for me");
            yield return ClickAt(auto.worldBound.center);
            Assert.AreEqual(KardashevAscent.Simulation.Planet.FitPuzzle.AutoFitShare, sim.ExperimentRangeShare(ExperimentKind.Gravity), 1e-9);
        }

        // ---- epistemic marks ----

        [UnityTest]
        public IEnumerator Marks_AnEstimateShowsItsRangeAndAMeasureLink_AMeasuredValueShowsNeither_AndTheLinkIsPressedByPointer()
        {
            var label = new ValueLabel();
            label.style.position = Position.Absolute; label.style.left = 10; label.style.top = 10;
            doc.rootVisualElement.Add(label);
            int pressed = 0;
            label.SetEstimate(new KnowledgeEstimate(78000.0, 49000.0), "0.#", "kPa", 0.001, "Measure", () => pressed++, "Order the barometer");
            for (int i = 0; i < 4; i++) yield return null;
            StringAssert.Contains("◐ estimate", label.Shown);
            StringAssert.Contains("about 78 kPa", label.Shown);
            StringAssert.Contains("29 to 127", label.Shown);
            StringAssert.Contains("Measure ›", label.Shown);
            Button link = label.Query<Button>().ToList().First(b => b.text.StartsWith("Measure"));
            yield return ClickAt(link.worldBound.center);
            Assert.AreEqual(1, pressed, "the Measure link opens the experiment");
            label.SetEstimate(new KnowledgeEstimate(93900.0, 0.0), "0.#", "kPa", 0.001, "Measure", () => pressed++);
            yield return null;
            StringAssert.Contains("● measured", label.Shown);
            StringAssert.DoesNotContain("Measure ›", label.Shown);
            StringAssert.DoesNotContain(" to ", label.Shown);
        }

        [Test]
        public void Marks_EveryMarkIsAShapeAndAWord()
        {
            foreach (Mark m in System.Enum.GetValues(typeof(Mark)))
            {
                Assert.IsNotEmpty(Marks.Glyph(m));
                Assert.IsNotEmpty(Marks.Word(m));
                Assert.IsNotEmpty(Marks.Tip(m));
            }
            Assert.AreEqual(5, System.Enum.GetValues(typeof(Mark)).Length);
        }

        [UnityTest]
        public IEnumerator Tips_ALongTipShowsTwoLinesWithMoreAndAGuideLink_NothingIsLost()
        {
            string longText = string.Concat(Enumerable.Repeat("Until a value is measured, buildings are designed for the cautious end of its range. ", 4)).Trim();
            var host2 = new VisualElement { style = { position = Position.Absolute, left = 10, top = 10, width = 600 } };
            var row = new Label("A value");
            host2.Add(row);
            doc.rootVisualElement.Add(host2);
            InfoTip tip = InfoTip.After(row, longText, "Cables and ratings");
            for (int i = 0; i < 4; i++) yield return null;
            yield return ClickAt(tip.Button.worldBound.center);
            for (int i = 0; i < 2; i++) yield return null;
            Assert.Less(tip.Detail.text.Length, longText.Length, "the tip is cut to about two lines");
            StringAssert.EndsWith("…", tip.Detail.text);
            var labels = host2.Query<Label>().ToList();
            Label more = labels.First(l => l.text.StartsWith("More"));
            Assert.IsTrue(labels.Any(l => l.text.StartsWith("Guide")), "a Guide link points to the page");
            yield return ClickAt(more.worldBound.center);
            Assert.AreEqual(longText, tip.Detail.text, "More shows all of it");
        }

        [UnityTest]
        public IEnumerator FlowBar_BothRowsAreOnOneScale_AndSegmentsAreNamedWithTheirWatts()
        {
            var bar = new FlowBar();
            bar.style.width = 600;
            doc.rootVisualElement.Add(bar);
            bar.Set(new[] { new FlowBar.Segment("Solar", 6000.0, Color.yellow), new FlowBar.Segment("RTG", 440.0, Color.cyan) },
                    new[] { new FlowBar.Segment("Colony", 5000.0, Color.blue), new FlowBar.Segment("Battery in", 1000.0, Color.green) });
            for (int i = 0; i < 3; i++) yield return null;
            var labels = bar.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.IsTrue(labels.Any(t => t.Contains("Solar 6 kW") && t.Contains("RTG 440 W")), string.Join(" | ", labels));
            Assert.IsTrue(labels.Any(t => t.Contains("Colony 5 kW") && t.Contains("Battery in 1 kW")));
            // the shorter row (6.0 kW used against 6.44 kW made) leaves an empty tail on the common scale
            var rows = bar.Query<VisualElement>().Where(e => e.style.height == 18).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.Greater(rows[0].childCount, 1);
        }

        [UnityTest]
        public IEnumerator Why_AChainHasStepsAndOneAction_AnEmptyChainIsHidden_AndTheActionIsPressedByPointer()
        {
            var chain = new WhyChain();
            chain.style.position = Position.Absolute; chain.style.left = 10; chain.style.top = 10; chain.style.width = 500;
            doc.rootVisualElement.Add(chain);
            int pressed = 0;
            chain.Set("Haul 3 is waiting", WhyChain.HaulSteps(4, 2705.0, 1926.0, -1), "Show depot advice", () => pressed++);
            for (int i = 0; i < 3; i++) yield return null;
            StringAssert.Contains("⚠ Haul 3 is waiting", chain.Shown);
            StringAssert.Contains("needs 2705 Wh in one leg, one charge gives 1926 Wh", chain.Shown);
            StringAssert.Contains("[Show depot advice]", chain.Shown);
            Button b = chain.Query<Button>().ToList().First();
            yield return ClickAt(b.worldBound.center);
            Assert.AreEqual(1, pressed);
            chain.Set("Nothing", new System.Collections.Generic.List<string>(), null, null);
            Assert.AreEqual(DisplayStyle.None, chain.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.None : chain.style.display.value);
            Assert.AreEqual(0, WhyChain.HaulSteps(0, 0, 0, -1).Count, "a haul that is fine has no chain");
            Assert.AreEqual(2, WhyChain.PlantSteps("OutputStorageUnavailable").Steps.Count);
            Assert.AreEqual(0, WhyChain.PlantSteps("None").Steps.Count);
        }
    }
}

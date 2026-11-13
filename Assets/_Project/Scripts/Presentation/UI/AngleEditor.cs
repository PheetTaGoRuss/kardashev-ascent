using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Edit a solar panel's angles by dragging rings around it, and keep named angle layouts.
    /// <list type="bullet">
    /// <item>The rings appear over the selected slot while <b>Edit angle</b> is on. Red turns the tilt (X), green turns
    /// the facing (Y). Blue (Z, roll) is drawn for orientation only: rolling a flat panel does not change its power.</item>
    /// <item>The rings only move the tilt and facing sliders of the open panel box, so every number, preview and
    /// "send a robot to turn it" rule stays as it was.</item>
    /// <item><b>Angle layouts</b>: save the angles with a name and a note. The saved layout also records the slot and
    /// the tile's latitude and longitude. Apply sends a robot (or sets the planned angles).</item>
    /// </list>
    /// Sets itself up after the scene loads, like <see cref="SystemsUI"/>, so no scene change is needed.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AngleEditor : MonoBehaviour
    {
        const float Box = 260f;
        static readonly Color Red = new Color(1f, 0.38f, 0.34f), Green = new Color(0.36f, 0.86f, 0.5f), Blue = new Color(0.4f, 0.62f, 1f);

        /// <summary>One ring: its look and its size on screen. Facing and tilt rings are flattened circles seen at an angle.</summary>
        readonly struct Ring
        {
            public readonly string Axis;
            public readonly float Rx, Ry;
            public readonly Color Color;
            public Ring(string axis, float rx, float ry, Color color) { Axis = axis; Rx = rx; Ry = ry; Color = color; }
        }

        static readonly Ring[] Rings =
        {
            new Ring("Y", 112f, 40f, Green),   // facing: turns about the vertical
            new Ring("X", 40f, 112f, Red),     // tilt: turns about the east-west line
            new Ring("Z", 92f, 92f, Blue),     // roll: no effect on the power of a flat panel
        };

        SimulationRunner runner;
        PanelsView panels;
        PanelPlacer placer;
        SiteView siteView;
        Camera cam;

        VisualElement gizmo, layoutsPanel, layoutsBody, list;
        Label gizmoLabel, layoutsMessage;
        Button layoutsToggle, saveButton, clearButton;
        TextField nameField, noteField;

        int hoverRing = -1, dragRing = -1;
        float lastAngle;
        bool layoutsOpen;
        int editingPreset = -1;
        string shownListKey;
        float nextRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            PanelsView source = FindFirstObjectByType<PanelsView>();
            if (source == null || FindFirstObjectByType<AngleEditor>() != null)
                return;
            UIDocument sourceDocument = source.GetComponent<UIDocument>();
            var go = new GameObject("Angle Editor");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = sourceDocument.panelSettings;
            doc.sortingOrder = sourceDocument.sortingOrder + 1;
            go.AddComponent<AngleEditor>();
            go.SetActive(true);
        }

        void OnEnable()
        {
            runner = FindFirstObjectByType<SimulationRunner>();
            panels = FindFirstObjectByType<PanelsView>();
            placer = FindFirstObjectByType<PanelPlacer>();
            siteView = FindFirstObjectByType<SiteView>();
            cam = Camera.main;

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            CopyStyles(root);
            ScreenInput.Register(root.panel);

            BuildGizmo(root);
            BuildLayouts(root);
        }

        void OnDisable()
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                ScreenInput.Unregister(document.rootVisualElement.panel);
        }

        /// <summary>The panels window's style sheets, so the layouts box looks like the other boxes.</summary>
        void CopyStyles(VisualElement root)
        {
            UIDocument source = panels != null ? panels.GetComponent<UIDocument>() : null;
            if (source == null || source.rootVisualElement == null)
                return;
            var elements = new List<VisualElement> { source.rootVisualElement };
            foreach (VisualElement child in source.rootVisualElement.Children()) elements.Add(child);
            foreach (VisualElement e in elements)
                for (int i = 0; i < e.styleSheets.count; i++)
                    if (!root.styleSheets.Contains(e.styleSheets[i]))
                        root.styleSheets.Add(e.styleSheets[i]);
        }

        // ---- The rings ----

        void BuildGizmo(VisualElement root)
        {
            gizmo = new VisualElement { pickingMode = PickingMode.Position };
            gizmo.style.position = Position.Absolute;
            gizmo.style.width = Box;
            gizmo.style.height = Box;
            gizmo.style.display = DisplayStyle.None;
            gizmo.generateVisualContent += DrawRings;
            gizmo.RegisterCallback<PointerDownEvent>(OnPointerDown);
            gizmo.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            gizmo.RegisterCallback<PointerUpEvent>(OnPointerUp);
            gizmo.RegisterCallback<PointerLeaveEvent>(e => { if (dragRing < 0) { hoverRing = -1; gizmo.MarkDirtyRepaint(); } });

            gizmoLabel = new Label();
            gizmoLabel.AddToClassList("caption");
            gizmoLabel.pickingMode = PickingMode.Ignore;
            gizmoLabel.style.position = Position.Absolute;
            gizmoLabel.style.left = 0; gizmoLabel.style.right = 0; gizmoLabel.style.bottom = -26;
            gizmoLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            gizmoLabel.style.color = new Color(0.9f, 0.93f, 1f);
            gizmo.Add(gizmoLabel);
            root.Add(gizmo);
        }

        void DrawRings(MeshGenerationContext ctx)
        {
            Painter2D p = ctx.painter2D;
            Vector2 c = new Vector2(Box * 0.5f, Box * 0.5f);
            for (int r = 0; r < Rings.Length; r++)
            {
                Ring ring = Rings[r];
                bool live = r == dragRing || (dragRing < 0 && r == hoverRing);
                Color color = ring.Color;
                color.a = ring.Axis == "Z" ? (live ? 0.7f : 0.35f) : (live ? 1f : 0.75f);
                p.strokeColor = color;
                p.lineWidth = live ? 6f : 3.5f;
                p.BeginPath();
                const int steps = 72;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i * Mathf.PI * 2f / steps;
                    var pt = new Vector2(c.x + Mathf.Cos(t) * ring.Rx, c.y - Mathf.Sin(t) * ring.Ry);
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.ClosePath();
                p.Stroke();
            }
            p.fillColor = new Color(1f, 1f, 1f, 0.9f);
            p.BeginPath();
            p.Arc(c, 4f, 0f, 360f);
            p.Fill();
        }

        /// <summary>Which ring is under this point (local to the gizmo)? −1 for none; the closest ring line wins.</summary>
        static int RingAt(Vector2 local)
        {
            Vector2 c = new Vector2(Box * 0.5f, Box * 0.5f);
            int best = -1;
            float bestDistance = 14f;   // pixels from the ring line
            for (int r = 0; r < Rings.Length; r++)
            {
                float u = (local.x - c.x) / Rings[r].Rx, v = (c.y - local.y) / Rings[r].Ry;
                float distance = Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 1f) * Mathf.Min(Rings[r].Rx, Rings[r].Ry);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = r;
                }
            }
            return best;
        }

        /// <summary>The angle of a point around a ring's centre (degrees, counter-clockwise on screen).</summary>
        static float AngleOf(int ring, Vector2 local)
        {
            Vector2 c = new Vector2(Box * 0.5f, Box * 0.5f);
            return Mathf.Atan2((c.y - local.y) / Rings[ring].Ry, (local.x - c.x) / Rings[ring].Rx) * Mathf.Rad2Deg;
        }

        void OnPointerDown(PointerDownEvent e)
        {
            int ring = RingAt(e.localPosition);
            if (ring < 0)
                return;
            dragRing = ring;
            lastAngle = AngleOf(ring, e.localPosition);
            gizmo.CapturePointer(e.pointerId);
            gizmo.MarkDirtyRepaint();
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (dragRing < 0)
            {
                int ring = RingAt(e.localPosition);
                if (ring != hoverRing)
                {
                    hoverRing = ring;
                    gizmo.MarkDirtyRepaint();
                }
                return;
            }
            float angle = AngleOf(dragRing, e.localPosition);
            float delta = Mathf.DeltaAngle(lastAngle, angle);
            lastAngle = angle;
            switch (Rings[dragRing].Axis)
            {
                case "X": panels.TryNudgeAngles(delta, 0f); break;     // tilt: counter-clockwise tips the panel up
                case "Y": panels.TryNudgeAngles(0f, -delta); break;    // facing: clockwise turns toward the east, like a compass
            }
            gizmo.MarkDirtyRepaint();
            e.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (dragRing < 0)
                return;
            gizmo.ReleasePointer(e.pointerId);
            dragRing = -1;
            gizmo.MarkDirtyRepaint();
        }

        static string Compass(float facingDeg)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(Mathf.Repeat(facingDeg, 360f) / 45f) % 8];
        }

        // ---- Angle layouts ----

        void BuildLayouts(VisualElement root)
        {
            layoutsToggle = new Button(() => { layoutsOpen = !layoutsOpen; nextRefresh = 0f; }) { text = "Angle layouts" };
            layoutsToggle.AddToClassList("action-button");
            layoutsToggle.style.position = Position.Absolute;
            layoutsToggle.style.left = 12; layoutsToggle.style.bottom = 86;
            layoutsToggle.style.width = 160;
            layoutsToggle.style.display = DisplayStyle.None;
            root.Add(layoutsToggle);

            layoutsPanel = new VisualElement();
            layoutsPanel.AddToClassList("panel");
            layoutsPanel.AddToClassList("box");
            layoutsPanel.style.position = Position.Absolute;
            layoutsPanel.style.left = 12; layoutsPanel.style.bottom = 116;
            layoutsPanel.style.width = 330;
            layoutsPanel.style.maxHeight = new Length(55, LengthUnit.Percent);
            layoutsPanel.style.display = DisplayStyle.None;
            root.Add(layoutsPanel);

            var head = new Label("ANGLE LAYOUTS");
            head.AddToClassList("heading");
            layoutsPanel.Add(head);
            var how = new Label("Name the angles of the selected panel and keep them. A layout remembers the slot and where on the planet it was saved.");
            how.AddToClassList("caption");
            how.style.whiteSpace = WhiteSpace.Normal;
            layoutsPanel.Add(how);

            nameField = new TextField { maxLength = AnglePreset.MaxNameLength, value = "" };
            nameField.label = "";
            nameField.AddToClassList("puzzle-field");
            nameField.style.marginTop = 6;
            nameField.tooltip = "Name for this slot or layout";
            layoutsPanel.Add(nameField);
            noteField = new TextField { maxLength = AnglePreset.MaxNoteLength, multiline = true, value = "" };
            noteField.AddToClassList("puzzle-field");
            noteField.style.height = 46;
            noteField.style.marginTop = 4;
            noteField.tooltip = "Your note";
            layoutsPanel.Add(noteField);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6;
            saveButton = new Button(SaveClicked) { text = "Save layout" };
            saveButton.AddToClassList("action-button");
            saveButton.AddToClassList("primary");
            clearButton = new Button(() => { editingPreset = -1; nameField.value = ""; noteField.value = ""; nextRefresh = 0f; }) { text = "New" };
            clearButton.AddToClassList("action-button");
            buttons.Add(saveButton);
            buttons.Add(clearButton);
            layoutsPanel.Add(buttons);

            layoutsMessage = new Label();
            layoutsMessage.AddToClassList("caption");
            layoutsMessage.style.whiteSpace = WhiteSpace.Normal;
            layoutsPanel.Add(layoutsMessage);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.marginTop = 6;
            list = scroll.contentContainer;
            layoutsBody = scroll;
            layoutsPanel.Add(scroll);
        }

        IPlanetSimulation Sim => runner != null ? runner.Simulation : null;

        void SaveClicked()
        {
            IPlanetSimulation sim = Sim;
            if (sim == null || siteView == null || siteView.Site == null)
                return;
            if (editingPreset >= 0)
            {
                bool ok = sim.UpdateAnglePreset(editingPreset, nameField.value, noteField.value);
                layoutsMessage.text = ok ? "Updated the name and note." : "That layout is gone.";
                editingPreset = -1;
                nextRefresh = 0f;
                return;
            }
            int slot = siteView.SelectedSlot;
            if (slot < 0)
            {
                layoutsMessage.text = "Select a slot first.";
                return;
            }
            AnglePreset saved = panels != null && panels.TryGetEditedAngles(out float tilt, out float facing)
                ? sim.SaveAnglePresetAt(siteView.Site.Tile, slot, nameField.value, noteField.value, tilt * Mathf.Deg2Rad, facing * Mathf.Deg2Rad)
                : sim.SaveAnglePreset(siteView.Site.Tile, slot, nameField.value, noteField.value);
            layoutsMessage.text = saved != null ? $"Saved \"{saved.Name}\"." : "Nothing to save: that slot has no panel or angles.";
            if (saved != null) { nameField.value = ""; noteField.value = ""; }
            nextRefresh = 0f;
        }

        void Apply(AnglePreset preset)
        {
            IPlanetSimulation sim = Sim;
            if (sim == null || siteView == null || siteView.Site == null || siteView.SelectedSlot < 0)
            {
                layoutsMessage.text = "Select a slot first.";
                return;
            }
            int tile = siteView.Site.Tile, slot = siteView.SelectedSlot;
            if (sim.HasPanel(tile, slot))
                layoutsMessage.text = sim.ApplyAnglePreset(preset.Id, tile, slot)
                    ? "A robot will turn it to this layout." : "Can't turn the panel to that layout here.";
            else if (panels != null && panels.TryGetEditedAngles(out _, out _))
            {
                panels.SetEditedAngles((float)preset.TiltDeg, (float)preset.FacingDeg);
                layoutsMessage.text = "Planned angles set from the layout.";
            }
            else
                layoutsMessage.text = "Select a panel to turn, or an empty slot to plan.";
        }

        void LoadForEditing(AnglePreset preset)
        {
            editingPreset = preset.Id;
            nameField.value = preset.Name;
            noteField.value = preset.Note;
            layoutsMessage.text = "Change the name or note, then press Update. The angles stay as saved.";
            nextRefresh = 0f;
        }

        static string Place(AnglePreset p) =>
            $"{Mathf.Abs((float)p.LatitudeDeg):0.0}°{(p.LatitudeDeg >= 0 ? "N" : "S")} {Mathf.Abs((float)p.LongitudeDeg):0.0}°{(p.LongitudeDeg >= 0 ? "E" : "W")}";

        void RebuildList(IPlanetSimulation sim)
        {
            list.Clear();
            IReadOnlyList<AnglePreset> presets = sim.AnglePresets;
            if (presets.Count == 0)
            {
                var none = new Label("No layouts yet.");
                none.AddToClassList("caption");
                list.Add(none);
                return;
            }
            for (int i = presets.Count - 1; i >= 0; i--)
            {
                AnglePreset preset = presets[i];
                var row = new VisualElement();
                row.style.marginTop = 6;
                row.style.paddingLeft = 6;
                row.style.borderLeftWidth = 2;
                row.style.borderLeftColor = new Color(1f, 1f, 1f, 0.15f);

                var title = new Label($"{preset.Name} · β {preset.TiltDeg:0}° γ {preset.FacingDeg:0}° {Compass((float)preset.FacingDeg)}");
                title.style.unityFontStyle = FontStyle.Bold;
                title.style.whiteSpace = WhiteSpace.Normal;
                row.Add(title);
                var where = new Label($"{Place(preset)} · tile #{preset.TileId} slot {preset.Slot + 1} · day {(int)(preset.SavedHours / 24.0) + 1}");
                where.AddToClassList("caption");
                row.Add(where);
                if (!string.IsNullOrEmpty(preset.Note))
                {
                    var note = new Label(preset.Note);
                    note.AddToClassList("caption");
                    note.style.whiteSpace = WhiteSpace.Normal;
                    row.Add(note);
                }
                var actions = new VisualElement();
                actions.style.flexDirection = FlexDirection.Row;
                actions.style.marginTop = 2;
                AnglePreset captured = preset;
                actions.Add(SmallButton("Apply", () => Apply(captured)));
                actions.Add(SmallButton("Name / note", () => LoadForEditing(captured)));
                actions.Add(SmallButton("Delete", () =>
                {
                    sim.DeleteAnglePreset(captured.Id);
                    if (editingPreset == captured.Id) editingPreset = -1;
                    layoutsMessage.text = "Deleted.";
                    nextRefresh = 0f;
                }));
                row.Add(actions);
                list.Add(row);
            }
        }

        static Button SmallButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("action-button");
            b.style.flexGrow = 0;
            b.style.paddingTop = b.style.paddingBottom = 1;
            return b;
        }

        // ---- Every frame ----

        void Update()
        {
            IPlanetSimulation sim = Sim;
            bool siteOpen = sim != null && siteView != null && siteView.Site != null;

            // The rings.
            bool show = false;
            if (siteOpen && panels != null && panels.EditAngles && placer != null && cam != null
                && placer.TryGetSelectedWorldPosition(out Vector3 world) && panels.TryGetEditedAngles(out float tilt, out float facing))
            {
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z > 0f)
                {
                    Vector2 at = RuntimePanelUtils.CameraTransformWorldToPanel(gizmo.panel, world, cam);
                    gizmo.style.left = at.x - Box * 0.5f;
                    gizmo.style.top = at.y - Box * 0.5f;
                    gizmoLabel.text = $"β {tilt:0}°   γ {facing:0}° {Compass(facing)}   ·   red: tilt · green: facing · blue: roll (no effect)";
                    show = true;
                    gizmo.MarkDirtyRepaint();
                }
            }
            gizmo.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

            // The layouts box.
            layoutsToggle.style.display = siteOpen ? DisplayStyle.Flex : DisplayStyle.None;
            layoutsPanel.style.display = siteOpen && layoutsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!siteOpen || Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + 0.5f;
            layoutsToggle.text = $"Angle layouts ({sim.AnglePresets.Count})";
            if (!layoutsOpen)
                return;
            saveButton.text = editingPreset >= 0 ? "Update" : "Save layout";
            string key = sim.AnglePresets.Count + "|" + editingPreset;
            foreach (AnglePreset p in sim.AnglePresets) key += "|" + p.Id + p.Name + p.Note;
            if (key != shownListKey)
            {
                shownListKey = key;
                RebuildList(sim);
            }
        }
    }
}

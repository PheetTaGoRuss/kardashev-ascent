using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// The experiments as a research tree (director 2026-11-15): the station tile's survey at the root, then each
    /// experiment after the one it needs, joined by lines that light up once the one before is done. Each node shows
    /// its state and result and has a start / stop button; what it is shows on hover.
    /// </summary>
    public sealed class ResearchTreeView : VisualElement
    {
        const float NodeW = 186f, NodeH = 104f, ColGap = 26f, RowGap = 12f;

        sealed class Node
        {
            public ExperimentKind? Kind;   // null: the survey at the root
            public int Col, Row;
            public VisualElement Box;
            public Label Status, Result;
            public Button Go;
            public Node Parent;
            public bool Done;
        }

        readonly List<Node> nodes = new List<Node>();
        readonly Node root;
        readonly Action<ExperimentKind> toggle;
        readonly Action surveyStation;

        public ResearchTreeView(Action<ExperimentKind> toggleExperiment, Action orderStationSurvey)
        {
            toggle = toggleExperiment;
            surveyStation = orderStationSurvey;
            style.position = Position.Relative;
            style.marginTop = 6;
            style.marginBottom = 6;

            root = Add(null, 0, 1, null);
            // Depth 1: what needs only the survey; depth 2: what needs one of those (Experiments.Needs).
            int row = 0;
            var placed = new Dictionary<ExperimentKind, Node>();
            foreach (ExperimentKind k in Experiments.All)
                if (!Experiments.Needs(k).HasValue)
                    placed[k] = Add(k, 1, row++, root);
            int rows = row;
            foreach (ExperimentKind k in Experiments.All)
            {
                ExperimentKind? need = Experiments.Needs(k);
                if (need.HasValue && placed.TryGetValue(need.Value, out Node parent))
                    placed[k] = Add(k, 2, parent.Row, parent);
            }
            root.Row = 0;
            root.Box.style.top = (rows * (NodeH + RowGap) - RowGap - NodeH) * 0.5f;
            style.height = rows * (NodeH + RowGap) - RowGap;
            style.width = 3 * NodeW + 2 * ColGap;
            generateVisualContent += DrawLinks;
        }

        Node Add(ExperimentKind? kind, int col, int row, Node parent)
        {
            var n = new Node { Kind = kind, Col = col, Row = row, Parent = parent };
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = col * (NodeW + ColGap);
            box.style.top = row * (NodeH + RowGap);
            box.style.width = NodeW;
            box.style.height = NodeH;
            box.style.paddingLeft = box.style.paddingRight = 7;
            box.style.paddingTop = 5;
            box.style.backgroundColor = new Color(0.05f, 0.08f, 0.15f, 0.95f);
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 2;
            box.tooltip = kind.HasValue ? Experiments.What(kind.Value)
                : "The lab's instruments stand on surveyed bedrock: survey the Research Station's tile before any experiment.";

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            var name = new Label(kind.HasValue ? Experiments.Name(kind.Value) : "Survey station tile");
            name.AddToClassList("robot-name");
            name.style.flexGrow = 1;
            name.style.whiteSpace = WhiteSpace.Normal;
            head.Add(name);
            var go = new Button(() =>
            {
                if (n.Kind.HasValue) toggle?.Invoke(n.Kind.Value);
                else surveyStation?.Invoke();
            }) { text = "►" };
            go.AddToClassList("small-button");
            go.style.width = 30;
            head.Add(go);
            box.Add(head);

            var status = new Label("");
            status.AddToClassList("job-text");
            status.style.whiteSpace = WhiteSpace.Normal;
            box.Add(status);
            var result = new Label("");
            result.AddToClassList("job-text");
            result.style.whiteSpace = WhiteSpace.Normal;
            result.style.fontSize = 12;
            result.style.color = UiPalette.Good;
            box.Add(result);

            n.Box = box; n.Status = status; n.Result = result; n.Go = go;
            Add(box);
            nodes.Add(n);
            return n;
        }

        public void Refresh(IPlanetSimulation sim)
        {
            if (sim == null) return;
            bool changed = false;
            int station = sim.FinishedStationTile(sim.BaseTileId);
            foreach (Node n in nodes)
            {
                bool done, running;
                string status, result = "", tip = null;
                bool enabled;
                if (!n.Kind.HasValue)
                {
                    done = sim.SurveyedStationTile(-1) >= 0;
                    running = station >= 0 && sim.IsSurveyOrdered(station);
                    status = done ? "● surveyed"
                        : station < 0 ? "○ build a Research Station"
                        : running ? $"● surveying {sim.SurveyProgress(station) * 100.0:0} %"
                        : "○ needs a survey of its tile";
                    enabled = !done && station >= 0;
                    n.Go.text = running ? "■" : "►";
                    tip = running ? "Stop the survey" : "Send a robot to survey the station's tile";
                }
                else
                {
                    ExperimentKind k = n.Kind.Value;
                    done = sim.IsExperimentKnown(k);
                    running = sim.IsExperimentOrdered(k);
                    string blocker = done || running ? null : sim.ExperimentBlocker(k);
                    status = done ? "● done"
                        : running ? $"● running {sim.ExperimentProgress(k) * 100.0:0} % · {Experiments.WorkHours(k):0} h"
                        : blocker != null ? "○ " + blocker
                        : $"○ ready · {Experiments.WorkHours(k):0} h of robot work";
                    result = done ? sim.ExperimentResult(k) ?? "" : "";
                    enabled = !done && (running || blocker == null);
                    n.Go.text = running ? "■" : "►";
                    tip = running ? "Stop the experiment" : blocker ?? "Send a robot to run it at the Research Station";
                }
                n.Status.text = status;
                n.Status.style.color = done ? UiPalette.Good : running ? UiPalette.Accent : UiPalette.Muted;
                n.Result.text = result;
                n.Result.style.display = result.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                n.Go.SetEnabled(enabled);
                n.Go.tooltip = tip;
                n.Go.style.display = done ? DisplayStyle.None : DisplayStyle.Flex;
                Color edge = done ? UiPalette.Good : running ? UiPalette.Accent : enabled ? UiPalette.Border * 1.6f : UiPalette.Border;
                n.Box.style.borderTopColor = n.Box.style.borderBottomColor = n.Box.style.borderLeftColor = n.Box.style.borderRightColor = edge;
                if (n.Done != done) { n.Done = done; changed = true; }
            }
            if (changed) MarkDirtyRepaint();
        }

        void DrawLinks(MeshGenerationContext ctx)
        {
            Painter2D p = ctx.painter2D;
            p.lineWidth = 3f;
            foreach (Node n in nodes)
            {
                if (n.Parent == null) continue;
                Rect from = Bounds(n.Parent), to = Bounds(n);
                var a = new Vector2(from.xMax, from.center.y);
                var b = new Vector2(to.xMin, to.center.y);
                p.strokeColor = n.Parent.Done ? UiPalette.Good : UiPalette.Border * 1.8f;
                p.BeginPath();
                p.MoveTo(a);
                float mid = (a.x + b.x) * 0.5f;
                p.BezierCurveTo(new Vector2(mid, a.y), new Vector2(mid, b.y), b);
                p.Stroke();
            }
        }

        Rect Bounds(Node n)
        {
            float top = n.Kind.HasValue ? n.Row * (NodeH + RowGap) : root.Box.style.top.value.value;
            return new Rect(n.Col * (NodeW + ColGap), top, NodeW, NodeH);
        }
    }
}

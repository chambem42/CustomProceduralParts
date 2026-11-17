using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.UI;
using KSP.UI.Screens;
using KspTest;
using ProceduralParts;
using UnityEngine;
using static CustomProceduralParts.Showcase.B2Showcase;

namespace CustomProceduralParts.Showcase
{
    /// <summary>
    /// A showcase video of the Spine's basics, not a check: a Procedural Parts part switched to Spine, its PAW controls
    /// and gizmo handles, then a boat hull built from a custom cross-section. The PAW, the gizmo and the dialogs are
    /// driven as a player would, with a drawn cursor (KSP can't be fed real mouse input, so each click or drag makes the
    /// change the control makes). Frames as B2Showcase's (frames/NNNNN.jpg, or CPP_SHOWCASE_DIR); assemble at 30 fps.
    /// Run it: dotnet test KspTests/CustomProceduralParts.Showcase.Run --filter FullyQualifiedName~Showcase_SpineBasics
    /// </summary>
    public static class SpineBasicsShowcase
    {
        private const string Win = "Spine Cross Sections";
        private const string Hull = "Boat hull";

        [UITest(Kind = "Showcase", Explicit = true, StartsAt = StartState.Sph, Craft = "SpineBasics", TimeoutSec = 3600)]
        public static IEnumerator Showcase_SpineBasics(TestContext ctx)
        {
            yield return Wait.Frames(30);
            Part part = EditorLogic.fetch.ship.parts[0];
            ProceduralPart pp = part.FindModuleImplementing<ProceduralPart>();
            ProceduralShapeSpine s = part.FindModuleImplementing<ProceduralShapeSpine>();
            Assert.NotNull(pp, "a Procedural Parts part");
            Assert.NotNull(s, "it offers the Spine");
            part.SetHighlightType(Part.HighlightType.Disabled);
            part.SetHighlight(false, false);
            SpineSettings.Set(SpineHandleVis.Active, false, false);
            SpineSettings.SetMirror(false, false);
            CrossSectionLibrary.Delete(Hull);   // a rerun starts without it
            CrossSectionEditor._gridW = 380f;
            CrossSectionEditor._gridH = 300f;
            Imgui.EnsurePatched();
            Textures.HoldNow();
            yield return Wait.Frames(20);

            string framesDir = Environment.GetEnvironmentVariable("CPP_SHOWCASE_DIR") is string sd && sd.Length > 0
                ? Path.Combine(sd, "basics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) : Path.Combine(ctx.ResultsDir, "frames");
            ctx.Record("framesDir", framesDir);
            var rec = FrameRecorder.Begin(framesDir);
            try
            {
                Vector3 home = part.transform.position;

                // ---- a Procedural Parts part -------------------------------------------------------------------------
                rec.Caption("Custom Procedural Parts", "Adds the Spine shape to every Procedural Parts part: a body lofted through a chain of cross-section nodes");
                Cam(home, 110, 20, 9f, 0f);
                yield return Frames(110, f => Cam(home, 110 + 40 * f, 20, 9f, 0f));

                // ---- the Shape selector: Spine ------------------------------------------------------------------------
                rec.HideUi = false;
                yield return Paw.Open(part);
                yield return Frames(4, null);
                MovePaw(part, new Vector2(Screen.width - 440f, 40f));
                yield return CamMove(home, 130, 20, 9f, -0.02f, 25);
                rec.Caption("Pick Spine in the Shape list", "Any Procedural Parts part: tank, structural, nose cone, crew tube...");
                FrameRecorder.CursorPos = new Vector2(0.5f * Screen.width, 0.6f * Screen.height);
                yield return PawChoose(part, "shapeName", () => pp.SetShapeName("Spine"));
                yield return Frames(30, null);

                rec.Caption("Length", "The Spine runs along the part; its first and last nodes sit at the ends");
                yield return PawFloat(part, s, "length", 2f, 8f, 60, f => Cam(home, 130, 20, Mathf.Lerp(9f, 15f, f), -0.02f));
                yield return Frames(15, null);

                // ---- one node's controls ----------------------------------------------------------------------------
                rec.Caption("Nodes", "Each node is a cross-section: pick one with the Node slider (or click it), then set it up");
                yield return PawFloat(part, s, "selectedNode", 0f, 1f, 20, null, step: true);
                yield return Frames(20, null);
                rec.Caption("Width and height", "Per node, in metres");
                yield return PawFloat(part, s, "nodeWidth", s.nodeWidth, 2.6f, 40);
                yield return PawFloat(part, s, "nodeHeight", s.nodeHeight, 2.0f, 40);
                yield return Frames(15, null);

                rec.Caption("Section", "Built-in Ellipse, Rectangle, Mk2 and Mk3, or any custom section");
                yield return PawChoose(part, "nodeProfileOpt", () => SetField(s, "nodeProfileOpt", "Rectangle"));
                yield return Frames(30, null);
                yield return PawChoose(part, "nodeProfileOpt", () => SetField(s, "nodeProfileOpt", "Mk2"));
                yield return Frames(30, null);
                yield return PawChoose(part, "nodeProfileOpt", () => SetField(s, "nodeProfileOpt", "Mk3"));
                yield return Frames(30, null);
                yield return PawChoose(part, "nodeProfileOpt", () => SetField(s, "nodeProfileOpt", "Ellipse"));
                yield return Frames(15, null);

                rec.Caption("Slope", "How the body runs from this node to the next: linear, concave, convex or waisted");
                foreach (string slope in new[] { "Concave", "Convex", "Waisted", "Linear" })
                {
                    string sl = slope;
                    yield return PawChoose(part, "nodeSlopeOpt", () => SetField(s, "nodeSlopeOpt", sl));
                    yield return Frames(28, null);
                }

                rec.Caption("Offsets and tilt", "Move a node's centre up/down or sideways; tilt its plane");
                yield return PawFloat(part, s, "nodeOffsetV", 0f, 0.8f, 35);
                yield return PawFloat(part, s, "nodeOffsetH", 0f, -0.6f, 30);
                yield return PawFloat(part, s, "nodeTiltV", 0f, 25f, 30);
                yield return Frames(15, null);
                yield return PawFloat(part, s, "nodeTiltV", 25f, 0f, 20);
                yield return PawFloat(part, s, "nodeOffsetH", -0.6f, 0f, 20);
                yield return PawFloat(part, s, "nodeOffsetV", 0.8f, 0f, 20);

                rec.Caption("Add and remove nodes", "A new node copies the selected one, halfway to its neighbour");
                yield return ClickPawButton(part, "Add node in front", () => s.AddNodeInFrontEvent());
                yield return Frames(30, null);
                yield return ClickPawButton(part, "Add node behind", () => s.AddNodeBehindEvent());
                yield return Frames(30, null);
                yield return ClickPawButton(part, "Remove node", () => s.RemoveNodeEvent());
                yield return Frames(25, null);

                // ---- the gizmo -----------------------------------------------------------------------------------------
                rec.Caption("Handles", "Spine Options: show every node's handles, just the selected node's, or none");
                yield return ClickPawButton(part, "Spine Options", () => s.SpineOptionsEvent());
                yield return Imgui.WaitWindow(SpineOptionsWindow.Title);
                var opts = UnityEngine.Object.FindObjectOfType<SpineOptionsWindow>();
                opts._win = new Rect(Screen.width - 440f - opts._win.width - 20f, 60f, opts._win.width, opts._win.height);
                yield return Frames(15, null);
                yield return ClickControl(SpineOptionsWindow.Title, c => c.Label == "All nodes",
                                          () => SpineSettings.Set(SpineHandleVis.All, SpineSettings.ClickBeforeDrag, SpineSettings.ShowOutlines));
                yield return Frames(20, null);
                yield return ClickControl(SpineOptionsWindow.Title, c => c.Label == "Close", () => UnityEngine.Object.Destroy(opts.gameObject));
                yield return CamMove(home, 145, 20, 12.5f, -0.02f, 30);

                int mid = 1;
                rec.Caption("Click a node's circle to select it", "End nodes are arrows: they stay at the ends");
                yield return ClickWorld(() => NodeHandle(s, 2), () => s.SelectNode(2));
                yield return Frames(15, null);
                yield return ClickWorld(() => NodeHandle(s, mid), () => s.SelectNode(mid));
                yield return Frames(15, null);
                rec.Caption("Drag the circle: up and down", "Left-drag moves the node's centre up or down");
                float v0 = s.nodes[mid].offsetV;
                yield return GizmoDrag(s, () => NodeHandle(s, mid), t => s.nodeOffsetV = s.nodes[mid].offsetV = v0 + 0.9f * Mathf.Sin(Mathf.PI * t), 70);
                rec.Caption("Right-drag: sideways", "Right-drag moves it left or right");
                float h0 = s.nodes[mid].offsetH;
                yield return GizmoDrag(s, () => NodeHandle(s, mid), t => s.nodeOffsetH = s.nodes[mid].offsetH = h0 + 0.8f * Mathf.Sin(Mathf.PI * t), 70);
                rec.Caption("Mouse wheel: taller or shorter", "Scroll over a node's circle");
                yield return Wheel(s, mid, +1, 6);
                yield return Wheel(s, mid, -1, 6);
                rec.Caption("Edge diamonds: width and position", "Drag out or in to widen or narrow; along the body to move the node");
                float w0 = s.nodes[mid].sizeH, p0 = s.nodes[mid].position;
                int side = NearSide(s, mid);
                yield return GizmoDrag(s, () => Tip(s, mid, side), t =>
                {
                    s.nodeWidth = s.nodes[mid].sizeH = w0 + 1.2f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, 2f * t));
                    s.nodePos = s.nodes[mid].position = p0 + 0.15f * Mathf.Sin(Mathf.PI * Mathf.Max(0f, 2f * t - 1f));
                }, 100);
                rec.Caption("Section glyph: middle-click to cycle", "Above each node: its section's shape");
                for (int k = 0; k < 4; k++)
                {
                    yield return ClickWorld(() => Glyph(s, mid), () => s.CycleProfile(mid));
                    yield return Frames(18, null);
                }
                FrameRecorder.CursorVisible = false;
                SpineSettings.Set(SpineHandleVis.Active, SpineSettings.ClickBeforeDrag, false);
                yield return Frames(20, null);

                // ---- a boat hull -------------------------------------------------------------------------------------
                FrameRecorder.Paused = true;   // a cut: a fresh three-node Spine
                rec.Caption("", "");
                ResetHull(s, part);
                s.SelectNode(1);
                yield return Frames(8, null);
                Cam(home, 125, 10, 19f, -0.02f);
                yield return Frames(4, null);
                FrameRecorder.Paused = false;

                rec.Caption("Building a boat hull", "Start from three nodes: stern, midships, bow");
                yield return Frames(60, f => Cam(home, 125 + 15 * f, 10, 19f, -0.02f));
                rec.Caption("Start from a built-in", "The middle node to Rectangle, then Edit Cross Sections");
                yield return PawChoose(part, "nodeProfileOpt", () => SetField(s, "nodeProfileOpt", "Rectangle"));
                yield return Frames(15, null);
                yield return ClickPawButton(part, "Edit Cross Sections", () => s.EditCrossSectionsEvent());
                yield return Imgui.WaitWindow(Win);
                var ed = UnityEngine.Object.FindObjectOfType<CrossSectionEditor>();
                ed._refit = true;
                yield return Wait.Frames(2);
                ed._win = new Rect(30f, 30f, ed._win.width, ed._win.height);
                yield return CamMove(home, 145, 12, 20f, 0.06f, 25);

                rec.Caption("Mirror left to right", "Edit one side; the other follows");
                yield return ClickControl(Win, c => c.Label != null && c.Label.Contains("Left to right"), () => ed.SetMirror(true, false));
                yield return Frames(15, null);
                rec.Caption("Drag a corner", "The bottom corner up and in: the chine");
                int corner = Nearest(ed._verts, new Vector2(-1f, -1f));
                yield return Drag(ed, corner, ed._verts[corner], new Vector2(-0.875f, -0.375f), 45);
                yield return Frames(10, null);
                rec.Caption("Double-click an edge to add a vertex", "Then drag it out: a rounder bottom");
                Vector2 want = new Vector2(-0.45f, -0.7f);
                int edge = NearestEdge(ed._verts, want);
                Vector2 midPt = 0.5f * (ed._verts[edge] + ed._verts[(edge + 1) % ed._verts.Count]);
                yield return DoubleClickPlot(ed, midPt, () => ed.InsertOnEdge(edge, ed.Place(midPt)));
                int added = Nearest(ed._verts, ed.Place(midPt));
                yield return Drag(ed, added, ed._verts[added], new Vector2(-0.5f, -0.75f), 35);
                yield return Frames(15, null);
                rec.Caption("Stretch", "Pulls the halves apart with straight runs between: a flat keel (left for flat sides)");
                yield return StretchSlider(ed, 0.35f, 40);
                yield return Frames(20, null);

                rec.Caption("Save As", "A name, and it joins your shared sections, offered on every craft");
                Rect? saveAs = ControlRect(Win, c => c.Label == "Save As");
                if (saveAs != null) yield return MoveCursor(() => saveAs.Value.center - new Vector2(160f, 0f), 18);
                FrameRecorder.CursorPressed = true;
                yield return Frames(4, null);
                FrameRecorder.CursorPressed = false;
                for (int k = 1; k <= Hull.Length; k++)
                {
                    ed._saveAsName = Hull.Substring(0, k);
                    yield return Frames(3, null);
                }
                yield return ClickControl(Win, c => c.Label == "Save As", () => ed.SaveTo(Hull, false));
                yield return Frames(25, null);
                Assert.NotNull(CrossSectionLibrary.Get(Hull), "the hull section saved");

                rec.Caption("Every node: Boat hull", "Click a node, pick the section - the dialog follows the selected node");
                for (int i = 0; i < s.nodes.Count; i++)
                {
                    int ii = i;
                    yield return ClickWorld(() => NodeHandle(s, ii), () => s.SelectNode(ii));
                    yield return Frames(6, null);
                    yield return PawChoose(part, "nodeProfileOpt", () => SetField(s, "nodeProfileOpt", Hull));
                    yield return Frames(14, null);
                }
                UnityEngine.Object.Destroy(ed.gameObject);
                yield return CamMove(home, 135, 8, 19f, -0.02f, 25);

                rec.Caption("A node for the bow's shoulder", "Add node in front of midships");
                yield return ClickWorld(() => NodeHandle(s, 1), () => s.SelectNode(1));
                yield return ClickPawButton(part, "Add node in front", () => s.AddNodeInFrontEvent());
                yield return Frames(20, null);
                int bow = s.nodes.Count - 1, shoulder = bow - 1;
                rec.Caption("Close the bow", "Drag the bow's edge diamond all the way in: the end closes to a stem");
                yield return ClickWorld(() => NodeHandle(s, bow), () => s.SelectNode(bow));
                float bw0 = s.nodes[bow].sizeH;
                int bside = NearSide(s, bow);
                yield return GizmoDrag(s, () => Tip(s, bow, bside), t => s.nodeWidth = s.nodes[bow].sizeH = Mathf.Lerp(bw0, 0f, t), 50);
                rec.Caption("Raise the bow", "Drag its centre up: the sheer rises and the forefoot lifts");
                float bv0 = s.nodes[bow].offsetV;
                yield return GizmoDrag(s, () => NodeHandle(s, bow), t => s.nodeOffsetV = s.nodes[bow].offsetV = bv0 + 0.5f * t, 40);
                rec.Caption("Rake the stem", "Node vertical tilt leans the bow's top forward");
                yield return PawFloat(part, s, "nodeTiltV", 0f, 28f, 35);
                yield return Frames(10, null);

                rec.Caption("Fill out the shoulder", "A wider node near the bow, a convex run into it");
                yield return ClickWorld(() => NodeHandle(s, shoulder), () => s.SelectNode(shoulder));
                float sw0 = s.nodes[shoulder].sizeH, sp0 = s.nodes[shoulder].position;
                int sside = NearSide(s, shoulder);
                yield return GizmoDrag(s, () => Tip(s, shoulder, sside), t =>
                {
                    s.nodeWidth = s.nodes[shoulder].sizeH = Mathf.Lerp(sw0, 2.6f, t);
                    s.nodePos = s.nodes[shoulder].position = Mathf.Lerp(sp0, 0.72f, t);
                }, 45);
                yield return PawFloat(part, s, "nodeOffsetV", s.nodes[shoulder].offsetV, 0.12f, 20);
                yield return PawChoose(part, "nodeSlopeOpt", () => SetField(s, "nodeSlopeOpt", "Convex"));
                yield return Frames(15, null);
                yield return ClickWorld(() => NodeHandle(s, 0), () => s.SelectNode(0));
                rec.Caption("A flat transom", "The stern node: a little narrower and shallower than midships");
                yield return PawFloat(part, s, "nodeWidth", s.nodes[0].sizeH, 2.5f, 25);
                yield return PawFloat(part, s, "nodeHeight", s.nodes[0].sizeV, 1.3f, 25);
                yield return PawFloat(part, s, "nodeOffsetV", s.nodes[0].offsetV, 0.12f, 20);
                FrameRecorder.CursorVisible = false;
                yield return Frames(15, null);

                // ---- options: smoother sections, outlines ---------------------------------------------------------------
                rec.Caption("Spine Options: points per section", "More points for smoother curves; outlines show every node's section");
                FrameRecorder.CursorPos = new Vector2(0.6f * Screen.width, 0.5f * Screen.height);
                yield return ClickPawButton(part, "Spine Options", () => s.SpineOptionsEvent());
                yield return Imgui.WaitWindow(SpineOptionsWindow.Title);
                opts = UnityEngine.Object.FindObjectOfType<SpineOptionsWindow>();
                opts._win = new Rect(Screen.width - 440f - opts._win.width - 20f, 60f, opts._win.width, opts._win.height);
                yield return Frames(10, null);
                yield return ClickControl(SpineOptionsWindow.Title, c => c.Label == "48", () => s.SetSectionPoints(48));
                yield return Frames(10, null);
                yield return ClickControl(SpineOptionsWindow.Title, c => c.Label == "Show cross-section outlines",
                                          () => SpineSettings.Set(SpineSettings.HandleVis, SpineSettings.ClickBeforeDrag, true));
                yield return Frames(25, null);
                yield return ClickControl(SpineOptionsWindow.Title, c => c.Label == "Close", () => UnityEngine.Object.Destroy(opts.gameObject));
                FrameRecorder.CursorVisible = false;
                rec.HideUi = true;
                Bounds hb = Bounds(new[] { part });
                ctx.Record("hull", hb.ToString());
                float hs = hb.size.magnitude;
                yield return CamMove(hb.center, 240, 8, 1.7f * hs, 0f, 40);
                yield return Frames(150, f => Cam(hb.center, 240 - 120 * f, 8 + 10 * Mathf.Sin(Mathf.PI * f), 1.7f * hs, 0f));

                // ---- outro -------------------------------------------------------------------------------------------
                SpineSettings.Set(SpineSettings.HandleVis, SpineSettings.ClickBeforeDrag, false);
                Paw.Close(part);
                rec.Caption("Spine for Procedural Parts", "Also: hollow shells, FAR, and RP-1 tooling");
                yield return Frames(150, f => Cam(hb.center, 120 + 80 * f, Mathf.Lerp(8, 4, f), Mathf.Lerp(1.7f * hs, 1.45f * hs, f), 0f));
            }
            finally
            {
                ctx.Record("frames", FrameRecorder.End());
                Textures.Release();
                var ed = UnityEngine.Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) UnityEngine.Object.Destroy(ed.gameObject);
                var ow = UnityEngine.Object.FindObjectOfType<SpineOptionsWindow>();
                if (ow != null) UnityEngine.Object.Destroy(ow.gameObject);
                SpineSettings.SetMirror(false, false);
            }
        }

        // Three Ellipse nodes for the hull: stern, midships, bow (9 m).
        private static void ResetHull(ProceduralShapeSpine s, Part part)
        {
            s.length = 9f;
            s.nodes.Clear();
            s.nodes.Add(new SpineNode(0f, 2.6f, 1.5f));
            s.nodes.Add(new SpineNode(0.45f, 3.0f, 1.6f));
            s.nodes.Add(new SpineNode(1f, 1.6f, 1.2f));
            s.sectionPoints = ProceduralShapeSpine.MinSides;
            s.selectedNode = 0;
            s.RefreshSelector();
            s.LoadProxyFromNode();
            s.RebuildAndPropagate();
            MonoUtilities.RefreshPartContextWindow(part);
        }

        // ---- the camera, tracked so moves start from where it is ----------------------------------------------------

        private static Vector3 _ct;
        private static float _cy, _cp, _cd, _cs;

        private static void Cam(Vector3 target, float yaw, float pitch, float dist, float shift)
        {
            _ct = target; _cy = yaw; _cp = pitch; _cd = dist; _cs = shift;
            Orbit(target, yaw, pitch, dist, 30, shift);
        }

        private static IEnumerator CamMove(Vector3 target, float yaw, float pitch, float dist, float shift, int n)
        {
            Vector3 t0 = _ct;
            float y0 = _cy, p0 = _cp, d0 = _cd, s0 = _cs;
            yield return Frames(n, f =>
            {
                float e = B2Showcase.Smooth(f);
                Cam(Vector3.Lerp(t0, target, e), Mathf.LerpAngle(y0, yaw, e), Mathf.Lerp(p0, pitch, e), Mathf.Lerp(d0, dist, e), Mathf.Lerp(s0, shift, e));
            });
        }

        // ---- the PAW ---------------------------------------------------------------------------------------------

        // A field as its PAW control sets it: the value, then the control's change handler.
        private static void SetField(PartModule m, string name, object value)
        {
            BaseField f = m.Fields[name];
            object old = f.GetValue(m);
            f.SetValue(value, m);
            f.uiControlEditor?.onFieldChanged?.Invoke(f, old);
        }

        // The field's PAW item on screen (GUI coordinates, y down), scrolled into view; null if it isn't shown.
        private static Rect? PawItemRect(Part part, string field)
        {
            UIPartActionFieldItem item = Paw.FindFieldItem<UIPartActionFieldItem>(part, field);
            if (item == null) return null;
            ScrollIntoView(part, item);
            var rt = (RectTransform)item.transform;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);   // bottom-left, top-left, top-right, bottom-right
            Camera ui = UIMasterController.Instance.uiCamera;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(ui, c[0]), b = RectTransformUtility.WorldToScreenPoint(ui, c[2]);
            return Rect.MinMaxRect(a.x, Screen.height - b.y, b.x, Screen.height - a.y);
        }

        // The cursor onto a choice field's right arrow, pressed, `act`, released.
        private static IEnumerator PawChoose(Part part, string field, Action act)
        {
            yield return Frames(3, null);
            Rect? r = PawItemRect(part, field);
            if (r != null) yield return MoveCursor(() => new Vector2(r.Value.xMax - 16f, r.Value.center.y), 18);
            FrameRecorder.CursorPressed = true;
            yield return Frames(5, null);
            act();
            FrameRecorder.CursorPressed = false;
            yield return Frames(6, null);
        }

        // The cursor onto a number field's slider, which it drags from `from` to `to` over n frames (`cam` each frame);
        // `step`: whole numbers (the Node slider).
        private static IEnumerator PawFloat(Part part, PartModule host, string field, float from, float to, int n, Action<float> cam = null, bool step = false)
        {
            yield return Frames(3, null);
            Rect? r = PawItemRect(part, field);
            Vector2 At(float f) => r == null ? FrameRecorder.CursorPos
                : new Vector2(r.Value.x + r.Value.width * (0.3f + 0.4f * f), r.Value.y + 0.72f * r.Value.height);
            if (r != null) yield return MoveCursor(() => At(0f), 18);
            FrameRecorder.CursorPressed = true;
            yield return Frames(4, null);
            for (int k = 1; k <= n; k++)
            {
                float f = B2Showcase.Smooth((float)k / n);
                float v = Mathf.Lerp(from, to, f);
                if (step) v = Mathf.Round(v);
                SetField(host, field, v);
                FrameRecorder.CursorPos = At(f);
                cam?.Invoke((float)k / n);
                yield return null;
            }
            FrameRecorder.CursorPressed = false;
            yield return Frames(6, null);
        }

        // ---- the gizmo ---------------------------------------------------------------------------------------------

        // A node's edge diamond: side -1 the part's -x edge, +1 its +x edge.
        private static Vector3 Tip(ProceduralShapeSpine s, int i, int side)
        {
            SpineNode n = s.nodes[i];
            return s.part.transform.TransformPoint(new Vector3(side * s.Erh(n) + s.EoffH(n), s.NodeY(n), s.Eoff(n)));
        }

        // The edge diamond nearer the camera.
        private static int NearSide(ProceduralShapeSpine s, int i)
        {
            Vector3 cam = (EditorLogic.fetch.editorCamera ?? Camera.main).transform.position;
            return (Tip(s, i, -1) - cam).sqrMagnitude < (Tip(s, i, 1) - cam).sqrMagnitude ? -1 : 1;
        }

        // The section glyph above a node's circle (as the Spine places it).
        private static Vector3 Glyph(ProceduralShapeSpine s, int i)
        {
            Camera cam = EditorLogic.fetch.editorCamera ?? Camera.main;
            return NodeHandle(s, i) + cam.transform.up * 0.32f;
        }

        // The cursor onto a point in the scene, pressed, `act`, released.
        private static IEnumerator ClickWorld(Func<Vector3> at, Action act)
        {
            yield return MoveCursor(() => ScreenOf(at()), 18);
            FrameRecorder.CursorPressed = true;
            yield return Frames(4, null);
            act();
            yield return Frames(3, null);
            FrameRecorder.CursorPressed = false;
            yield return Frames(6, null);
        }

        // Press on a handle, `apply(t)` for t 0..1 over n frames (what dragging it does) with the cursor on the
        // handle, release: one full rebuild, as the gizmo's own drag ends.
        private static IEnumerator GizmoDrag(ProceduralShapeSpine s, Func<Vector3> grab, Action<float> apply, int n)
        {
            yield return MoveCursor(() => ScreenOf(grab()), 18);
            FrameRecorder.CursorPressed = true;
            yield return Frames(5, null);
            for (int k = 1; k <= n; k++)
            {
                apply(B2Showcase.Smooth((float)k / n));
                s.RebuildLive();
                FrameRecorder.CursorPos = ScreenOf(grab());
                yield return null;
            }
            s.RebuildAndPropagate();
            s.LoadProxyFromNode();
            MonoUtilities.RefreshPartContextWindow(s.part);
            FrameRecorder.CursorPressed = false;
            yield return Frames(10, null);
        }

        // The cursor on a node's circle, the wheel turned `clicks` notches (no press drawn).
        private static IEnumerator Wheel(ProceduralShapeSpine s, int i, int dir, int clicks)
        {
            yield return MoveCursor(() => ScreenOf(NodeHandle(s, i)), 14);
            for (int k = 0; k < clicks; k++)
            {
                s.ResizeNode(i, dir);
                FrameRecorder.CursorPos = ScreenOf(NodeHandle(s, i));
                yield return Frames(5, null);
            }
            yield return Frames(8, null);
        }

        // ---- the section dialog ------------------------------------------------------------------------------------

        private static Rect? ControlRect(string win, Func<Imgui.Control, bool> which) =>
            Imgui.FindWindow(win)?.Controls.FirstOrDefault(which)?.ScreenRect;

        // The editor's Stretch slider dragged to `to`. The slider has no label of its own: it runs from under the
        // mirror toggles (its row's label is as wide as theirs) to its value label (110 px) before the "0" button.
        private static IEnumerator StretchSlider(CrossSectionEditor ed, float to, int n)
        {
            Rect? lr = ControlRect(Win, c => c.Label != null && c.Label.Contains("Left to right"));
            Rect? zero = ControlRect(Win, c => c.Kind == Imgui.Kind.Button && c.Label == "0");
            if (lr == null || zero == null) { ed.SetStretch(to); yield break; }
            float x0 = lr.Value.x + 6f, x1 = zero.Value.x - 124f, y = zero.Value.center.y;
            Func<float, Vector2> at = v => new Vector2(Mathf.Lerp(x0, x1, 0.5f * (v + 1f)), y);
            float from = ed._stretch;
            yield return MoveCursor(() => at(from), 18);
            FrameRecorder.CursorPressed = true;
            yield return Frames(3, null);
            for (int i = 1; i <= n; i++)
            {
                float v = Mathf.Lerp(from, to, B2Showcase.Smooth((float)i / n));
                FrameRecorder.CursorPos = at(v);
                ed.SetStretch(Mathf.Round(v * 20f) / 20f);
                yield return null;
            }
            FrameRecorder.CursorPressed = false;
            yield return Frames(5, null);
        }

        private static int Nearest(List<Vector2> v, Vector2 p) =>
            Enumerable.Range(0, v.Count).OrderBy(i => (v[i] - p).sqrMagnitude).First();

        // The edge (i, i+1) whose midpoint is nearest p.
        private static int NearestEdge(List<Vector2> v, Vector2 p) =>
            Enumerable.Range(0, v.Count).OrderBy(i => (0.5f * (v[i] + v[(i + 1) % v.Count]) - p).sqrMagnitude).First();

        private static IEnumerator DoubleClickPlot(CrossSectionEditor ed, Vector2 unit, Action act)
        {
            yield return MoveCursor(() => OnPlot(ed, unit), 18);
            for (int k = 0; k < 2; k++)
            {
                FrameRecorder.CursorPressed = true;
                yield return Frames(3, null);
                FrameRecorder.CursorPressed = false;
                yield return Frames(3, null);
            }
            act();
            yield return Frames(8, null);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        // The craft's one part's Spine (after an undo, the part that replaced it).
        private static ProceduralShapeSpine Spine0() => EditorLogic.fetch.ship.parts[0].FindModuleImplementing<ProceduralShapeSpine>();

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
            SpineSettings.Set(SpineHandleVis.Active, false, true);   // cross-section outlines on from the start
            SpineSettings.SetMirror(false, false);
            CrossSectionLibrary.Delete(Hull);   // a rerun starts without it
            CrossSectionEditor._gridW = 380f;
            CrossSectionEditor._gridH = 300f;
            Imgui.EnsurePatched();
            Textures.HoldNow();
            Pointer(false);   // the real mouse, wherever it is, mustn't hover the UI (part list tooltips)
            yield return Wait.Frames(20);

            string framesDir = Environment.GetEnvironmentVariable("CPP_SHOWCASE_DIR") is string sd && sd.Length > 0
                ? Path.Combine(sd, "basics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) : Path.Combine(ctx.ResultsDir, "frames");
            ctx.Record("framesDir", framesDir);
            var rec = FrameRecorder.Begin(framesDir);
            try
            {
                Vector3 home = part.transform.position;

                // ---- a Procedural Parts part -------------------------------------------------------------------------
                // the intro take (IntroShowcase) comes before this one in the video and ends dark: straight to the card
                Cam(home, 150, 20, 9f, 0f);
                rec.Caption("", "");
                yield return FrameRecorder.Card("PAW Controls", 1f, 1f, 60);
                yield return FrameRecorder.Card("PAW Controls", 1f, 0f, 18);

                // ---- the Shape selector: Spine ------------------------------------------------------------------------
                rec.HideUi = false;
                yield return Paw.Open(part);
                yield return Frames(4, null);
                MovePaw(part, new Vector2(Screen.width - 440f, 40f));
                Pointer(false);
                yield return CamMove(home, 130, 20, 9f, -0.02f, 25);
                rec.Caption("Pick Spine in the Shape list", "Works on any Procedural Parts part: tanks, structural parts, nose cones, crew tubes...");
                FrameRecorder.CursorPos = new Vector2(0.5f * Screen.width, 0.6f * Screen.height);
                yield return PawChoose(part, "shapeName", "Spine", () => pp.SetShapeName("Spine"));
                yield return Frames(15, null);

                rec.Caption("Length", "The body stretches between its first and last nodes");
                yield return PawFloat(part, s, "length", 2f, 8f, f => Cam(home, 130, 20, Mathf.Lerp(9f, 15f, f), -0.02f));
                yield return Frames(8, null);

                // ---- one node's controls ----------------------------------------------------------------------------
                rec.Caption("Nodes", "Each node is one cross-section. Pick one with the Node slider (or click it), then shape it");
                yield return PawFloat(part, s, "selectedNode", 0f, 1f);
                yield return Frames(10, null);
                rec.Caption("Width and height", "Set for each node, in meters");
                yield return PawFloat(part, s, "nodeWidth", s.nodeWidth, 2.5f);
                yield return PawFloat(part, s, "nodeHeight", s.nodeHeight, 2.0f);
                yield return Frames(8, null);

                rec.Caption("Section", "The cross-section's shape: Ellipse, Rectangle, Mk2, Mk3, or one you draw");
                yield return PawChoose(part, "nodeProfileOpt", "Rectangle", () => SetField(s, "nodeProfileOpt", "Rectangle"));
                yield return Frames(15, null);
                yield return PawChoose(part, "nodeProfileOpt", "Mk2", () => SetField(s, "nodeProfileOpt", "Mk2"));
                yield return Frames(15, null);
                yield return PawChoose(part, "nodeProfileOpt", "Mk3", () => SetField(s, "nodeProfileOpt", "Mk3"));
                yield return Frames(15, null);

                // each step goes on from where the last left the part: nothing is clicked back
                rec.Caption("Slope", "How the body curves from this node to the next");
                foreach (string slope in new[] { "Concave", "Convex", "Waisted" })
                {
                    string sl = slope;
                    yield return PawChoose(part, "nodeSlopeOpt", sl, () => SetField(s, "nodeSlopeOpt", sl));
                    yield return Frames(14, null);
                }

                rec.Caption("Offsets and tilt", "Move a node up, down or sideways, or tilt it");
                yield return PawFloat(part, s, "nodeOffsetV", s.nodes[s.SelIndex].offsetV, 0.8f);
                yield return PawFloat(part, s, "nodeOffsetH", s.nodes[s.SelIndex].offsetH, -0.6f);
                yield return PawFloat(part, s, "nodeTiltV", s.nodes[s.SelIndex].tiltV, 25f);
                yield return Frames(8, null);
                string afterOffsets = s.DumpAll();   // where undo goes back to, after the mouse controls

                rec.Caption("Add and remove nodes", "A new node copies the selected one, halfway to the next");
                yield return ClickPawButton(part, "Add node in front", () => s.AddNodeInFrontEvent());
                yield return Frames(15, null);
                yield return ClickPawButton(part, "Add node behind", () => s.AddNodeBehindEvent());
                yield return Frames(15, null);
                yield return ClickPawButton(part, "Remove node", () => s.RemoveNodeEvent());
                yield return Frames(13, null);

                // ---- the gizmo -----------------------------------------------------------------------------------------
                yield return FrameRecorder.Card("Mouse Controls", 0f, 1f, 18);
                rec.Caption("", "");
                FrameRecorder.CursorVisible = false;
                yield return FrameRecorder.Card("Mouse Controls", 1f, 1f, 60);
                yield return FrameRecorder.Card("Mouse Controls", 1f, 0f, 18);
                rec.Caption("Handles", "In Spine Options: show handles on every node, only the selected one, or none");
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

                // an inner node other than the one the PAW part left selected, so the click selects something new
                int mid = Enumerable.Range(1, s.nodes.Count - 2).OrderBy(i => i == s.SelIndex ? 1 : 0).ThenBy(i => Mathf.Abs(i - 1)).First();
                rec.Caption("Click a node's circle to select it", "The end nodes are arrows; they stay at the ends");
                yield return ClickWorld(() => NodeHandle(s, mid), () => s.SelectNode(mid));
                yield return Frames(15, null);
                rec.Caption("Drag the circle up and down", "Left-drag moves the node up or down");
                float v0 = s.nodes[mid].offsetV;
                yield return GizmoDrag(s, () => NodeHandle(s, mid), t => s.nodeOffsetV = s.nodes[mid].offsetV = v0 + 0.6f * t, 60);
                rec.Caption("Right-drag: sideways", "Right-drag moves the node left or right");
                float h0 = s.nodes[mid].offsetH;
                yield return GizmoDrag(s, () => NodeHandle(s, mid), t => s.nodeOffsetH = s.nodes[mid].offsetH = h0 + 0.6f * t, 60);
                rec.Caption("Mouse wheel: taller or shorter", "Scroll over a node's circle");
                yield return Wheel(s, mid, +1, 6);
                rec.Caption("Diamonds: width and position", "Drag out or in to change the width, or along the body to move the node");
                float w0 = s.nodes[mid].sizeH, p0 = s.nodes[mid].position;
                int side = NearSide(s, mid);
                // out for width, then along the body for position, ending where it stops
                float pTo = Mathf.Min(p0 + 0.12f, s.nodes[mid + 1].position - 0.05f);
                yield return GizmoDrag(s, () => Tip(s, mid, side), t =>
                {
                    s.nodeWidth = s.nodes[mid].sizeH = w0 + 1.0f * B2Showcase.Smooth(Mathf.Min(1f, 2f * t));
                    s.nodePos = s.nodes[mid].position = Mathf.Lerp(p0, pTo, B2Showcase.Smooth(Mathf.Max(0f, 2f * t - 1f)));
                }, 100);
                rec.Caption("Shape icon: middle-click to change", "The icon above each node shows its cross-section");
                for (int k = 0; k < 3; k++)
                {
                    // after the first, clicks on the same icon: twice as quick
                    yield return ClickWorld(() => Glyph(s, mid), () => s.CycleProfile(mid), again: k > 0);
                    yield return Frames(k > 0 ? 9 : 18, null);
                }
                FrameRecorder.CursorVisible = false;
                SpineSettings.Set(SpineHandleVis.Active, SpineSettings.ClickBeforeDrag, true);   // the hull keeps the outlines
                yield return Frames(20, null);

                // ---- undo, back to the body as the offsets and tilt left it ---------------------------------------------
                // Each press one step: the PAW and handle edits since then, each a step (repeats of one control one
                // step). Undo replaces the part, so it's looked up again after.
                {
                    var restore = typeof(EditorLogic).GetMethod("RestoreState", BindingFlags.Instance | BindingFlags.NonPublic);
                    int presses = 0;
                    while (presses < 25 && Spine0().DumpAll() != afterOffsets)
                    {
                        presses++;
                        rec.Caption($"Undo x {presses}", "Ctrl+Z steps back one change at a time: here to the offsets and tilt from before");
                        restore.Invoke(EditorLogic.fetch, new object[] { -1 });
                        yield return Frames(presses == 1 ? 18 : 9, null);   // presses after the first: twice as quick
                    }
                    ctx.Record("undoPresses", presses);
                    part = EditorLogic.fetch.ship.parts[0];
                    pp = part.FindModuleImplementing<ProceduralPart>();
                    s = part.FindModuleImplementing<ProceduralShapeSpine>();
                    Assert.Equal(afterOffsets, s.DumpAll(), $"{presses} undos back to the offsets and tilt");
                    part.SetHighlightType(Part.HighlightType.Disabled);
                    part.SetHighlight(false, false);
                    yield return Paw.Open(part);   // the undone part's PAW closed with it
                    yield return Frames(4, null);
                    MovePaw(part, new Vector2(Screen.width - 440f, 40f));
                    Pointer(false);
                    yield return Frames(24, null);
                    mid = Mathf.Clamp(s.SelIndex, 1, s.nodes.Count - 2);
                }

                // ---- attaching parts ------------------------------------------------------------------------------------
                yield return FrameRecorder.Card("Attaching Parts", 0f, 1f, 18);
                rec.Caption("", "");
                yield return FrameRecorder.Card("Attaching Parts", 1f, 1f, 60);
                yield return FrameRecorder.Card("Attaching Parts", 1f, 0f, 18);
                rec.Caption("Attach parts anywhere on it", "A part turns to sit flat on whatever face is under it");
                Part intakeTop = null, intakeSide = null;
                {
                    // both halfway along a straight stretch (between its two nodes, clear of a node's section change,
                    // tilt and slope): one over the side, up and onto the top; another across the top (beyond the
                    // first) and down onto the side
                    int seg = StraightSegment(s);
                    ctx.Record("attachSegment", seg);
                    RaycastHit? top = SkinBetween(part, s, seg, 0.5f, Up(part)), sideHit = SkinBetween(part, s, seg, 0.5f, Side(part));
                    RaycastHit? side2 = SkinBetween(part, s, seg, 0.5f, Side(part)), top2 = SkinBetween(part, s, seg, 0.8f, Up(part));
                    if (top != null && sideHit != null && side2 != null && top2 != null)
                    {
                        yield return AttachRadial(part, "airScoop", new[] { side2.Value, top.Value }, p => intakeTop = p);
                        yield return Frames(15, null);
                        yield return AttachRadial(part, "airScoop", new[] { top2.Value, sideHit.Value }, p => intakeSide = p);
                    }
                    else Debug.LogWarning("[SpineBasicsShowcase] no spots on the body for the intakes");
                }
                yield return Frames(20, null);
                rec.Caption("Attached parts follow the shape", "Change the body and they stay on it");
                yield return ClickWorld(() => NodeHandle(s, mid), () => s.SelectNode(mid));
                yield return PawFloat(part, s, "nodeWidth", s.nodes[mid].sizeH, s.nodes[mid].sizeH + 1.25f);
                yield return Frames(10, null);
                yield return PawChoose(part, "nodeProfileOpt", "Rectangle", () => SetField(s, "nodeProfileOpt", "Rectangle"));
                yield return Frames(20, null);
                float va = s.nodes[mid].offsetV;
                yield return GizmoDrag(s, () => NodeHandle(s, mid), t => s.nodeOffsetV = s.nodes[mid].offsetV = va - 0.6f * t, 60);
                yield return Frames(20, null);
                FrameRecorder.CursorVisible = false;

                // ---- a boat hull -------------------------------------------------------------------------------------
                yield return FrameRecorder.Card("Building a Boat Hull", 0f, 1f, 18);
                rec.Caption("", "");
                FrameRecorder.Paused = true;   // a cut, under the card: a fresh three-node Spine
                foreach (Part p in new[] { intakeTop, intakeSide }) Remove(p);
                ResetHull(s, part);
                s.SelectNode(1);
                yield return Frames(8, null);
                Cam(home, 125, 10, 19f, -0.02f);
                yield return Frames(4, null);
                FrameRecorder.Paused = false;
                yield return FrameRecorder.Card("Building a Boat Hull", 1f, 1f, 60);
                yield return FrameRecorder.Card("Building a Boat Hull", 1f, 0f, 18);

                rec.Caption("Three nodes to start", "Back, middle and front");
                yield return Frames(60, f => Cam(home, 125 + 15 * f, 10, 19f, -0.02f));
                rec.Caption("Start from a built-in shape", "Set the middle node to Rectangle, then click Edit Cross Sections");
                yield return PawChoose(part, "nodeProfileOpt", "Rectangle", () => SetField(s, "nodeProfileOpt", "Rectangle"));
                yield return Frames(15, null);
                yield return ClickPawButton(part, "Edit Cross Sections", () => s.EditCrossSectionsEvent());
                yield return Imgui.WaitWindow(Win);
                var ed = UnityEngine.Object.FindObjectOfType<CrossSectionEditor>();
                ed._refit = true;
                yield return Wait.Frames(2);
                ed._win = new Rect(30f, 30f, ed._win.width, ed._win.height);
                yield return CamMove(home, 145, 12, 20f, 0.06f, 25);

                rec.Caption("Mirror left to right", "Edit one side and the other follows");
                yield return Hover(() => ControlRect(Win, c => c.Label != null && c.Label.Contains("Left to right"))?.center ?? FrameRecorder.CursorPos, 80);
                yield return ClickControl(Win, c => c.Label != null && c.Label.Contains("Left to right"), () => ed.SetMirror(true, false));
                yield return Frames(15, null);
                rec.Caption("Drag a corner", "Move the bottom corner up and in");
                int corner = Nearest(ed._verts, new Vector2(-1f, -1f));
                yield return Drag(ed, corner, ed._verts[corner], new Vector2(-0.875f, -0.375f), 45);
                yield return Frames(10, null);
                rec.Caption("Double-click an edge to add a point", "Then drag it out for a rounder bottom");
                Vector2 want = new Vector2(-0.45f, -0.7f);
                int edge = NearestEdge(ed._verts, want);
                Vector2 midPt = 0.5f * (ed._verts[edge] + ed._verts[(edge + 1) % ed._verts.Count]);
                yield return DoubleClickPlot(ed, midPt, () => ed.InsertOnEdge(edge, ed.Place(midPt)));
                int added = Nearest(ed._verts, ed.Place(midPt));
                yield return Drag(ed, added, ed._verts[added], new Vector2(-0.5f, -0.75f), 35);
                yield return Frames(15, null);
                rec.Caption("Stretch", "Adds a straight run through the middle: here a flat bottom (drag left for flat sides)");
                yield return Hover(() => StretchAt(ed._stretch) ?? FrameRecorder.CursorPos, 80);
                yield return StretchSlider(ed, 0.35f, 40);
                yield return Frames(20, null);

                rec.Caption("Save As", "Give it a name and it's ready to use on any craft");
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
                yield return Hover(() => ControlRect(Win, c => c.Label == "Save As")?.center ?? FrameRecorder.CursorPos, 75);
                yield return ClickControl(Win, c => c.Label == "Save As", () => ed.SaveTo(Hull, false));
                yield return Frames(25, null);
                Assert.NotNull(CrossSectionLibrary.Get(Hull), "the hull section saved");

                rec.Caption("Use it on every node", "Click a node, then pick the shape; the editor shows the selected node");
                for (int i = 0; i < s.nodes.Count; i++)
                {
                    int ii = i;
                    yield return ClickWorld(() => NodeHandle(s, ii), () => s.SelectNode(ii));
                    yield return Frames(6, null);
                    yield return PawChoose(part, "nodeProfileOpt", Hull, () => SetField(s, "nodeProfileOpt", Hull));
                    yield return Frames(14, null);
                }
                UnityEngine.Object.Destroy(ed.gameObject);
                yield return CamMove(home, 135, 8, 19f, -0.02f, 25);

                rec.Caption("Add a node near the front", "Add node in front of the middle one");
                yield return ClickWorld(() => NodeHandle(s, 1), () => s.SelectNode(1));
                yield return ClickPawButton(part, "Add node in front", () => s.AddNodeInFrontEvent());
                yield return Frames(20, null);
                int bow = s.nodes.Count - 1, shoulder = bow - 1;
                rec.Caption("Bring the front to a point", "Drag the front node's diamond all the way in");
                yield return ClickWorld(() => NodeHandle(s, bow), () => s.SelectNode(bow));
                float bw0 = s.nodes[bow].sizeH;
                int bside = NearSide(s, bow);
                yield return GizmoDrag(s, () => Tip(s, bow, bside), t => s.nodeWidth = s.nodes[bow].sizeH = Mathf.Lerp(bw0, 0f, t), 50);
                rec.Caption("Raise the front", "Drag its circle up");
                float bv0 = s.nodes[bow].offsetV;
                yield return GizmoDrag(s, () => NodeHandle(s, bow), t => s.nodeOffsetV = s.nodes[bow].offsetV = bv0 + 0.5f * t, 40);
                rec.Caption("Lean the front forward", "With the node's vertical tilt");
                yield return PawFloat(part, s, "nodeTiltV", 0f, 30f);
                yield return Frames(10, null);

                rec.Caption("Round out the front", "A wider node near the front, and a full curve from it to the tip");
                yield return ClickWorld(() => NodeHandle(s, shoulder), () => s.SelectNode(shoulder));
                float sw0 = s.nodes[shoulder].sizeH, sp0 = s.nodes[shoulder].position;
                int sside = NearSide(s, shoulder);
                yield return GizmoDrag(s, () => Tip(s, shoulder, sside), t =>
                {
                    s.nodeWidth = s.nodes[shoulder].sizeH = Mathf.Lerp(sw0, 2.6f, t);
                    s.nodePos = s.nodes[shoulder].position = Mathf.Lerp(sp0, 0.72f, t);
                }, 45);
                yield return PawFloat(part, s, "nodeOffsetV", s.nodes[shoulder].offsetV, 0.1f);
                // the run narrows to the bow: Concave (the blend's name) keeps it full till the stem, a convex look
                yield return PawChoose(part, "nodeSlopeOpt", "Concave", () => SetField(s, "nodeSlopeOpt", "Concave"));
                yield return Frames(15, null);
                yield return ClickWorld(() => NodeHandle(s, 0), () => s.SelectNode(0));
                rec.Caption("A flat back", "The back node: a little narrower and lower than the middle");
                yield return PawFloat(part, s, "nodeWidth", s.nodes[0].sizeH, 2.5f);
                yield return PawFloat(part, s, "nodeHeight", s.nodes[0].sizeV, 1.25f);
                yield return PawFloat(part, s, "nodeOffsetV", s.nodes[0].offsetV, 0.1f);
                FrameRecorder.CursorVisible = false;
                yield return Frames(15, null);

                // ---- the finished hull --------------------------------------------------------------------------------
                rec.Caption("The finished hull", "");
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
                Pointer(true);
                Textures.Release();
                var ed = UnityEngine.Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) UnityEngine.Object.Destroy(ed.gameObject);
                var ow = UnityEngine.Object.FindObjectOfType<SpineOptionsWindow>();
                if (ow != null) UnityEngine.Object.Destroy(ow.gameObject);
                SpineSettings.SetMirror(false, false);
            }
        }

        // Straight up from the body, and sideways toward the camera, both square to the Spine's axis.
        private static Vector3 Up(Part body) => (Vector3.up - Vector3.Project(Vector3.up, body.transform.up)).normalized;

        private static Vector3 Side(Part body)
        {
            Vector3 v = EditorLogic.fetch.editorCamera.transform.position - body.transform.position;
            v -= Vector3.Project(v, body.transform.up);
            v -= Vector3.Project(v, Up(body));
            return v.normalized;
        }

        // Where a ray from outside, in direction dir, toward the centre `t` of the way from node i's centre to node
        // i + 1's (their offsets included) meets the body's collider.
        private static RaycastHit? SkinBetween(Part body, ProceduralShapeSpine s, int i, float t, Vector3 dir)
        {
            Vector3 Centre(SpineNode n) => new Vector3(s.EoffH(n), s.NodeY(n), s.Eoff(n));
            Vector3 axisPt = body.transform.TransformPoint(Vector3.Lerp(Centre(s.nodes[i]), Centre(s.nodes[i + 1]), t));
            return BodyHit(body, new Ray(axisPt + 20f * dir, -dir));
        }

        // The longest straight stretch: the segment (from node k to k + 1) with a Linear slope, longest first.
        private static int StraightSegment(ProceduralShapeSpine s) =>
            Enumerable.Range(0, s.nodes.Count - 1).OrderBy(k => s.nodes[k].slopeAbove == SpineSlope.Linear ? 0 : 1)
                .ThenByDescending(k => s.nodes[k + 1].position - s.nodes[k].position).First();

        private static RaycastHit? BodyHit(Part body, Ray ray)
        {
            RaycastHit? best = null;
            foreach (Collider c in body.GetComponentsInChildren<Collider>())
                if (c.enabled && c.Raycast(ray, out RaycastHit h, 100f) && (best == null || h.distance < best.Value.distance)) best = h;
            return best;
        }

        // A part flat on a face: its attach node on the point, its attach direction into the body, its forward along
        // the Spine (as the editor turns a part to the face under the cursor).
        private static void FlatOn(Part p, Part body, Vector3 point, Vector3 normal)
        {
            Vector3 along = body.transform.up - Vector3.Project(body.transform.up, normal);
            Quaternion rot = Quaternion.LookRotation(along, -normal) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.forward, p.srfAttachNode.orientation));
            p.transform.rotation = rot;
            p.transform.position = point - rot * p.srfAttachNode.position;
        }

        // A stock part picked from the parts list, carried over the body through the `path` spots (the cursor's ray
        // onto the body each frame: the part sits flat on whatever face is under it, as the editor shows a part being
        // placed; off the body it floats on the ray), and attached at the last one with EditorLogic's own attach
        // (private; its Attachment is internal).
        private static IEnumerator AttachRadial(Part body, string partName, RaycastHit[] path, Action<Part> made)
        {
            Camera cam = EditorLogic.fetch.editorCamera;
            AvailablePart ap = PartLoader.getPartInfoByName(partName);
            Vector2 list = new Vector2(70f, 0.42f * Screen.height);   // the parts list
            yield return MoveCursor(() => list, 22);
            FrameRecorder.CursorPressed = true;
            yield return Frames(4, null);
            FrameRecorder.CursorPressed = false;
            Part p = UnityEngine.Object.Instantiate(ap.partPrefab);
            p.gameObject.SetActive(true);
            p.name = ap.name;
            p.persistentId = FlightGlobals.CheckPartpersistentId(p.persistentId, p, false, true);
            p.InitializeModules();
            p.SetHighlightType(Part.HighlightType.Disabled);
            FlatOn(p, body, path[0].point, path[0].normal);
            float depth = Vector3.Distance(cam.transform.position, path[0].point);

            // the cursor: list -> each spot, resting a moment on each
            var stops = new List<Vector2> { list };
            foreach (RaycastHit h in path) stops.Add(ScreenOf(h.point));
            for (int leg = 1; leg < stops.Count; leg++)
            {
                int n = Quick(leg == 1 ? 40 : 50);
                for (int k = 1; k <= n; k++)
                {
                    Vector2 c = Vector2.Lerp(stops[leg - 1], stops[leg], B2Showcase.Smooth((float)k / n));
                    FrameRecorder.CursorPos = c;
                    Ray r = cam.ScreenPointToRay(new Vector3(c.x, Screen.height - c.y, 0f));
                    if (BodyHit(body, r) is RaycastHit h) FlatOn(p, body, h.point, h.normal);
                    else p.transform.position = r.GetPoint(depth) - p.transform.rotation * p.srfAttachNode.position;
                    yield return Frames(1, null);
                }
                yield return Frames(leg == stops.Count - 1 ? 6 : 18, null);
            }
            RaycastHit end = path[path.Length - 1];
            FlatOn(p, body, end.point, end.normal);
            FrameRecorder.CursorPressed = true;
            yield return Frames(4, null);
            p.attachMode = AttachModes.SRF_ATTACH;
            Type attT = typeof(EditorLogic).Assembly.GetType("Attachment");
            object att = Activator.CreateInstance(attT, true);
            void Set(string f, object v) => attT.GetField(f).SetValue(att, v);
            Set("possible", true);
            Set("mode", AttachModes.SRF_ATTACH);
            Set("potentialParent", body);
            Set("caller", p);
            Set("callerPartNode", p.srfAttachNode);
            Set("position", p.transform.position);
            Set("rotation", p.transform.rotation);
            typeof(EditorLogic).GetMethod("attachPart", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(EditorLogic.fetch, new[] { p, att });
            EditorLogic.fetch.SetBackup();
            GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartAttached, p);
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            p.SetHighlightType(Part.HighlightType.Disabled);
            p.SetHighlight(false, false);
            FrameRecorder.CursorPressed = false;
            made(p);
            yield return Frames(8, null);
        }

        // A PAW tap does nothing while an input lock holds the part menus' controls (a window's lock, taken while the
        // real mouse happens to be over it): those locks off before the take's taps, named in the log.
        internal static void Unlock(string field)
        {
            if (InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_ANYCONTROL)) return;
            var held = InputLockManager.lockStack.Where(kv => (kv.Value & (ulong)ControlTypes.TWEAKABLES_ANYCONTROL) != 0).Select(kv => kv.Key).ToList();
            Debug.LogWarning($"[SpineBasicsShowcase] {field}: locks held the PAW ({string.Join(", ", held)}); cleared");
            foreach (string id in held) InputLockManager.RemoveControlLock(id);
        }

        // A part taken off the craft and gone (EditorLogic.DeletePart trips over its node icons when nothing is held).
        private static void Remove(Part p)
        {
            if (p == null) return;
            if (p.parent != null) p.parent.children.Remove(p);
            if (p.srfAttachNode != null) p.srfAttachNode.attachedPart = null;
            p.parent = null;
            p.transform.parent = null;
            EditorLogic.fetch.ship.Remove(p);
            UnityEngine.Object.Destroy(p.gameObject);
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
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

        internal static void Cam(Vector3 target, float yaw, float pitch, float dist, float shift)
        {
            _ct = target; _cy = yaw; _cp = pitch; _cd = dist; _cs = shift;
            Orbit(target, yaw, pitch, dist, 30, shift);
        }

        internal static IEnumerator CamMove(Vector3 target, float yaw, float pitch, float dist, float shift, int n)
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
        internal static void SetField(PartModule m, string name, object value)
        {
            BaseField f = m.Fields[name];
            object old = f.GetValue(m);
            f.SetValue(value, m);
            f.uiControlEditor?.onFieldChanged?.Invoke(f, old);
        }

        // The field's PAW item on screen (GUI coordinates, y down), scrolled into view; null if it isn't shown.
        internal static Rect? PawItemRect(Part part, string field)
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

        // A choice field set to `option` through its own arrow buttons, tapped (the cursor on the arrow) until the PAW
        // shows it. `act` sets it directly if the PAW can't.
        internal static IEnumerator PawChoose(Part part, string field, string option, Action act)
        {
            yield return Frames(3, null);
            PawItemRect(part, field);   // scrolled into view
            UIPartActionChooseOption Item() => Paw.FindFieldItem<UIPartActionChooseOption>(part, field);
            string[] Options(UIPartActionChooseOption i) => ((UI_ChooseOption)i.Field.uiControlEditor).options;
            string Value(UIPartActionChooseOption i) => Convert.ToString(i.Field.GetValue(i.Field.host));
            UIButtonToggle last = null;
            for (int taps = 0; taps < 30 && Item() is UIPartActionChooseOption it; taps++)
            {
                int want = Array.IndexOf(Options(it), option), at = Array.IndexOf(Options(it), Value(it));
                if (want < 0 || want == at) break;
                bool up = want > at;
                UIButtonToggle b = up ? it.inc : it.dec;
                bool again = b == last;   // the same arrow again: a repeated click, twice as quick
                if (!again) yield return MoveCursor(() => Center((RectTransform)b.transform), last == null ? 18 : 8);
                last = b;
                FrameRecorder.CursorPressed = true;
                yield return Frames(again ? 2 : 3, null);
                Unlock(field);
                if (up) it.OnTap_inc(); else it.OnTap_dec();
                FrameRecorder.CursorPressed = false;
                yield return Frames(again ? 2 : 3, null);
            }
            if (!(Item() is UIPartActionChooseOption done) || Value(done) != option)
            {
                Debug.LogWarning($"[SpineBasicsShowcase] {field}: the PAW didn't reach {option}; set directly");
                act();
            }
            yield return Frames(3, null);
        }

        // A PAW float changed through its own controls, as a player would, so the PAW shows it. An edit field: its
        // arrow buttons tapped, large steps then small, while a tap gets closer to `to` (so `to` wants to be a whole
        // number of steps away). A range slider (the Node one, whole steps): one click at `to`'s place on it. The
        // cursor goes to the button or the slider. `cam` gets the progress from `from` to `to`, eased.
        internal static IEnumerator PawFloat(Part part, PartModule host, string field, float from, float to, Action<float> cam = null)
        {
            yield return Frames(3, null);
            PawItemRect(part, field);   // scrolled into view
            BaseField bf = host.Fields[field];
            float Cur() => Convert.ToSingle(bf.GetValue(host));
            float shown = 0f;
            void Ease(float _)
            {
                if (cam == null) return;
                float p = Mathf.Approximately(from, to) ? 1f : Mathf.Clamp01((Cur() - from) / (to - from));
                shown += (p - shown) * 0.3f;
                cam(shown);
            }

            if (Paw.FindFieldItem<UIPartActionFieldItem>(part, field) is UIPartActionFloatRange)
            {
                var ctl = (UI_FloatRange)bf.uiControlEditor;
                UIPartActionFloatRange Item() => Paw.FindFieldItem<UIPartActionFloatRange>(part, field);
                float t = Mathf.InverseLerp(ctl.minValue, ctl.maxValue, to);
                Vector2 At()
                {
                    var c = new Vector3[4];
                    ((RectTransform)Item().slider.transform).GetWorldCorners(c);
                    Vector3 w = Vector3.Lerp(0.5f * (c[0] + c[1]), 0.5f * (c[2] + c[3]), t);
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(UIMasterController.Instance.uiCamera, w);
                    return new Vector2(p.x, Screen.height - p.y);
                }
                yield return MoveCursor(At, 18);
                FrameRecorder.CursorPressed = true;
                yield return Frames(3, null);
                Item().slider.normalizedValue = t;
                FrameRecorder.CursorPressed = false;
                yield return Frames(3, Ease);
                yield break;
            }

            UIPartActionFloatEdit Edit() => Paw.FindFieldItem<UIPartActionFloatEdit>(part, field);
            if (Edit() == null) { SetField(host, field, to); yield break; }
            var ectl = (UI_FloatEdit)bf.uiControlEditor;
            UIButtonToggle last = null;
            for (int taps = 0; taps < 40; taps++)
            {
                float rem = to - Cur(), a = Mathf.Abs(rem);
                bool large = ectl.incrementLarge > 0f && a >= ectl.incrementLarge - 1e-4f;
                if (!large && !(ectl.incrementSmall > 0f && a > 0.5f * ectl.incrementSmall + 1e-4f)) break;
                bool up = rem > 0f;
                UIPartActionFloatEdit e = Edit();
                UIButtonToggle b = large ? (up ? e.incLarge : e.decLarge) : (up ? e.incSmall : e.decSmall);
                bool again = b == last;   // the same button again: a repeated click, twice as quick
                if (!again) yield return MoveCursor(() => Center((RectTransform)b.transform), last == null ? 18 : 8);
                last = b;
                FrameRecorder.CursorPressed = true;
                yield return Frames(again ? 2 : 3, Ease);
                Unlock(field);
                if (large) { if (up) e.OnTap_incLarge(); else e.OnTap_decLarge(); }
                else { if (up) e.OnTap_incSmall(); else e.OnTap_decSmall(); }
                FrameRecorder.CursorPressed = false;
                yield return Frames(3, Ease);
            }
            Debug.Log($"[SpineBasicsShowcase] {field}: {from} -> {Cur()} (asked {to})");
            yield return Frames(4, Ease);
        }

        // A UI element's centre in GUI coordinates (y down).
        internal static Vector2 Center(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 p = RectTransformUtility.WorldToScreenPoint(UIMasterController.Instance.uiCamera, 0.5f * (c[0] + c[2]));
            return new Vector2(p.x, Screen.height - p.y);
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
        // `again`: a repeated click on the same spot, twice as quick (the cursor is already there).
        internal static IEnumerator ClickWorld(Func<Vector3> at, Action act, bool again = false)
        {
            if (!again) yield return MoveCursor(() => ScreenOf(at()), 18);
            else FrameRecorder.CursorPos = ScreenOf(at());
            FrameRecorder.CursorPressed = true;
            yield return Frames(again ? 2 : 4, null);
            act();
            yield return Frames(again ? 2 : 3, null);
            FrameRecorder.CursorPressed = false;
            yield return Frames(again ? 3 : 6, null);
        }

        // Press on a handle, `apply(t)` for t 0..1 over n frames (what dragging it does) with the cursor on the
        // handle, release: one full rebuild, as the gizmo's own drag ends.
        internal static IEnumerator GizmoDrag(ProceduralShapeSpine s, Func<Vector3> grab, Action<float> apply, int n)
        {
            yield return MoveCursor(() => ScreenOf(grab()), 18);
            FrameRecorder.CursorPressed = true;
            s.StartDragUndo();   // as the gizmo's own drag: one undo step
            yield return Frames(5, null);
            n = Quick(n);
            for (int k = 1; k <= n; k++)
            {
                apply(B2Showcase.Smooth((float)k / n));
                s.RebuildLive();
                FrameRecorder.CursorPos = ScreenOf(grab());
                yield return null;
            }
            s.RebuildAndPropagate();
            s._dragMoved = true;
            s.EndDragUndo();
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
        // The cursor onto something, resting there till its tooltip shows (the Spine windows' tips follow the drawn
        // cursor while it's here), then the tips off again.
        internal static IEnumerator Hover(Func<Vector2> at, int n)
        {
            yield return MoveCursor(at, 18);
            for (int i = 0; i < n; i++)
            {
                SpineTips.PointerOverride = FrameRecorder.CursorPos;
                yield return Frames(1, null);
            }
            SpineTips.PointerOverride = FrameRecorder.TipsOff;
        }

        // The Stretch slider's handle at value v (-1..1), on screen.
        private static Vector2? StretchAt(float v)
        {
            Rect? lr = ControlRect(Win, c => c.Label != null && c.Label.Contains("Left to right"));
            Rect? zero = ControlRect(Win, c => c.Kind == Imgui.Kind.Button && c.Label == "0");
            if (lr == null || zero == null) return null;
            float x0 = lr.Value.x + 6f, x1 = zero.Value.x - 124f;
            return new Vector2(Mathf.Lerp(x0, x1, 0.5f * (v + 1f)), zero.Value.center.y - 8f);   // the slider sits above the button's centre
        }

        private static IEnumerator StretchSlider(CrossSectionEditor ed, float to, int n)
        {
            if (StretchAt(0f) == null) { ed.SetStretch(to); yield break; }
            Func<float, Vector2> at = v => StretchAt(v) ?? FrameRecorder.CursorPos;
            float from = ed._stretch;
            yield return MoveCursor(() => at(from), 18);
            FrameRecorder.CursorPressed = true;
            yield return Frames(3, null);
            n = Quick(n);
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

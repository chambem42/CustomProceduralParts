using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KspTest;
using ProceduralParts;
using UnityEngine;

namespace CustomProceduralParts.KspTests
{
    /// <summary>The Spine shape and its custom cross-sections, in the editor and in flight.</summary>
    public static class SpineTests
    {
        // Throwaway sections are named Zz* and deleted by the test that made them.
        private static readonly Vector2[] Triangle = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(0.1f, 1f) };
        private static readonly Vector2[] Diamond = { new Vector2(1f, 0.2f), new Vector2(0f, 1f), new Vector2(-1f, -0.3f), new Vector2(0.1f, -1f) };
        private static readonly Vector2[] Star = StarVerts(5, 0.35f);
        private static readonly Vector2[] Plus =
        {
            new Vector2(0.15f, 0.15f), new Vector2(0.15f, 1f), new Vector2(-0.15f, 1f), new Vector2(-0.15f, 0.15f),
            new Vector2(-1f, 0.15f), new Vector2(-1f, -0.15f), new Vector2(-0.15f, -0.15f), new Vector2(-0.15f, -1f),
            new Vector2(0.15f, -1f), new Vector2(0.15f, -0.15f), new Vector2(1f, -0.15f), new Vector2(1f, 0.15f),
        };
        private static readonly Vector2[] L =
        {
            new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, -0.6f), new Vector2(-0.6f, -0.6f), new Vector2(-0.6f, 1f), new Vector2(-1f, 1f),
        };

        private static Vector2[] StarVerts(int points, float inner)
        {
            var v = new Vector2[2 * points];
            for (int i = 0; i < v.Length; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / points, r = i % 2 == 0 ? 1f : inner;
                v[i] = new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
            }
            return v;
        }

        private static ProceduralShapeSpine Spine(Part p = null) =>
            (p != null ? new[] { p } : EditorLogic.fetch.ship.parts.ToArray())
            .Select(x => x.FindModuleImplementing<ProceduralShapeSpine>()).First(m => m != null);

        // Saves `verts` as custom section `name` and puts it on the nodes picked by `which` (all by default).
        private static IEnumerator Use(ProceduralShapeSpine s, string name, IList<Vector2> verts, System.Func<int, bool> which = null)
        {
            Assert.True(SectionGeometry.Validate(verts) == null, $"{name} is a valid section ({SectionGeometry.Validate(verts)})");
            CrossSectionLibrary.Save(name, SectionGeometry.Normalize(verts));
            for (int i = 0; i < s.nodes.Count; i++)
                if (which == null || which(i)) s.nodes[i].section = name;
            s.UpdateShape(true);
            yield return Wait.Frames(2);
        }

        private static void Cleanup(params string[] names)
        {
            foreach (string n in names) CrossSectionLibrary.Delete(n);
        }

        // Gives a named IMGUI control keyboard focus, as clicking it would.
        private sealed class FocusNamed : MonoBehaviour
        {
            public string Name;
            private void OnGUI() => GUI.FocusControl(Name);
        }

        // The session's tests share one loaded craft: a test that changes it puts it back.
        private sealed class Snapshot
        {
            private readonly ProceduralShapeSpine _s;
            private readonly List<SpineNode> _nodes;
            private readonly bool _shell;
            private readonly float _thickness;

            public Snapshot(ProceduralShapeSpine s)
            {
                _s = s;
                _nodes = s.nodes.Select(n => n.Clone()).ToList();
                _shell = s.shellMode;
                _thickness = s.shellThickness;
            }

            public void Restore()
            {
                _s.nodes.Clear();
                _s.nodes.AddRange(_nodes.Select(n => n.Clone()));
                _s.shellMode = _shell;
                _s.shellThickness = _thickness;
                _s.UpdateShape(true);
            }
        }

        /// <summary>A craft saved before custom sections (profile = ...) loads with its built-ins and their volume.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_LegacyCraftKeepsItsVolume(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            Assert.Equal("Ellipse|Mk2|Rectangle|Ellipse", string.Join("|", s.nodes.Select(n => n.section)), "legacy profiles read as sections");
            ctx.Record("volume", s.Volume);
            // 6.68466 before the built-in Mk2's points were placed on its true outline (2026-11-06): its node and the
            // two segments blending into it grew. Change this only on purpose, with the built-in geometry.
            Assert.Near(6.85143, s.Volume, 1e-3, "volume");
            yield break;
        }

        /// <summary>
        /// The collider is a convex hull of a few points per ring; a custom section's tips must be among them, wherever
        /// they fall in the 24-point outline, so the collider spans the visible mesh.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_ColliderKeepsSectionTips(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            try
            {
                foreach (var (name, verts) in new[] { ("ZzTriangle", Triangle), ("ZzDiamond", Diamond), ("ZzStar", Star) })
                {
                    yield return Use(s, name, verts);
                    Bounds vis = s.PPart.SidesMesh.bounds;
                    MeshCollider mc = s.part.GetComponentsInChildren<MeshCollider>(true).First(c => c.gameObject.name == "collisionMesh");
                    Bounds col = mc.sharedMesh.bounds;
                    ctx.Record(name + ".visible", vis.ToString("F3"));
                    ctx.Record(name + ".collider", col.ToString("F3"));
                    float tol = 0.002f * vis.size.magnitude;
                    for (int a = 0; a < 3; a++)
                    {
                        Assert.Near(vis.min[a], col.min[a], tol, $"{name} collider min[{a}]");
                        Assert.Near(vis.max[a], col.max[a], tol, $"{name} collider max[{a}]");
                    }
                }
            }
            finally { snap.Restore(); Cleanup("ZzTriangle", "ZzDiamond", "ZzStar"); }
        }

        /// <summary>
        /// Shell mode on concave sections with thin arms and a wall thicker than the arms: every inner ring stays a
        /// simple polygon inside its outer ring (the wall thins where it has to), and the shell's volume is between 0
        /// and the solid's.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_ShellWallStaysInsideConcaveSections(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            try
            {
                foreach (var (name, verts) in new[] { ("ZzPlus", Plus), ("ZzL", L), ("ZzStar", Star) })
                {
                    snap.Restore();
                    yield return Use(s, name, verts, i => i == 1 || i == 2);
                    float solid = s.CalculateVolume();
                    s.shellMode = true;
                    s.shellThickness = 0.3f;
                    s.UpdateShape(true);
                    yield return Wait.Frames(2);
                    List<ProceduralShapeSpine.Ring> rings = s.BuildRings();
                    for (int r = 0; r < rings.Count; r++)
                    {
                        Vector2[] o = rings[r].outline, iv = s.InnerRing(rings[r]).outline;
                        Assert.False(o.SequenceEqual(iv), $"{name} ring {r} has a wall");   // no end of this craft is closed
                        string err = SectionGeometry.Validate(iv);
                        Assert.True(err == null, $"{name} ring {r} inner outline is simple ({err})");
                        Assert.True(iv.All(v => InPoly(v, o)), $"{name} ring {r} inner outline inside the outer");
                    }
                    float shell = s.CalculateVolume();
                    ctx.Record(name + ".solidVolume", solid);
                    ctx.Record(name + ".shellVolume", shell);
                    Assert.InRange(1e-4, solid, shell, name + " shell volume within (0, solid]");
                }
            }
            finally
            {
                snap.Restore();
                Cleanup("ZzPlus", "ZzL", "ZzStar");
            }
        }

        private static bool InPoly(Vector2 p, Vector2[] poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }

        /// <summary>A concave section's end cap covers its outline exactly: triangle areas sum to the polygon's.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_EndCapCoversConcaveSection(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            try
            {
                yield return Use(s, "ZzL", L);
                Mesh ends = s.PPart.EndsMesh;
                Vector3[] v = ends.vertices;
                int[] t = ends.triangles;
                int sides = s.Sides;
                double tri = 0, poly = 0;
                for (int i = 0; i < (sides - 2) * 3; i += 3)   // the bottom cap's triangles come first
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    tri += System.Math.Abs(0.5 * ((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)));
                }
                for (int j = 0; j < sides; j++) { Vector3 a = v[j], b = v[(j + 1) % sides]; poly += a.x * b.z - b.x * a.z; }
                poly = System.Math.Abs(0.5 * poly);
                ctx.Record("capTriangleArea", tri);
                ctx.Record("capPolygonArea", poly);
                Assert.Near(poly, tri, 1e-4 * poly, "cap triangles cover the outline once");
            }
            finally { snap.Restore(); Cleanup("ZzL"); }
        }

        /// <summary>Re-saving a section in use replaces its gizmo icon without leaving the old mesh behind.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_SectionIconsAreFreed(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            try
            {
                yield return Use(s, "ZzIcon", Diamond);
                yield return Paw.Open(s.part);
                yield return Wait.Frames(5);
                int Alive() => Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name == "SpineProfCustom");
                int before = Alive();
                for (int k = 0; k < 6; k++)
                {
                    Vector2[] v = Diamond.Select(p => p + new Vector2(0.02f * (k + 1), 0f) * (p.x > 0 ? 1f : 0f)).ToArray();
                    CrossSectionLibrary.Save("ZzIcon", SectionGeometry.Normalize(v));
                    yield return Wait.Frames(4);
                }
                int after = Alive();
                ctx.Record("iconMeshesBefore", before);
                ctx.Record("iconMeshesAfter", after);
                Assert.True(before >= 1, "the gizmo drew the section's icon");
                Assert.Equal(before, after, "icon meshes alive after 6 re-saves");
            }
            finally
            {
                Paw.CloseAll();
                snap.Restore();
                Cleanup("ZzIcon");
            }
        }

        /// <summary>
        /// A craft's section against the shared list: the shared section of its name as it is now, or an earlier
        /// version of it, matches (the craft follows the edit); a different shape under the name, or a same shape
        /// under another name, doesn't. Matching never adds to the shared list. The history is saved with it.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_CraftSectionsFollowSharedEdits(TestContext ctx)
        {
            List<Vector2> N(IList<Vector2> v) => SectionGeometry.Normalize(v);
            CrossSection Match(string name, IList<Vector2> v) => CrossSectionLibrary.SharedVersionOf(CrossSection.Custom(name, N(v)));
            try
            {
                CrossSectionLibrary.Save("ZzImp", N(Diamond));
                CrossSectionLibrary.Save("ZzImp", N(Triangle));   // an edit: Diamond is now history
                CrossSection imp = CrossSectionLibrary.Get("ZzImp");
                Assert.True(Match("ZzImp", Diamond) == imp, "a craft saved before the edit");
                Assert.True(Match("ZzImp", Triangle) == imp, "a craft saved after the edit");
                Assert.True(Match("ZzImp", L) == null, "a different shape under the name");
                CrossSectionLibrary.Save("ZzOther", N(Plus));
                Assert.True(Match("ZzTwin", Plus) == null, "a same-shape section under another name");
                CrossSectionLibrary.Save("ZzImp", N(Diamond));    // back to Diamond: Triangle joins the history
                Assert.True(Match("ZzImp", Triangle) == CrossSectionLibrary.Get("ZzImp"), "a craft from before the revert");
                Assert.Equal("ZzImp|ZzOther", string.Join("|", CrossSectionLibrary.Names.Where(n => n.StartsWith("Zz"))),
                    "shared sections (nothing added by matching)");
                string file = File.ReadAllText(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "CustomProceduralParts", "PluginData", "CrossSections.cfg"));
                Assert.True(file.Contains("prev = " + SectionGeometry.Hash(N(Triangle))), "history saved in CrossSections.cfg");
            }
            finally { Cleanup("ZzImp", "ZzOther"); }
            yield break;
        }

        /// <summary>
        /// A craft carrying a section the shared list lacks keeps it to itself in the editor: the nodes use their own
        /// copy, the dialog lists it as the craft's, and only the dialog's Save As puts it in the shared list (the
        /// craft then uses the shared one).
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineEmbedded")]
        public static IEnumerator Editor_CraftSectionStaysWithCraft(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            CrossSection own = s.nodes[1].pinned;
            try
            {
                Assert.True(CrossSectionLibrary.Get("ZzEmbedded") == null, "loading the craft added nothing to the shared list");
                Assert.Equal("ZzEmbedded", s.nodes[1].section, "node 1's section");
                Assert.NotNull(own, "node 1 holds the craft's copy");
                Assert.False(s.nodes[1].Section.IsBuiltIn, "node 1 uses the custom geometry");
                Assert.True(ProceduralShapeSpine.CraftSections().Any(c => c.name == "ZzEmbedded"), "listed among the craft's sections");
                Assert.True(s.AvailableSections().Any(c => c == own), "a node can pick it");

                s.SelectNode(1);
                s.EditCrossSectionsEvent();
                yield return Imgui.WaitWindow("Spine Cross Sections");
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                Assert.True(ed._craft && ed._current == "ZzEmbedded", "the dialog opens the craft's section as the craft's");
                Assert.True(CrossSectionEditor.DropList().Any(e => e.Item1 == own && e.Item2), "the dropdown lists it under Craft");
                ed.SaveTo("ZzEmbedded", false);
                CrossSection lib = CrossSectionLibrary.Get("ZzEmbedded");
                Assert.NotNull(lib, "Save As put it in the shared list");
                Assert.True(s.nodes[1].pinned == null, "node 1 uses the shared one now");
                Assert.True(s.nodes[1].Section == lib, "node 1's section is the shared one");
                Assert.Equal(own.ShapeHash, lib.ShapeHash, "same shape");
            }
            finally
            {
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
                foreach (SpineNode n in s.nodes) if (n.section == "ZzEmbedded") n.pinned = own;
                s.UpdateShape(true);
                s.SelectNode(0);
                Cleanup("ZzEmbedded");
            }
        }

        /// <summary>
        /// In flight a vessel keeps the sections its craft carried: its nodes hold the embedded geometry, and a later
        /// change to the library's section of that name doesn't reshape it.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = "SpineEmbedded")]
        public static IEnumerator Flight_VesselKeepsItsSections(TestContext ctx)
        {
            Part p = FlightGlobals.ActiveVessel.parts.First(x => x.FindModuleImplementing<ProceduralShapeSpine>() != null);
            ProceduralShapeSpine s = Spine(p);
            try
            {
                CrossSection sec = s.nodes[1].Section;
                ctx.Record("node1Section", sec.name);
                Assert.Equal("ZzEmbedded", sec.name, "node 1's section");
                Assert.False(sec.IsBuiltIn, "node 1 uses the custom geometry, not the Ellipse fallback");
                Assert.NotNull(s.nodes[1].pinned, "node 1 holds the craft's own copy");
                string shape = sec.ShapeHash;
                float volume = s.CalculateVolume();
                CrossSectionLibrary.Save("ZzEmbedded", SectionGeometry.Normalize(Diamond));   // the library's copy changes
                yield return Wait.Frames(2);
                Assert.Equal(shape, s.nodes[1].Section.ShapeHash, "node 1's geometry after the library changed");
                Assert.Near(volume, s.CalculateVolume(), 1e-6 * volume, "volume after the library changed");
            }
            finally { Cleanup("ZzEmbedded"); }
        }

        /// <summary>Typing in the editor dialog's Save-as field locks the editor's keys, wherever the mouse is.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_TypingANameLocksEditorKeys(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            s.EditCrossSectionsEvent();
            try
            {
                yield return Imgui.WaitWindow("Spine Cross Sections");
                // the harness sets a field's text without focusing it: focus it as a click would
                var focus = new GameObject("ZzFocus").AddComponent<FocusNamed>();
                focus.Name = CrossSectionEditor.SaveAsControl;
                yield return Wait.Frames(3);
                Object.Destroy(focus.gameObject);
                yield return Wait.Frames(2);
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                Assert.True(ed._typing, "the Save-as field has focus");
                // move the window off the pointer (far off screen: batchmode's screen and pointer aren't real ones), so
                // the lock must come from typing alone
                ed._win.x = 20000f;
                ed._win.y = 20000f;
                yield return Wait.Frames(3);
                Assert.False(ed._mouseOver, "pointer outside the window");
                Assert.True(ed._typing, "the Save-as field still has focus");
                ControlTypes locked = InputLockManager.GetControlLock("SpineCrossSectionEditor");
                ctx.Record("lock", locked.ToString());
                Assert.True((locked & ControlTypes.KEYBOARDINPUT) != 0, "keyboard locked while typing");
            }
            finally
            {
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
            }
        }

        /// <summary>The section dialog shows the selected node's section as the selection moves, until it has unsaved edits.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_SectionDialogFollowsSelection(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            s.SelectNode(0);
            s.EditCrossSectionsEvent();
            try
            {
                yield return Imgui.WaitWindow("Spine Cross Sections");
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                Assert.Equal("Ellipse", ed._current, "opens on the selected node's section");
                s.SelectNode(1);
                yield return Wait.Frames(2);
                Assert.Equal("Mk2", ed._current, "follows the selection");
                s.SelectNode(2);
                yield return Wait.Frames(2);
                Assert.Equal("Rectangle", ed._current, "follows again");
                // an edit (as a vertex drag makes): the selection no longer replaces it
                ed._verts[0] += new Vector2(0.05f, 0f);
                ed._dirty = true;
                s.SelectNode(1);
                yield return Wait.Frames(2);
                Assert.Equal("Rectangle", ed._current, "unsaved edits stay");
                Assert.True(ed._dirty, "still edited");
            }
            finally
            {
                s.SelectNode(0);
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
            }
        }

        // Every vertex's mirror image (by flags) is a vertex too.
        private static bool MirrorsOnto(IList<Vector2> v, int flags) =>
            v.All(p => v.Any(q => (SectionSymmetry.Flip(p, flags) - q).sqrMagnitude < 1e-8f));

        /// <summary>
        /// Mirroring cuts a section at the mirror line(s) and rebuilds the far side from the near one: the result is
        /// a valid polygon, symmetric as asked, within the vertex cap, for assorted shapes.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_MirrorMakesSymmetricSections(TestContext ctx)
        {
            var shapes = new Dictionary<string, IList<Vector2>>
            {
                ["Triangle"] = Triangle, ["Diamond"] = Diamond, ["Star"] = Star, ["Plus"] = Plus, ["L"] = L,
                ["Mk2"] = CrossSectionLibrary.Resolve("Mk2").EditableVerts(),
            };
            int bad = 0;
            foreach (var kv in shapes)
                foreach ((bool lr, bool tb) in new[] { (true, false), (false, true), (true, true) })
                {
                    int cap = ProceduralShapeSpine.MaxSectionVerts;
                    List<Vector2> c = SectionSymmetry.Chain(kv.Value, lr, tb, SectionSymmetry.MaxChain(lr, tb, cap));
                    string what = $"{kv.Key} {(lr ? "LR" : "")}{(tb ? "TB" : "")}";
                    if (c == null) { ctx.Record(what, "no chain"); bad++; continue; }
                    List<Vector2> full = SectionSymmetry.Full(c, lr, tb, null, null);
                    string err = SectionGeometry.Validate(full);
                    bool sym = (!lr || MirrorsOnto(full, SectionSymmetry.FlipX)) && (!tb || MirrorsOnto(full, SectionSymmetry.FlipY));
                    ctx.Record(what, $"{full.Count} verts, {(err ?? "valid")}, {(sym ? "symmetric" : "NOT symmetric")}");
                    if (err != null || !sym || full.Count > cap) bad++;
                }
            Assert.Equal(0, bad, "sections that came out invalid or lopsided");
            yield break;
        }

        /// <summary>
        /// With mirroring on, dragging, adding or deleting a vertex on either side edits its master and every image;
        /// Revert turns mirroring off and brings the section back.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_MirrorEditsFollowAcross(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            s.SelectNode(0);
            s.EditCrossSectionsEvent();
            try
            {
                yield return Imgui.WaitWindow("Spine Cross Sections");
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                ed._verts = new List<Vector2>(Diamond);
                ed.SetMirror(true, false);
                Assert.True(ed._mirrorLR && ed.Mirroring, "left to right on");
                yield return Wait.Frames(3);
                yield return Snap.Shot(ctx, "section-editor-mirror");
                Assert.True(MirrorsOnto(ed._verts, SectionSymmetry.FlipX), "symmetric left-right");
                Assert.True(ed._dirty, "the replaced side counts as an edit");
                int n0 = ed._verts.Count;

                // a master vertex (not on the mirror line) moves its image
                int i = Enumerable.Range(0, n0).First(k => ed._mapF[k] == 0 && ed._mapM[k] > 0 && ed._mapM[k] < ed._chain.Count - 1);
                ed.MoveVertex(i, new Vector2(-0.8f, 0.5f));
                Assert.True(MirrorsOnto(ed._verts, SectionSymmetry.FlipX), "still symmetric after a master drag");
                Assert.True(ed._verts.Any(p => (p - new Vector2(0.8f, 0.5f)).sqrMagnitude < 1e-8f), "the image moved");

                // an image moves its master
                int j = Enumerable.Range(0, n0).First(k => ed._mapF[k] == SectionSymmetry.FlipX);
                ed.MoveVertex(j, new Vector2(0.6f, -0.7f));
                Assert.True(ed._verts.Any(p => (p - new Vector2(-0.6f, -0.7f)).sqrMagnitude < 1e-8f), "the master moved");
                Assert.True(MirrorsOnto(ed._verts, SectionSymmetry.FlipX), "still symmetric after an image drag");

                // adding on an edge adds two, deleting removes two
                Assert.True(ed.InsertOnEdge(0, 0.5f * (ed._verts[0] + ed._verts[1])), "inserted");
                Assert.Equal(n0 + 2, ed._verts.Count, "an insert adds a vertex and its image");
                int d = Enumerable.Range(0, ed._verts.Count).First(k => ed._mapM[k] > 0 && ed._mapM[k] < ed._chain.Count - 1);
                Assert.True(ed.DeleteVertex(d), "deleted");
                Assert.Equal(n0, ed._verts.Count, "a delete removes a vertex and its image");
                Assert.True(MirrorsOnto(ed._verts, SectionSymmetry.FlipX), "symmetric after insert and delete");

                // both: four quadrants
                ed.SetMirror(true, true);
                Assert.True(ed._verts.Count % 4 == 0, "both mirrors: four copies");
                Assert.True(MirrorsOnto(ed._verts, SectionSymmetry.FlipX) && MirrorsOnto(ed._verts, SectionSymmetry.FlipY), "symmetric both ways");
                Assert.True(SectionGeometry.Validate(ed._verts) == null, $"valid ({SectionGeometry.Validate(ed._verts)})");
                ctx.Record("both", SectionGeometry.Format(ed._verts));

                // Revert: the section as saved, mirroring off
                string cur = ed._current;
                ed.Select(cur);
                Assert.False(ed._mirrorLR || ed._mirrorTB || ed.Mirroring, "Revert turns mirroring off");
                Assert.False(ed._dirty, "reverted");
                Assert.Equal(SectionGeometry.Format(CrossSectionLibrary.Resolve(cur).EditableVerts()), SectionGeometry.Format(ed._verts), "the section back");
            }
            finally
            {
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
            }
        }

        /// <summary>
        /// The Spine Options window holds the display options (out of the PAW, the same for every Spine); its outline
        /// option draws every node's section on the part (and its hidden part, faint), the selected one wider, with a
        /// vertical line through each node's handle; turning it off removes them.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_SpineOptionsShowOutlines(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            Assert.True(s.Events[nameof(ProceduralShapeSpine.SpineOptionsEvent)].guiActiveEditor, "Spine Options in the PAW");
            // OnStart ran through: the last PAW field it wires has its handler (a throw part way leaves it unset)
            Assert.NotNull(s.Fields[nameof(ProceduralShapeSpine.shellThickness)].uiControlEditor.onFieldChanged, "OnStart wired the PAW fields");
            LineRenderer[] Lines() => s.part.GetComponentsInChildren<LineRenderer>().Where(l => l.name == "SpineSectionOutline").ToArray();
            SpineHandleVis vis0 = SpineSettings.HandleVis;
            bool click0 = SpineSettings.ClickBeforeDrag, outlines0 = SpineSettings.ShowOutlines;
            SpineSettings.Set(vis0, click0, false);
            s.SpineOptionsEvent();
            try
            {
                yield return Imgui.WaitWindow(SpineOptionsWindow.Title);
                yield return Wait.Frames(3);
                yield return Snap.Shot(ctx, "spine-options");
                Assert.Equal(0, Lines().Length, "no outlines before the option");
                SpineSettings.Set(vis0, click0, true);
                s.SelectNode(1);
                yield return Wait.Frames(3);
                Assert.Equal(0, Lines().Length, "no outlines while the part isn't selected (PAW closed)");
                yield return Paw.Open(s.part);
                yield return Wait.Frames(3);
                LineRenderer[] lines = Lines();
                Assert.Equal(s.nodes.Count, lines.Length, "an outline per node");
                Assert.True(lines.All(l => l.positionCount == s.Sides), "each the ring's points");
                ctx.Record("widths", string.Join(" ", lines.Select(l => l.widthMultiplier.ToString("F3"))));
                Assert.True(lines[1].widthMultiplier > lines[0].widthMultiplier, "the selected node's outline stands out");
                for (int i = 0; i < s.nodes.Count; i++)
                {
                    // its hidden part: the same loop, fainter
                    Transform h = lines[i].transform.Find("SpineSectionOutlineHidden");
                    Assert.NotNull(h, $"node {i}'s hidden outline");
                    var hidden = h.GetComponent<LineRenderer>();
                    Assert.True(hidden.positionCount == lines[i].positionCount && hidden.startColor.a < lines[i].startColor.a, $"node {i}'s hidden outline: same loop, fainter");
                    // the vertical through its node's handle (the node centre the gizmo places it at)
                    Transform c = lines[i].transform.Find("SpineSectionChord");
                    Assert.NotNull(c, $"node {i}'s line");
                    var chord = c.GetComponent<LineRenderer>();
                    SpineNode n = s.nodes[i];
                    var handle = new Vector3(s.EoffH(n), (n.position - 0.5f) * s.length, s.Eoff(n));
                    Assert.Equal(3, chord.positionCount, $"node {i}'s line points");
                    Vector3 lo = chord.GetPosition(0), mid = chord.GetPosition(1), hi = chord.GetPosition(2);
                    Assert.True((mid - handle).magnitude < 1e-4f, $"node {i}'s line through its handle ({mid} vs {handle})");
                    Assert.True(Mathf.Abs(lo.x - handle.x) < 1e-4f && Mathf.Abs(hi.x - handle.x) < 1e-4f && lo.z < handle.z && hi.z > handle.z,
                                $"node {i}'s line vertical, bottom to top ({lo} .. {hi})");
                }
                yield return Snap.Shot(ctx, "outlines");
                Paw.CloseAll();
                yield return Wait.Frames(3);
                Assert.Equal(0, Lines().Length, "outlines gone with the part deselected");
                yield return Paw.Open(s.part);
                yield return Wait.Frames(3);
                Assert.Equal(s.nodes.Count, Lines().Length, "back when it's selected again");
                SpineSettings.Set(vis0, click0, false);
                yield return Wait.Frames(3);
                Assert.Equal(0, Lines().Length, "outlines gone with the option off");
            }
            finally
            {
                Paw.CloseAll();
                SpineSettings.Set(vis0, click0, outlines0);
                s.SelectNode(0);
                var w = Object.FindObjectOfType<SpineOptionsWindow>();
                if (w != null) Object.Destroy(w.gameObject);
            }
        }

        /// <summary>
        /// Points per section is the part's own: more points give every ring (and the mesh) that many, each node's
        /// section a little fuller (its curves closer to true), and it's saved with the part. The volume moves only a
        /// little: between two different sections the morph pairs points by index, and which points pair shifts with
        /// the count (the SpineTest craft: 6.851 at 24 points, 6.772 at 48).
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_SectionPointsPerPart(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            float v24 = s.CalculateVolume();
            float[] Areas() => s.nodes.Select(nd => ProceduralShapeSpine.RingArea(s.NodeRing(nd))).ToArray();
            float[] a24 = Areas();
            try
            {
                Assert.Equal(24, s.Sides, "24 points by default");
                s.SetSectionPoints(48);
                yield return Wait.Frames(2);
                Assert.Equal(48, s.Sides, "48 after the option");
                Assert.True(s.BuildRings().All(r => r.outline.Length == 48), "every ring 48 points");
                float v48 = s.CalculateVolume();
                float[] a48 = Areas();
                ctx.Record("volume", $"{v24:F5} -> {v48:F5}");
                ctx.Record("nodeAreas", string.Join(" ", a24.Zip(a48, (x, y) => $"{x:F4}->{y:F4}")));
                Assert.True(a24.Zip(a48, (x, y) => y >= x - 1e-5f).All(b => b), "each node's section at least as full");
                Assert.True(Mathf.Abs(v48 - v24) < 0.03f * v24, "the volume within 3 %");
                var cfg = new ConfigNode();
                s.Save(cfg);
                Assert.Equal("48", cfg.GetValue(nameof(ProceduralShapeSpine.sectionPoints)), "saved with the part");
                s.SetSectionPoints(30);
                Assert.True(s.Sides >= 24 && s.Sides <= 96, $"a value between the choices is clamped into range ({s.Sides})");
            }
            finally
            {
                s.SetSectionPoints(24);
            }
            yield return Wait.Frames(2);
            Assert.Near(v24, s.CalculateVolume(), 1e-5 * v24, "back to 24: the volume as before");
        }

        /// <summary>The section list drops down over the dialog without resizing it, and a pick closes it.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_SectionListDropsDownOverTheDialog(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            s.SelectNode(0);
            s.EditCrossSectionsEvent();
            try
            {
                yield return Imgui.WaitWindow("Spine Cross Sections");
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                yield return Wait.Frames(3);
                Rect before = ed._win;
                ed._dropOpen = true;
                yield return Wait.Frames(3);
                ctx.Record("dialog", $"{before.width:F0}x{before.height:F0} -> {ed._win.width:F0}x{ed._win.height:F0}");
                ctx.Record("list", ed._dropRect.ToString());
                Assert.True(ed._dropOpen, "the list stays open");
                Assert.Near(before.height, ed._win.height, 0.5, "dialog height with the list open");
                Assert.True(ed._dropRect.height > 100f && ed._dropRect.y >= ed._win.y, "the list hangs under the dialog's top");
                Assert.True(ed._dropList != null && ed._dropList.Count > 0, "the list has entries");
                CrossSection pick = ed._dropList.Select(e => e.Item1).First(c => c.name == "Rectangle");
                ed.Select(pick, false);
                ed._dropOpen = false;
                Assert.Equal("Rectangle", ed._current, "picked");
            }
            finally
            {
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
            }
        }

        /// <summary>
        /// Custom sections take up to MaxSectionVerts (96) vertices. A part with fewer points per section shows one
        /// simplified to its count, still starting at the rightmost point; a part with as many keeps every vertex.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_BigSectionsSimplifyOnSmallParts(TestContext ctx)
        {
            // 48 vertices round a slightly squashed circle, the rightmost (1, -0.1) pushed out a little
            var v = new List<Vector2>();
            for (int i = 0; i < 48; i++)
            {
                float a = 2f * Mathf.PI * i / 48;
                v.Add(new Vector2(Mathf.Cos(a), 0.8f * Mathf.Sin(a)));
            }
            v[0] = new Vector2(1.02f, -0.1f);
            Assert.True(SectionGeometry.Validate(v) == null, $"48 vertices is a valid section ({SectionGeometry.Validate(v)})");
            var many = Enumerable.Range(0, ProceduralShapeSpine.MaxSectionVerts + 1)
                .Select(i => new Vector2(Mathf.Cos(2f * Mathf.PI * i / 97), Mathf.Sin(2f * Mathf.PI * i / 97))).ToList();
            Assert.True(SectionGeometry.Validate(many) != null, "over the cap is refused");
            CrossSection cs = CrossSection.Custom("ZzBig", SectionGeometry.Normalize(v));
            Vector2[] u24 = cs.Unit(24), u48 = cs.Unit(48);
            Assert.Equal(24, u24.Length, "24 points on a 24-point part");
            Assert.Equal(48, u48.Length, "48 on a 48-point part");
            Vector2 start = cs.verts.OrderByDescending(p => p.x).First();
            Assert.True((u24[0] - start).sqrMagnitude < 1e-8f && (u48[0] - start).sqrMagnitude < 1e-8f, $"both start at the rightmost vertex ({u24[0]}, {u48[0]}, {start})");
            Assert.True(cs.verts.All(p => u48.Any(q => (q - p).sqrMagnitude < 1e-8f)), "48 points keep every vertex");
            Assert.True(SectionGeometry.Validate(u24) == null, $"the 24-point simplification is a valid polygon ({SectionGeometry.Validate(u24)})");
            ctx.Record("area", $"{SectionGeometry.SignedArea(u24):F4} of {SectionGeometry.SignedArea(u48):F4}");
            yield break;
        }

        /// <summary>
        /// A custom section's outline starts at its rightmost point (the nearest mid-height among ties), even where
        /// another point lies nearer the +x axis: sections cut from one model with a vertical side morph untwisted.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_CustomSectionStartsAtRightmostPoint(TestContext ctx)
        {
            // the side's middle (1, -0.6) stands out a hair; the corner (0.999, -0.2) lies nearer the axis
            var verts = new List<Vector2>
            {
                new Vector2(1f, -0.6f), new Vector2(0.999f, -0.2f), new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(0.999f, -1f),
            };
            Assert.True(SectionGeometry.Validate(verts) == null, $"valid ({SectionGeometry.Validate(verts)})");
            CrossSection cs = CrossSection.Custom("ZzStart", SectionGeometry.Normalize(verts));
            Vector2 p0 = cs.UnitOutline[0];
            ctx.Record("start", $"{p0.x:F4},{p0.y:F4}");
            Assert.Near(1.0, p0.x, 1e-4, "start x");
            Assert.Near(-0.6, p0.y, 1e-4, "start y");
            yield break;
        }
    }
}

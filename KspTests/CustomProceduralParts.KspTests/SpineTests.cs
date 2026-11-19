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
            // two segments blending into it grew; 6.85143 until fillets went (2026-11-17) and its Rectangle node's
            // rounded top corners with them. Change this only on purpose, with the built-in geometry.
            Assert.Near(7.05121, s.Volume, 1e-3, "volume");
            yield break;
        }

        /// <summary>
        /// The collider is convex pieces of a few points per ring; a custom section's tips must be among them, wherever
        /// they fall in the outline, so the collider spans the visible mesh.
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
                    MeshCollider[] pieces = s.part.GetComponentsInChildren<MeshCollider>(true).Where(c => c.gameObject.name == "collisionMesh").ToArray();
                    Bounds col = pieces[0].sharedMesh.bounds;
                    foreach (MeshCollider mc in pieces) col.Encapsulate(mc.sharedMesh.bounds);   // one holder: one frame
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
        /// Revert brings the section back, unedited.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_MirrorEditsFollowAcross(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            bool lr0 = SpineSettings.MirrorLR, tb0 = SpineSettings.MirrorTB;
            SpineSettings.SetMirror(false, false);
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

                // Revert: the section as saved (mirrored again only if that leaves it as it is)
                string cur = ed._current;
                ed.Select(cur);
                Assert.False(ed._dirty, "reverted");
                Assert.True(CrossSectionEditor.SamePolygon(CrossSectionLibrary.Resolve(cur).EditableVerts(), ed._verts), "the section back");
            }
            finally
            {
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
                SpineSettings.SetMirror(lr0, tb0);
            }
        }

        // Symmetric both ways (edges, not vertices, cross both mirror lines), left-right only, and neither.
        private static readonly Vector2[] Hexagon =
            { new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(-0.5f, 1f), new Vector2(-1f, 0f), new Vector2(-0.5f, -1f), new Vector2(0.5f, -1f) };
        private static readonly Vector2[] Arch =
            { new Vector2(1f, -1f), new Vector2(1f, 0.3f), new Vector2(0f, 1f), new Vector2(-1f, 0.3f), new Vector2(-1f, -1f) };

        /// <summary>
        /// The mirror toggles are remembered (PluginData/SpineOptions.cfg, every craft): ticked, they come back on for
        /// each section opened that they fit (both, or the one that fits), without making it an edit. A section they'd
        /// change opens unmirrored and unedited, and Revert still undoes mirroring it.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_MirrorTogglesPersist(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            bool lr0 = SpineSettings.MirrorLR, tb0 = SpineSettings.MirrorTB;
            SpineSettings.SetMirror(false, false);
            CrossSectionLibrary.Save("ZzHexagon", SectionGeometry.Normalize(Hexagon));
            CrossSectionLibrary.Save("ZzArch", SectionGeometry.Normalize(Arch));
            CrossSectionLibrary.Save("ZzDiamond", SectionGeometry.Normalize(Diamond));
            s.SelectNode(0);
            s.EditCrossSectionsEvent();
            try
            {
                yield return Imgui.WaitWindow("Spine Cross Sections");
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                ed.Select("ZzHexagon");
                Assert.False(ed.Mirroring, "off to start with");
                ed.SetMirror(true, true);
                Assert.True(ed._mirrorLR && ed._mirrorTB, "both on");
                Assert.False(ed._dirty, "mirroring a symmetric section isn't an edit");
                Assert.True(SpineSettings.MirrorLR && SpineSettings.MirrorTB, "remembered");

                ed.Select("ZzArch");
                Assert.True(ed._mirrorLR && !ed._mirrorTB, "an arch: left to right only");
                Assert.False(ed._dirty, "opening it isn't an edit");

                ed.Select("ZzDiamond");
                ctx.Record("diamond status", ed._status);
                Assert.False(ed.Mirroring, "a lopsided section opens unmirrored");
                Assert.False(ed._dirty, "and unedited");
                Assert.True(CrossSectionEditor.SamePolygon(CrossSectionLibrary.Resolve("ZzDiamond").EditableVerts(), ed._verts), "as saved");
                Assert.True(SpineSettings.MirrorLR && SpineSettings.MirrorTB, "still remembered");

                ed.SetMirror(true, false);
                Assert.True(ed._dirty, "mirroring it is an edit");
                ed.Select(ed._current);
                Assert.False(ed.Mirroring || ed._dirty, "Revert undoes it");

                ed.Select("ZzHexagon");
                Assert.True(ed._mirrorLR && !ed._mirrorTB, "the last ticked (left to right) comes back");

                // a new dialog, and the options read back from the file as at startup
                Object.Destroy(ed.gameObject);
                yield return Wait.Frames(2);
                SpineSettings.SetMirror(true, true);
                SpineSettings._loaded = false;
                SpineSettings._mirrorLR = SpineSettings._mirrorTB = false;
                Assert.True(SpineSettings.MirrorLR && SpineSettings.MirrorTB, "read back from SpineOptions.cfg");
                s.EditCrossSectionsEvent();
                yield return Imgui.WaitWindow("Spine Cross Sections");
                ed = Object.FindObjectOfType<CrossSectionEditor>();
                ed.Select("ZzHexagon");
                Assert.True(ed._mirrorLR && ed._mirrorTB, "on in a new dialog");
            }
            finally
            {
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
                SpineSettings.SetMirror(lr0, tb0);
                Cleanup("ZzHexagon", "ZzArch", "ZzDiamond");
            }
        }

        private static bool OnRun(List<Vector2> v, Vector2 a, Vector2 b) =>
            Enumerable.Range(0, v.Count).Any(i => (v[i] - a).sqrMagnitude < 1e-6f && (v[(i + 1) % v.Count] - b).sqrMagnitude < 1e-6f);

        /// <summary>
        /// Stretch pulls a section's halves apart with straight runs between, fitted back to the box: right gives a flat
        /// top and bottom, left flat sides. The slider works from the shape it started on (back to 0 restores it, any
        /// other edit starts again from there), keeps a mirrored section mirrored, and the result saves.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_StretchStraightensTheMiddle(TestContext ctx)
        {
            // the pure function: a diamond (vertices on both cuts) and a circle-ish octagon (edges across them)
            List<Vector2> d = SectionGeometry.StretchMiddle(Diamond, 1f);
            ctx.Record("diamond +1", SectionGeometry.Format(d));
            Assert.True(SectionGeometry.Validate(d) == null, $"valid ({SectionGeometry.Validate(d)})");
            var oct = Enumerable.Range(0, 8).Select(k => new Vector2(Mathf.Cos((k + 0.5f) * Mathf.PI / 4f), Mathf.Sin((k + 0.5f) * Mathf.PI / 4f))).ToList();
            float m = oct.Max(p => p.x), h = 0.5f * m;   // s = 1 doubles the width, squeezed back: the cut's two sides land at +/- m/2
            List<Vector2> wide = SectionGeometry.StretchMiddle(oct, 1f);
            ctx.Record("octagon +1", SectionGeometry.Format(wide));
            Assert.Equal(12, wide.Count, "two points where each of the two crossing edges was cut");
            Assert.True(Mathf.Abs(wide.Max(p => p.x) - m) < 1e-5f && Mathf.Abs(wide.Min(p => p.x) + m) < 1e-5f, "the same width");
            Assert.True(Mathf.Abs(wide.Max(p => p.y) - m) < 1e-5f, "the same height");
            Assert.True(OnRun(wide, new Vector2(h, m), new Vector2(-h, m)), "a flat top across the middle half");
            List<Vector2> tall = SectionGeometry.StretchMiddle(oct, -1f);
            Assert.True(OnRun(tall, new Vector2(-m, h), new Vector2(-m, -h)), "left: a flat left side");

            ProceduralShapeSpine s = Spine();
            bool lr0 = SpineSettings.MirrorLR, tb0 = SpineSettings.MirrorTB;
            SpineSettings.SetMirror(false, false);
            s.SelectNode(0);
            s.EditCrossSectionsEvent();
            try
            {
                yield return Imgui.WaitWindow("Spine Cross Sections");
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                ed.Select("Ellipse");
                List<Vector2> start = new List<Vector2>(ed._verts);
                Assert.True(ed.SetStretch(0.5f), "stretched");
                Assert.True(ed._dirty, "an edit");
                yield return Wait.Frames(3);
                yield return Snap.Shot(ctx, "section-editor-stretch");
                Assert.True(ed.SetStretch(1f) && ed.SetStretch(0f), "and back");
                Assert.True(CrossSectionEditor.SamePolygon(start, ed._verts), "0 again: the shape it started on");

                // another edit starts the slider again from the edited shape
                ed.SetStretch(0.5f);
                List<Vector2> half = new List<Vector2>(ed._verts);
                ed.MoveVertex(0, ed._verts[0] + new Vector2(0f, 0.01f));
                Assert.Equal(0f, ed._stretch, "the slider back at 0");
                ed.SetStretch(-0.3f);
                ed.SetStretch(0f);
                Assert.False(CrossSectionEditor.SamePolygon(start, ed._verts) || CrossSectionEditor.SamePolygon(half, ed._verts), "0 is the edited shape");

                // mirrored both ways it stays mirrored
                ed.Select("ZzStretchHex");
                ed._verts = new List<Vector2>(Hexagon);
                ed.SetMirror(true, true);
                Assert.True(ed.SetStretch(0.6f), "stretched mirrored");
                Assert.True(ed._mirrorLR && ed._mirrorTB, "still mirrored");
                Assert.True(MirrorsOnto(ed._verts, SectionSymmetry.FlipX) && MirrorsOnto(ed._verts, SectionSymmetry.FlipY), "symmetric both ways");
                ed.MoveVertex(0, ed._verts[0] + new Vector2(0f, -0.05f));
                Assert.True(MirrorsOnto(ed._verts, SectionSymmetry.FlipY), "a drag after it still mirrors");
                ed._saveAsName = "ZzStretchHex";
                ed.SaveTo(ed._saveAsName, overwriteOk: false);
                CrossSection saved = CrossSectionLibrary.Get("ZzStretchHex");
                Assert.True(saved != null, $"saved ({ed._status})");
                ctx.Record("saved", SectionGeometry.Format(saved.verts));
            }
            finally
            {
                var ed = Object.FindObjectOfType<CrossSectionEditor>();
                if (ed != null) Object.Destroy(ed.gameObject);
                SpineSettings.SetMirror(lr0, tb0);
                Cleanup("ZzStretchHex");
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

        // A square, and a square whose top is an arch of many vertices: very different vertex counts.
        private static readonly Vector2[] Square = { new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f) };

        private static Vector2[] ArchTop()
        {
            var v = new List<Vector2> { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 0.3f) };
            for (int d = 10; d <= 170; d += 10)
                v.Add(new Vector2(Mathf.Cos(d * Mathf.Deg2Rad), 0.3f + 0.7f * Mathf.Sin(d * Mathf.Deg2Rad)));
            v.Add(new Vector2(-1f, 0.3f));
            return v.ToArray();
        }

        // Mean distance between paired points of two outlines, and the largest angle (about the centre) between them.
        private static (float mean, float maxDeg) Pairing(Vector2[] a, Vector2[] b)
        {
            float sum = 0f, max = 0f;
            for (int j = 0; j < a.Length; j++)
            {
                sum += (a[j] - b[j]).magnitude;
                max = Mathf.Max(max, Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(a[j].y, a[j].x) * Mathf.Rad2Deg, Mathf.Atan2(b[j].y, b[j].x) * Mathf.Rad2Deg)));
            }
            return (sum / a.Length, max);
        }

        private static bool HasPoint(Vector2[] ring, Vector2 p) => ring.Any(q => (q - p).sqrMagnitude < 1e-6f);

        /// <summary>
        /// Neighbouring sections with very different vertex counts pair their points by best fit: a 4-vertex square next to
        /// a 21-vertex arched top morphs corner to corner, where sampling each on its own (the old way) slid the pairing
        /// around the outline and twisted the segment. Both sections' corners stay exact.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_BestFitPairingAcrossVertexCounts(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            try
            {
                yield return Use(s, "ZzSquare", Square, i => i == 1);
                yield return Use(s, "ZzArch", ArchTop(), i => i == 2);
                Vector2[][] units = s._alignUnits;
                Assert.NotNull(units, "a spine with custom sections is aligned");
                Vector2[] a = units[1], b = units[2];
                ctx.Record("points", a.Length);
                (float mean, float maxDeg) fit = Pairing(a, b);
                CrossSection sq = CrossSectionLibrary.Get("ZzSquare"), arch = CrossSectionLibrary.Get("ZzArch");
                (float mean, float maxDeg) old = Pairing(SectionGeometry.Resample(sq.verts, s.Sides), SectionGeometry.Resample(arch.verts, s.Sides));
                ctx.Record("bestFit", $"mean {fit.mean:F3} max {fit.maxDeg:F1} deg");
                ctx.Record("separately", $"mean {old.mean:F3} max {old.maxDeg:F1} deg");
                Assert.True(fit.mean < 0.6f * old.mean, $"pairs closer than sampling each alone ({fit.mean:F3} vs {old.mean:F3})");
                Assert.True(fit.maxDeg < 45f, $"no pair more than 45 deg apart around the outline ({fit.maxDeg:F1})");
                foreach (Vector2 c in sq.verts) Assert.True(HasPoint(a, c), $"the square's corner {c} is a ring point");
                foreach (Vector2 c in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 0.3f), new Vector2(-1f, 0.3f) })
                    Assert.True(HasPoint(b, arch.verts.First(v => (v - c).sqrMagnitude < 1e-4f)), $"the arch's corner {c} is a ring point");
                // the bottom corners pair with each other
                int ia = Enumerable.Range(0, a.Length).First(j => (a[j] - new Vector2(1f, -1f)).sqrMagnitude < 1e-6f);
                Assert.True((b[ia] - arch.verts[1]).sqrMagnitude < 1e-4f, $"the square's bottom-right corner pairs with the arch's ({b[ia]})");
                yield return Snap.Shot(ctx, "square-to-arch");
            }
            finally
            {
                snap.Restore();
                Cleanup("ZzSquare", "ZzArch");
            }
        }

        /// <summary>Neighbouring sections with the same vertex count pair vertex to vertex.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_SameVertexCountPairsVertexToVertex(TestContext ctx)
        {
            Vector2[] hexA = Enumerable.Range(0, 6).Select(i => new Vector2(Mathf.Cos(i * Mathf.PI / 3f), Mathf.Sin(i * Mathf.PI / 3f))).ToArray();
            // an edit of it, as a player would make one: the upper-right vertex pulled up and out, the left one raised
            Vector2[] hexB = (Vector2[])hexA.Clone();
            hexB[1] = new Vector2(0.75f, 1f);
            hexB[3] = new Vector2(-1f, 0.3f);
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            try
            {
                yield return Use(s, "ZzHexA", hexA, i => i == 1);
                yield return Use(s, "ZzHexB", hexB, i => i == 2);
                Vector2[] a = s._alignUnits[1], b = s._alignUnits[2];
                List<Vector2> va = CrossSectionLibrary.Get("ZzHexA").verts, vb = CrossSectionLibrary.Get("ZzHexB").verts;
                var at = new List<int>();
                for (int v = 0; v < va.Count; v++)
                {
                    int j = System.Array.FindIndex(a, p => (p - va[v]).sqrMagnitude < 1e-6f);
                    Assert.True(j >= 0, $"hexA vertex {v} is a ring point");
                    at.Add(j);
                }
                // each of A's vertices meets one of B's, in the same order around
                var hitB = at.Select(j => vb.FindIndex(p => (p - b[j]).sqrMagnitude < 1e-6f)).ToList();
                ctx.Record("pairs", string.Join(" ", at.Zip(hitB, (j, v) => $"{j}:{v}")));
                Assert.True(hitB.All(v => v >= 0), "every vertex of A pairs with a vertex of B");
                for (int k = 1; k < hitB.Count; k++)
                    Assert.True((hitB[k] - hitB[k - 1] + 6) % 6 == 1, $"in order around ({string.Join(",", hitB)})");
            }
            finally
            {
                snap.Restore();
                Cleanup("ZzHexA", "ZzHexB");
            }
        }

        // Rays from all round the part at its axis, against its colliders and against the visible skin (an exact,
        // non-convex collider of the side mesh): how far apart the hits are, and the angle between their normals.
        private struct SkinMiss { public float maxDist, p95Dist, maxAngle, p95Angle, meanAngle; public int rays; }

        private static SkinMiss ColliderVsSkin(ProceduralShapeSpine s)
        {
            Part part = s.part;
            MeshFilter sides = part.GetComponentsInChildren<MeshFilter>(true).First(mf => mf.sharedMesh == s.PPart.SidesMesh);
            var go = new GameObject("ZzSkinProbe");
            go.transform.SetPositionAndRotation(sides.transform.position, sides.transform.rotation);
            go.transform.localScale = sides.transform.lossyScale;
            var skin = go.AddComponent<MeshCollider>();
            skin.sharedMesh = s.PPart.SidesMesh;
            Collider[] cols = part.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && c != skin).ToArray();
            var dists = new List<float>();
            var angles = new List<float>();
            var worst = new List<(float angle, string what)>();
            try
            {
                float R = 3f * s.MaxDiameter + 1f;
                for (float fy = 0.035f; fy < 0.98f; fy += 0.02f)   // off the node rings (0.4, 0.75): a ray on a crease hits either face
                    for (int a = 0; a < 360; a += 10)
                    {
                        float y = (fy - 0.5f) * s.length, th = a * Mathf.Deg2Rad;
                        Vector3 o = part.transform.TransformPoint(new Vector3(R * Mathf.Cos(th), y, R * Mathf.Sin(th)));
                        Vector3 to = part.transform.TransformPoint(new Vector3(0f, y, 0f));
                        var ray = new Ray(o, (to - o).normalized);
                        if (!skin.Raycast(ray, out RaycastHit hs, 2f * R)) continue;
                        float best = float.MaxValue;
                        RaycastHit hc = default;
                        foreach (Collider c in cols)
                            if (c.Raycast(ray, out RaycastHit h, 2f * R) && h.distance < best) { best = h.distance; hc = h; }
                        if (best == float.MaxValue) { dists.Add(float.PositiveInfinity); angles.Add(180f); continue; }
                        dists.Add(Mathf.Abs(hs.distance - hc.distance));
                        angles.Add(Vector3.Angle(hs.normal, hc.normal));
                        worst.Add((angles[angles.Count - 1], $"y {fy:F2} a {a} d {dists[dists.Count - 1]:F3} skin {part.transform.InverseTransformDirection(hs.normal).ToString("F2")} col {part.transform.InverseTransformDirection(hc.normal).ToString("F2")} {hc.collider.name}"));
                    }
            }
            finally { Object.DestroyImmediate(go); }
            LastWorst = string.Join(" | ", worst.OrderByDescending(w => w.angle).Take(12).Select(w => $"{w.angle:F0}: {w.what}"));
            dists.Sort();
            angles.Sort();
            float P95(List<float> l) => l.Count == 0 ? 0f : l[(int)(0.95f * (l.Count - 1))];
            return new SkinMiss { rays = dists.Count, maxDist = dists.LastOrDefault(), p95Dist = P95(dists),
                                  maxAngle = angles.LastOrDefault(), p95Angle = P95(angles), meanAngle = angles.Count == 0 ? 0f : angles.Average() };
        }

        private static string LastWorst;

        private static string Show(SkinMiss m) =>
            $"rays {m.rays}: dist max {m.maxDist:F3} p95 {m.p95Dist:F3}; normal angle max {m.maxAngle:F1} p95 {m.p95Angle:F1} mean {m.meanAngle:F1}";

        /// <summary>
        /// The collider follows the visible skin closely enough to surface-attach to: a ray hits it about where it
        /// hits the skin, with about the skin's normal (KSP orients a surface-attached part by that normal) -- on
        /// curved runs, a waist, and a concave section.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_ColliderFollowsSkin(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            try
            {
                yield return Wait.Frames(2);
                SkinMiss craft = ColliderVsSkin(s);
                ctx.Record("craft", Show(craft));
                ctx.Record("craft worst", LastWorst);
                ctx.Record("craft colliders", s.part.GetComponentsInChildren<Collider>(false).Length);
                s.nodes[0].slopeAbove = SpineSlope.Waisted;
                s.nodes[2].slopeAbove = SpineSlope.Concave;
                s.UpdateShape(true);
                yield return Wait.Frames(2);
                SkinMiss waist = ColliderVsSkin(s);
                ctx.Record("waist+concave", Show(waist));
                yield return Use(s, "ZzPlus", Plus, i => i == 1);
                SkinMiss plus = ColliderVsSkin(s);
                ctx.Record("plus", Show(plus));
                ctx.Record("plus worst", LastWorst);
                ctx.Record("plus colliders", s.part.GetComponentsInChildren<Collider>(false).Length);
                foreach (var (name, m) in new[] { ("craft", craft), ("waist+concave", waist), ("plus", plus) })
                {
                    Assert.True(m.rays > 1000, $"{name}: the rays hit ({m.rays})");
                    Assert.True(m.p95Dist < 0.03f, $"{name}: a ray hits the collider within 3 cm of the skin (95%: {m.p95Dist:F3} m)");
                    Assert.True(m.meanAngle < 5f, $"{name}: the collider's normal is the skin's, near enough (mean {m.meanAngle:F1} deg)");
                    // the rest: a morph pairs corners with smooth points, so the visible mesh twists a little there and its
                    // triangles tilt off the true slope; the collider, a hull, is the smoother of the two (1.7 cm apart)
                    Assert.True(m.p95Angle < 16f, $"{name}: and nearly everywhere (95%: {m.p95Angle:F1} deg)");
                }
            }
            finally { snap.Restore(); Cleanup("ZzPlus"); }
        }

        /// <summary>
        /// An end closed to a wedge (zero width, some height: a boat's bow) shades like the sides that meet there:
        /// each tip vertex's normal faces the way the one a ring back does, not the other side's.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_WedgeTipShadesLikeItsSides(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var snap = new Snapshot(s);
            float length0 = s.length;
            try
            {
                SpineNode tip = s.nodes[s.nodes.Count - 1];
                tip.sizeH = 0f;
                tip.sizeV = 0.8f;
                s.UpdateShape(true);
                yield return Wait.Frames(2);
                Mesh m = s.PPart.SidesMesh;
                Vector3[] v = m.vertices, nrm = m.normals;
                float top = v.Max(p => p.y);
                int per = v.Count(p => Mathf.Abs(p.y - top) < 1e-4f);
                Assert.True(per > 2 && v.Length % per == 0, $"one ring of {per} at the tip, of {v.Length}");
                int bad = 0;
                var which = new List<string>();
                for (int i = v.Length - per; i < v.Length; i++)
                    if (Vector3.Dot(nrm[i], nrm[i - per]) <= 0f)
                    {
                        bad++;
                        which.Add($"k{i - (v.Length - per)} at {v[i].ToString("F2")} n {nrm[i].ToString("F2")} back {v[i - per].ToString("F2")} n {nrm[i - per].ToString("F2")}");
                    }
                ctx.Record("tip", $"{per} vertices, {bad} facing away from their side");
                ctx.Record("away", string.Join(" | ", which));
                Assert.True(bad == 0, $"every tip normal faces its side's way ({bad} of {per} don't)");

                // The demo's bow: a hull run narrowing (Concave) to a raised, forward-leaning wedge. Each vertex
                // normal of the strip at the tip should lie close to the faces it shades, or the strip's two
                // triangle halves light differently (a sawtooth along the stem).
                var cases = new (string name, string section, float tilt, SpineSlope slope)[]
                {
                    ("ellipse", "Ellipse", 0f, SpineSlope.Linear),
                    ("ellipse lean", "Ellipse", 30f, SpineSlope.Concave),
                    ("rect lean", "Rectangle", 30f, SpineSlope.Concave),
                    ("mk3 lean", "Mk3", 30f, SpineSlope.Concave),
                };
                var fails = new List<string>();
                foreach (var c in cases)
                {
                    s.length = 9f;   // the demo hull's
                    s.nodes.Clear();
                    s.nodes.Add(new SpineNode(0f, 2.5f, 1.25f) { section = c.section, offsetV = 0.1f });
                    s.nodes.Add(new SpineNode(0.72f, 2.6f, 1.6f) { section = c.section, offsetV = 0.1f, slopeAbove = c.slope });
                    s.nodes.Add(new SpineNode(1f, 0f, 1.2f) { section = c.section, offsetV = 0.5f, tiltV = c.tilt });
                    s.UpdateShape(true);
                    yield return Wait.Frames(1);
                    Mesh mm = s.PPart.SidesMesh;
                    Vector3[] vv = mm.vertices, nn = mm.normals;
                    int[] tri = mm.triangles;
                    int stride = tri[1] - tri[0];   // the first quad's (a, next ring's a, b): one ring apart
                    int rings = vv.Length / stride;
                    // across the stem strip: each tip normal is the one a ring back, so no quad creases
                    float strip = 0f;
                    for (int i = vv.Length - stride; i < vv.Length; i++) strip = Mathf.Max(strip, Vector3.Angle(nn[i], nn[i - stride]));
                    // and no triangle turned inside out (a fold: the stem's points doubling back), which faces
                    // against the normals it is lit with. The strip into the stem fans a wide ring onto a line, so
                    // its faces are steep against their normals by construction; a fold is the opposite way round.
                    float against = 0f;
                    string worst = "";
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        int a = tri[i], b = tri[i + 1], d = tri[i + 2];
                        Vector3 f = Vector3.Cross(vv[b] - vv[a], vv[d] - vv[a]);
                        if (f.magnitude < 1e-5f) continue;
                        float ang = Vector3.Angle(nn[a] + nn[b] + nn[d], f);
                        if (ang > against) { against = ang; worst = $"r{a / stride} k{a % stride} face {f.normalized.ToString("F2")} tri {vv[a].ToString("F3")} {vv[b].ToString("F3")} {vv[d].ToString("F3")}"; }
                    }
                    ctx.Record(c.name, $"{rings} rings; stem strip {strip:F1} deg; worst face vs its normals {against:F1} deg at {worst}");
                    if (strip > 1f || against > 120f) fails.Add($"{c.name}: strip {strip:F1}, vs face {against:F1}");
                }
                Assert.True(fails.Count == 0, "stem shades smoothly, no normal against its faces: " + string.Join(", ", fails));
            }
            finally { s.length = length0; snap.Restore(); }
        }

        /// <summary>
        /// A hull built standing up: 47 waterline sections (74 points each) from keel to deck, a shell open on top.
        /// It rebuilds in well under a second (the shell's inner wall was 6 s of crossing tests), and the sections'
        /// ring points stay on their features from node to node (a point sliding round the outline is a shard).
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineHorizontal")]
        public static IEnumerator Editor_ManySectionShellHull(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            s._alignKey = null;
            Vector2[][] al = s.AlignedUnits();
            ctx.Record("align ms", sw.ElapsedMilliseconds.ToString());
            sw.Restart();
            var rings = s.BuildRings();
            ctx.Record("rings ms", $"{sw.ElapsedMilliseconds} ({rings.Count} rings, {s.Sides} sides)");
            sw.Restart(); bool[] hard = ProceduralShapeSpine.ComputeHardColumns(rings); ctx.Record("hard ms", $"{sw.ElapsedMilliseconds} ({hard.Count(h => h)} hard)");
            sw.Restart(); s.BuildInnerRings(rings); ctx.Record("inner ms", sw.ElapsedMilliseconds.ToString());
            sw.Restart(); s.GenerateSideMesh(rings); ctx.Record("side ms", sw.ElapsedMilliseconds.ToString());
            sw.Restart(); s.GenerateCapMesh(rings); ctx.Record("cap ms", sw.ElapsedMilliseconds.ToString());
            sw.Restart(); s.GenerateColliderMesh(rings); ctx.Record("collider ms", sw.ElapsedMilliseconds.ToString());
            sw.Restart(); s.CalculateVolume(rings); ctx.Record("volume ms", sw.ElapsedMilliseconds.ToString());
            long update = long.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                sw.Restart();
                s.UpdateShape(true);
                update = System.Math.Min(update, sw.ElapsedMilliseconds);
                ctx.Record($"update ms {pass}", sw.ElapsedMilliseconds.ToString());
            }
            yield return Wait.Frames(2);
            Mesh m = s.PPart.SidesMesh;
            ctx.Record("side mesh", $"{m.vertexCount} verts, {m.triangles.Length / 3} tris, {m.indexFormat}");
            // past 65535 vertices a 16-bit index wraps and the triangles land on the wrong vertices (a shattered skin)
            Assert.True(m.vertexCount > 65535, $"this hull is past 16-bit indices ({m.vertexCount} vertices)");
            Assert.True(m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32, $"so its mesh indexes with 32 bits ({m.indexFormat})");
            Assert.True(m.triangles.Max() == m.vertexCount - 1, $"and its triangles reach its last vertex ({m.triangles.Max()} of {m.vertexCount})");
            MeshCollider[] cols = s.part.GetComponentsInChildren<MeshCollider>(true);
            ctx.Record("colliders", $"{cols.Length}, {cols.Sum(c => c.sharedMesh != null ? c.sharedMesh.vertexCount : 0)} verts");

            // the aligned rings: how far ring point j moves from one node to the next (unit box), and how far it is
            // from its own section's outline
            Assert.NotNull(al, "custom sections are aligned");
            float jump = 0f;
            int crossings = 0;
            {
                var lines = new List<string>();
                for (int i = 0; i + 1 < al.Length; i++)
                {
                    float worst = 0f; int wj = 0;
                    for (int j = 0; j < al[i].Length; j++)
                    {
                        float d = (al[i][j] - al[i + 1][j]).magnitude;
                        if (d > worst) { worst = d; wj = j; }
                    }
                    // ring points out of order around the outline (a ring crossing itself)
                    int back = 0;
                    Vector2[] r = al[i];
                    for (int j = 0; j < r.Length; j++)
                        for (int k = j + 2; k < r.Length; k++)
                            if ((j != 0 || k != r.Length - 1) && Cross(r[j], r[(j + 1) % r.Length], r[k], r[(k + 1) % r.Length])) back++;
                    lines.Add($"{i}->{i + 1}: max jump {worst:F3} at {wj}, crossings {back}");
                    jump = Mathf.Max(jump, worst);
                    crossings += back;
                }
                ctx.Record("align", string.Join(" | ", lines));
            }
            Assert.True(crossings == 0, $"no aligned ring crosses itself ({crossings})");
            Assert.True(jump < 0.25f, $"ring points keep to their features node to node (worst move {jump:F3} of the unit box)");
            Assert.True(update < 2000, $"rebuilds quickly ({update} ms)");
            yield break;
        }

        /// <summary>Probe: pictures of the many-section hull, to look at its skin (KSP folder, probe/).</summary>
        [UITest(Kind = "Probe", StartsAt = StartState.Sph, Craft = "SpineHorizontal")]
        public static IEnumerator Probe_ManySectionHullShots(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            yield return Wait.Frames(5);
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (mb != null && (mb.GetType().Name == "SPHCamera" || mb.GetType().Name == "VABCamera")) mb.enabled = false;
            Bounds b = s.part.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r is MeshRenderer).Select(r => r.bounds)
                .Aggregate((x, y) => { x.Encapsulate(y); return x; });
            ctx.Record("bounds", b.ToString());
            ctx.Record("spine axis (world)", s.part.transform.up.ToString("F2"));

            // the inner wall, ring by ring: points outside the outer outline, edges turned back, and the wall thickness
            var rings = s.BuildRings();
            var stats = new List<string>();
            int badRings = 0;
            for (int r = 0; r < rings.Count; r++)
            {
                var outer = rings[r];
                var inner = s.InnerRing(outer);
                Vector2[] o = outer.outline, q = inner.outline;
                int n = o.Length, outside = 0, flipped = 0, tiny = 0;
                float minEdge = float.MaxValue, maxMove = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (!ProceduralShapeSpine.InOutline(o, q[j]) && (q[j] - o[j]).sqrMagnitude > 1e-10f) outside++;
                    Vector2 e = o[(j + 1) % n] - o[j], f = q[(j + 1) % n] - q[j];
                    if (Vector2.Dot(e, f) <= 0f && e.sqrMagnitude > 1e-12f) flipped++;
                    minEdge = Mathf.Min(minEdge, e.magnitude);
                    if (e.magnitude < 1e-3f) tiny++;
                    maxMove = Mathf.Max(maxMove, (q[j] - o[j]).magnitude);
                }
                if (outside > 0 || flipped > 0 || r % 40 == 0)
                    stats.Add($"r{r} y{outer.y:F3} rH{outer.rH:F3} rV{outer.rV:F3}: out {outside} flip {flipped} tiny {tiny} minEdge {minEdge * 1000f:F2}mm wall<= {maxMove * 1000f:F1}mm");
                if (outside > 0 || flipped > 0) badRings++;
            }
            ctx.Record("inner bad rings", $"{badRings} of {rings.Count}");
            {
                // normals against the triangles they light, outer skin and inner, and how tall the triangles are
                Mesh m = s.PPart.SidesMesh;
                Vector3[] v = m.vertices, nn = m.normals;
                int[] t = m.triangles;
                int half = v.Length / 2;
                int[] over90 = new int[2], over60 = new int[2], total = new int[2], facesIn = new int[2], normalsIn = new int[2];
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 f = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    if (f.sqrMagnitude < 1e-14f) continue;
                    int skin = t[i] >= half ? 1 : 0;
                    Vector3 ns = nn[t[i]] + nn[t[i + 1]] + nn[t[i + 2]];
                    float ang = Vector3.Angle(ns, f);
                    total[skin]++;
                    if (ang > 60f) over60[skin]++;
                    if (ang > 90f) over90[skin]++;
                    // which way is out: away from the spine axis (local y), sideways
                    Vector3 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    Vector3 radial = new Vector3(c.x, 0f, c.z).normalized * (skin == 0 ? 1f : -1f);
                    if (Vector3.Dot(f.normalized, radial) < -0.3f) facesIn[skin]++;
                    if (Vector3.Dot(ns.normalized, radial) < -0.3f) normalsIn[skin]++;
                }
                ctx.Record("normals vs faces", $"outer {over60[0]}/{total[0]} >60deg, {over90[0]} >90; inner {over60[1]}/{total[1]} >60, {over90[1]} >90");
                ctx.Record("pointing in", $"outer faces {facesIn[0]}, normals {normalsIn[0]}; inner faces {facesIn[1]}, normals {normalsIn[1]}");
                ctx.Record("tri counts", $"{t.Length / 3} tris, {v.Length} verts, index {m.indexFormat}, largest index {t.Max()}");
                var gaps = new List<float>();
                for (int r = 1; r < rings.Count; r++) gaps.Add(rings[r].y - rings[r - 1].y);
                gaps.Sort();
                ctx.Record("ring spacing mm", $"min {gaps[0] * 1000f:F2} median {gaps[gaps.Count / 2] * 1000f:F2} max {gaps[gaps.Count - 1] * 1000f:F2}");
            }
            ctx.Record("inner", string.Join(" | ", stats.Take(60)));

            string dir = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "probe");
            System.IO.Directory.CreateDirectory(dir);
            int shot = 0;
            foreach (var (yaw, pitch) in new[] { (90f, 15f), (0f, 15f), (135f, 50f), (45f, -25f), (0f, 85f), (90f, -85f) })
            {
                Vector3 pos = b.center - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * (1.6f * b.size.magnitude);
                foreach (Camera c in Camera.allCameras)
                {
                    if (c == null || c.name.StartsWith("UI")) continue;
                    if (c.transform.parent != null) c.transform.SetParent(null, true);
                    c.fieldOfView = 35f;
                    c.transform.position = pos;
                    c.transform.LookAt(b.center, Vector3.up);
                }
                yield return Wait.Frames(3);
                string path = System.IO.Path.Combine(dir, $"hull{shot++}.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return Wait.Frames(3);
            }
        }

        private static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float O(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            return O(a, b, c) * O(a, b, d) < 0f && O(c, d, a) * O(c, d, b) < 0f;
        }

        // How far a world point is from the visible skin (the side mesh's triangles).
        private static float SkinDistance(ProceduralShapeSpine s, Vector3 world)
        {
            MeshFilter sides = s.part.GetComponentsInChildren<MeshFilter>(true).First(mf => mf.sharedMesh == s.PPart.SidesMesh);
            Vector3 p = sides.transform.InverseTransformPoint(world);
            Vector3[] v = s.PPart.SidesMesh.vertices;
            int[] t = s.PPart.SidesMesh.triangles;
            float best = float.MaxValue;
            for (int i = 0; i < t.Length; i += 3)
                best = Mathf.Min(best, (ProceduralShapeSpine.ClosestOnTriangle(p, v[t[i]], v[t[i + 1]], v[t[i + 2]], out _, out _) - p).magnitude);
            return best * sides.transform.lossyScale.x;
        }

        // A node field changed as its PAW control changes it.
        private static void Edit(ProceduralShapeSpine s, string field, object value)
        {
            BaseField f = s.Fields[field];
            object old = f.GetValue(s);
            f.SetValue(value, s);
            s.OnNodeFieldChanged(f, old);
        }

        // A whole-part dimension changed as its PAW slider changes it (the Procedural Parts path).
        private static void Dimension(ProceduralShapeSpine s, string field, float value)
        {
            BaseField f = s.Fields[field];
            object old = f.GetValue(s);
            f.SetValue(value, s);
            s.OnShapeDimensionChanged(f, old);
        }

        /// <summary>
        /// A part surface-attached to the Spine stays on its skin through every kind of edit: node width, height,
        /// offsets, tilt, section, slope and position, and the part's length and width scale. And flat on it: it
        /// turns as the face under it turns.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineAttach")]
        public static IEnumerator Editor_SurfaceAttachmentsStayOnTheSkin(TestContext ctx)
        {
            ProceduralShapeSpine s = Spine();
            Part child = s.part.children.FirstOrDefault();
            Assert.NotNull(child, "the barometer is attached to the Spine");
            AttachNode an = child.FindAttachNodeByPart(s.part);
            Assert.True(an != null && an.nodeType == AttachNode.NodeType.Surface, "surface-attached");
            Vector3 At() => an.owner.transform.TransformPoint(an.position);
            // the skin's outward normal at the attach point (the loft as built), and how far the part leans off it
            // (its attach direction points into the body, as the editor sets a part on a face)
            Vector3 SkinNormal() => s.part.transform.TransformDirection(s.SpotOf(s._built, s.part.transform.InverseTransformPoint(At())).normal);
            float Lean() => Vector3.Angle(-an.owner.transform.TransformDirection(an.orientation), SkinNormal());

            // onto the skin, between the Mk2 and Rectangle nodes, round from the side
            MeshFilter sides = s.part.GetComponentsInChildren<MeshFilter>(true).First(mf => mf.sharedMesh == s.PPart.SidesMesh);
            var go = new GameObject("ZzSkinProbe");
            go.transform.SetPositionAndRotation(sides.transform.position, sides.transform.rotation);
            go.transform.localScale = sides.transform.lossyScale;
            var skin = go.AddComponent<MeshCollider>();
            skin.sharedMesh = s.PPart.SidesMesh;
            float y = 0.1f * s.length, th = 40f * Mathf.Deg2Rad, R = 5f;
            Vector3 o = s.part.transform.TransformPoint(new Vector3(R * Mathf.Cos(th), y, R * Mathf.Sin(th)));
            var ray = new Ray(o, (s.part.transform.TransformPoint(new Vector3(0f, y, 0f)) - o).normalized);
            bool onSkin = skin.Raycast(ray, out RaycastHit hit, 2f * R);
            Object.DestroyImmediate(go);
            Assert.True(onSkin, "the probe ray hits the skin");
            child.transform.Translate(hit.point - At(), Space.World);
            // flat on the face there
            child.transform.rotation = Quaternion.FromToRotation(-an.owner.transform.TransformDirection(an.orientation), SkinNormal()) * child.transform.rotation;
            child.transform.Translate(hit.point - At(), Space.World);
            yield return Wait.Frames(2);
            ctx.Record("start", $"{SkinDistance(s, At()):F4} m off, leaning {Lean():F2} deg");

            var steps = new (string name, System.Action act)[]
            {
                ("select Rectangle node", () => s.SelectNode(2)),
                ("width", () => Edit(s, "nodeWidth", 2.4f)),
                ("height", () => Edit(s, "nodeHeight", 1.9f)),
                ("height offset", () => Edit(s, "nodeOffsetV", 0.4f)),
                ("width offset", () => Edit(s, "nodeOffsetH", -0.3f)),
                ("tilt", () => Edit(s, "nodeTiltV", 20f)),
                ("section", () => Edit(s, "nodeProfileOpt", "Ellipse")),
                ("select Mk2 node", () => s.SelectNode(1)),
                ("slope", () => Edit(s, "nodeSlopeOpt", "Concave")),
                ("position", () => Edit(s, "nodePos", 0.3f)),
                ("Mk2 width", () => Edit(s, "nodeWidth", 1.6f)),
                ("length", () => Dimension(s, "length", 5f)),
                ("width scale", () => Dimension(s, "hScale", 1.3f)),
            };
            var fails = new List<string>();
            foreach (var (name, act) in steps)
            {
                Vector3 before = At();
                act();
                yield return Wait.Frames(2);
                float d = SkinDistance(s, At()), lean = Lean();
                ctx.Record(name, $"{d:F4} m off, leaning {lean:F2} deg, moved {(At() - before).magnitude:F3} m");
                if (d > 0.01f) fails.Add($"{name}: {d:F3} m off the skin");
                if (lean > 0.5f) fails.Add($"{name}: leaning {lean:F1} deg off the skin");
            }
            Assert.True(fails.Count == 0, "the barometer stays flat on the skin: " + string.Join("; ", fails));
        }

        /// <summary>
        /// Spine edits are editor undo steps (stock KSP records only part attach / detach / move): each undo goes back
        /// one edit, redo forward again. Repeats of one control on one node are one step (PAW arrow taps, clicks on
        /// through the shapes), and selecting a node is none. Undo replaces the parts, so the Spine is looked up again
        /// after each; the test ends undone to where it started.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = "SpineTest")]
        public static IEnumerator Editor_SpineEditsUndo(TestContext ctx)
        {
            var restore = typeof(EditorLogic).GetMethod("RestoreState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(restore, "EditorLogic.RestoreState");
            string State() => Spine().DumpAll();
            void Pawed(string field, object to)
            {
                ProceduralShapeSpine sp = Spine();
                BaseField f = sp.Fields[field];
                object old = f.GetValue(sp);
                f.SetValue(to, sp);
                f.uiControlEditor.onFieldChanged(f, old);   // as the PAW calls it
            }

            ProceduralShapeSpine s = Spine();
            s.SelectNode(1);
            var states = new List<string> { State() };
            float w = s.nodes[1].sizeH;
            for (int k = 1; k <= 3; k++) Pawed("nodeWidth", w + 0.1f * k);   // three taps: one step
            states.Add(State());
            s.SelectNode(2);                                                    // no step
            s.AddNodeInFrontEvent();
            states.Add(State());
            Spine().CycleProfile(1);
            Spine().CycleProfile(1);                                            // on through the shapes: one step
            states.Add(State());
            Spine().SelectNode(1);
            Pawed("nodeOffsetV", Spine().nodes[1].offsetV + 0.5f);
            states.Add(State());
            Pawed("length", Spine().length + 1f);
            states.Add(State());
            ctx.Record("steps", states.Count - 1);
            yield return Wait.Frames(5);

            var fails = new List<string>();
            for (int i = states.Count - 2; i >= 0; i--)
            {
                restore.Invoke(EditorLogic.fetch, new object[] { -1 });
                yield return Wait.Frames(5);
                if (State() != states[i]) fails.Add($"undo to step {i}: {State()} (want {states[i]})");
            }
            for (int i = 1; i < states.Count; i++)
            {
                restore.Invoke(EditorLogic.fetch, new object[] { 1 });
                yield return Wait.Frames(5);
                if (State() != states[i]) fails.Add($"redo to step {i}: {State()} (want {states[i]})");
            }
            // back to the craft as loaded, for the other tests
            for (int i = 0; i < 2 * states.Count && State() != states[0]; i++)
            {
                restore.Invoke(EditorLogic.fetch, new object[] { -1 });
                yield return Wait.Frames(5);
            }
            Assert.True(fails.Count == 0, string.Join("\n", fails));
            Assert.Equal(states[0], State(), "undone to the start");
        }
    }
}

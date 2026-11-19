using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.UI;
using KSP.UI.Screens;
using KspTest;
using UnityEngine;
using UnityEngine.UI;

namespace CustomProceduralParts.Showcase
{
    /// <summary>
    /// A short showcase video, not a check: a plane body example (showcase/B2Showcase.craft: a Spine centre body of custom
    /// sections, a nacelle pair and B9 wings, built with the body local z up) shown whole, cut down to the centre body,
    /// its cross-section outlines turned on, and once around it. Every rendered frame goes to frames/NNNNN.jpg under
    /// the results folder (or CPP_SHOWCASE_DIR), with captions; assemble at 30 fps:
    /// ffmpeg -framerate 30 -i %05d.jpg -c:v libx264 -pix_fmt yuv420p -crf 18 b2.mp4
    /// Run it: dotnet test KspTests/CustomProceduralParts.Showcase.Run --filter FullyQualifiedName~Showcase_B2Fuselage
    /// </summary>
    public static class B2Showcase
    {
        private const string Craft = "B2Showcase";

        internal static readonly Color Paint = new Color(0.30f, 0.32f, 0.35f);

        [UITest(Kind = "Showcase", Explicit = true, Mods = "B9-PWings-Fork;KerbalFoundriesContinued;TexturesUnlimited",
                StartsAt = StartState.Sph, Craft = Craft, TimeoutSec = 3600)]
        public static IEnumerator Showcase_B2Fuselage(TestContext ctx)
        {
            yield return Wait.Frames(30);
            List<Part> parts = EditorLogic.fetch.ship.parts.ToList();
            Part root = parts[0];
            ProceduralShapeSpine body = root.FindModuleImplementing<ProceduralShapeSpine>();
            Assert.NotNull(body, "the root is the Spine body");
            Assert.True(body.nodes.Count >= 10, $"body nodes ({body.nodes.Count})");
            ctx.Record("parts", parts.Count);
            foreach (Part p in parts)
            {
                p.SetHighlightType(Part.HighlightType.Disabled);
                p.SetHighlight(false, false);
            }
            foreach (Part p in parts) PaintPart(p);
            SpineSettings.Set(SpineHandleVis.None, false, false);   // no handles: outlines only, once on
            Bounds all = Bounds(parts);
            Bounds bb = Bounds(new[] { root });
            ctx.Record("craft", all.ToString());
            ctx.Record("body", bb.ToString());
            float size = all.size.magnitude, bsize = bb.size.magnitude;
            Imgui.EnsurePatched();
            Textures.HoldNow();
            Pointer(false);   // the real mouse mustn't hover the UI (part list tooltips)
            B9PanelOff();
            yield return Wait.Frames(20);

            string framesDir = Environment.GetEnvironmentVariable("CPP_SHOWCASE_DIR") is string sd && sd.Length > 0
                ? Path.Combine(sd, DateTime.Now.ToString("yyyyMMdd-HHmmss")) : Path.Combine(ctx.ResultsDir, "frames");
            ctx.Record("framesDir", framesDir);
            var rec = FrameRecorder.Begin(framesDir);
            try
            {
                // ---- the whole craft ------------------------------------------------------------------------------------
                Orbit(all.center, 205, 26, 0.85f * size, 30);
                yield return FrameRecorder.Card("Airplane Example", 1f, 1f, 60);
                rec.Caption("Plane body example", "");
                yield return FrameRecorder.Card("Airplane Example", 1f, 0f, 18);
                yield return Frames(150, f => Orbit(all.center, 205 + 35 * f, 26, 0.85f * size, 30));

                // ---- down to the centre body: the rest removed, from the tips in --------------------------------------------
                rec.Caption("Plane body example", "The center body is one Spine part");
                List<Part> others = parts.Where(p => p != root)
                    .OrderByDescending(p => Mathf.Abs(p.transform.position.x - root.transform.position.x)).ToList();
                int per = Mathf.Max(1, 90 / Mathf.Max(1, others.Count));
                int k = 0;
                yield return Frames(per * others.Count + 30, f =>
                {
                    Orbit(Vector3.Lerp(all.center, bb.center, Smooth(f)), 240 + 15 * f, Mathf.Lerp(26, 24, f),
                          Mathf.Lerp(0.85f * size, 1.3f * bsize, Smooth(f)), 30);
                    int upTo = Mathf.Min(others.Count, (int)(f * (per * others.Count + 30) / per));
                    for (; k < upTo; k++) Hide(others[k]);
                });
                foreach (Part p in others) Hide(p);

                // ---- cross-section outlines on (the part selected, its PAW hidden), then once around it -----------------
                yield return Paw.Open(root);
                Pointer(false);
                yield return Frames(15, f => Orbit(bb.center, 255, 24, 1.3f * bsize, 30));
                rec.Caption("Plane body example", $"Cross-section outlines: {body.nodes.Count} nodes, each with its own shape");
                SpineSettings.Set(SpineHandleVis.None, false, true);
                yield return Frames(30, f => Orbit(bb.center, 255, 24, 1.3f * bsize, 30));
                yield return Frames(360, f => Orbit(bb.center, 255 + 360 * Smooth(f), 24 - 14 * Mathf.Sin(Mathf.PI * f), 1.3f * bsize, 30));
                yield return Frames(20, f => Orbit(bb.center, 255, 24, 1.3f * bsize, 30));
            }
            finally
            {
                ctx.Record("frames", FrameRecorder.End());
                Pointer(true);
                Textures.Release();
                Paw.Close(root);
                SpineSettings.Set(SpineHandleVis.Active, false, false);
            }
        }

        // The gizmo's node handle (the node's centre, offsets included), as the Spine places it.
        internal static Vector3 NodeHandle(ProceduralShapeSpine s, int i)
        {
            SpineNode n = s.nodes[i];
            return s.part.transform.TransformPoint(new Vector3(s.EoffH(n), (n.position - 0.5f) * s.length, s.Eoff(n)));
        }

        // ---- the parts' look ------------------------------------------------------------------------------------------

        // The B-2's dark grey: B9 surfaces through Textures Unlimited's recoloring, the rest by material.
        internal static void PaintPart(Part p)
        {
            if (p.Modules.Cast<PartModule>().Any(m => m.moduleName == "KSPTextureSwitch") && TuRecolor(p, Paint)) return;
            if (p.FindModuleImplementing<ProceduralShapeSpine>() == null && !p.name.StartsWith("B9")) return;   // gear, probe: as they are
            foreach (Renderer r in p.FindModelComponents<Renderer>())
            {
                if (!(r is MeshRenderer)) continue;
                foreach (Material m in r.materials)
                {
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", Paint);
                }
            }
        }

        internal static bool TuRecolor(Part p, Color c)
        {
            bool any = false;
            foreach (PartModule m in p.Modules)
            {
                if (m.moduleName != "KSPTextureSwitch") continue;
                Type t = m.GetType();
                var names = t.GetMethod("getSectionNames");
                var set = t.GetMethods().FirstOrDefault(x => x.Name == "setSectionColors" && x.GetParameters().Length == 2);
                if (names == null || set == null) continue;
                Type rdT = set.GetParameters()[1].ParameterType.GetElementType();
                var ctor = rdT.GetConstructors().FirstOrDefault(k => k.GetParameters().Length > 0 && k.GetParameters()[0].ParameterType == typeof(Color));
                if (ctor == null) continue;
                object[] args = ctor.GetParameters().Select((q, k) => k == 0 ? (object)c
                    : q.Name.ToLowerInvariant().Contains("spec") ? (object)0.15f
                    : q.Name.ToLowerInvariant().Contains("metal") ? (object)0f
                    : (object)1f).ToArray();
                object rd = ctor.Invoke(args);
                Array arr = Array.CreateInstance(rdT, 3);
                for (int k = 0; k < 3; ++k) arr.SetValue(rd, k);
                foreach (string section in (string[])names.Invoke(m, null))
                {
                    set.Invoke(m, new object[] { section, arr });
                    any = true;
                }
            }
            return any;
        }

        internal static void Hide(Part p)
        {
            foreach (Renderer r in p.FindModelComponents<Renderer>()) r.enabled = false;
        }

        // B9's "press J" panel (it shows whenever B9 is loaded in the editor) off; B9 is only in this test's instance.
        internal static System.Reflection.FieldInfo _b9Active;
        internal static bool _b9Looked;

        internal static void B9PanelOff()
        {
            if (!_b9Looked)
            {
                _b9Looked = true;
                Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("WingProcedural.WingProcedural", false)).FirstOrDefault(x => x != null);
                _b9Active = t?.GetField("uiWindowActive", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            }
            _b9Active?.SetValue(null, false);
        }

        internal static Bounds Bounds(IEnumerable<Part> parts)
        {
            Bounds b = default;
            bool any = false;
            foreach (Part p in parts)
                foreach (Renderer r in p.FindModelComponents<Renderer>())   // its own: child parts hang under it
                {
                    if (!r.enabled || !(r is MeshRenderer)) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            return b;
        }

        // ---- the drawn cursor, the PAW and the IMGUI windows ----------------------------------------------------------

        internal static Vector2 ScreenOf(Vector3 world)
        {
            Camera cam = EditorLogic.fetch.editorCamera ?? Camera.main;
            Vector3 v = cam.WorldToScreenPoint(world);
            return new Vector2(v.x, Screen.height - v.y);
        }

        internal static IEnumerator MoveCursor(Func<Vector2> to, int n)
        {
            FrameRecorder.CursorVisible = true;
            Vector2 from = FrameRecorder.CursorPos;
            for (int i = 1; i <= n; ++i)
            {
                FrameRecorder.CursorPos = Vector2.Lerp(from, to(), Smooth((float)i / n));
                B9PanelOff();
                yield return null;
            }
        }

        // The cursor onto the IMGUI control `which` in window `win` (rects the harness's IMGUI patch records), pressed,
        // `act`, released.
        internal static IEnumerator ClickControl(string win, Func<Imgui.Control, bool> which, Action act)
        {
            Rect? r = null;
            for (int t = 0; t < 60 && r == null; t++)
            {
                r = Imgui.FindWindow(win)?.Controls.FirstOrDefault(which)?.ScreenRect;
                if (r == null) yield return null;
            }
            if (r != null) yield return MoveCursor(() => r.Value.center, 20);
            FrameRecorder.CursorPressed = true;
            yield return Frames(5, null);
            act();
            FrameRecorder.CursorPressed = false;
            yield return Frames(8, null);
        }

        // The cursor onto a PAW button (scrolled into view), pressed, `act` (what the button does), released.
        internal static IEnumerator ClickPawButton(Part part, string label, Action act)
        {
            UIPartActionButton b = Paw.FindButtonItem(part, label);
            if (b != null) ScrollIntoView(part, b);
            yield return Frames(4, null);
            b = Paw.FindButtonItem(part, label);   // the PAW rebuilds its items now and then: the one found above may be gone
            if (b != null)
            {
                var rt = (RectTransform)b.transform;
                Vector3 sp = RectTransformUtility.WorldToScreenPoint(UIMasterController.Instance.uiCamera, rt.TransformPoint(rt.rect.center));
                Vector2 gui = new Vector2(sp.x, Screen.height - sp.y);
                yield return MoveCursor(() => gui, 26);
            }
            FrameRecorder.CursorPressed = true;
            yield return Frames(3, null);
            act();
            FrameRecorder.CursorPressed = false;
            yield return Frames(4, null);
        }

        internal static void ScrollIntoView(Part part, Component item)
        {
            UIPartActionWindow w = Paw.Window(part);
            ScrollRect sr = w != null ? w.GetComponentInChildren<ScrollRect>() : null;
            if (sr == null || sr.content == null || sr.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            var rt = (RectTransform)item.transform;
            float contentH = sr.content.rect.height, viewH = sr.viewport.rect.height;
            float below = -sr.content.InverseTransformPoint(rt.TransformPoint(rt.rect.center)).y;
            if (contentH > viewH) sr.verticalNormalizedPosition = 1f - Mathf.Clamp01((below - 0.4f * viewH) / (contentH - viewH));
            Canvas.ForceUpdateCanvases();
        }

        // The part's PAW with its top left at a GUI point (y down).
        internal static void MovePaw(Part part, Vector2 topLeftGui)
        {
            UIPartActionWindow w = Paw.Window(part);
            if (w == null) return;
            var rt = (RectTransform)w.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);   // bottom-left, top-left, top-right, bottom-right
            Camera ui = UIMasterController.Instance.uiCamera;
            Vector3 cur = RectTransformUtility.WorldToScreenPoint(ui, corners[1]);
            Vector2 want = new Vector2(topLeftGui.x, Screen.height - topLeftGui.y);
            RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)rt.parent, want, ui, out Vector3 wWant);
            RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)rt.parent, cur, ui, out Vector3 wCur);
            rt.position += wWant - wCur;
        }

        // Unit-box point -> GUI point on screen.
        internal static Vector2 OnPlot(CrossSectionEditor ed, Vector2 u) => ed._win.position + ed.ToPx(u);

        // Press on vertex i, sweep it from -> to (the cursor on it; the editor's own move, mirroring included), release.
        internal static IEnumerator Drag(CrossSectionEditor ed, int i, Vector2 from, Vector2 to, int n)
        {
            yield return MoveCursor(() => OnPlot(ed, from), 18);
            FrameRecorder.CursorPressed = true;
            yield return Frames(5, null);
            ed._drag = i;
            for (int k = 1; k <= n; k++)
            {
                Vector2 raw = Vector2.Lerp(from, to, Smooth((float)k / n));
                FrameRecorder.CursorPos = OnPlot(ed, raw);
                ed.MoveVertex(i, ed.Place(raw));
                yield return null;
            }
            ed._drag = -1;
            FrameRecorder.CursorPressed = false;
            yield return Frames(6, null);
        }

        // ---- camera --------------------------------------------------------------------------------------------------

        // Every UI raycaster on or off: off, a scripted take ignores the real mouse.
        internal static void Pointer(bool on)
        {
            foreach (var r in UnityEngine.Object.FindObjectsOfType<UnityEngine.EventSystems.BaseRaycaster>()) r.enabled = on;
        }

        internal static float Smooth(float t) => t * t * (3 - 2 * t);

        internal static IEnumerator Frames(int n, Action<float> each)
        {
            for (int i = 0; i < n; ++i)
            {
                each?.Invoke(n > 1 ? (float)i / (n - 1) : 1f);
                B9PanelOff();
                yield return null;
            }
        }

        internal static bool camerasFreed;

        // Every scene camera at `target` from `dist` m, heading `yaw` (0: from behind, looking along +z), `pitch` deg
        // above; `shift` slides the view so the target sits that share of the screen width right of centre.
        internal static void Orbit(Vector3 target, float yaw, float pitch, float dist, float fov, float shift = 0f)
        {
            if (!camerasFreed)
            {
                foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                    if (mb != null && (mb.GetType().Name == "SPHCamera" || mb.GetType().Name == "VABCamera"))
                        mb.enabled = false;
                camerasFreed = true;
            }
            Vector3 pos = target - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * dist;
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || c.name.StartsWith("UI")) continue;
                if (c.transform.parent != null) c.transform.SetParent(null, true);
                c.fieldOfView = fov;
                c.transform.position = pos;
                c.transform.LookAt(target, Vector3.up);
                float halfW = dist * Mathf.Tan(0.5f * fov * Mathf.Deg2Rad) * c.aspect;
                c.transform.position -= c.transform.right * (shift * 2f * halfW);
            }
        }
    }

    /// <summary>Saves every rendered frame (with a caption drawn over it, and a drawn cursor) as a JPEG.</summary>
    public class FrameRecorder : MonoBehaviour
    {
        private static FrameRecorder instance;
        private string dir, title = "", line = "";
        private int frame;
        private GUIStyle titleStyle, lineStyle;

        /// <summary>KSP's own UI (the PAW, the editor's panels) hidden: true for camera work, false while it's used.</summary>
        public bool HideUi = true;
        private bool uiHidden;

        public static FrameRecorder Begin(string dir)
        {
            End();
            Directory.CreateDirectory(dir);
            var go = new GameObject("CppShowcase.FrameRecorder");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FrameRecorder>();
            instance.dir = dir;
            TestRunner.HideOverlay = true;
            SpineTips.PointerOverride = TipsOff;   // the real mouse shows no tooltips; Hover moves the pointer
            GameEvents.onHideUI.Fire();
            instance.uiHidden = true;
            instance.StartCoroutine(instance.Capture());
            return instance;
        }

        public static int End()
        {
            if (instance == null) return 0;
            int n = instance.frame;
            CursorVisible = CursorPressed = Paused = false;
            CardAlpha = 0f;
            TestRunner.HideOverlay = false;
            SpineTips.PointerOverride = null;
            GameEvents.onShowUI.Fire();
            Destroy(instance.gameObject);
            instance = null;
            return n;
        }

        public void Caption(string t, string l) { title = t; line = l; }

        /// <summary>Where the Spine windows' tooltips think the pointer is while nothing is hovered: nowhere.</summary>
        public static readonly Vector2 TipsOff = new Vector2(-10000f, -10000f);

        // A full-screen title card over the frame, at `CardAlpha` (0 hidden, 1 opaque); the caption bar hidden under it.
        public static string CardText;
        public static float CardAlpha;
        private GUIStyle cardStyle;

        /// <summary>The card faded from `a0` to `a1` over n frames (n 0: set at once), `each` called every frame.</summary>
        public static IEnumerator Card(string text, float a0, float a1, int n, Action<float> each = null)
        {
            CardText = text;
            for (int i = 0; i < n; ++i)
            {
                float f = n > 1 ? (float)i / (n - 1) : 1f;
                CardAlpha = Mathf.Lerp(a0, a1, B2Showcase.Smooth(f));
                each?.Invoke(f);
                B2Showcase.B9PanelOff();
                yield return null;
            }
            CardAlpha = a1;
        }

        /// <summary>No frames saved while set (a cut: set-up the video shouldn't show).</summary>
        public static bool Paused;

        private IEnumerator Capture()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                // the editor puts its UI back now and then: keep it hidden while HideUi
                if (HideUi && (!uiHidden || frame % 10 == 0)) { GameEvents.onHideUI.Fire(); uiHidden = true; }
                else if (!HideUi && uiHidden) { GameEvents.onShowUI.Fire(); uiHidden = false; }
                yield return eof;
                if (Paused) continue;
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (CursorVisible) DrawCursor(tex);
                File.WriteAllBytes(Path.Combine(dir, frame.ToString("D5") + ".jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
                ++frame;
            }
        }

        public static Vector2 CursorPos;
        public static bool CursorVisible, CursorPressed;

        private static Color[] arrowPix, ringPix;
        private static int arrowW, arrowH;
        private const int RingSize = 60, ArrowPad = 3;

        private static void BuildCursor()
        {
            const float s = 1.7f;
            Vector2[] poly = { new Vector2(0, 0), new Vector2(0, 21), new Vector2(5, 16), new Vector2(9, 25), new Vector2(12, 24), new Vector2(8, 15), new Vector2(15, 15) };
            for (int i = 0; i < poly.Length; ++i) poly[i] = poly[i] * s + new Vector2(ArrowPad, ArrowPad);
            arrowW = Mathf.CeilToInt(15 * s) + 2 * ArrowPad;
            arrowH = Mathf.CeilToInt(25 * s) + 2 * ArrowPad;
            arrowPix = new Color[arrowW * arrowH];
            for (int y = 0; y < arrowH; ++y)
                for (int x = 0; x < arrowW; ++x)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = false;
                    float d = float.MaxValue;
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    {
                        Vector2 a = poly[i], b = poly[j];
                        if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                        Vector2 ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                        d = Mathf.Min(d, (a + t * ab - p).magnitude);
                    }
                    Color c = Color.clear;
                    if (inside && d > 1.6f) c = Color.white;
                    else if (inside || d < 1.2f) c = Color.black;
                    arrowPix[(arrowH - 1 - y) * arrowW + x] = c;
                }
            ringPix = new Color[RingSize * RingSize];
            var ring = new Color(1f, 0.72f, 0.15f, 0.95f);
            for (int y = 0; y < RingSize; ++y)
                for (int x = 0; x < RingSize; ++x)
                {
                    float r = new Vector2(x + 0.5f - RingSize / 2f, y + 0.5f - RingSize / 2f).magnitude;
                    ringPix[y * RingSize + x] = r <= 26 && r >= 21 ? ring : r < 21 ? new Color(ring.r, ring.g, ring.b, 0.28f) : Color.clear;
                }
        }

        private static void DrawCursor(Texture2D tex)
        {
            if (arrowPix == null) BuildCursor();
            int x = Mathf.RoundToInt(CursorPos.x), y = Mathf.RoundToInt(CursorPos.y);
            if (CursorPressed) Stamp(tex, ringPix, RingSize, RingSize, x - RingSize / 2, y - RingSize / 2);
            Stamp(tex, arrowPix, arrowW, arrowH, x - ArrowPad, y - ArrowPad);
            tex.Apply(false);
        }

        private static void Stamp(Texture2D tex, Color[] src, int w, int h, int left, int top)
        {
            int x0 = left, y0 = tex.height - top - h;
            int cx0 = Mathf.Max(0, x0), cy0 = Mathf.Max(0, y0);
            int cx1 = Mathf.Min(tex.width, x0 + w), cy1 = Mathf.Min(tex.height, y0 + h);
            if (cx1 <= cx0 || cy1 <= cy0) return;
            int bw = cx1 - cx0, bh = cy1 - cy0;
            Color[] dst = tex.GetPixels(cx0, cy0, bw, bh);
            for (int yy = 0; yy < bh; ++yy)
                for (int xx = 0; xx < bw; ++xx)
                {
                    Color s = src[(yy + cy0 - y0) * w + (xx + cx0 - x0)];
                    if (s.a <= 0f) continue;
                    int k = yy * bw + xx;
                    Color o = Color.Lerp(dst[k], s, s.a);
                    o.a = 1f;
                    dst[k] = o;
                }
            tex.SetPixels(cx0, cy0, bw, bh, dst);
        }

        private void OnGUI()
        {
            GUI.depth = -100;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
                lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, wordWrap = true, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.92f, 0.92f, 0.92f) } };
            }
            if (CardAlpha >= 0.999f) { DrawCard(); return; }
            if (!string.IsNullOrEmpty(title)) DrawCaption();
            if (CardAlpha > 0f) DrawCard();
        }

        private void DrawCard()
        {
            if (cardStyle == null)
                cardStyle = new GUIStyle(GUI.skin.label) { fontSize = 84, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            Color old = GUI.color;
            GUI.color = new Color(0.07f, 0.08f, 0.09f, CardAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, CardAlpha);
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), CardText ?? "", cardStyle);
            GUI.color = old;
        }

        private void DrawCaption()
        {
            // across the top, centred
            float w = Screen.width;
            var box = new Rect(0, 0, w, string.IsNullOrEmpty(line) ? 76 : 120);
            Color old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(60, 10, w - 120, 56), title, titleStyle);
            if (!string.IsNullOrEmpty(line)) GUI.Label(new Rect(60, 66, w - 120, 50), line, lineStyle);
        }
    }
}

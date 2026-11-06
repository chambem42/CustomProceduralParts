using System.Collections.Generic;
using UnityEngine;

namespace CustomProceduralParts
{
    // "Edit Cross Sections" dialog (editor only). Pick a section from the dropdown, drag its vertices on
    // the grid, then Save (customs) or Save As (any, including a modified built-in). Saved sections go
    // to the library and appear in every Spine node's Section list.
    public class CrossSectionEditor : MonoBehaviour
    {
        private static CrossSectionEditor _instance;

        // The Spine gizmo ignores the mouse while it's over this window.
        public static bool MouseOverWindow => _instance != null && _instance._mouseOver;

        public static void Open(string section)
        {
            if (_instance == null)
                _instance = new GameObject("SpineCrossSectionEditor").AddComponent<CrossSectionEditor>();
            _instance.Select(section);
        }

        private const string LockId = "SpineCrossSectionEditor";
        private const float GridPx = 400f;
        private const float ViewHalf = 1.15f;       // unit box is [-1,1]; a margin around it
        private const float HitPx = 8f;
        private static readonly float[] GridSteps = { 0.5f, 0.25f, 0.2f, 0.125f, 0.1f, 0.0625f, 0.05f, 0.025f };

        private Rect _win = new Rect(320f, 90f, 440f, 10f);
        private readonly int _winId = "SpineCrossSectionEditor".GetHashCode();
        private bool _mouseOver;
        private bool _locked;

        private string _current;
        private List<Vector2> _verts = new List<Vector2>();
        private bool _dirty;
        private bool _dropOpen;
        private Vector2 _dropScroll;
        private bool _snap = true;
        private int _gridIdx = 3;
        private int _drag = -1;
        private string _saveAsName = "";
        private string _status = "";
        private bool _statusErr;
        private string _confirm;   // armed "Delete" / "Overwrite:<name>" awaiting a second click

        private Rect _gridRect;

        private void Select(string name)
        {
            CrossSection cs = CrossSectionLibrary.Resolve(name);
            _current = cs.name;
            _verts = cs.EditableVerts();
            _dirty = false;
            _drag = -1;
            _confirm = null;
            _saveAsName = cs.IsBuiltIn ? cs.name + " custom" : cs.name;
            SetStatus(cs.IsBuiltIn ? "Built-in: edit, then Save As a new name." : "", false);
        }

        private void SetStatus(string s, bool err) { _status = s; _statusErr = err; }

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor) { Destroy(gameObject); return; }
            bool want = _mouseOver || _drag >= 0;
            if (want && !_locked) { InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK | ControlTypes.CAMERACONTROLS, LockId); _locked = true; }
            else if (!want && _locked) { InputLockManager.RemoveControlLock(LockId); _locked = false; }
        }

        private void OnDestroy()
        {
            if (_locked) InputLockManager.RemoveControlLock(LockId);
            if (_tex != null) Destroy(_tex);
            if (_instance == this) _instance = null;
        }

        private void OnGUI()
        {
            GUI.skin = HighLogic.Skin;
            _win = GUILayout.Window(_winId, _win, DrawWindow, "Spine Cross Sections", GUILayout.Width(440f));
            if (Event.current.type == EventType.Repaint || Event.current.isMouse)
                _mouseOver = _win.Contains(Event.current.mousePosition);
        }

        private void DrawWindow(int id)
        {
            CrossSection cs = CrossSectionLibrary.Get(_current);
            bool builtIn = cs == null || cs.IsBuiltIn;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Section:", GUILayout.Width(60f));
            string label = _current + (builtIn ? "  (built-in)" : "") + (_dirty ? "  *" : "") + "   ▾";
            if (GUILayout.Button(label)) _dropOpen = !_dropOpen;
            GUILayout.EndHorizontal();

            if (_dropOpen)
            {
                _dropScroll = GUILayout.BeginScrollView(_dropScroll, GUILayout.Height(140f));
                foreach (CrossSection s in CrossSectionLibrary.All)
                {
                    if (GUILayout.Button(s.name + (s.IsBuiltIn ? "  (built-in)" : "")))
                    {
                        Select(s.name);   // discards unsaved edits
                        _dropOpen = false;
                    }
                }
                GUILayout.EndScrollView();
            }

            _gridRect = GUILayoutUtility.GetRect(GridPx, GridPx, GUILayout.ExpandWidth(false));
            HandleGridInput();
            if (Event.current.type == EventType.Repaint) DrawGrid();

            GUILayout.BeginHorizontal();
            _snap = GUILayout.Toggle(_snap, " Snap to grid");
            GUILayout.FlexibleSpace();
            GUILayout.Label("Grid");
            if (GUILayout.Button("-", GUILayout.Width(26f))) _gridIdx = Mathf.Max(0, _gridIdx - 1);
            GUILayout.Label(GridSteps[_gridIdx].ToString("0.####"), GUILayout.Width(48f));
            if (GUILayout.Button("+", GUILayout.Width(26f))) _gridIdx = Mathf.Min(GridSteps.Length - 1, _gridIdx + 1);
            GUILayout.EndHorizontal();

            GUILayout.Label($"Drag vertices. Double-click an edge to add one, right-click a vertex to remove it. " +
                            $"{_verts.Count}/{ProceduralShapeSpine.Sides} vertices. Box = node width x height.");

            GUILayout.BeginHorizontal();
            GUI.enabled = !builtIn && _dirty;
            if (GUILayout.Button("Save")) SaveTo(_current, overwriteOk: true);
            GUI.enabled = _dirty;
            if (GUILayout.Button("Revert")) Select(_current);
            GUI.enabled = !builtIn;
            if (GUILayout.Button(_confirm == "Delete" ? "Really delete?" : "Delete"))
            {
                if (_confirm == "Delete")
                {
                    CrossSectionLibrary.Delete(_current);
                    string gone = _current;
                    Select(nameof(SpineProfile.Ellipse));
                    SetStatus($"Deleted \"{gone}\". Nodes still using it show as Ellipse.", false);
                }
                else { _confirm = "Delete"; SetStatus("Click again to delete.", false); }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Save as:", GUILayout.Width(60f));
            _saveAsName = GUILayout.TextField(_saveAsName ?? "");
            if (GUILayout.Button("Save As", GUILayout.Width(80f))) SaveTo(_saveAsName, overwriteOk: false);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
            {
                Color c = GUI.contentColor;
                if (_statusErr) GUI.contentColor = new Color(1f, 0.5f, 0.4f);
                GUILayout.Label(_status);
                GUI.contentColor = c;
            }

            if (GUILayout.Button("Close")) Destroy(gameObject);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void SaveTo(string name, bool overwriteOk)
        {
            name = name?.Trim();
            string nameErr = CrossSectionLibrary.ValidateName(name);
            if (nameErr != null) { SetStatus(nameErr, true); return; }
            string geomErr = SectionGeometry.Validate(_verts);
            if (geomErr != null) { SetStatus(geomErr, true); return; }
            if (!overwriteOk && name != _current && CrossSectionLibrary.Get(name) != null && _confirm != "Overwrite:" + name)
            {
                _confirm = "Overwrite:" + name;
                SetStatus($"\"{name}\" exists. Click Save As again to overwrite it.", false);
                return;
            }
            CrossSectionLibrary.Save(name, SectionGeometry.Normalize(_verts));
            Select(name);
            SetStatus($"Saved \"{name}\" (fitted to the box). Pick it in a node's Section list.", false);
        }

        // --- grid ---

        private float Scale => _gridRect.width / (2f * ViewHalf);

        private Vector2 ToPx(Vector2 u) =>
            new Vector2(_gridRect.x + (u.x + ViewHalf) * Scale, _gridRect.y + (ViewHalf - u.y) * Scale);

        private Vector2 ToUnit(Vector2 px) =>
            new Vector2((px.x - _gridRect.x) / Scale - ViewHalf, ViewHalf - (px.y - _gridRect.y) / Scale);

        private Vector2 Place(Vector2 u)
        {
            if (_snap)
            {
                float s = GridSteps[_gridIdx];
                u = new Vector2(Mathf.Round(u.x / s) * s, Mathf.Round(u.y / s) * s);
            }
            return new Vector2(Mathf.Clamp(u.x, -1f, 1f), Mathf.Clamp(u.y, -1f, 1f));
        }

        private int HitVertex(Vector2 px)
        {
            int best = -1;
            float bd = HitPx;
            for (int i = 0; i < _verts.Count; i++)
            {
                float d = Vector2.Distance(ToPx(_verts[i]), px);
                if (d <= bd) { bd = d; best = i; }
            }
            return best;
        }

        // Nearest edge within HitPx: the index of its first vertex, and where on it the mouse projects.
        private int HitEdge(Vector2 px, out Vector2 at)
        {
            int best = -1;
            float bd = HitPx;
            at = default;
            for (int i = 0; i < _verts.Count; i++)
            {
                Vector2 a = ToPx(_verts[i]), b = ToPx(_verts[(i + 1) % _verts.Count]);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(px - a, ab) / ab.sqrMagnitude) : 0f;
                Vector2 q = a + t * ab;
                float d = Vector2.Distance(q, px);
                if (d <= bd) { bd = d; best = i; at = q; }
            }
            return best;
        }

        private void HandleGridInput()
        {
            int ctl = GUIUtility.GetControlID(FocusType.Passive, _gridRect);
            Event e = Event.current;
            switch (e.GetTypeForControl(ctl))
            {
                case EventType.MouseDown:
                    if (!_gridRect.Contains(e.mousePosition)) return;
                    int v = HitVertex(e.mousePosition);
                    if (e.button == 0)
                    {
                        if (v >= 0) { _drag = v; GUIUtility.hotControl = ctl; }
                        else if (e.clickCount == 2)
                        {
                            int edge = HitEdge(e.mousePosition, out Vector2 at);
                            if (edge >= 0 && _verts.Count < ProceduralShapeSpine.Sides)
                            {
                                _verts.Insert(edge + 1, Place(ToUnit(at)));
                                MarkDirty();
                            }
                            else if (edge >= 0) SetStatus($"At most {ProceduralShapeSpine.Sides} vertices.", true);
                        }
                        e.Use();
                    }
                    else if (e.button == 1 && v >= 0)
                    {
                        if (_verts.Count > 3) { _verts.RemoveAt(v); MarkDirty(); }
                        else SetStatus("A section needs at least 3 vertices.", true);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != ctl || _drag < 0) return;
                    Vector2 p = Place(ToUnit(e.mousePosition));
                    if (p != _verts[_drag]) { _verts[_drag] = p; MarkDirty(); }
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != ctl) return;
                    GUIUtility.hotControl = 0;
                    _drag = -1;
                    string err = SectionGeometry.Validate(_verts);
                    if (err != null) SetStatus(err, true);
                    else if (_statusErr) SetStatus("", false);
                    e.Use();
                    break;
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            _confirm = null;
        }

        // Grid, box and outline are rasterized into a texture (redrawn only when they change): IMGUI
        // clips a rotated line's rect before rotating it, so rotated-quad lines vanish near the window
        // edge. Vertex handles are axis-aligned rects and are drawn live.
        private Texture2D _tex;
        private Color32[] _px;
        private string _texKey;
        private const int TexSize = (int)GridPx;

        private void DrawGrid()
        {
            bool valid = SectionGeometry.Validate(_verts) == null;
            Color edge = valid ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(1f, 0.35f, 0.3f, 1f);
            string key = _gridIdx + "|" + valid + "|" + SectionGeometry.Format(_verts);
            if (_tex == null || key != _texKey)
            {
                RenderGrid(edge);
                _texKey = key;
            }
            GUI.DrawTexture(_gridRect, _tex);

            int hover = _drag >= 0 ? _drag : HitVertex(Event.current.mousePosition);
            for (int i = 0; i < _verts.Count; i++)
            {
                Vector2 c = ToPx(_verts[i]);
                float r = (i == hover) ? 5f : 3.5f;
                Color col = (i == hover) ? Color.white : (i == 0 ? new Color(0.4f, 1f, 0.5f) : edge);
                GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, col, 0f, 0f);
            }
            if (hover >= 0)
            {
                Vector2 c = ToPx(_verts[hover]);
                GUI.Label(new Rect(c.x + 8f, c.y - 22f, 140f, 20f), SectionGeometry.Format(_verts[hover]));
            }
        }

        private void RenderGrid(Color edge)
        {
            if (_tex == null)
            {
                _tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _px = new Color32[TexSize * TexSize];
            }
            var bg = new Color32(20, 23, 28, 255);
            for (int i = 0; i < _px.Length; i++) _px[i] = bg;

            float step = GridSteps[_gridIdx];
            if (step * TexScale >= 5f)
            {
                int n = Mathf.FloorToInt(ViewHalf / step);
                for (int k = -n; k <= n; k++)
                {
                    TexLine(new Vector2(k * step, -ViewHalf), new Vector2(k * step, ViewHalf), new Color(1f, 1f, 1f, 0.10f), 1);
                    TexLine(new Vector2(-ViewHalf, k * step), new Vector2(ViewHalf, k * step), new Color(1f, 1f, 1f, 0.10f), 1);
                }
            }
            TexLine(new Vector2(0f, -ViewHalf), new Vector2(0f, ViewHalf), new Color(1f, 1f, 1f, 0.28f), 1);
            TexLine(new Vector2(-ViewHalf, 0f), new Vector2(ViewHalf, 0f), new Color(1f, 1f, 1f, 0.28f), 1);
            Vector2[] box = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            for (int i = 0; i < 4; i++) TexLine(box[i], box[(i + 1) % 4], new Color(0.4f, 0.8f, 1f, 0.6f), 1);
            for (int i = 0; i < _verts.Count; i++) TexLine(_verts[i], _verts[(i + 1) % _verts.Count], edge, 2);

            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        private static float TexScale => TexSize / (2f * ViewHalf);

        // Unit-space segment into the texture (row 0 = bottom, so +y is up as in unit space), alpha-blended.
        private void TexLine(Vector2 a, Vector2 b, Color col, int width)
        {
            Vector2 pa = (a + new Vector2(ViewHalf, ViewHalf)) * TexScale, pb = (b + new Vector2(ViewHalf, ViewHalf)) * TexScale;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(pb.x - pa.x), Mathf.Abs(pb.y - pa.y))));
            int lastX = int.MinValue, lastY = int.MinValue;
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(pa, pb, (float)s / steps);
                int x0 = Mathf.FloorToInt(p.x - 0.5f * (width - 1)), y0 = Mathf.FloorToInt(p.y - 0.5f * (width - 1));
                if (x0 == lastX && y0 == lastY) continue;
                lastX = x0; lastY = y0;
                for (int dy = 0; dy < width; dy++)
                    for (int dx = 0; dx < width; dx++) Blend(x0 + dx, y0 + dy, col);
            }
        }

        private void Blend(int x, int y, Color col)
        {
            if (x < 0 || y < 0 || x >= TexSize || y >= TexSize) return;
            int i = y * TexSize + x;
            Color d = _px[i];
            _px[i] = Color.Lerp(d, new Color(col.r, col.g, col.b, 1f), col.a);
        }
    }
}

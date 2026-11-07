using System.Collections.Generic;
using UnityEngine;

namespace CustomProceduralParts
{
    // "Edit Cross Sections" dialog (editor only). Pick a section from the dropdown (built-ins, the shared
    // list, and the craft's own sections), drag its vertices on the grid, then Save or Save As. Save As goes
    // to the shared list (every craft's Section lists); Save on a craft section updates the craft's copy.
    public class CrossSectionEditor : MonoBehaviour
    {
        private static CrossSectionEditor _instance;

        // The Spine gizmo ignores the mouse while it's over this window.
        public static bool MouseOverWindow => _instance != null && _instance._mouseOver;

        // `craft`: the section is the craft's own copy (a node's pinned one), not the shared list's.
        public static void Open(CrossSection section, bool craft)
        {
            if (_instance == null)
                _instance = new GameObject("SpineCrossSectionEditor").AddComponent<CrossSectionEditor>();
            _instance.Select(section, craft);
        }

        // A Spine's selected node (or its section) changed: show that section, unless the open one has
        // unsaved edits (those stay until saved or reverted).
        public static void FollowNode(CrossSection section, bool craft)
        {
            if (_instance == null || _instance._dirty) return;
            if (section == _instance._cs) return;
            _instance.Select(section, craft);
            _instance._dropOpen = false;
        }

        // Which sources the dropdown lists (kept between openings).
        private static bool _showBuiltIn = true, _showShared = true, _showCraft = true;

        private const string LockId = "SpineCrossSectionEditor";
        private const float MinGridPx = 200f, MaxGridPx = 1200f;
        private const float ViewHalf = 1.15f;       // unit box is [-1,1]; a margin around it
        private const float HitPx = 8f;
        private static readonly float[] GridSteps = { 0.5f, 0.25f, 0.2f, 0.125f, 0.1f, 0.0625f, 0.05f, 0.025f };

        private Rect _win = new Rect(320f, 90f, 440f, 10f);
        private readonly int _winId = "SpineCrossSectionEditor".GetHashCode();
        private bool _mouseOver;
        private bool _locked;
        private bool _typing;   // the Save-as field has keyboard focus
        private const string SaveAsControl = "SpineSaveAsName";

        private string _current;
        private CrossSection _cs;   // the section opened (_current is its name)
        private bool _craft;        // ... the craft's own copy, not the shared list's
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

        // Kept between openings. The grid stretches freely: tracing a part on screen means fitting the box to
        // its cross-section, whatever its aspect (the saved section is fitted to the box either way).
        private static float _gridW = 400f, _gridH = 400f;
        private static float _bgAlpha = 1f;   // the plot's background; low to see the scene behind it
        private bool _resizing;

        // Mirror editing (SectionSymmetry): the master chain, and per polygon vertex its chain index and flips.
        private bool _mirrorLR, _mirrorTB;
        private List<Vector2> _chain;
        private readonly List<int> _mapM = new List<int>(), _mapF = new List<int>();
        private bool Mirroring => _chain != null;

        private void Remirror() => _verts = SectionSymmetry.Full(_chain, _mirrorLR, _mirrorTB, _mapM, _mapF);

        // Turning a mirror on cuts the section at its line and replaces the far side with the near side's image
        // (Revert brings it back); turning both off leaves the polygon as it is.
        internal void SetMirror(bool lr, bool tb)
        {
            if (lr == _mirrorLR && tb == _mirrorTB) return;
            _drag = -1;
            if (!lr && !tb)
            {
                _mirrorLR = _mirrorTB = false;
                _chain = null;
                return;
            }
            List<Vector2> c = SectionSymmetry.Chain(_verts, lr, tb, SectionSymmetry.MaxChain(lr, tb, ProceduralShapeSpine.MaxSectionVerts));
            if (c == null)
            {
                SetStatus("To mirror it, the section has to reach across the mirror line.", true);
                return;
            }
            _mirrorLR = lr;
            _mirrorTB = tb;
            _chain = c;
            string before = SectionGeometry.Format(_verts);
            Remirror();
            if (SectionGeometry.Format(_verts) != before) MarkDirty();
        }

        // Vertex edits; with a mirror on they edit the master point (a mirrored vertex's own) and every image follows.
        internal void MoveVertex(int i, Vector2 p)
        {
            if (Mirroring)
            {
                int m = _mapM[i];
                Vector2 q = SectionSymmetry.Constrain(SectionSymmetry.Flip(p, _mapF[i]), m, _chain.Count - 1, _mirrorLR, _mirrorTB);
                if (q == _chain[m]) return;
                _chain[m] = q;
                Remirror();
            }
            else
            {
                if (p == _verts[i]) return;
                _verts[i] = p;
            }
            MarkDirty();
        }

        internal bool InsertOnEdge(int i, Vector2 p)
        {
            if (Mirroring)
            {
                if (_chain.Count >= SectionSymmetry.MaxChain(_mirrorLR, _mirrorTB, ProceduralShapeSpine.MaxSectionVerts)) return false;
                int j = (i + 1) % _verts.Count, n = _chain.Count - 1;
                int ma = _mapM[i], mb = _mapM[j];
                if (Mathf.Abs(ma - mb) != 1) return false;
                // an end point (on a mirror line) belongs to both images: the edge's other end says which
                int f = ma == 0 || ma == n ? _mapF[j] : _mapF[i];
                int at = Mathf.Max(ma, mb);
                _chain.Insert(at, SectionSymmetry.Constrain(SectionSymmetry.Flip(p, f), at, n + 1, _mirrorLR, _mirrorTB));
                Remirror();
            }
            else
            {
                if (_verts.Count >= ProceduralShapeSpine.MaxSectionVerts) return false;
                _verts.Insert(i + 1, p);
            }
            MarkDirty();
            return true;
        }

        internal bool DeleteVertex(int i)
        {
            if (Mirroring)
            {
                if (_chain.Count <= SectionSymmetry.MinChain(_mirrorLR, _mirrorTB)) return false;
                _chain.RemoveAt(_mapM[i]);
                int n = _chain.Count - 1;   // a new end goes onto its mirror line
                _chain[0] = SectionSymmetry.Constrain(_chain[0], 0, n, _mirrorLR, _mirrorTB);
                _chain[n] = SectionSymmetry.Constrain(_chain[n], n, n, _mirrorLR, _mirrorTB);
                Remirror();
            }
            else
            {
                if (_verts.Count <= 3) return false;
                _verts.RemoveAt(i);
            }
            MarkDirty();
            return true;
        }

        private void Select(string name)
            => Select(CrossSectionLibrary.Resolve(name), false);

        private void Select(CrossSection cs, bool craft)
        {
            _cs = cs;
            _craft = craft && !cs.IsBuiltIn;
            _current = cs.name;
            _verts = cs.EditableVerts();
            _mirrorLR = _mirrorTB = false;
            _chain = null;
            _dirty = false;
            _drag = -1;
            _confirm = null;
            _saveAsName = cs.IsBuiltIn ? cs.name + " custom" : cs.name;
            SetStatus(cs.IsBuiltIn ? "Built-in: edit, then Save As a new name."
                      : _craft ? "This craft's own section: Save updates the craft, Save As adds it to your shared sections." : "", false);
        }

        private void SetStatus(string s, bool err) { _status = s; _statusErr = err; }

        private const float DropHeight = 440f;
        private List<(CrossSection, bool)> _dropList;

        // The dropdown's entries for the ticked sources: built-ins, the craft's own sections, the shared ones.
        private static List<(CrossSection, bool)> DropList()
        {
            var o = new List<(CrossSection, bool)>();
            IList<CrossSection> lib = CrossSectionLibrary.All;
            if (_showBuiltIn) foreach (CrossSection s in lib) if (s.IsBuiltIn) o.Add((s, false));
            if (_showCraft) foreach (CrossSection s in ProceduralShapeSpine.CraftSections()) o.Add((s, true));
            if (_showShared) foreach (CrossSection s in lib) if (!s.IsBuiltIn) o.Add((s, false));
            return o;
        }

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor) { Destroy(gameObject); return; }
            // Typing a name must not reach the editor's hotkeys, wherever the mouse is.
            bool want = _mouseOver || _drag >= 0 || _typing || _resizing;
            if (want && !_locked)
            {
                InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK | ControlTypes.CAMERACONTROLS | ControlTypes.KEYBOARDINPUT, LockId);
                _locked = true;
            }
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
            // A click anywhere else ends typing; IMGUI would otherwise keep the field focused (and the
            // keyboard locked) after the user clicks into the scene.
            if (_typing && Event.current.type == EventType.MouseDown && !_win.Contains(Event.current.mousePosition))
            {
                GUIUtility.keyboardControl = 0;
                _typing = false;
            }
            // The window style draws no background: DrawFrame paints it around the plot, so the plot alone can be
            // see-through.
            if (_frameStyle == null)
            {
                _frameStyle = new GUIStyle(HighLogic.Skin.window);
                foreach (GUIStyleState st in new[] { _frameStyle.normal, _frameStyle.onNormal, _frameStyle.hover, _frameStyle.onHover,
                                                     _frameStyle.active, _frameStyle.onActive, _frameStyle.focused, _frameStyle.onFocused })
                    st.background = null;
            }
            // GUILayout.Window grows to fit but never shrinks: after the grid shrinks, one layout pass from zero size.
            Rect r = _win;
            if (_refit && Event.current.type == EventType.Layout) { r.width = 0f; r.height = 0f; _refit = false; }
            // The title is passed (it names the window to anything looking it up) although DrawFrame paints it again
            // over the background it draws.
            _win = GUILayout.Window(_winId, r, DrawWindow, Title, _frameStyle, GUILayout.Width(Mathf.Max(440f, _gridW + 24f)));
            DropDown();
            if (Event.current.type == EventType.Repaint || Event.current.isMouse)
                _mouseOver = _win.Contains(Event.current.mousePosition) || (_dropOpen && _dropRect.Contains(Event.current.mousePosition));
        }

        // The section list: a window of its own under the Section button, over the dialog (which keeps its size),
        // closed by a pick or a click anywhere else.
        private readonly int _dropId = "SpineCrossSectionDropDown".GetHashCode();
        private Rect _dropBtn;    // the Section button at the last repaint (dialog coordinates)
        private Rect _dropRect;   // the list (screen coordinates)

        private void DropDown()
        {
            if (!_dropOpen) return;
            _dropRect = new Rect(_win.x + _dropBtn.x, _win.y + _dropBtn.yMax + 2f, Mathf.Max(_dropBtn.width, 340f), DropHeight + 44f);
            _dropRect.y = Mathf.Max(0f, Mathf.Min(_dropRect.y, Screen.height - _dropRect.height));   // on screen
            Event e = Event.current;
            var button = new Rect(_win.x + _dropBtn.x, _win.y + _dropBtn.y, _dropBtn.width, _dropBtn.height);
            if (e.type == EventType.MouseDown && !_dropRect.Contains(e.mousePosition) && !button.Contains(e.mousePosition))
            {
                _dropOpen = false;
                return;
            }
            GUI.Window(_dropId, _dropRect, DrawDropDown, GUIContent.none, HighLogic.Skin.window);
            GUI.BringWindowToFront(_dropId);
        }

        private void DrawDropDown(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Show sections from:", GUILayout.Width(130f));
            _showBuiltIn = Check(_showBuiltIn, "Built-in", 80f);
            _showShared = Check(_showShared, "Shared", 72f);
            _showCraft = Check(_showCraft, "Craft", 64f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            // the list is fixed for the frame at Layout (IMGUI wants the same controls through Repaint)
            if (Event.current.type == EventType.Layout || _dropList == null) _dropList = DropList();
            _dropScroll = GUILayout.BeginScrollView(_dropScroll, GUILayout.Height(DropHeight));
            foreach ((CrossSection s, bool craft) in _dropList)
            {
                if (GUILayout.Button(s.name + (s.IsBuiltIn ? "  (built-in)" : craft ? "  (craft)" : "")))
                {
                    Select(s, craft);   // discards unsaved edits
                    _dropOpen = false;
                }
            }
            if (_dropList.Count == 0) GUILayout.Label("(nothing: tick a source above)");
            GUILayout.EndScrollView();
        }

        // A checkbox with its label beside it (SpineGui.Check).
        private static bool Check(bool on, string text, float width) => SpineGui.Check(on, text);

        private const string Title = "Spine Cross Sections";
        private GUIStyle _frameStyle;
        private bool _refit;
        private Rect _hole;   // the plot's rect at the last repaint (window coordinates)

        // The skin's window background everywhere but the plot (drawn in clipped pieces around it), then the title.
        private void DrawFrame()
        {
            var full = new Rect(0f, 0f, _win.width, _win.height);
            GUIStyle bg = HighLogic.Skin.window;
            if (_bgAlpha >= 1f || _hole.width <= 0f) bg.Draw(full, false, false, false, false);
            else
            {
                Rect h = _hole;
                Rect[] pieces =
                {
                    new Rect(0f, 0f, full.width, h.yMin),
                    new Rect(0f, h.yMax, full.width, full.height - h.yMax),
                    new Rect(0f, h.yMin, h.xMin, h.height),
                    new Rect(h.xMax, h.yMin, full.width - h.xMax, h.height),
                };
                foreach (Rect p in pieces)
                {
                    if (p.width <= 0f || p.height <= 0f) continue;
                    GUI.BeginGroup(p);
                    bg.Draw(new Rect(-p.x, -p.y, full.width, full.height), false, false, false, false);
                    GUI.EndGroup();
                }
            }
            _frameStyle.Draw(full, new GUIContent(Title), false, false, false, false);
        }

        private void DrawWindow(int id)
        {
            if (Event.current.type == EventType.Repaint) DrawFrame();
            bool builtIn = _cs == null || _cs.IsBuiltIn;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Section:", GUILayout.Width(60f));
            string label = _current + (builtIn ? "  (built-in)" : _craft ? "  (craft)" : "") + (_dirty ? "  *" : "") + "   ▾";
            if (GUILayout.Button(label)) _dropOpen = !_dropOpen;
            if (Event.current.type == EventType.Repaint) _dropBtn = GUILayoutUtility.GetLastRect();
            GUILayout.EndHorizontal();

            _gridRect = GUILayoutUtility.GetRect(_gridW, _gridH, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            if (Event.current.type == EventType.Repaint) _hole = _gridRect;
            HandleGridInput();
            if (Event.current.type == EventType.Repaint) DrawGrid();

            GUILayout.BeginHorizontal();
            _snap = Check(_snap, "Snap to grid", 120f);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Grid");
            if (GUILayout.Button("-", GUILayout.Width(26f))) _gridIdx = Mathf.Max(0, _gridIdx - 1);
            GUILayout.Label(GridSteps[_gridIdx].ToString("0.####"), GUILayout.Width(48f));
            if (GUILayout.Button("+", GUILayout.Width(26f))) _gridIdx = Mathf.Min(GridSteps.Length - 1, _gridIdx + 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mirror:", GUILayout.Width(52f));
            bool lr = Check(_mirrorLR, "Left to right", 120f);
            bool tb = Check(_mirrorTB, "Top to bottom", 130f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (lr != _mirrorLR || tb != _mirrorTB) SetMirror(lr, tb);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Plot opacity", GUILayout.Width(90f));
            _bgAlpha = GUILayout.HorizontalSlider(_bgAlpha, 0f, 1f, GUILayout.ExpandWidth(true));
            GUILayout.Label(Mathf.RoundToInt(_bgAlpha * 100f) + "%", GUILayout.Width(40f));
            if (GUILayout.Button("Square", GUILayout.Width(64f))) { _gridW = _gridH = Mathf.Min(_gridW, _gridH); _refit = true; }
            GUILayout.EndHorizontal();

            GUILayout.Label($"Drag vertices. Double-click an edge to add one, right-click a vertex to remove it. " +
                            $"{_verts.Count}/{ProceduralShapeSpine.MaxSectionVerts} vertices. Box = node width x height. " +
                            "Drag the corner to stretch the grid, e.g. over a part to trace it. " +
                            "Mirror: edits on one side show on the other; turning it on replaces the far side (Revert undoes).");

            GUILayout.BeginHorizontal();
            GUI.enabled = !builtIn && _dirty;
            if (GUILayout.Button(_craft ? "Save to craft" : "Save"))
            {
                if (_craft) SaveToCraft();
                else SaveTo(_current, overwriteOk: true);
            }
            GUI.enabled = _dirty;
            if (GUILayout.Button("Revert")) Select(_cs, _craft);
            GUI.enabled = !builtIn && !_craft;   // a craft's own section goes when no node uses it
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
            GUI.SetNextControlName(SaveAsControl);
            _saveAsName = GUILayout.TextField(_saveAsName ?? "");
            if (Event.current.type == EventType.Repaint) _typing = GUI.GetNameOfFocusedControl() == SaveAsControl;
            if (GUILayout.Button("Save As", GUILayout.Width(80f))) SaveTo(_saveAsName, overwriteOk: false);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
            {
                Color c = GUI.contentColor;
                if (_statusErr) GUI.contentColor = new Color(1f, 0.5f, 0.4f);
                GUILayout.Label(_status);
                GUI.contentColor = c;
            }

            GUILayout.BeginHorizontal();   // Close stays clear of the corner grip
            if (GUILayout.Button("Close", GUILayout.Width(120f))) Destroy(gameObject);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            HandleResizeGrip();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        // Bottom-right corner grip: dragging it resizes the grid (and the window around it) in x and y.
        private void HandleResizeGrip()
        {
            const float size = 16f;
            var grip = new Rect(_win.width - size - 2f, _win.height - size - 2f, size, size);
            int ctl = GUIUtility.GetControlID(FocusType.Passive, grip);
            Event e = Event.current;
            switch (e.GetTypeForControl(ctl))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !grip.Contains(e.mousePosition)) break;
                    GUIUtility.hotControl = ctl;
                    _resizing = true;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != ctl) break;
                    _gridW = Mathf.Clamp(_gridW + e.delta.x, MinGridPx, MaxGridPx);
                    _gridH = Mathf.Clamp(_gridH + e.delta.y, MinGridPx, MaxGridPx);
                    _refit = true;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != ctl) break;
                    GUIUtility.hotControl = 0;
                    _resizing = false;
                    e.Use();
                    break;
                case EventType.Repaint:
                    var col = new Color(1f, 1f, 1f, _resizing || grip.Contains(e.mousePosition) ? 0.9f : 0.45f);
                    for (int i = 0; i < 3; i++)   // three diagonal rows of dots, a corner grip
                        for (int j = 0; j <= i; j++)
                            GUI.DrawTexture(new Rect(grip.xMax - 4f - 5f * j, grip.yMax - 4f - 5f * (i - j), 2.5f, 2.5f),
                                            Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, col, 0f, 0f);
                    break;
            }
        }

        private void SaveTo(string name, bool overwriteOk)
        {
            name = name?.Trim();
            string nameErr = CrossSectionLibrary.ValidateName(name);
            if (nameErr != null) { SetStatus(nameErr, true); return; }
            string geomErr = SectionGeometry.Validate(_verts);
            if (geomErr != null) { SetStatus(geomErr, true); return; }
            if (!overwriteOk && (name != _current || _craft) && CrossSectionLibrary.Get(name) != null && _confirm != "Overwrite:" + name)
            {
                _confirm = "Overwrite:" + name;
                SetStatus($"Your shared sections have a \"{name}\". Click Save As again to overwrite it.", false);
                return;
            }
            CrossSectionLibrary.Save(name, SectionGeometry.Normalize(_verts));
            // the craft's own copies of that name give way to the shared one (Save As is how a craft's section is
            // brought into the shared list)
            bool fromCraft = false;
            foreach (ProceduralShapeSpine m in ProceduralShapeSpine.EditorSpines())
                if (m.nodes.Exists(n => n.pinned != null && n.pinned.name == name)) { m.ReplaceCraftSection(name, null); fromCraft = true; }
            Select(name);
            SetStatus($"Saved \"{name}\" to your shared sections (fitted to the box)." +
                      (fromCraft ? " This craft uses the shared one now." : " Pick it in a node's Section list."), false);
        }

        // A craft's own section edited: every node on the craft that has it takes the new shape.
        private void SaveToCraft()
        {
            string geomErr = SectionGeometry.Validate(_verts);
            if (geomErr != null) { SetStatus(geomErr, true); return; }
            CrossSection cs = CrossSection.Custom(_current, SectionGeometry.Normalize(_verts));
            foreach (ProceduralShapeSpine m in ProceduralShapeSpine.EditorSpines()) m.ReplaceCraftSection(_current, cs);
            Select(cs, true);
            SetStatus($"Saved \"{_current}\" on this craft (your shared sections are unchanged).", false);
        }

        // --- grid ---

        private float ScaleX => _gridRect.width / (2f * ViewHalf);
        private float ScaleY => _gridRect.height / (2f * ViewHalf);

        private Vector2 ToPx(Vector2 u) =>
            new Vector2(_gridRect.x + (u.x + ViewHalf) * ScaleX, _gridRect.y + (ViewHalf - u.y) * ScaleY);

        private Vector2 ToUnit(Vector2 px) =>
            new Vector2((px.x - _gridRect.x) / ScaleX - ViewHalf, ViewHalf - (px.y - _gridRect.y) / ScaleY);

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
                            if (edge >= 0 && !InsertOnEdge(edge, Place(ToUnit(at))))
                                SetStatus($"At most {ProceduralShapeSpine.MaxSectionVerts} vertices.", true);
                        }
                        e.Use();
                    }
                    else if (e.button == 1 && v >= 0)
                    {
                        if (!DeleteVertex(v)) SetStatus("A section needs at least 3 vertices.", true);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != ctl || _drag < 0) return;
                    MoveVertex(_drag, Place(ToUnit(e.mousePosition)));
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
        private int _texW, _texH;

        private void DrawGrid()
        {
            bool valid = SectionGeometry.Validate(_verts) == null;
            Color edge = valid ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(1f, 0.35f, 0.3f, 1f);
            int w = Mathf.RoundToInt(_gridRect.width), h = Mathf.RoundToInt(_gridRect.height);
            string key = _gridIdx + "|" + valid + "|" + w + "x" + h + "|" + Mathf.RoundToInt(_bgAlpha * 100f) + "|" + _mirrorLR + _mirrorTB
                         + "|" + SectionGeometry.Format(_verts);
            if (_tex == null || key != _texKey)
            {
                RenderGrid(edge, w, h);
                _texKey = key;
            }
            GUI.DrawTexture(_gridRect, _tex);

            int hover = _drag >= 0 ? _drag : HitVertex(Event.current.mousePosition);
            for (int i = 0; i < _verts.Count; i++)
            {
                Vector2 c = ToPx(_verts[i]);
                float r = (i == hover) ? 5f : 3.5f;
                Color col = (i == hover) ? Color.white : (i == 0 ? new Color(0.4f, 1f, 0.5f) : edge);
                if (i != hover && Mirroring && _mapF[i] != 0) col.a = 0.45f;   // a mirror image: dimmer
                GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, col, 0f, 0f);
            }
            if (hover >= 0)
            {
                Vector2 c = ToPx(_verts[hover]);
                GUI.Label(new Rect(c.x + 8f, c.y - 22f, 140f, 20f), SectionGeometry.Format(_verts[hover]));
            }
        }

        private void RenderGrid(Color edge, int w, int h)
        {
            w = Mathf.Max(1, w); h = Mathf.Max(1, h);
            if (_tex == null || _texW != w || _texH != h)
            {
                if (_tex != null) Destroy(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _px = new Color32[w * h];
                _texW = w; _texH = h;
            }
            var bg = new Color32(20, 23, 28, (byte)Mathf.RoundToInt(255f * _bgAlpha));
            for (int i = 0; i < _px.Length; i++) _px[i] = bg;

            // fainter lines read on the dark fill; over the scene they need more weight
            float gridA = Mathf.Lerp(0.4f, 0.10f, _bgAlpha), axisA = Mathf.Lerp(0.6f, 0.28f, _bgAlpha);
            float step = GridSteps[_gridIdx];
            if (step * Mathf.Min(TexScaleX, TexScaleY) >= 5f)
            {
                int n = Mathf.FloorToInt(ViewHalf / step);
                for (int k = -n; k <= n; k++)
                {
                    TexLine(new Vector2(k * step, -ViewHalf), new Vector2(k * step, ViewHalf), new Color(1f, 1f, 1f, gridA), 1);
                    TexLine(new Vector2(-ViewHalf, k * step), new Vector2(ViewHalf, k * step), new Color(1f, 1f, 1f, gridA), 1);
                }
            }
            // a mirror line drawn bolder and tinted
            var mirrorCol = new Color(1f, 0.45f, 0.9f, 0.85f);
            TexLine(new Vector2(0f, -ViewHalf), new Vector2(0f, ViewHalf), _mirrorLR ? mirrorCol : new Color(1f, 1f, 1f, axisA), _mirrorLR ? 2 : 1);
            TexLine(new Vector2(-ViewHalf, 0f), new Vector2(ViewHalf, 0f), _mirrorTB ? mirrorCol : new Color(1f, 1f, 1f, axisA), _mirrorTB ? 2 : 1);
            Vector2[] box = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            for (int i = 0; i < 4; i++) TexLine(box[i], box[(i + 1) % 4], new Color(0.4f, 0.8f, 1f, 0.6f), 1);
            for (int i = 0; i < _verts.Count; i++) TexLine(_verts[i], _verts[(i + 1) % _verts.Count], edge, 2);

            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        private float TexScaleX => _texW / (2f * ViewHalf);
        private float TexScaleY => _texH / (2f * ViewHalf);

        // Unit-space segment into the texture (row 0 = bottom, so +y is up as in unit space), alpha-blended.
        private void TexLine(Vector2 a, Vector2 b, Color col, int width)
        {
            var sc = new Vector2(TexScaleX, TexScaleY);
            Vector2 pa = Vector2.Scale(a + new Vector2(ViewHalf, ViewHalf), sc), pb = Vector2.Scale(b + new Vector2(ViewHalf, ViewHalf), sc);
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

        // "Over" compositing, so lines keep their own weight on a see-through background.
        private void Blend(int x, int y, Color col)
        {
            if (x < 0 || y < 0 || x >= _texW || y >= _texH) return;
            int i = y * _texW + x;
            Color d = _px[i];
            float a = col.a + d.a * (1f - col.a);
            if (a <= 0f) return;
            Color rgb = (col * col.a + d * (d.a * (1f - col.a))) / a;
            _px[i] = new Color(rgb.r, rgb.g, rgb.b, a);
        }
    }
}

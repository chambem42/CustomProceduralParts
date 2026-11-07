using UnityEngine;

namespace CustomProceduralParts
{
    // "Spine Options" (editor only): the Spine's display and handling options, out of the PAW. They apply to the
    // part it was opened for and its symmetry counterparts.
    public class SpineOptionsWindow : MonoBehaviour
    {
        private static SpineOptionsWindow _instance;
        public const string Title = "Spine Options";

        public static bool MouseOverWindow => _instance != null && _instance._mouseOver;

        public static void Open(ProceduralShapeSpine spine)
        {
            if (_instance == null)
                _instance = new GameObject("SpineOptionsWindow").AddComponent<SpineOptionsWindow>();
            _instance._spine = spine;
        }

        private ProceduralShapeSpine _spine;
        private Rect _win = new Rect(320f, 120f, 380f, 10f);
        private readonly int _winId = "SpineOptionsWindow".GetHashCode();
        private bool _mouseOver, _locked;
        private static GUIStyle _heading;
        private const string LockId = "SpineOptionsWindow";

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor || _spine == null || _spine.part == null) { Destroy(gameObject); return; }
            if (_mouseOver && !_locked) { InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK | ControlTypes.CAMERACONTROLS, LockId); _locked = true; }
            else if (!_mouseOver && _locked) { InputLockManager.RemoveControlLock(LockId); _locked = false; }
        }

        private void OnDestroy()
        {
            if (_locked) InputLockManager.RemoveControlLock(LockId);
            if (_instance == this) _instance = null;
        }

        private void OnGUI()
        {
            if (_spine == null) return;
            GUI.skin = HighLogic.Skin;
            _win = GUILayout.Window(_winId, _win, Draw, Title, GUILayout.Width(380f));
            if (Event.current.type == EventType.Repaint || Event.current.isMouse) _mouseOver = _win.Contains(Event.current.mousePosition);
        }

        private void Draw(int id)
        {
            ProceduralShapeSpine s = _spine;
            SpineSettings.EnsureLoaded();

            // every Spine's (saved for the game, not the craft)
            GUILayout.Label("All Spine parts", _heading ??= new GUIStyle(HighLogic.Skin.label) { fontStyle = FontStyle.Bold });
            GUILayout.Label("Handles shown");
            SpineHandleVis vis = SpineSettings.HandleVis, pick = vis;
            GUILayout.BeginHorizontal();
            if (SpineGui.Radio(vis == SpineHandleVis.All, "All nodes")) pick = SpineHandleVis.All;
            if (SpineGui.Radio(vis == SpineHandleVis.Active, "Selected node")) pick = SpineHandleVis.Active;
            if (SpineGui.Radio(vis == SpineHandleVis.None, "None")) pick = SpineHandleVis.None;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            bool click = SpineGui.Check(SpineSettings.ClickBeforeDrag, "Click a node before dragging it");
            bool outlines = SpineGui.Check(SpineSettings.ShowOutlines, "Show cross-section outlines");
            SpineSettings.Set(pick, click, outlines);

            // this part's (saved with the craft; its symmetry counterparts follow)
            GUILayout.Space(6f);
            GUILayout.Label("This part", _heading);
            GUILayout.Label("Points per section (more: smoother curves)");
            GUILayout.BeginHorizontal();
            foreach (int n in ProceduralShapeSpine.PointChoices)
                if (SpineGui.Radio(s.sectionPoints == n, n.ToString())) s.SetSectionPoints(n);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            if (GUILayout.Button("Close", GUILayout.Width(100f))) Destroy(gameObject);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CustomProceduralParts
{
    // IMGUI bits shared by the Spine's windows, in KSP's skin.
    internal static class SpineGui
    {
        private static GUIStyle _led, _text;

        // A checkbox the game's way: the skin's toggle is an LED in a fixed 30x30 control whose text is pushed out to
        // the right by its content offset, so in a layout the text spills over whatever comes next. Here the LED is
        // the skin's toggle with no text, and the label a layout element of its own beside it (a borderless button,
        // so clicking the text flips it too).
        public static bool Check(bool on, string text)
        {
            if (_led == null)
            {
                _led = new GUIStyle(HighLogic.Skin.toggle);
                _text = new GUIStyle(HighLogic.Skin.label) { wordWrap = false, stretchWidth = false, margin = new RectOffset(0, 14, 6, 0) };
            }
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
            bool v = GUILayout.Toggle(on, GUIContent.none, _led);
            if (GUILayout.Button(text, _text)) v = !on;
            GUILayout.EndHorizontal();
            return v;
        }

        // One of a set of choices (the same LED): true when it was just picked (clicking the picked one leaves it).
        public static bool Radio(bool picked, string text) => Check(picked, text) && !picked;
    }

    // Tooltips for a window: each control's rect noted as it's drawn (Add, in the window function), and the tip of the
    // one under the pointer shown beside it (Draw, in OnGUI) once the pointer has rested there for HoverDelay. A
    // press or a move starts the wait again.
    internal sealed class SpineTips
    {
        public const float HoverDelay = 0.6f;

        // Where the pointer is, in GUI coordinates, instead of the mouse (a showcase's drawn cursor).
        public static Vector2? PointerOverride { get; set; }

        private readonly int _id;
        private List<(Rect r, string text)> _tips = new List<(Rect, string)>(), _next = new List<(Rect, string)>();
        private Vector2 _rest;
        private float _since;
        private string _text;
        private static GUIStyle _style;

        public SpineTips(string name) { _id = (name + ".Tips").GetHashCode(); }

        public static Vector2 Pointer => PointerOverride ?? new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        // The last layout control's tip; `origin` is the window's top left. Call it in the window function.
        public void Add(string text, Vector2 origin)
        {
            if (Event.current.type != EventType.Repaint) return;
            Rect r = GUILayoutUtility.GetLastRect();
            r.position += origin;
            _next.Add((r, text));
        }

        // A tip for a rect in window coordinates.
        public void Add(Rect r, string text, Vector2 origin)
        {
            if (Event.current.type != EventType.Repaint) return;
            r.position += origin;
            _next.Add((r, text));
        }

        // Call in the window function's first Repaint line: the tips drawn last time are the ones to look up.
        public void BeginRepaint()
        {
            if (Event.current.type != EventType.Repaint) return;
            (_tips, _next) = (_next, _tips);
            _next.Clear();
        }

        // In OnGUI, after the window: the tip, in a window of its own in front. `inside` false (the window closed or
        // hidden) shows none.
        public void Draw(bool inside = true)
        {
            Vector2 p = Pointer;
            bool pressed = PointerOverride == null && (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2));
            if ((p - _rest).sqrMagnitude > 4f || pressed) { _rest = p; _since = Time.unscaledTime; }
            if (Event.current.type == EventType.Layout)
            {
                _text = null;
                if (inside && Time.unscaledTime - _since >= HoverDelay)
                    for (int i = _tips.Count - 1; i >= 0; i--)
                        if (_tips[i].r.Contains(p)) { _text = _tips[i].text; break; }
            }
            if (_text == null) return;
            if (_style == null)
            {
                // the skin's box is nearly see-through: a solid dark panel with a light edge, readable over anything
                var tex = new Texture2D(3, 3) { filterMode = FilterMode.Point };
                var edge = new Color(0.55f, 0.6f, 0.65f, 1f);
                var fill = new Color(0.08f, 0.09f, 0.11f, 0.97f);
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++) tex.SetPixel(x, y, x == 1 && y == 1 ? fill : edge);
                tex.Apply();
                _style = new GUIStyle(HighLogic.Skin.label)
                {
                    wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 13,
                    padding = new RectOffset(8, 8, 6, 6), border = new RectOffset(1, 1, 1, 1),
                    normal = { textColor = Color.white, background = tex },
                };
            }
            var content = new GUIContent(_text);
            float w = Mathf.Min(300f, _style.CalcSize(content).x + 2f);
            float h = _style.CalcHeight(content, w);
            var rect = new Rect(p.x + 16f, p.y + 22f, w, h);
            if (rect.xMax > Screen.width) rect.x = Mathf.Max(0f, p.x - w - 8f);
            if (rect.yMax > Screen.height) rect.y = Mathf.Max(0f, p.y - h - 8f);
            string t = _text;
            GUI.Window(_id, rect, _ => GUI.Label(new Rect(0f, 0f, w, h), t, _style), GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(_id);
        }
    }
}

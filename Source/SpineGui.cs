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
}

using System;
using System.IO;
using UnityEngine;

namespace CustomProceduralParts
{
    // The Spine's display and handling options (the Spine Options window), the same for every Spine part:
    // PluginData/SpineOptions.cfg. A part's own option (points per section) is a field on the part.
    public static class SpineSettings
    {
        private const string ModTag = "[SpineSettings]";

        private static SpineHandleVis _handleVis = SpineHandleVis.Active;
        private static bool _clickBeforeDrag = true, _showOutlines;

        public static SpineHandleVis HandleVis { get { EnsureLoaded(); return _handleVis; } private set => _handleVis = value; }
        public static bool ClickBeforeDrag { get { EnsureLoaded(); return _clickBeforeDrag; } private set => _clickBeforeDrag = value; }
        public static bool ShowOutlines { get { EnsureLoaded(); return _showOutlines; } private set => _showOutlines = value; }

        // Bumps on every change, for anything drawn from the options (the outlines).
        public static int Version { get; private set; }

        private static bool _loaded;

        private static string FilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "CustomProceduralParts", "PluginData", "SpineOptions.cfg");

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                ConfigNode n = ConfigNode.Load(FilePath)?.GetNode("SPINE_OPTIONS");
                if (n == null) return;
                HandleVis = ProceduralShapeSpine.ParseEnum(n.GetValue("handles"), SpineHandleVis.Active);
                if (bool.TryParse(n.GetValue("clickBeforeDrag"), out bool click)) ClickBeforeDrag = click;
                if (bool.TryParse(n.GetValue("showOutlines"), out bool outlines)) ShowOutlines = outlines;
            }
            catch (Exception e) { Debug.LogError($"{ModTag} loading {FilePath}: {e}"); }
        }

        public static void Set(SpineHandleVis handles, bool clickBeforeDrag, bool showOutlines)
        {
            EnsureLoaded();
            if (handles == HandleVis && clickBeforeDrag == ClickBeforeDrag && showOutlines == ShowOutlines) return;
            HandleVis = handles;
            ClickBeforeDrag = clickBeforeDrag;
            ShowOutlines = showOutlines;
            Version++;
            try
            {
                var root = new ConfigNode();
                ConfigNode n = root.AddNode("SPINE_OPTIONS");
                n.AddValue("handles", HandleVis.ToString());
                n.AddValue("clickBeforeDrag", ClickBeforeDrag);
                n.AddValue("showOutlines", ShowOutlines);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                root.Save(FilePath);
            }
            catch (Exception e) { Debug.LogError($"{ModTag} saving {FilePath}: {e}"); }
        }
    }
}

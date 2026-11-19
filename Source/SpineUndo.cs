using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CustomProceduralParts
{
    /// <summary>
    /// Spine edits as editor undo steps. Stock KSP only records part attach / detach / move, so a PAW or handle
    /// edit can't be undone on its own. Two models of the undo list are in use:
    /// - stock: each entry is the ship after an action, the last one the current state;
    /// - KSPCommunityFixes' BetterEditorUndoRedo: each entry is the ship before an action, the current state not
    ///   in the list until an undo saves it (for redo).
    /// So a change records the ship after it (stock) or before it (KSPCF). Repeats of one change on one part
    /// (a PAW arrow tapped again, the wheel, a shape icon clicked again) within a couple of seconds are one step.
    /// Callers: Before(owner, key) before changing anything (null when no snapshot is needed), the change,
    /// then After(owner, key, what Before returned). A null key never joins a run.
    /// </summary>
    internal static class SpineUndo
    {
        private const float RunSeconds = 2f;

        private static object _owner;
        private static string _key;
        private static float _time;
        private static ConfigNode _last;   // the entry the last change left at the end of the list

        private static readonly FieldInfo UndoLevelField = typeof(EditorLogic).GetField("undoLevel", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo UndoLimitField = typeof(EditorLogic).GetField("undoLimit", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool? _beforeModel;
        // KSPCF's BetterEditorUndoRedo applied (looked up once, by name: no reference to KSPCF).
        private static bool BeforeModel
        {
            get
            {
                if (_beforeModel == null)
                {
                    Type t = AssemblyLoader.loadedAssemblies.Select(a => a.assembly.GetType("KSPCommunityFixes.KSPCommunityFixes", false))
                        .FirstOrDefault(x => x != null);
                    var patches = t?.GetField("patchInstances", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IDictionary;
                    _beforeModel = patches != null && patches.Keys.Cast<Type>().Any(k => k.Name == "BetterEditorUndoRedo");
                    Debug.Log($"[CustomProceduralParts] editor undo: {(_beforeModel.Value ? "KSPCF (state before each change)" : "stock (state after each change)")}");
                }
                return _beforeModel.Value;
            }
        }

        private static EditorLogic Editor =>
            HighLogic.LoadedSceneIsEditor && EditorLogic.fetch is EditorLogic el && el.ship != null && el.ship.parts.Count > 0 ? el : null;

        private static int UndoLevel(EditorLogic el) => UndoLevelField != null ? (int)UndoLevelField.GetValue(el) : ShipConstruction.backups.Count;

        // This change carries on the last one: same part and control, soon after, nothing undone or recorded since.
        private static bool Continues(object owner, string key)
        {
            var b = ShipConstruction.backups;
            return key != null && ReferenceEquals(owner, _owner) && key == _key && Time.realtimeSinceStartup - _time < RunSeconds
                && Editor is EditorLogic el && UndoLevel(el) == b.Count && b.Count > 0 && ReferenceEquals(b[b.Count - 1], _last);
        }

        /// <summary>Whether Before would snapshot the ship (so a caller can set up the state before first).</summary>
        internal static bool WantsBefore(object owner, string key) => Editor != null && BeforeModel && !Continues(owner, key);

        /// <summary>The ship as it is, if this change needs it recorded before it happens; else null.</summary>
        internal static ConfigNode Before(object owner, string key) => WantsBefore(owner, key) ? Snapshot() : null;

        internal static ConfigNode Snapshot() => Editor?.ship.SaveShip();

        /// <summary>After a change: record it as an undo step (or as part of the run it carries on).</summary>
        internal static void After(object owner, string key, ConfigNode before)
        {
            if (!(Editor is EditorLogic el)) return;
            if (BeforeModel)
            {
                if (before == null) { if (Continues(owner, key)) Touch(owner, key); return; }
                Push(el, before, key);
            }
            else
            {
                ConfigNode now = el.ship.SaveShip();
                if (Continues(owner, key)) ShipConstruction.backups[ShipConstruction.backups.Count - 1] = now;
                else Push(el, now, key);
                ShipConstruction.ShipConfig = now;   // as SetBackup leaves it (the crew manifest reads it)
                _last = now;
            }
            Touch(owner, key);
        }

        private static void Touch(object owner, string key)
        {
            _owner = owner;
            _key = key;
            _time = Time.realtimeSinceStartup;
        }

        // An entry at the end of the list, as SetBackup adds one: the redo states dropped, the oldest shifted out
        // at the limit. No onEditorShipModified: the change itself fires that.
        private static void Push(EditorLogic el, ConfigNode node, string key)
        {
            var b = ShipConstruction.backups;
            int level = UndoLevel(el);
            if (level < b.Count) b.RemoveRange(level, b.Count - level);
            int limit = UndoLimitField != null ? (int)UndoLimitField.GetValue(el) : GameSettings.EDITOR_UNDO_REDO_LIMIT;
            if (limit > 0 && b.Count >= limit) b.RemoveAt(0);
            b.Add(node);
            UndoLevelField?.SetValue(el, b.Count);
            Debug.Log($"[CustomProceduralParts] undo step {b.Count}: {key ?? "edit"}");
            _last = node;
            GameEvents.onEditorSetBackup.Fire(el.ship);
        }
    }
}

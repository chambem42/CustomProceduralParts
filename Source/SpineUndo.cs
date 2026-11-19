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
        private static bool _stale;        // stock: _last is behind the ship (a run's repeats, saved once it pauses)

        private static readonly FieldInfo UndoLevelField = typeof(EditorLogic).GetField("undoLevel", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo UndoLimitField = typeof(EditorLogic).GetField("undoLimit", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SavedLevelField = typeof(EditorLogic).GetField("undoIndexAtLastSave", BindingFlags.Instance | BindingFlags.NonPublic);

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

        // This change carries on the last one: same part and control, soon after, nothing undone, recorded or
        // saved since (joining a step the craft file was saved at would hide the change from the unsaved-changes
        // check).
        private static bool Continues(object owner, string key)
        {
            var b = ShipConstruction.backups;
            return key != null && ReferenceEquals(owner, _owner) && key == _key && Time.realtimeSinceStartup - _time < RunSeconds
                && Editor is EditorLogic el && UndoLevel(el) == b.Count && b.Count > 0 && ReferenceEquals(b[b.Count - 1], _last)
                && !(SavedLevelField != null && (int)SavedLevelField.GetValue(el) == b.Count);
        }

        /// <summary>Whether Before would snapshot the ship (so a caller can set up the state before first).</summary>
        internal static bool WantsBefore(object owner, string key) => Editor != null && BeforeModel && !Continues(owner, key);

        /// <summary>The ship as it is, if this change needs it recorded before it happens; else null.</summary>
        internal static ConfigNode Before(object owner, string key) => WantsBefore(owner, key) ? Snapshot() : null;

        internal static ConfigNode Snapshot() => Editor is EditorLogic el ? Save(el) : null;

        private static PropertyInfo _textProp;
        private static string Text(object field)
        {
            if (field == null) return null;
            _textProp = _textProp ?? field.GetType().GetProperty("text");
            return _textProp?.GetValue(field, null) as string;
        }

        // The ship as SetBackup saves it: name, description and flag from the editor's fields first.
        private static ConfigNode Save(EditorLogic el)
        {
            if (Text(el.shipNameField) is string name) el.ship.shipName = name;
            if (Text(el.shipDescriptionField) is string desc) el.ship.shipDescription = desc;
            el.ship.missionFlag = EditorLogic.FlagURL;
            return el.ship.SaveShip();
        }

        /// <summary>After a change: record it as an undo step (or as part of the run it carries on).</summary>
        internal static void After(object owner, string key, ConfigNode before)
        {
            if (!(Editor is EditorLogic el)) return;
            Watch(el);
            if (BeforeModel)
            {
                if (before == null) { if (Continues(owner, key)) Touch(owner, key); return; }
                Push(el, before, key);
            }
            else if (Continues(owner, key))
                _stale = true;   // a repeat: the run's entry is saved again once it pauses (Flush)
            else
            {
                Flush();
                Push(el, Save(el), key);
            }
            Touch(owner, key);
        }

        private static void Touch(object owner, string key)
        {
            _owner = owner;
            _key = key;
            _time = Time.realtimeSinceStartup;
        }

        // Stock: the run's entry brought up to the ship, if it's still the last (nothing recorded or undone since).
        internal static void Flush()
        {
            if (!_stale) return;
            _stale = false;
            var b = ShipConstruction.backups;
            if (!(Editor is EditorLogic el) || b.Count == 0 || !ReferenceEquals(b[b.Count - 1], _last) || UndoLevel(el) != b.Count) return;
            ConfigNode now = Save(el);
            b[b.Count - 1] = now;
            ShipConstruction.ShipConfig = now;
            _last = now;
        }

        // Forget the run (an undo, a redo, the editor gone).
        private static void Reset()
        {
            _owner = null;
            _key = null;
            _last = null;
            _stale = false;
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
            ShipConstruction.ShipConfig = node;   // as CreateBackup leaves it (the crew manifest reads it)
            ShipConstruction.ShipType = el.ship.shipFacility;
            UndoLevelField?.SetValue(el, b.Count);
            Debug.Log($"[CustomProceduralParts] undo step {b.Count}: {key ?? "edit"}");
            _last = node;
            GameEvents.onEditorSetBackup.Fire(el.ship);
        }

        private static void Watch(EditorLogic el)
        {
            if (el.GetComponent<Watcher>() == null) el.gameObject.AddComponent<Watcher>();
        }

        // Lives on the editor: saves a paused run (stock), forgets the run on undo / redo, and lets go of it all
        // when the editor goes.
        private class Watcher : MonoBehaviour
        {
            private void OnEnable()
            {
                GameEvents.onEditorUndo.Add(OnRestore);
                GameEvents.onEditorRedo.Add(OnRestore);
            }

            private void OnDisable()
            {
                GameEvents.onEditorUndo.Remove(OnRestore);
                GameEvents.onEditorRedo.Remove(OnRestore);
            }

            private void OnDestroy() => Reset();

            private static void OnRestore(ShipConstruct _) => Reset();

            // a frame with no repeat: the run has paused (a slider held still, between taps)
            private void LateUpdate()
            {
                if (_stale && Time.realtimeSinceStartup > _time) Flush();
            }
        }
    }
}

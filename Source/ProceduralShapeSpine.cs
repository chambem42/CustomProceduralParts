using KSPAPIExtensions;
using ProceduralParts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace CustomProceduralParts
{
    // Built-in (parametric) cross-sections. Custom polygon sections live in CrossSectionLibrary; a node
    // names its section, and every section yields the same Sides-point ordering so a segment can morph
    // point-for-point between any two.
    public enum SpineProfile { Ellipse, Rectangle, Mk2, Mk3 }

    // Silhouette of the segment ABOVE this node. Phase 1 implements Linear; the rest in Phase 2.
    public enum SpineSlope { Linear, Concave, Convex, Waisted }

    // Which nodes show their edge (resize) tip handles. The node selectors themselves always show.
    public enum SpineHandleVis { All, Active, None }

    // One cross-section along the spine. Persisted as a SPINE_NODE child node (see OnSave/OnLoad),
    // the same variable-length pattern ProceduralSRB uses for its bell configs.
    [Serializable]
    public class SpineNode : IConfigNode
    {
        [Persistent] public float position = 0.5f;   // 0..1 along the spine (0 = bottom, 1 = top)
        [Persistent] public float sizeH = 1.25f;     // horizontal diameter (m), before hScale
        [Persistent] public float sizeV = 1.25f;     // vertical diameter (m), before vScale
        [Persistent] public float offsetV = 0f;      // vertical centre offset (m), before vScale
        [Persistent] public float offsetH = 0f;      // horizontal centre offset (m), before hScale
        [Persistent] public float tiltV = 0f;        // vertical tilt (deg): +ve pitches the top of the ring forward (+spine)
        [Persistent] public float tiltH = 0f;        // horizontal tilt (deg): +ve yaws the right of the ring forward (+spine)
        [Persistent] public string section = nameof(SpineProfile.Ellipse);   // CrossSectionLibrary name
        [Persistent] public SpineSlope slopeAbove = SpineSlope.Linear;

        public SpineNode() { }
        public SpineNode(float pos, float h, float v) { position = pos; sizeH = h; sizeV = v; }
        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
            // Crafts from before custom sections stored the built-in as "profile".
            if (!node.HasValue("section") && node.HasValue("profile")) section = node.GetValue("profile");
        }
        public void Save(ConfigNode node) => ConfigNode.CreateConfigFromObject(this, node);
        // Outside the editor a node keeps the custom section its craft embedded, so a later library edit
        // (or a missing section) never reshapes a vessel already built. In the editor the library rules.
        internal CrossSection pinned;
        public CrossSection Section => pinned ?? CrossSectionLibrary.Resolve(section);
        public SpineNode Clone() => new SpineNode
        {
            position = position, sizeH = sizeH, sizeV = sizeV, offsetV = offsetV, offsetH = offsetH,
            tiltV = tiltV, tiltH = tiltH,
            section = section, slopeAbove = slopeAbove, pinned = pinned,
        };
    }

    public class ProceduralShapeSpine : ProceduralAbstractShape
    {
        private new const string ModTag = "[ProceduralShapeSpine]";
        internal const int MinSides = 24;          // points per ring by default, and the fewest (icons use it too)
        internal const int MaxSectionVerts = 96;   // a custom section's vertex cap (a part with fewer points per section simplifies it)

        public override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, EndDiameter(0));

        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 2f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Width scale", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 3)]
        public float hScale = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Height scale", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 3)]
        public float vScale = 1f;

        // Shell: open both ends and drop the solid interior (a thin skin) -- for custom shrouds / shields.
        // affectSymCounterparts None: the onFieldChanged -> RebuildAndPropagate -> SyncToSymmetry path
        // already copies shellMode to and rebuilds every counterpart (stock All would double that to N^2).
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Hollow shell", groupName = ProceduralPart.PAWGroupName),
            UI_Toggle(scene = UI_Scene.Editor, enabledText = "On", disabledText = "Off", affectSymCounterparts = UI_Scene.None)]
        public bool shellMode = false;

        // Wall thickness (m) of the hollow shell: the inner skin is inset from the outer by this, and the
        // open ends get a rim so the wall reads solid instead of paper-thin. Shown only in shell mode.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Shell thickness", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.005f, maxValue = 1.0f, incrementLarge = 0.1f, incrementSmall = 0.02f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "m", useSI = true, affectSymCounterparts = UI_Scene.None)]
        public float shellThickness = 0.05f;
        // Max fraction of a section's smaller half-extent the wall may inset (so the inner bore never
        // crosses the centre). Shared by the geometry clamp (InnerRing) and the slider's upper bound.
        private const float ShellInsetFraction = 0.49f;
        private const float MinShellThickness = 0.005f;

        // Points around each section of this part (the loft's ring outlines): more for smoother curves. At least
        // the custom-section vertex cap, so every vertex of every section is kept. Set in the Spine Options window.
        [KSPField(isPersistant = true)]
        public int sectionPoints = MinSides;
        internal static readonly int[] PointChoices = { 24, 32, 48, 64, 96 };
        internal int Sides => Mathf.Clamp(sectionPoints, MinSides, PointChoices[PointChoices.Length - 1]);

        // Collider points per ring, at most (no more than the ring's own points): its level of detail for physics,
        // surface attachment and whatever reads its triangles (FAR's aero and water forces). Saved with the part;
        // no control for it (a test sets it to see how a reader's results go with the detail).
        [KSPField(isPersistant = true)]
        public int colliderPoints = 24;
        private int ColliderSides => Mathf.Clamp(colliderPoints, 4, MaxSectionVerts);

        // The display and handling options (handles shown, click before drag, outlines) are SpineSettings,
        // the same for every Spine; this part's own is sectionPoints.
        [KSPEvent(guiActiveEditor = true, guiName = "Spine Options", groupName = ProceduralPart.PAWGroupName)]
        public void SpineOptionsEvent() => SpineOptionsWindow.Open(this);

        // This part's points per section, on it and its symmetry counterparts, re-lofted.
        internal void SetSectionPoints(int points)
        {
            points = Mathf.Clamp(points, MinSides, PointChoices[PointChoices.Length - 1]);
            if (points == sectionPoints) return;
            ConfigNode pre = SpineUndo.Before(this, nameof(sectionPoints));
            sectionPoints = points;
            foreach (Part p in part.symmetryCounterparts)
                if (FindAbstractShapeModule(p, this) is ProceduralShapeSpine m) m.sectionPoints = points;
            RebuildAndPropagate();
            SpineUndo.After(this, nameof(sectionPoints), pre);
        }

        [KSPField] public string TopNodeName = "top";
        [KSPField] public string BottomNodeName = "bottom";

        // Smallest bounding-circle diameter over the whole spine (set in UpdateShape). MaxDiameter is the
        // largest; RP-1 tooling reads both to tool the spine as its min + max bounding cylinders.
        public float MinBoundingDiameter { get; private set; }

        // Spine cross-sections, sorted by position. Serialized as SPINE_NODE subnodes.
        public readonly List<SpineNode> nodes = new List<SpineNode>();

        // --- Per-node editing proxies (PAW) ---
        // KSPFields are scalars, so these proxy whichever node `selectedNode` points at: changing one
        // writes back to that SpineNode. Non-persistent (the node list is the source of truth) except
        // selectedNode. Symmetry is handled manually (SyncToSymmetry), so they don't auto-propagate.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Node", guiFormat = "F0", groupName = ProceduralPart.PAWGroupName),
            UI_FloatRange(minValue = 0, maxValue = 2, stepIncrement = 1, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public float selectedNode = 0;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node width", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 4, unit = "m", useSI = true, affectSymCounterparts = UI_Scene.None)]
        public float nodeWidth = 1.25f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node height", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 4, unit = "m", useSI = true, affectSymCounterparts = UI_Scene.None)]
        public float nodeHeight = 1.25f;

        // Tilt the ring's plane: vertical tilt rakes the top of the section fore/aft, horizontal tilt rakes
        // the right side fore/aft (a shear along the spine, not editable from the gizmo).
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node vertical tilt", guiFormat = "F1", guiUnits = "°", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -60f, maxValue = 60f, incrementLarge = 15f, incrementSmall = 5f, incrementSlide = 0.1f, sigFigs = 1, affectSymCounterparts = UI_Scene.None)]
        public float nodeTiltV = 0f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node horizontal tilt", guiFormat = "F1", guiUnits = "°", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -60f, maxValue = 60f, incrementLarge = 15f, incrementSmall = 5f, incrementSlide = 0.1f, sigFigs = 1, affectSymCounterparts = UI_Scene.None)]
        public float nodeTiltH = 0f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node position", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 1f, incrementLarge = 0.25f, incrementSmall = 0.05f, incrementSlide = SliderPrecision, sigFigs = 3, affectSymCounterparts = UI_Scene.None)]
        public float nodePos = 0.5f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Slope above", groupName = ProceduralPart.PAWGroupName),
            UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public string nodeSlopeOpt = "Linear";

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Section", groupName = ProceduralPart.PAWGroupName),
            UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public string nodeProfileOpt = "Ellipse";

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node height offset", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -10f, maxValue = 10f, incrementLarge = 0.5f, incrementSmall = 0.1f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "m", affectSymCounterparts = UI_Scene.None)]
        public float nodeOffsetV = 0f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node width offset", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -10f, maxValue = 10f, incrementLarge = 0.5f, incrementSmall = 0.1f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "m", affectSymCounterparts = UI_Scene.None)]
        public float nodeOffsetH = 0f;

        [KSPEvent(guiActiveEditor = true, guiName = "Add node in front", groupName = ProceduralPart.PAWGroupName)]
        public void AddNodeInFrontEvent() => AddNode(true);

        [KSPEvent(guiActiveEditor = true, guiName = "Add node behind", groupName = ProceduralPart.PAWGroupName)]
        public void AddNodeBehindEvent() => AddNode(false);

        // Insert a copy of the selected node (same section type / mode / slope / size) toward the +position
        // (front) or -position (behind) side, halfway to the neighbour (or the spine end).
        private void AddNode(bool inFront)
        {
            int i = SelIndex;
            SpineNode cur = nodes[i];
            float bound = inFront ? ((i < nodes.Count - 1) ? nodes[i + 1].position : 1f)
                                  : ((i > 0) ? nodes[i - 1].position : 0f);
            if (Mathf.Abs(bound - cur.position) < 0.02f)
            {
                ScreenMessages.PostScreenMessage($"No room to add a node {(inFront ? "in front" : "behind")}.", 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            ConfigNode pre = SpineUndo.Before(this, null);
            SpineNode add = cur.Clone();
            add.position = 0.5f * (cur.position + bound);
            nodes.Add(add);
            SortNodes();
            ClampNodeSizes();   // a clone of a closed end lands inside the body, where zero isn't allowed
            selectedNode = nodes.IndexOf(add);
            RefreshSelector();
            LoadProxyFromNode();
            FollowSelected();
            RebuildAndPropagate();
            SpineUndo.After(this, null, pre);
            MonoUtilities.RefreshPartContextWindow(part);
        }

        [KSPEvent(guiActiveEditor = true, guiName = "Remove node", groupName = ProceduralPart.PAWGroupName)]
        public void RemoveNodeEvent()
        {
            if (nodes.Count <= 2)
            {
                ScreenMessages.PostScreenMessage("Spine needs at least 2 nodes.", 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            ConfigNode pre = SpineUndo.Before(this, null);
            nodes.RemoveAt(SelIndex);
            selectedNode = Mathf.Clamp((int)selectedNode, 0, nodes.Count - 1);
            ClampNodeSizes();   // dropping to two nodes can leave both ends closed -> reopen one
            RefreshSelector();
            LoadProxyFromNode();
            FollowSelected();
            RebuildAndPropagate();
            SpineUndo.After(this, null, pre);
            MonoUtilities.RefreshPartContextWindow(part);
        }

        [KSPEvent(guiActiveEditor = true, guiName = "Edit Cross Sections", groupName = ProceduralPart.PAWGroupName)]
        public void EditCrossSectionsEvent()
        {
            if (nodes.Count == 0) CrossSectionEditor.Open(CrossSectionLibrary.Resolve(null), false);
            else CrossSectionEditor.Open(nodes[SelIndex].Section, nodes[SelIndex].pinned != null);
        }

        private int SelIndex => Mathf.Clamp((int)selectedNode, 0, Mathf.Max(0, nodes.Count - 1));

        // The section dialog, if open and unedited, shows the selected node's section.
        private void FollowSelected()
        {
            if (nodes.Count > 0) CrossSectionEditor.FollowNode(nodes[SelIndex].Section, nodes[SelIndex].pinned != null);
        }

        // The editor's Spines: the ship's, and the part being carried (not on the ship yet) with its children.
        internal static List<ProceduralShapeSpine> EditorSpines()
        {
            var parts = new List<Part>();
            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null) parts.AddRange(EditorLogic.fetch.ship.parts);
            Part held = EditorLogic.SelectedPart;
            if (held != null && !parts.Contains(held))
            {
                parts.Add(held);
                parts.AddRange(held.FindChildParts<Part>(true).Where(p => !parts.Contains(p)));
            }
            var o = new List<ProceduralShapeSpine>();
            foreach (Part p in parts) o.AddRange(p.FindModulesImplementing<ProceduralShapeSpine>());
            return o;
        }

        // The custom sections the craft in the editor carries itself (its nodes' own copies, not in the shared
        // list), one per name, A-Z. `also`: a Spine to count in whether or not it's on the ship.
        internal static List<CrossSection> CraftSections(ProceduralShapeSpine also = null)
        {
            var byName = new Dictionary<string, CrossSection>();
            var spines = EditorSpines();
            if (also != null && !spines.Contains(also)) spines.Insert(0, also);
            foreach (ProceduralShapeSpine m in spines)
                foreach (SpineNode n in m.nodes)
                    if (n.pinned != null && !byName.ContainsKey(n.pinned.name)) byName[n.pinned.name] = n.pinned;
            return byName.Values.OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // What a node can pick: the built-ins, the craft's own sections, then the shared ones (a craft section
        // hides a shared one of its name: within the craft, its own wins).
        private List<CrossSection> AvailableSections()
        {
            List<CrossSection> craft = CraftSections(this);
            var names = new HashSet<string>(craft.Select(s => s.name));
            IList<CrossSection> lib = CrossSectionLibrary.All;
            var o = lib.Where(s => s.IsBuiltIn).ToList();
            o.AddRange(craft);
            o.AddRange(lib.Where(s => !s.IsBuiltIn && !names.Contains(s.name)));
            return o;
        }

        private static void AssignSection(SpineNode n, CrossSection cs)
        {
            n.section = cs.name;
            n.pinned = cs.IsBuiltIn || CrossSectionLibrary.Get(cs.name) == cs ? null : cs;
        }

        private void RefreshSectionOptions()
        {
            if (Fields[nameof(nodeProfileOpt)].uiControlEditor is UI_ChooseOption opt)
                opt.options = AvailableSections().Select(s => s.name).ToArray();
        }

        // The craft's own section `name` replaced by `with` on every node that has it, or with null dropped so
        // they use the shared section of that name.
        internal void ReplaceCraftSection(string name, CrossSection with)
        {
            bool changed = false;
            foreach (SpineNode n in nodes)
                if (n.pinned != null && n.pinned.name == name) { n.pinned = with; changed = true; }
            if (!changed) return;
            DropCustomIcon(name);
            UpdateShape();
            UpdateInterops();
            RefreshSectionOptions();
            LoadProxyFromNode();
            if (part.PartActionWindow != null) MonoUtilities.RefreshPartContextWindow(part);
        }

        // The first/last nodes are the spine ends -- pinned fore/aft, can't be repositioned along the axis.
        private bool IsEndNode(int i) => i == 0 || i == nodes.Count - 1;

        #endregion

        public override string ShapeKey
        {
            get
            {
                var sb = new StringBuilder("PP-Spine|").Append(length).Append('|').Append(hScale).Append('|').Append(vScale)
                    .Append('|').Append(shellMode ? 1 : 0).Append(',').Append(shellThickness);
                foreach (SpineNode n in nodes)
                    sb.Append('|').Append(n.position).Append(',').Append(n.sizeH).Append(',').Append(n.sizeV)
                      .Append(',').Append(n.offsetV).Append(',').Append(n.offsetH).Append(',').Append(n.tiltV).Append(',').Append(n.tiltH)
                      .Append(',').Append(n.Section.Key).Append(',').Append((int)n.slopeAbove);
                return sb.ToString();
            }
        }

        #region Serialization

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            // Custom sections travel with the craft, and its nodes keep the embedded geometry (pinned): loading
            // a craft leaves the shared list alone. In the editor, one that is a shared section as it is now or
            // was before an edit uses the shared one instead, so the craft follows the edit.
            bool editor = HighLogic.LoadedSceneIsEditor;
            var embedded = new Dictionary<string, CrossSection>();
            foreach (ConfigNode c in node.GetNodes(CrossSectionLibrary.NodeName))
            {
                CrossSection cs = CrossSection.FromNode(c);
                if (cs == null) { Debug.LogWarning($"{ModTag} {part?.name}: ignoring invalid embedded cross-section \"{c.GetValue("name")}\""); continue; }
                if (editor && CrossSectionLibrary.SharedVersionOf(cs) != null) continue;
                embedded[cs.name] = cs;
            }
            ConfigNode[] cfgs = node.GetNodes("SPINE_NODE");
            if (cfgs != null && cfgs.Length > 0)
            {
                nodes.Clear();
                foreach (ConfigNode c in cfgs)
                {
                    SpineNode n = new SpineNode();
                    n.Load(c);
                    if (embedded.TryGetValue(n.section, out CrossSection pin)) n.pinned = pin;
                    nodes.Add(n);
                }
                SortNodes();
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            foreach (SpineNode n in nodes)
                n.Save(node.AddNode("SPINE_NODE"));
            var embedded = new HashSet<string>();
            foreach (SpineNode n in nodes)
            {
                CrossSection cs = n.Section;   // a missing section resolves to the built-in Ellipse: nothing to embed
                if (!cs.IsBuiltIn && embedded.Add(cs.name)) node.AddNode(cs.ToNode());
            }
        }

        // Editor copy (alt-drag / clone) copies KSPFields but not our node list, so carry it across here.
        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);
            if (fromModule is ProceduralShapeSpine src)
            {
                nodes.Clear();
                foreach (SpineNode n in src.nodes) nodes.Add(n.Clone());
                selectedNode = src.selectedNode;
            }
        }

        private void SortNodes()
        {
            nodes.Sort((a, b) => a.position.CompareTo(b.position));
            NormalizeEnds();
        }

        // The first/last nodes are the spine ends and are pinned at 0/1. Enforcing it here (rather than
        // only assuming it in the gizmo) keeps the mesh extent aligned with the stack attach nodes even
        // after a craft loads with off-end positions, or RemoveNode promotes an interior node to an end.
        private void NormalizeEnds()
        {
            if (nodes.Count < 2) return;
            nodes[0].position = 0f;
            nodes[nodes.Count - 1].position = 1f;
        }

        // Provide a sensible default spine if the part config / craft supplied none.
        private void EnsureNodes()
        {
            if (nodes.Count >= 2) return;
            nodes.Clear();
            nodes.Add(new SpineNode(0f, 1.25f, 1.25f));
            nodes.Add(new SpineNode(0.5f, 1.6f, 1.0f));
            nodes.Add(new SpineNode(1f, 0.8f, 0.8f));
        }

        #endregion

        #region Initialization

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            EnsureNodes();
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
                Fields[nameof(length)].uiControlEditor.onFieldChanged = OnDimensionChanged;
                Fields[nameof(hScale)].uiControlEditor.onFieldChanged = OnDimensionChanged;
                Fields[nameof(vScale)].uiControlEditor.onFieldChanged = OnDimensionChanged;

                Fields[nameof(selectedNode)].uiControlEditor.onFieldChanged = OnSelectedNodeChanged;
                UI_ChooseOption slopeOpt = Fields[nameof(nodeSlopeOpt)].uiControlEditor as UI_ChooseOption;
                slopeOpt.options = Enum.GetNames(typeof(SpineSlope));
                slopeOpt.onFieldChanged = OnNodeFieldChanged;
                UI_ChooseOption profOpt = Fields[nameof(nodeProfileOpt)].uiControlEditor as UI_ChooseOption;
                profOpt.options = AvailableSections().Select(s => s.name).ToArray();
                profOpt.onFieldChanged = OnNodeFieldChanged;
                if (!_subscribed) { CrossSectionLibrary.Changed += OnSectionLibraryChanged; _subscribed = true; }
                Fields[nameof(nodeWidth)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeHeight)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeTiltV)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeTiltH)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodePos)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeOffsetV)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeOffsetH)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(shellMode)].uiControlEditor.onFieldChanged = (f, o) => Undoable(f, o, () => { UpdateShellVisibility(); RebuildAndPropagate(); });
                Fields[nameof(shellThickness)].uiControlEditor.onFieldChanged = (f, o) => Undoable(f, o, RebuildAndPropagate);

                UpdateShellVisibility();

                RefreshSelector();
                LoadProxyFromNode();
            }
        }

        private bool _subscribed;

        // A PAW field's change as an undo step (SpineUndo): the ship recorded as it was before (the field still
        // at its old value, nothing else changed yet), the change, the step recorded. Repeats of the field on
        // the same node join one step.
        private void Undoable(BaseField f, object old, Action change)
        {
            // the PAW re-sends a value now and then (after a rebuild refreshes it) a rounding off: not a step
            object cur = f.GetValue(this);
            if (Equals(cur, old) || cur is float a && old is float b && Mathf.Abs(a - b) <= 1e-4f * Mathf.Max(1f, Mathf.Abs(a)))
            { change(); return; }
            string key = f.name + "#" + SelIndex;
            ConfigNode pre = null;
            if (SpineUndo.WantsBefore(this, key))
            {
                // the PAW sets a field that affects counterparts on them before calling this: theirs back too
                var hosts = new List<ProceduralShapeSpine> { this };
                if ((f.uiControlEditor.affectSymCounterparts & UI_Scene.Editor) != 0)
                    foreach (Part p in part.symmetryCounterparts)
                        if (FindAbstractShapeModule(p, this) is ProceduralShapeSpine m) hosts.Add(m);
                object[] now = hosts.Select(h => h.Fields[f.name].GetValue(h)).ToArray();
                foreach (ProceduralShapeSpine h in hosts) h.Fields[f.name].SetValue(old, h);
                try { pre = SpineUndo.Snapshot(); }
                finally { for (int i = 0; i < hosts.Count; i++) hosts[i].Fields[f.name].SetValue(now[i], hosts[i]); }
            }
            change();
            SpineUndo.After(this, key, pre);
        }

        private void OnDimensionChanged(BaseField f, object old) => Undoable(f, old, () => OnShapeDimensionChanged(f, old));

        // A custom section was saved / changed / deleted: refresh the Section list, and re-loft if any
        // node uses it. Symmetry counterparts get their own call, so no SyncToSymmetry here.
        private void OnSectionLibraryChanged(string name)
        {
            if (this == null || part == null) return;
            RefreshSectionOptions();
            DropCustomIcon(name);
            if (PPart != null && PPart.CurrentShape == this && nodes.Exists(n => n.section == name && n.pinned == null))
            {
                UpdateShape();
                UpdateInterops();
            }
            LoadProxyFromNode();
            if (part.PartActionWindow != null) MonoUtilities.RefreshPartContextWindow(part);
        }

        public override void UpdateTechConstraints()
        {
            EnsureNodes();
            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            UI_FloatEdit hEdit = Fields[nameof(hScale)].uiControlEditor as UI_FloatEdit;
            UI_FloatEdit vEdit = Fields[nameof(vScale)].uiControlEditor as UI_FloatEdit;
            hEdit.incrementLarge = vEdit.incrementLarge = 0.25f;
            hEdit.incrementSmall = vEdit.incrementSmall = 0.05f;

            foreach (string fn in new[] { nameof(nodeWidth), nameof(nodeHeight) })
            {
                UI_FloatEdit e = Fields[fn].uiControlEditor as UI_FloatEdit;
                e.maxValue = MaxSize;
                e.incrementLarge = PPart.diameterLargeStep;
                e.incrementSmall = PPart.diameterSmallStep;
            }
            UpdateSizeFieldBounds();

            ClampNodeSizes();   // heal any out-of-range sizes from older builds/crafts
            AdjustDimensionBounds();
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            hScale = Mathf.Clamp(hScale, hEdit.minValue, hEdit.maxValue);
            vScale = Mathf.Clamp(vScale, vEdit.minValue, vEdit.maxValue);
            LoadProxyFromNode();
        }

        #endregion

        #region Update

        // Set while a rebuild is in flight. The Volume setter fires onEditorShipModified, which can
        // synchronously rebuild the PAW and re-fire our proxy field handlers -> guard against that
        // re-entering and recursively mangling the node sizes (it was a stack overflow / crash).
        private bool _rebuilding;

        public override void UpdateShape(bool force = true)
        {
            Profiler.BeginSample("UpdateShape Spine");
            bool prevRebuilding = _rebuilding;
            _rebuilding = true;
            try
            {
            EnsureNodes();
            SortNodes();
            _shapeVersion++;   // the outlines redraw
            part.CoMOffset = CoMOffset;
            if (HighLogic.LoadedSceneIsEditor && shellMode) UpdateShellThicknessRange();

            Loft built = BuildLoft();
            List<Ring> rings = built.rings;
            _built = built;   // the skin attachments sit on, till the next build
            float maxDia = 0f, minDia = float.MaxValue, minBoundDia = float.MaxValue;
            foreach (Ring r in rings)
            {
                float bound = 2f * Mathf.Max(r.rH, r.rV);   // bounding-circle diameter of this ring
                maxDia = Mathf.Max(maxDia, bound);
                minBoundDia = Mathf.Min(minBoundDia, bound);
                minDia = Mathf.Min(minDia, 2f * Mathf.Min(r.rH, r.rV));
            }
            MaxDiameter = maxDia;
            MinDiameter = minDia;
            // Smallest bounding-circle diameter over the spine (widest extent of the narrowest ring). Paired
            // with MaxDiameter it gives RP-1 tooling the min/max bounding cylinders; equal for a constant
            // section (even a non-circular one), so such a spine tools as a single cylinder, not two.
            MinBoundingDiameter = (minBoundDia == float.MaxValue) ? maxDia : minBoundDia;
            InnerMaxDiameter = InnerMinDiameter = -1f;
            Length = length;

            List<Ring> innerRings = shellMode ? BuildInnerRings(rings) : null;   // the shell wall, once: mesh and volume
            GenerateSideMesh(rings, innerRings);
            GenerateCapMesh(rings);          // emits empty end meshes when shellMode (open ends)
            GenerateColliderMesh(rings);

            UpdateNodeSize(BottomNodeName, EndDiameter(0));
            UpdateNodeSize(TopNodeName, EndDiameter(rings.Count - 1));

            // Set volume last (the setter fires OnPartVolumeChanged / onEditorShipModified) so the
            // visible mesh is already updated even if a volume listener throws.
            NominalVolume = CalculateVolume(rings, innerRings);
            Volume = NominalVolume;

            PPart.UpdateProps();
            RaiseModelAndColliderChanged();
            }
            finally
            {
                _rebuilding = prevRebuilding;
                Profiler.EndSample();
            }
        }

        // A loft ring: position + half-extents (for diameter) + centre offsets + tilt (the ring plane is
        // sheared along the spine by tan(tilt) per unit of in-plane coordinate) + the precomputed
        // Sides-point boundary outline (profile morph already applied).
        private struct Ring { public float y, rH, rV, offsetZ, offsetX, tanTiltV, tanTiltH; public Vector2[] outline; }

        // 3D vertex of ring outline point j (index wraps): outline (x=width, y=height) placed at the ring's
        // y and shifted by its centre offsets, with the plane sheared along the spine by the tilts (top/right
        // of the section move fore/aft). Tilt uses the point's in-plane coords so it pivots about the centre.
        private static Vector3 RingV(Ring ring, int j)
        {
            int n = ring.outline.Length;
            Vector2 p = ring.outline[((j % n) + n) % n];
            float y = ring.y + p.y * ring.tanTiltV + p.x * ring.tanTiltH;
            return new Vector3(p.x + ring.offsetX, y, p.y + ring.offsetZ);
        }

        // Sides-point outline of a ring, morphing section pa->pb by blend, scaled to rH x rV.
        private static Vector2[] RingOutline(CrossSection pa, CrossSection pb, float blend, float rH, float rV, int n)
        {
            Vector2[] a = pa.Outline(rH, rV, n);
            if (blend <= 0f || pa == pb) return a;
            Vector2[] b = pb.Outline(rH, rV, n);
            var o = new Vector2[n];
            for (int j = 0; j < n; j++) o[j] = Vector2.Lerp(a[j], b[j], blend);
            return o;
        }

        // Sides boundary points of a profile scaled to rH x rV. Each profile has a FIXED Sides-point
        // topology (its corners forced to vertices) so flats stay flat, corners stay crisp, and rings
        // of the same profile loft with aligned edges.
        internal static Vector2[] ProfileOutline(SpineProfile profile, float rH, float rV, int n)
        {
            switch (profile)
            {
                case SpineProfile.Rectangle: return BuildRect(rH, rV, n);
                case SpineProfile.Mk3: return BuildMk3Outline(rH, rV, n);
            }

            Vector2[] unit = (profile == SpineProfile.Mk2) ? UnitMk2(n) : UnitCircle(n);   // Mk2 / plain Ellipse
            Vector2 scale = new Vector2(rH, rV);
            var o = new Vector2[n];
            for (int j = 0; j < n; j++) o[j] = Vector2.Scale(unit[j], scale);
            return o;
        }

        // Rectangle: half-extents (rH, rV), its 4 corners forced sharp, starting mid right edge.
        private static Vector2[] BuildRect(float rH, float rV, int n)
        {
            var pts = new List<Vector2>
            {
                new Vector2(rH, 0f), new Vector2(rH, rV), new Vector2(-rH, rV), new Vector2(-rH, -rV), new Vector2(rH, -rV),
            };
            return ResampleWithCorners(pts, new List<int> { 1, 2, 3, 4 }, n);
        }

        // Mk3: a vertical circle (radius = rV) with its left/right sides sliced off flat at x = +/-rH, so
        // the top/bottom are arcs and the sides are flats (the real Mk3 is a 3.75 m circle cut to 3.25 m
        // wide). The 4 flat/arc junctions are forced sharp. If rH >= rV there's no truncation -> ellipse.
        private static Vector2[] BuildMk3Outline(float rH, float rV, int n)
        {
            float R = rV;
            if (rH >= R)
            {
                var e = new Vector2[n];
                for (int j = 0; j < n; j++) { float a = 2f * Mathf.PI * j / n; e[j] = new Vector2(Mathf.Cos(a) * rH, Mathf.Sin(a) * rV); }
                return e;
            }
            float yc = Mathf.Sqrt(Mathf.Max(0f, R * R - rH * rH));   // flat/arc junction height
            float tc = Mathf.Atan2(yc, rH);                          // junction angle
            const int arc = 10;
            var pts = new List<Vector2>();
            var corners = new List<int>();
            corners.Add(pts.Count); pts.Add(new Vector2(rH, -yc));   // right flat, bottom
            corners.Add(pts.Count); pts.Add(new Vector2(rH, yc));    // right flat, top
            for (int i = 1; i < arc; i++) { float a = Mathf.Lerp(tc, Mathf.PI - tc, (float)i / arc); pts.Add(new Vector2(Mathf.Cos(a) * R, Mathf.Sin(a) * R)); }  // top arc
            corners.Add(pts.Count); pts.Add(new Vector2(-rH, yc));   // left flat, top
            corners.Add(pts.Count); pts.Add(new Vector2(-rH, -yc));  // left flat, bottom
            for (int i = 1; i < arc; i++) { float a = Mathf.Lerp(Mathf.PI + tc, 2f * Mathf.PI - tc, (float)i / arc); pts.Add(new Vector2(Mathf.Cos(a) * R, Mathf.Sin(a) * R)); }  // bottom arc
            return ResampleWithCorners(pts, corners, n);
        }

        // Unit outlines in the [-1,1] box, n points each, starting near +x going CCW (cached per n).
        private static readonly Dictionary<int, Vector2[]> _unitCircle = new Dictionary<int, Vector2[]>(), _unitMk2 = new Dictionary<int, Vector2[]>();
        private static Vector2[] UnitCircle(int n)
        {
            if (!_unitCircle.TryGetValue(n, out Vector2[] u))
            {
                _unitCircle[n] = u = new Vector2[n];
                for (int j = 0; j < n; j++) { float a = 2f * Mathf.PI * j / n; u[j] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
            }
            return u;
        }
        private static Vector2[] UnitMk2(int n)
        {
            if (!_unitMk2.TryGetValue(n, out Vector2[] u)) _unitMk2[n] = u = BuildMk2Unit(n);
            return u;
        }

        // Resample a closed polygon to N points by arc length, forcing the corner vertices to be kept
        // (flats stay flat, corners stay crisp).
        internal static Vector2[] ResampleWithCorners(IList<Vector2> verts, IList<int> cornerIdx, int N)
        {
            int M = verts.Count;
            float[] arcAt = new float[M + 1];
            for (int i = 0; i < M; i++) arcAt[i + 1] = arcAt[i] + (verts[(i + 1) % M] - verts[i]).magnitude;
            float perim = arcAt[M];
            var targets = new List<float>();
            foreach (int ci in cornerIdx) targets.Add(arcAt[ci]);
            int fills = Mathf.Max(0, N - cornerIdx.Count);
            for (int i = 0; i < fills; i++) targets.Add((i + 0.5f) / fills * perim);
            targets.Sort();
            var outp = new Vector2[N];
            for (int k = 0; k < N; k++) outp[k] = PointAtArc(verts, arcAt, perim, targets[k]);
            return outp;
        }

        private static Vector2 PointAtArc(IList<Vector2> verts, float[] arcAt, float perim, float arc)
        {
            int M = verts.Count;
            arc = Mathf.Clamp(arc, 0f, perim);
            for (int i = 0; i < M; i++)
            {
                if (arc <= arcAt[i + 1] + 1e-6f)
                {
                    float seg = arcAt[i + 1] - arcAt[i];
                    float u = (seg > 1e-9f) ? (arc - arcAt[i]) / seg : 0f;
                    return Vector2.Lerp(verts[i], verts[(i + 1) % M], u);
                }
            }
            return verts[0];
        }

        // --- Mk2 cross-section (ported from ProceduralShapeMk2) ---
        // Proportions as ratios of the half-width, calibrated against the real Mk2.
        private const float Mk2HeightRatio = 0.6047f;    // full height / full width
        private const float Mk2SideFlatRatio = 0.11907f; // side-flat half-height / half-width
        private const float Mk2RoundRatio = 0.63628f;    // top/bottom arc radius / half-width

        // Mk2 silhouette normalized into [-1,1]^2, its Sides points placed by hand rather than by arc length (which
        // spent most of them on the straight diagonals, left the arcs a few, and wasn't mirror-symmetric): the
        // mid-sides, the 4 shoulder corners and the 4 diagonal/arc tangent points exactly, nothing inside the
        // straight runs, and the rest evenly by angle along the top and bottom arcs. Symmetric both ways, starting
        // at +x, with the arc tops at Sides/4 and 3*Sides/4 where the ellipse has its top and bottom.
        private static Vector2[] BuildMk2Unit(int n)
        {
            const float hw = 1f;
            float hh = Mk2HeightRatio * hw, sf = Mk2SideFlatRatio * hw, tr = Mk2RoundRatio * hw;
            Vector2 cTop = new Vector2(0f, hh - tr);
            Vector2 tUr = Mk2Tangent(new Vector2(hw, sf), cTop, tr);
            float thR = Mathf.Atan2(tUr.y - cTop.y, tUr.x - cTop.x), thL = Mathf.PI - thR;
            int half = n / 2, arcIn = half - 5;   // the upper half: 0..half, of which 5 are fixed points

            var o = new Vector2[n];
            o[0] = new Vector2(hw, 0f);                    // mid right side
            o[1] = new Vector2(hw, sf);                    // right-top shoulder
            o[2] = tUr;                                    // diagonal meets the top arc
            for (int k = 1; k <= arcIn; k++)
            {
                float th = thR + (thL - thR) * k / (arcIn + 1);
                o[2 + k] = cTop + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * tr;
            }
            o[half - 2] = new Vector2(-tUr.x, tUr.y);
            o[half - 1] = new Vector2(-hw, sf);            // left-top shoulder
            o[half] = new Vector2(-hw, 0f);                // mid left side
            for (int j = 1; j < half; j++) o[n - j] = new Vector2(o[j].x, -o[j].y);   // lower half mirrors the upper
            for (int i = 0; i < n; i++) o[i] = new Vector2(o[i].x, o[i].y / hh);
            return o;
        }

        // Tangent point on circle (centre C, radius r) from external point P, toward the cap apex.
        private static Vector2 Mk2Tangent(Vector2 P, Vector2 C, float r)
        {
            Vector2 d = P - C;
            float dist = d.magnitude;
            float a = Mathf.Atan2(d.y, d.x);
            float b = Mathf.Acos(Mathf.Clamp(r / dist, -1f, 1f));
            Vector2 t1 = C + new Vector2(Mathf.Cos(a + b), Mathf.Sin(a + b)) * r;
            Vector2 t2 = C + new Vector2(Mathf.Cos(a - b), Mathf.Sin(a - b)) * r;
            return (Mathf.Abs(t1.y) >= Mathf.Abs(t2.y)) ? t1 : t2;
        }

        // Inset a (CCW) outline inward by `t` metres along the local inward normal -- the shell's inner
        // wall. Interior is to the LEFT of edge travel for a CCW polygon, so an edge (dx,dy)'s inward
        // normal is (-dy,dx); a vertex uses the averaged normal of its two edges. Averaging undershoots
        // slightly at corners (fine for a wall). Caller clamps `t` so a thick wall can't invert a small ring.
        private static Vector2[] InsetOutline(Vector2[] o, float t)
        {
            int n = o.Length;
            var res = new Vector2[n];
            for (int j = 0; j < n; j++)
            {
                Vector2 prev = o[(j - 1 + n) % n], cur = o[j], next = o[(j + 1) % n];
                Vector2 e1 = cur - prev, e2 = next - cur;
                Vector2 in1 = new Vector2(-e1.y, e1.x); if (in1.sqrMagnitude > 1e-12f) in1.Normalize();
                Vector2 in2 = new Vector2(-e2.y, e2.x); if (in2.sqrMagnitude > 1e-12f) in2.Normalize();
                Vector2 inward = in1 + in2;
                inward = (inward.sqrMagnitude > 1e-12f) ? inward.normalized : in2;
                res[j] = cur + inward * t;
            }
            return res;
        }

        // A ring's inner wall: same placement (y / offsets / tilt), outline inset by the shell thickness,
        // clamped so a thick wall on a small section can't collapse or invert the hole. That clamp is enough
        // for a convex section (every built-in); a concave custom one (thin arms, notches) can tangle at a
        // smaller inset, so there the wall is thinned until the inner outline is clean.
        private Ring InnerRing(Ring outer)
        {
            float t = Mathf.Min(shellThickness, ShellInsetFraction * Mathf.Min(outer.rH, outer.rV));
            Ring inner = outer;                       // struct copy carries y/offsets/tilt
            inner.outline = InsetOutline(outer.outline, t);
            if (InsetOk(outer.outline, inner.outline)) return inner;
            float lo = 0f, hi = t;
            Vector2[] best = (Vector2[])outer.outline.Clone();   // zero wall: always valid
            for (int it = 0; it < 8; it++)
            {
                float mid = 0.5f * (lo + hi);
                Vector2[] o = InsetOutline(outer.outline, mid);
                if (InsetOk(outer.outline, o)) { lo = mid; best = o; }
                else hi = mid;
            }
            inner.outline = best;
            return inner;
        }

        // The inset outline is usable: no edge flipped direction, and for a concave outline also no edge
        // crossing another inner edge or the outer outline.
        private static bool InsetOk(Vector2[] outer, Vector2[] inner)
        {
            int n = outer.Length;
            bool convex = true;
            for (int j = 0; j < n; j++)
            {
                Vector2 e = outer[(j + 1) % n] - outer[j];
                if (e.sqrMagnitude > 1e-12f && Vector2.Dot(inner[(j + 1) % n] - inner[j], e) <= 0f) return false;
                Vector2 f = outer[(j + 2) % n] - outer[(j + 1) % n];
                if (e.x * f.y - e.y * f.x < -1e-6f * (e.sqrMagnitude + f.sqrMagnitude)) convex = false;
            }
            if (convex) return true;
            return !InnerCrosses(outer, inner);
        }

        // Does an inner edge cross an outer edge, or a non-adjacent inner edge? A sweep along the outline's long
        // axis, so only edges side by side along it are tested: a long thin section (a waterline) of n points costs
        // about n pairs, not n squared, and the shell's thinning search runs this many times per ring.
        private static bool InnerCrosses(Vector2[] outer, Vector2[] inner)
        {
            int n = outer.Length;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in outer) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            int ax = (maxX - minX >= maxY - minY) ? 0 : 1, bx = 1 - ax;
            // edge e < n: inner edge e; e >= n: outer edge e - n
            Vector2 A(int e) => e < n ? inner[e] : outer[e - n];
            Vector2 B(int e) => e < n ? inner[(e + 1) % n] : outer[(e - n + 1) % n];
            var order = new int[2 * n];
            var lo = new float[2 * n];
            for (int e = 0; e < 2 * n; e++) { order[e] = e; lo[e] = Mathf.Min(A(e)[ax], B(e)[ax]); }
            Array.Sort(lo, order);
            var active = new List<int>();
            for (int s = 0; s < order.Length; s++)
            {
                int e = order[s];
                Vector2 a = A(e), b = B(e);
                float eLo = lo[s], eMinB = Mathf.Min(a[bx], b[bx]), eMaxB = Mathf.Max(a[bx], b[bx]);
                active.RemoveAll(f => Mathf.Max(A(f)[ax], B(f)[ax]) < eLo);
                foreach (int f in active)
                {
                    if (e >= n && f >= n) continue;                     // outer against outer: the section itself
                    if (e < n && f < n)
                    {
                        int d = Math.Abs(e - f);
                        if (d <= 1 || d == n - 1) continue;             // adjacent inner edges share a vertex
                    }
                    Vector2 c = A(f), dd = B(f);
                    if (Mathf.Max(c[bx], dd[bx]) < eMinB || Mathf.Min(c[bx], dd[bx]) > eMaxB) continue;
                    if (SectionGeometry.SegmentsCross(a, b, c, dd)) return true;
                }
                active.Add(e);
            }
            return false;
        }

        private List<Ring> BuildInnerRings(List<Ring> rings)
        {
            var inner = new List<Ring>(rings.Count);
            foreach (Ring r in rings) inner.Add(InnerRing(r));
            return inner;
        }

        // Polygon area of a ring's outline (shoelace), used for an accurate volume of any profile.
        private static float RingArea(Ring ring) => Mathf.Abs(SectionGeometry.SignedArea(ring.outline));

        // Sane size bounds. A node "size" is a cross-section diameter. The hard 100 m cap on the
        // effective (scaled) radius keeps the volume well within the SI formatter's range -- an
        // absurd value made TankContentSwitcher throw "Illegal prefix".
        private float MaxSize => (PPart.diameterMax == float.PositiveInfinity) ? 50f : Mathf.Min(PPart.diameterMax, 50f);
        private float MinSize => Mathf.Max(0.1f, PPart.diameterMin);

        // A section with a zero half-extent has no area -- it has collapsed to a point (both axes) or a
        // line (one axis), so it contributes no volume and nothing renders across it.
        private static bool IsClosedSection(SpineNode n) => n.sizeH <= 0f || n.sizeV <= 0f;

        // The two end nodes may shrink to zero so the loft closes to a point (a proper cone/ogive tip);
        // interior nodes keep the normal minimum, since a pinched waist would just self-intersect.
        // Exception: a two-node spine needs one open end -- closing both leaves a point-to-point line
        // with nothing to loft, i.e. an invisible part. The second one is held at the normal minimum.
        private float MinSizeFor(int i)
        {
            if (!IsEndNode(i)) return MinSize;
            return (nodes.Count == 2 && IsClosedSection(nodes[1 - i])) ? MinSize : 0f;
        }

        // Heal any out-of-range node sizes (e.g. a part bloated by an earlier bug) back into bounds.
        private void ClampNodeSizes()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                SpineNode n = nodes[i];
                float min = MinSizeFor(i);
                n.sizeH = Mathf.Clamp(n.sizeH, min, MaxSize);
                n.sizeV = Mathf.Clamp(n.sizeV, min, MaxSize);
            }
        }

        // Effective (scaled) half-extents of a node, clamped finite/positive. The lower bound is 0 so a
        // closed end collapses to a point; interior nodes are held at MinSize by the clamps above.
        private float Erh(SpineNode n) => 0.5f * Mathf.Clamp(n.sizeH * hScale, 0f, 100f);
        private float Erv(SpineNode n) => 0.5f * Mathf.Clamp(n.sizeV * vScale, 0f, 100f);
        private float Eoff(SpineNode n) => Mathf.Clamp(n.offsetV * vScale, -100f, 100f);
        private float EoffH(SpineNode n) => Mathf.Clamp(n.offsetH * hScale, -100f, 100f);
        private static float TiltTan(float deg) => Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(deg, -85f, 85f));
        private float NodeY(SpineNode n) => (n.position - 0.5f) * length;

        private Ring NodeRing(SpineNode n)
        {
            float rH = Erh(n), rV = Erv(n);
            return new Ring
            {
                y = NodeY(n), rH = rH, rV = rV, offsetZ = Eoff(n), offsetX = EoffH(n),
                tanTiltV = TiltTan(n.tiltV), tanTiltH = TiltTan(n.tiltH),
                outline = RingOutline(n.Section, n.Section, 0f, rH, rV, Sides),
            };
        }

        // Build the loft rings, subdividing each segment per its lower node's slopeAbove so the
        // silhouette curves (concave/convex/waisted), and morphing the cross-section profile from the
        // lower node's to the upper node's across the segment. Linear/same-profile segments stay coarse.
        // `segStart`, if given, gets each segment's first ring (its lower node's) and, last, the last ring.
        private List<Ring> BuildRings(List<int> segStart = null)
        {
            EnsureNodes();
            SortNodes();
            Vector2[][] aligned = AlignedUnits();
            Ring first = NodeRing(nodes[0]);
            if (aligned != null) first.outline = Scaled(aligned[0], aligned[0], 0f, first.rH, first.rV);
            var rings = new List<Ring> { first };
            for (int i = 1; i < nodes.Count; i++)
            {
                segStart?.Add(rings.Count - 1);
                SpineNode lo = nodes[i - 1], hi = nodes[i];
                float aY = NodeY(lo), bY = NodeY(hi), aH = Erh(lo), aV = Erv(lo), bH = Erh(hi), bV = Erv(hi);
                float aOff = Eoff(lo), bOff = Eoff(hi);
                float aOffX = EoffH(lo), bOffX = EoffH(hi);
                float aTv = lo.tiltV, bTv = hi.tiltV, aTh = lo.tiltH, bTh = hi.tiltH;
                SpineSlope slope = lo.slopeAbove;
                CrossSection loS = lo.Section, hiS = hi.Section;
                bool morph = loS != hiS;
                int k = (slope == SpineSlope.Linear && !morph) ? 1 : 10;
                for (int s = 1; s <= k; s++)
                {
                    float t = (float)s / k;
                    float h = SlopeBlend(slope, t);
                    float w = WaistFactor(slope, t);
                    float rH = Mathf.Lerp(aH, bH, h) * w;
                    float rV = Mathf.Lerp(aV, bV, h) * w;
                    rings.Add(new Ring
                    {
                        y = Mathf.Lerp(aY, bY, t),
                        rH = rH,
                        rV = rV,
                        offsetZ = Mathf.Lerp(aOff, bOff, h),
                        offsetX = Mathf.Lerp(aOffX, bOffX, h),
                        tanTiltV = TiltTan(Mathf.Lerp(aTv, bTv, h)),
                        tanTiltH = TiltTan(Mathf.Lerp(aTh, bTh, h)),
                        outline = aligned != null
                            ? Scaled(aligned[i - 1], aligned[i], t, rH, rV)
                            : RingOutline(loS, hiS, t, rH, rV, Sides),
                    });
                }
            }
            segStart?.Add(rings.Count - 1);
            foreach (Ring r in rings) if (Collapsed(r)) Unfold(r.outline, r.rH < r.rV ? 1 : 0);
            return rings;
        }

        // A ring squashed to a line (a wedge's stem) lays its outline along that line. Where the section goes back
        // on itself in that direction (a keel lower off the centreline, a Mk3's bottom), the points double back and
        // the strip into the stem folds inside out. Walk each side from one end of the line to the other and hold a
        // point that would turn back on the furthest one so far: those triangles go flat instead of inverted.
        private static void Unfold(Vector2[] o, int axis)
        {
            int n = o.Length;
            if (n < 3) return;
            int lo = 0, hi = 0;
            for (int j = 1; j < n; j++)
            {
                if (o[j][axis] < o[lo][axis]) lo = j;
                if (o[j][axis] > o[hi][axis]) hi = j;
            }
            if (lo == hi) return;
            // lo -> hi going forward (counter-clockwise), then hi -> lo: up one side, down the other
            float run = o[lo][axis];
            for (int j = (lo + 1) % n; j != hi; j = (j + 1) % n) { run = Mathf.Max(run, o[j][axis]); o[j][axis] = run; }
            run = o[hi][axis];
            for (int j = (hi + 1) % n; j != lo; j = (j + 1) % n) { run = Mathf.Min(run, o[j][axis]); o[j][axis] = run; }
        }

        // A spine with any custom section pairs its rings' points by best fit (SectionAlignment): each node's unit
        // outline sampled so point k sits on matching features all along, whatever the vertex counts. Built-in-only
        // spines keep the built-ins' fixed layouts (which line up by construction), so they're unchanged. Cached on
        // what the alignment depends on: the sections, and a built-in's aspect where that shapes it.
        private string _alignKey;
        private Vector2[][] _alignUnits;

        private Vector2[][] AlignedUnits()
        {
            if (nodes.TrueForAll(n => n.Section.IsBuiltIn)) { _alignKey = null; _alignUnits = null; return null; }
            var key = new System.Text.StringBuilder().Append(Sides);
            foreach (SpineNode n in nodes)
            {
                CrossSection s = n.Section;
                key.Append('|').Append(s.Key);
                if (s.builtIn == SpineProfile.Rectangle || s.builtIn == SpineProfile.Mk3)
                    key.Append(',').Append((AlignExtent(Erh(n)) / AlignExtent(Erv(n))).ToString("F3"));
            }
            string k = key.ToString();
            if (k == _alignKey && _alignUnits != null) return _alignUnits;

            var shapes = new List<SectionAlignment.Shape>(nodes.Count);
            foreach (SpineNode n in nodes)
            {
                CrossSection s = n.Section;
                if (!s.IsBuiltIn)
                {
                    shapes.Add(new SectionAlignment.Shape { poly = s.verts.ToArray() });
                    continue;
                }
                // a built-in densely, back in the unit box (a rounded rectangle's corners depend on its aspect)
                float a = AlignExtent(Erh(n)), b = AlignExtent(Erv(n));
                Vector2[] o = s.Outline(a, b, Mathf.Max(4 * Sides, 96));
                for (int j = 0; j < o.Length; j++) o[j] = new Vector2(o[j].x / a, o[j].y / b);
                shapes.Add(new SectionAlignment.Shape { poly = o, samplesOnly = true });
            }
            _alignUnits = SectionAlignment.Align(shapes, Sides);
            _alignKey = k;
            return _alignUnits;
        }

        private static float AlignExtent(float r) => Mathf.Max(r, 1e-3f);

        // Unit outlines a and b lerped by t, scaled to rH x rV.
        private static Vector2[] Scaled(Vector2[] a, Vector2[] b, float t, float rH, float rV)
        {
            var o = new Vector2[a.Length];
            for (int j = 0; j < o.Length; j++)
            {
                Vector2 p = Vector2.Lerp(a[j], b[j], t);
                o[j] = new Vector2(p.x * rH, p.y * rV);
            }
            return o;
        }

        // Radius blend across a segment, t in [0,1]. Maps the straight 0..1 chord into a curve.
        private static float SlopeBlend(SpineSlope slope, float t)
        {
            switch (slope)
            {
                case SpineSlope.Concave: return t * t;            // caves inward (below the chord)
                case SpineSlope.Convex: return t * (2f - t);      // bulges outward (above the chord)
                default: return t;                                 // Linear / Waisted use the chord
            }
        }

        // Multiplicative pinch for the waisted profile (narrow in the middle), 1 otherwise.
        private static float WaistFactor(SpineSlope slope, float t) =>
            slope == SpineSlope.Waisted ? 1f - 0.35f * Mathf.Sin(Mathf.PI * t) : 1f;

        // Bounding diameter of an end ring (used for stack node sizing).
        private float EndDiameter(int ringIndex)
        {
            EnsureNodes();
            SpineNode n = nodes[Mathf.Clamp(ringIndex, 0, nodes.Count - 1)];
            return Mathf.Max(n.sizeH * hScale, n.sizeV * vScale);
        }

        public override float CalculateVolume() => CalculateVolume(BuildRings());

        // Trapezoidal integration of the material cross-section area along the spine -- correct for any
        // profile (ellipse/rect/Mk2/Mk3). Solid uses the full outline area; a shell uses only the wall,
        // i.e. the outer section minus the inner (inset) section from the same InnerRing the mesh builds.
        // That respects the thickness clamp and can't double-count once the wall is a big fraction of the
        // radius (perimeter*thickness overcounts the corners there). Both the Volume property and this
        // override use this single definition so SeekVolume / volume bounds stay self-consistent.
        // `inner`: the shell's inner rings, if already built (the mesh's), else built here. The inner skin lerps its
        // vertices between them just as the outer does, so its midpoint section is their average too.
        private float CalculateVolume(List<Ring> rings, List<Ring> inner = null)
        {
            if (shellMode) inner ??= BuildInnerRings(rings);
            float Area(int i) => shellMode ? Mathf.Max(0f, RingArea(rings[i]) - RingArea(inner[i])) : RingArea(rings[i]);
            float v = 0f;
            float prevArea = Area(0);
            for (int i = 1; i < rings.Count; i++)
            {
                float area = Area(i);
                // Simpson rather than trapezoid: the loft interpolates its vertices linearly between two
                // rings, so the section area varies QUADRATICALLY along the segment (a product of two
                // linear half-extents). The trapezoid rule over-reports every taper -- by 50% on a segment
                // that closes to a point -- and a Linear segment isn't subdivided, so there's no ring
                // density to hide it. Simpson is exact for a quadratic, so this matches the real mesh.
                float midArea = RingArea(MidRing(rings[i - 1], rings[i]));
                if (shellMode) midArea = Mathf.Max(0f, midArea - RingArea(MidRing(inner[i - 1], inner[i])));
                v += (rings[i].y - rings[i - 1].y) * (prevArea + 4f * midArea + area) / 6f;
                prevArea = area;
            }
            return Mathf.Abs(v);
        }

        // The section halfway between two rings. The mesh lerps its vertices, so the midpoint outline is
        // the per-point average; only the outline and half-extents matter here (area, and the shell's
        // inset clamp). Placement fields are irrelevant -- the outline is in local section coordinates.
        private static Ring MidRing(Ring a, Ring b)
        {
            Ring m = a;
            m.rH = 0.5f * (a.rH + b.rH);
            m.rV = 0.5f * (a.rV + b.rV);
            var o = new Vector2[a.outline.Length];
            for (int j = 0; j < o.Length; j++) o[j] = 0.5f * (a.outline[j] + b.outline[j]);
            m.outline = o;
            return m;
        }

        public override void AdjustDimensionBounds()
        {
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = PPart.lengthMin;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = PPart.lengthMax;

            // Scale ranges are derived from the part's diameter limits relative to the largest node.
            float largest = 0.01f;
            foreach (SpineNode n in nodes) largest = Mathf.Max(largest, Mathf.Max(n.sizeH, n.sizeV));
            float minScale = Mathf.Max(0.05f, PPart.diameterMin / largest);
            float maxScale = (PPart.diameterMax == float.PositiveInfinity) ? 10f : PPart.diameterMax / largest;
            foreach (string fn in new[] { nameof(hScale), nameof(vScale) })
            {
                (Fields[fn].uiControlEditor as UI_FloatEdit).minValue = minScale;
                (Fields[fn].uiControlEditor as UI_FloatEdit).maxValue = Mathf.Max(minScale + 0.1f, maxScale);
            }
        }

        public override bool SeekVolume(float targetVolume, int dir) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new object[] { part, "diam1", MaxDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new object[] { part, "diam2", MaxDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new object[] { part, "length", length, "ProceduralParts" });
        }

        // Length: the stack nodes (and what's on them) move with the ends as Procedural Parts moves them; surface
        // attachments keep their spots on the skin (ReplaceAttachments), as do they for the scales. PP calls this
        // on each symmetry counterpart itself.
        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            bool len = f.name == nameof(length), scale = f.name == nameof(hScale) || f.name == nameof(vScale);
            if (!(obj is float old) || (!len && !scale) || (scale && old <= 0f)) return;
            object now = f.GetValue(this);
            f.SetValue(old, this);   // the skin as it was (BuildRings reads the fields)
            Loft was = BuildLoft();
            f.SetValue(now, this);
            List<AttachSnap> snaps = SnapshotAttachments(counterparts: false, L: was);
            if (len)
            {
                HandleLengthChange((float)now, old);
                snaps.RemoveAll(s => s.kind == SnapKind.Stack);
            }
            ReplaceAttachments(snaps);
        }

        // Phase-1 circular approximation: normalize by the widest radius. Phase 2 makes this
        // per-(angle, height) against the actual elliptical rings for accurate surface attach.
        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            float r = Mathf.Max(0.01f, MaxDiameter / 2);
            coords.r /= r;
            coords.y /= Mathf.Max(0.01f, length);
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= Mathf.Max(0.01f, MaxDiameter / 2);
            coords.y *= Mathf.Max(0.01f, length);
        }

        // --- Attachment following ---------------------------------------------------------------------
        // An edit changes the skin, and what's attached should stay on it. Every rebuild (RebuildAndPropagate, and
        // RebuildLive during a gizmo drag) snapshots the attachments against the skin as last built (_built: what
        // they sit on), rebuilds, then re-places them on the new one (ReplaceAttachments) -- whatever the edit was.
        // A stack part follows its end's section centre. A surface-attached part keeps its
        // spot on the skin -- the segment it's on (by its two nodes), how far along, and where round the section --
        // and its height off it, and moves (without turning: a wing's incidence stays put) to where that spot is
        // now. This part's own surface attachment works the other way round: its node moves to its spot on the new
        // skin, and the part moves so the node stays on what it's attached to (at the end of a drag, from where
        // it was at the start). Symmetry counterparts' attachments are re-placed with them. Procedural Parts'
        // whole-part length / scale sliders rebuild by themselves, then call TranslateAttachmentsAndNodes, which
        // does the same from the skin as it was.

        // Centre (offsetX/Z) + half-extents (rH/rV) of the body at a height y, sampled from the loft rings.
        private struct SurfSlice { public float y, cX, cZ, sX, sZ; }

        private static List<SurfSlice> SampleSurface(Loft L)
        {
            List<Ring> rings = L.rings;   // sorted by y ascending
            var s = new List<SurfSlice>(rings.Count);
            foreach (Ring r in rings)
                s.Add(new SurfSlice { y = r.y, cX = r.offsetX, cZ = r.offsetZ,
                                      sX = Mathf.Max(1e-3f, r.rH), sZ = Mathf.Max(1e-3f, r.rV) });
            return s;
        }

        // Interpolate the section centre / size at an arbitrary height y (clamped to the end rings).
        private static SurfSlice SurfaceAt(List<SurfSlice> s, float y)
        {
            int n = s.Count;
            if (n == 0) return new SurfSlice { y = y, sX = 1f, sZ = 1f };
            if (y <= s[0].y) { SurfSlice r0 = s[0]; r0.y = y; return r0; }
            if (y >= s[n - 1].y) { SurfSlice r1 = s[n - 1]; r1.y = y; return r1; }
            for (int i = 1; i < n; i++)
            {
                if (y > s[i].y) continue;
                SurfSlice a = s[i - 1], b = s[i];
                float t = (y - a.y) / Mathf.Max(1e-3f, b.y - a.y);
                return new SurfSlice { y = y, cX = Mathf.Lerp(a.cX, b.cX, t), cZ = Mathf.Lerp(a.cZ, b.cZ, t),
                                       sX = Mathf.Lerp(a.sX, b.sX, t), sZ = Mathf.Lerp(a.sZ, b.sZ, t) };
            }
            SurfSlice last = s[n - 1]; last.y = y; return last;
        }

        // The loft's rings and where each segment's start (index of its lower node's ring; one past the last
        // segment, the last ring).
        private sealed class Loft
        {
            public List<Ring> rings;
            public List<int> segStart;
            public List<SpineNode> nodes;   // the nodes it was built from, and their positions then
            public float[] pos;
        }

        private Loft _built;

        private Loft BuildLoft()
        {
            var seg = new List<int>();
            List<Ring> rings = BuildRings(seg);
            return new Loft { rings = rings, segStart = seg, nodes = new List<SpineNode>(nodes), pos = nodes.Select(n => n.position).ToArray() };
        }

        // A spot on the skin: segment (its lower and upper node, and their positions then), how far along it
        // (0..1, by ring), round the section (outline index + fraction), and the attach point's height off the skin.
        // Or on an end cap (cap -1 the bottom, +1 the top): where on it, as a share of the end ring's half-extents.
        // normal: the skin's outward normal there then (part frame), so what sits on it can turn with the skin.
        private struct SkinSpot
        {
            public SpineNode lo, hi;
            public float loPos, hiPos, along, round, height;
            public int cap;
            public Vector2 capAt;
            public Vector3 normal;
        }

        // A point in a ring's plane (outline coordinates) in the part's frame, as RingV places outline points.
        private static Vector3 RingPoint(Ring ring, Vector2 p) =>
            new Vector3(p.x + ring.offsetX, ring.y + p.y * ring.tanTiltV + p.x * ring.tanTiltH, p.y + ring.offsetZ);

        private static bool InOutline(Vector2[] o, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = o.Length - 1; i < o.Length; j = i++)
                if ((o[i].y > p.y) != (o[j].y > p.y) && p.x < (o[j].x - o[i].x) * (p.y - o[i].y) / (o[j].y - o[i].y) + o[i].x)
                    inside = !inside;
            return inside;
        }

        private SkinSpot SpotOf(Loft L, Vector3 p)
        {
            List<Ring> rings = L.rings;
            int rc = rings.Count, n = rings[0].outline.Length;
            float best = float.MaxValue, ringF = 0f, roundF = 0f;
            Vector3 bestPt = p;
            for (int r = 0; r < rc - 1; r++)
                for (int j = 0; j < n; j++)
                {
                    Vector3 a = RingV(rings[r], j), b = RingV(rings[r], j + 1), c = RingV(rings[r + 1], j), d = RingV(rings[r + 1], j + 1);
                    // (a, c, b): a + v(b - a) + w(c - a) = round j + v, ring r + w
                    Vector3 q = ClosestOnTriangle(p, a, b, c, out float v, out float w);
                    float dd = (q - p).sqrMagnitude;
                    if (dd < best) { best = dd; ringF = r + w; roundF = j + v; bestPt = q; }
                    // (b, c, d): b + v(c - b) + w(d - b) = round j + 1 - v, ring r + v + w
                    q = ClosestOnTriangle(p, b, c, d, out v, out w);
                    dd = (q - p).sqrMagnitude;
                    if (dd < best) { best = dd; ringF = r + v + w; roundF = j + 1 - v; bestPt = q; }
                }
            // nearer an end cap? (solid ends only)
            if (!shellMode)
                foreach (int cap in new[] { -1, 1 })
                {
                    Ring R = rings[cap < 0 ? 0 : rc - 1];
                    var p2 = new Vector2(p.x - R.offsetX, p.z - R.offsetZ);
                    float h = (p.y - RingPoint(R, p2).y) * cap;
                    if (h * h >= best || !InOutline(R.outline, p2)) continue;
                    return new SkinSpot { cap = cap, height = h, normal = cap * Vector3.up,
                                          capAt = new Vector2(p2.x / Mathf.Max(1e-4f, R.rH), p2.y / Mathf.Max(1e-4f, R.rV)) };
                }
            int s = SegmentOfRing(L, ringF);
            float span = Mathf.Max(1, L.segStart[s + 1] - L.segStart[s]);
            Skin(L, ringF, roundF, out Vector3 at, out Vector3 nrm);
            return new SkinSpot
            {
                lo = L.nodes[s], hi = L.nodes[s + 1], loPos = L.pos[s], hiPos = L.pos[s + 1],
                along = Mathf.Clamp01((ringF - L.segStart[s]) / span), round = roundF,
                height = Vector3.Dot(p - at, nrm),
                normal = nrm,
            };
        }

        private static int SegmentOfRing(Loft L, float ringF)
        {
            int segs = L.segStart.Count - 1;
            for (int s = 0; s < segs; s++)
                if (ringF <= L.segStart[s + 1] + 1e-4f) return s;
            return Math.Max(0, segs - 1);
        }

        // The spot's nodes are still neighbours (an end cap's spot has none).
        private bool Current(SkinSpot sp)
        {
            if (sp.cap != 0) return true;
            int s = nodes.IndexOf(sp.lo);
            return s >= 0 && s + 1 < nodes.Count && nodes[s + 1] == sp.hi;
        }

        // The spot on the nodes as they are now: on its own segment while its nodes are still neighbours, else
        // on the segment at the spine position it was at (a node added or removed around it).
        private SkinSpot Rebased(SkinSpot sp)
        {
            if (Current(sp)) return sp;
            float pos = Mathf.Lerp(sp.loPos, sp.hiPos, sp.along);
            int s = 0;
            while (s < nodes.Count - 2 && nodes[s + 1].position < pos) s++;
            sp.lo = nodes[s];
            sp.hi = nodes[s + 1];
            sp.loPos = nodes[s].position;
            sp.hiPos = nodes[s + 1].position;
            sp.along = Mathf.Clamp01(Mathf.InverseLerp(sp.loPos, sp.hiPos, pos));
            return sp;
        }

        // Where a spot is on the current loft.
        private Vector3 PlaceOf(Loft L, SkinSpot sp, out Vector3 normal)
        {
            sp = Rebased(sp);
            if (sp.cap != 0)
            {
                Ring R = L.rings[sp.cap < 0 ? 0 : L.rings.Count - 1];
                normal = sp.cap * Vector3.up;
                return RingPoint(R, new Vector2(sp.capAt.x * R.rH, sp.capAt.y * R.rV)) + sp.height * normal;
            }
            int s = nodes.IndexOf(sp.lo);
            float ringF = L.segStart[s] + sp.along * (L.segStart[s + 1] - L.segStart[s]);
            Skin(L, ringF, sp.round, out Vector3 at, out normal);
            return at + sp.height * normal;
        }

        // The skin at (ring, round) fractional indices: the point (bilinear in the quad) and the outward normal.
        private static void Skin(Loft L, float ringF, float roundF, out Vector3 at, out Vector3 normal)
        {
            List<Ring> rings = L.rings;
            int rc = rings.Count, n = rings[0].outline.Length;
            int r = Mathf.Clamp(Mathf.FloorToInt(ringF), 0, rc - 2);
            float w = Mathf.Clamp01(ringF - r);
            int j = Mathf.FloorToInt(roundF);
            float v = roundF - j;
            Vector3 P(int ri, float jf)
            {
                int j0 = Mathf.FloorToInt(jf);
                return Vector3.Lerp(RingV(rings[ri], j0), RingV(rings[ri], j0 + 1), jf - j0);
            }
            at = Vector3.Lerp(P(r, roundF), P(r + 1, roundF), w);
            Vector3 dRing = P(r + 1, roundF) - P(r, roundF);
            Vector3 dRound = Vector3.Lerp(RingV(rings[r], j + 1) - RingV(rings[r], j), RingV(rings[r + 1], j + 1) - RingV(rings[r + 1], j), w);
            normal = Vector3.Cross(dRound, dRing);
            // outward: the outline's turning sense says which side of its edge is out
            Vector2[] o = rings[w < 0.5f ? r : r + 1].outline;
            float area = 0f;
            for (int k = 0; k < n; k++) area += o[k].x * o[(k + 1) % n].y - o[(k + 1) % n].x * o[k].y;
            Vector2 e = o[((j + 1) % n + n) % n] - o[((j % n) + n) % n];
            var outHint = new Vector3(e.y, 0f, -e.x) * Mathf.Sign(area == 0f ? 1f : area);
            if (normal.sqrMagnitude < 1e-12f) normal = outHint;
            if (Vector3.Dot(normal, outHint) < 0f) normal = -normal;
            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;
        }

        // Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5), with its weights:
        // the point is a + v(b - a) + w(c - a).
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float v, out float w)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { v = w = 0f; return a; }
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { v = 1f; w = 0f; return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { v = d1 / (d1 - d3); w = 0f; return a + v * ab; }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { v = 0f; w = 1f; return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { v = 0f; w = d2 / (d2 - d6); return a + w * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                v = 1f - t; w = t;
                return b + t * (c - b);
            }
            float denom = 1f / (va + vb + vc);
            v = vb * denom; w = vc * denom;
            return a + ab * v + ac * w;
        }

        // One attachment captured before an edit, on the Spine `owner` (this part or a symmetry counterpart): a
        // stack node (local: its position), a part surface-attached to it (local: its attach point in the owner's
        // frame, spot: where that is on the skin), or the owner's own surface attachment (self).
        private enum SnapKind { Stack, Child, Self }

        private struct AttachSnap
        {
            public ProceduralShapeSpine owner;
            public SnapKind kind;
            public AttachNode node;
            public Vector3 local;
            public SkinSpot spot;
            public SurfSlice centre;   // Stack: the section at the node, then
        }

        // The attachments against loft L (by default the skin as last built); `counterparts` adds the symmetry
        // counterparts' (against theirs), `self` this part's own surface attachment.
        private List<AttachSnap> SnapshotAttachments(bool counterparts = true, bool self = true, Loft L = null)
        {
            var snaps = new List<AttachSnap>();
            if (!HighLogic.LoadedSceneIsEditor) return snaps;
            SnapshotInto(snaps, L ?? _built, self);
            if (counterparts)
                foreach (Part p in part.symmetryCounterparts)
                    if (FindAbstractShapeModule(p, this) is ProceduralShapeSpine pm) pm.SnapshotInto(snaps, pm._built, self);
            return snaps;
        }

        private void SnapshotInto(List<AttachSnap> snaps, Loft L, bool self)
        {
            if (L == null || L.rings.Count < 2) return;
            List<SurfSlice> surf = null;
            foreach (AttachNode n in part.attachNodes)
                if (n.nodeType == AttachNode.NodeType.Stack)
                {
                    surf = surf ?? SampleSurface(L);
                    snaps.Add(new AttachSnap { owner = this, kind = SnapKind.Stack, node = n, local = n.position, centre = SurfaceAt(surf, n.position.y) });
                }
            foreach (Part c in part.children)
                if (c.FindAttachNodeByPart(part) is AttachNode cn && cn.nodeType == AttachNode.NodeType.Surface)
                {
                    Vector3 local = part.transform.InverseTransformPoint(cn.owner.transform.TransformPoint(cn.position));
                    // still where the last rebuild put it: the same spot (finding it again by nearest point lands a
                    // hair off, with a normal a hair different, and over many edits the part would creep and lean)
                    SkinSpot spot = _placed.TryGetValue(c, out var was) && (was.local - local).sqrMagnitude < 1e-8f && Current(was.spot)
                        ? was.spot : SpotOf(L, local);
                    snaps.Add(new AttachSnap { owner = this, kind = SnapKind.Child, node = cn, local = local, spot = spot });
                }
            if (_placed.Count > 0) foreach (Part gone in _placed.Keys.Where(k => k == null || k.parent != part).ToList()) _placed.Remove(gone);
            if (self && SelfSnap(L) is AttachSnap me) snaps.Add(me);
        }

        private AttachSnap? SelfSnap(Loft L)
        {
            if (L == null || L.rings.Count < 2 || !(part.srfAttachNode is AttachNode srf) || srf.attachedPart == null || srf.attachedPart != part.parent)
                return null;
            return new AttachSnap { owner = this, kind = SnapKind.Self, node = srf, local = srf.position, spot = SpotOf(L, srf.position) };
        }

        // Re-place the snapshotted attachments on each owner's skin as now built.
        private static void ReplaceAttachments(List<AttachSnap> snaps)
        {
            if (snaps == null || snaps.Count == 0) return;
            foreach (var group in snaps.GroupBy(s => s.owner))
            {
                ProceduralShapeSpine m = group.Key;
                if (m == null || m._built == null) continue;
                Loft L = m._built;
                List<SurfSlice> newS = null;
                // children first: the owner's own move (Self) takes them with it
                foreach (AttachSnap s in group.OrderBy(x => x.kind == SnapKind.Self ? 1 : 0))
                {
                    switch (s.kind)
                    {
                        case SnapKind.Stack:
                        {
                            newS = newS ?? SampleSurface(L);
                            SurfSlice a = s.centre, b = SurfaceAt(newS, s.local.y);
                            Vector3 d = new Vector3(b.cX - a.cX, 0f, b.cZ - a.cZ);
                            if (d.sqrMagnitude < 1e-8f) break;
                            m.TranslateNode(s.node, d);
                            if (s.node.attachedPart is Part ap) m.TranslatePart(ap, d);
                            break;
                        }
                        case SnapKind.Child:
                        {
                            if (s.node.owner == null) break;
                            Transform ct = s.node.owner.transform;
                            SkinSpot placed = m.Rebased(s.spot);
                            Vector3 to = m.part.transform.TransformPoint(m.PlaceOf(L, placed, out Vector3 normal));
                            // turned as the skin under it turned (a part sits flat on its face), then moved onto it
                            Quaternion turn = Quaternion.FromToRotation(m.part.transform.TransformDirection(s.spot.normal),
                                                                        m.part.transform.TransformDirection(normal));
                            if (Quaternion.Angle(turn, Quaternion.identity) > 0.01f) ct.rotation = turn * ct.rotation;
                            Vector3 now = ct.TransformPoint(s.node.position);
                            if ((to - now).sqrMagnitude > 1e-10f) ct.Translate(to - now, Space.World);
                            placed.normal = normal;
                            m._placed[s.node.owner] = (m.part.transform.InverseTransformPoint(to), placed);
                            break;
                        }
                        case SnapKind.Self:
                        {
                            Vector3 nodeTo = m.PlaceOf(L, s.spot, out _);
                            Vector3 touch = m.part.transform.TransformPoint(s.node.position);   // on what it's attached to
                            m.MoveNode(s.node, nodeTo);
                            Vector3 moved = touch - m.part.transform.TransformPoint(nodeTo);
                            if (moved.sqrMagnitude > 1e-10f) m.part.transform.Translate(moved, Space.World);
                            break;
                        }
                    }
                }
            }
        }

        // A gizmo drag moves this part's own surface attachment once, at the end, from where it was at the start
        // (moving the part under the cursor mid-drag would fight the drag).
        private AttachSnap? _dragSelf;

        // Each surface-attached child's spot as last placed, and where that put its attach point (part frame).
        private readonly Dictionary<Part, (Vector3 local, SkinSpot spot)> _placed = new Dictionary<Part, (Vector3, SkinSpot)>();

        #endregion

        #region Node editing

        private void RefreshSelector()
        {
            if (Fields[nameof(selectedNode)].uiControlEditor is UI_FloatRange fr)
                fr.maxValue = Mathf.Max(0, nodes.Count - 1);
            selectedNode = SelIndex;
        }

        // Pull the selected node's data into the PAW proxy fields.
        private void LoadProxyFromNode()
        {
            if (nodes.Count == 0) return;
            if (HighLogic.LoadedSceneIsEditor) RefreshSectionOptions();   // the craft's sections come and go
            SpineNode n = nodes[SelIndex];
            nodeSlopeOpt = n.slopeAbove.ToString();
            nodeProfileOpt = n.Section.name;   // a missing section shows as the Ellipse it renders as
            nodePos = n.position;
            nodeWidth = n.sizeH;
            nodeHeight = n.sizeV;
            nodeTiltV = n.tiltV;
            nodeTiltH = n.tiltH;
            nodeOffsetV = n.offsetV;
            nodeOffsetH = n.offsetH;
            Fields[nameof(nodePos)].guiActiveEditor = !IsEndNode(SelIndex);   // end nodes are pinned fore/aft
            UpdateSizeFieldBounds();
            UpdateAddButtons();
        }

        // The width/height sliders bottom out at 0 on an end node (closed tip) and at MinSize elsewhere,
        // so the lower bound is re-applied whenever the selection changes.
        private void UpdateSizeFieldBounds()
        {
            float min = MinSizeFor(SelIndex);
            foreach (string fn in new[] { nameof(nodeWidth), nameof(nodeHeight) })
                if (Fields[fn].uiControlEditor is UI_FloatEdit e) e.minValue = min;
        }

        // The shell-thickness slider only matters when the hollow shell is on.
        private void UpdateShellVisibility() => Fields[nameof(shellThickness)].guiActiveEditor = shellMode;

        // Scale the shell-thickness slider's range to the part: the wall can inset at most
        // ShellInsetFraction of the narrowest section's smaller half-extent, so cap the slider there
        // (and scale its steps) so every value on the slider is actually achievable. Also heals a saved
        // thickness that's now too big for a shrunken part.
        private void UpdateShellThicknessRange()
        {
            if (!(Fields[nameof(shellThickness)].uiControlEditor is UI_FloatEdit e)) return;
            float minHalf = float.MaxValue;
            foreach (SpineNode n in nodes)
            {
                // A closed end collapses to a point and has no wall to inset, so it constrains nothing.
                float half = Mathf.Min(Erh(n), Erv(n));
                if (half > 0f) minHalf = Mathf.Min(minHalf, half);
            }
            if (minHalf == float.MaxValue || minHalf <= 0f) minHalf = 0.5f;
            float max = Mathf.Max(2f * MinShellThickness, ShellInsetFraction * minHalf);
            e.minValue = MinShellThickness;
            e.maxValue = max;
            e.incrementLarge = max * 0.25f;
            e.incrementSmall = max * 0.05f;
            shellThickness = Mathf.Clamp(shellThickness, MinShellThickness, max);
        }

        // At each end of the spine only the inward insert makes sense: the top node (highest position /
        // last index) hides "in front", the bottom node (index 0) hides "behind". The end nodes are also
        // pinned, so they can't be removed (Remove hidden there too).
        private void UpdateAddButtons()
        {
            bool isEnd = IsEndNode(SelIndex);
            BaseEvent front = Events[nameof(AddNodeInFrontEvent)];
            BaseEvent behind = Events[nameof(AddNodeBehindEvent)];
            BaseEvent remove = Events[nameof(RemoveNodeEvent)];
            if (front != null) front.guiActiveEditor = SelIndex != nodes.Count - 1;
            if (behind != null) behind.guiActiveEditor = SelIndex != 0;
            if (remove != null) remove.guiActiveEditor = !isEnd;
        }

        private void OnSelectedNodeChanged(BaseField f, object obj)
        {
            // Ignore re-fires triggered by our own rebuild, and no-op re-fires from PAW construction.
            if (_rebuilding) return;
            if (Equals(f.GetValue(this), obj)) return;
            LoadProxyFromNode();
            FollowSelected();
            MonoUtilities.RefreshPartContextWindow(part);
        }

        // A single proxy field changed -> write just that field back to the selected node (the other
        // proxies stay authoritative, so they can't be clobbered if load/change ever drift out of sync).
        private void OnNodeFieldChanged(BaseField f, object obj)
        {
            // Ignore re-fires triggered by our own rebuild (the Volume setter can synchronously
            // rebuild the PAW), and the no-op re-fires PAW construction issues.
            if (_rebuilding) return;
            if (Equals(f.GetValue(this), obj)) return;
            if (nodes.Count == 0) return;
            Undoable(f, obj, () => ApplyNodeField(f, obj));
        }

        private void ApplyNodeField(BaseField f, object obj)
        {
            SpineNode n = nodes[SelIndex];
            Debug.Log($"{ModTag} OnNodeFieldChanged {f.name}: {obj} -> {f.GetValue(this)} | sel={SelIndex}/{nodes.Count} before: {Dump(n)}");
            try
            {
                switch (f.name)
                {
                    case nameof(nodeSlopeOpt): n.slopeAbove = ParseEnum(nodeSlopeOpt, SpineSlope.Linear); break;
                    case nameof(nodeWidth): n.sizeH = nodeWidth = Mathf.Clamp(nodeWidth, MinSizeFor(SelIndex), MaxSize); break;
                    case nameof(nodeHeight): n.sizeV = nodeHeight = Mathf.Clamp(nodeHeight, MinSizeFor(SelIndex), MaxSize); break;
                    case nameof(nodeTiltV): n.tiltV = nodeTiltV = Mathf.Clamp(nodeTiltV, -60f, 60f); break;
                    case nameof(nodeTiltH): n.tiltH = nodeTiltH = Mathf.Clamp(nodeTiltH, -60f, 60f); break;
                    case nameof(nodeOffsetV): n.offsetV = nodeOffsetV = Mathf.Clamp(nodeOffsetV, -10f, 10f); break;
                    case nameof(nodeOffsetH): n.offsetH = nodeOffsetH = Mathf.Clamp(nodeOffsetH, -10f, 10f); break;
                    case nameof(nodeProfileOpt):
                        AssignSection(n, AvailableSections().FirstOrDefault(s => s.name == nodeProfileOpt)
                                         ?? CrossSectionLibrary.Resolve(nodeProfileOpt));
                        FollowSelected();
                        MonoUtilities.RefreshPartContextWindow(part);
                        break;
                    case nameof(nodePos):
                        // Position is clamped strictly between the neighbours; end nodes are pinned.
                        if (!IsEndNode(SelIndex))
                        {
                            float loPos = (SelIndex > 0) ? nodes[SelIndex - 1].position + 0.01f : 0f;
                            float hiPos = (SelIndex < nodes.Count - 1) ? nodes[SelIndex + 1].position - 0.01f : 1f;
                            if (loPos > hiPos) loPos = hiPos = 0.5f * (loPos + hiPos);
                            n.position = Mathf.Clamp(nodePos, loPos, hiPos);
                        }
                        nodePos = n.position;   // reflect the clamp / pin in the slider
                        break;
                }

                // A position edit can reorder; keep the selection on the same node.
                SortNodes();
                selectedNode = Mathf.Clamp(nodes.IndexOf(n), 0, nodes.Count - 1);
                RebuildAndPropagate();   // attached parts stay on the new surface
                Debug.Log($"{ModTag}  applied -> sel={SelIndex} after: {Dump(n)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{ModTag} OnNodeFieldChanged({f.name}) FAILED: {e}\n  nodes: {DumpAll()}");
            }
        }

        private static string Dump(SpineNode n) => $"[pos={n.position:F3} H={n.sizeH:F3} V={n.sizeV:F3} off={n.offsetV:F3}/{n.offsetH:F3} tilt={n.tiltV:F1}/{n.tiltH:F1} sec={n.section} slope={n.slopeAbove}]";
        private string DumpAll() => string.Join(" ", nodes.ConvertAll(Dump).ToArray()) + $" | len={length:F3} hS={hScale:F3} vS={vScale:F3}";

        internal static T ParseEnum<T>(string s, T fallback) where T : struct =>
            Enum.TryParse(s, out T v) ? v : fallback;

        private void RebuildAndPropagate()
        {
            List<AttachSnap> snaps = SnapshotAttachments(self: _dragSelf == null);
            if (_dragSelf is AttachSnap me) snaps.Add(me);
            _dragSelf = null;
            UpdateShape();          // sets Volume -> fires OnPartVolumeChanged + onEditorShipModified
            UpdateInterops();       // FAR / TestFlight
            SyncToSymmetry();
            ReplaceAttachments(snaps);   // what's attached stays on the new skin
        }

        // Lightweight rebuild for continuous gizmo drags: refresh this part's mesh only, deferring the
        // expensive FAR/TestFlight interop and the per-counterpart symmetry rebuild to drag end (a full
        // RebuildAndPropagate fires once on mouse-up), so a drag doesn't re-voxelise and re-mesh the whole
        // symmetry group every frame.
        private void RebuildLive()
        {
            List<AttachSnap> snaps = SnapshotAttachments(counterparts: false, self: false);
            UpdateShape();
            ReplaceAttachments(snaps);   // what's attached rides along
        }

        // The node list isn't a scalar KSPField, so symmetry isn't auto-mirrored. Copy our state to
        // each counterpart's spine module and rebuild it.
        private void SyncToSymmetry()
        {
            foreach (Part p in part.symmetryCounterparts)
            {
                if (FindAbstractShapeModule(p, this) is ProceduralShapeSpine pm)
                {
                    pm.length = length;
                    pm.sectionPoints = sectionPoints;
                    pm.hScale = hScale;
                    pm.vScale = vScale;
                    pm.shellMode = shellMode;
                    pm.shellThickness = shellThickness;
                    pm.selectedNode = selectedNode;
                    pm.nodes.Clear();
                    foreach (SpineNode n in nodes) pm.nodes.Add(n.Clone());
                    pm.UpdateShape();
                    pm.UpdateInterops();
                    pm.LoadProxyFromNode();
                }
            }
        }

        #endregion

        #region Editor gizmo (interactive)

        // Per node, billboarded glyphs drawn through the hull:
        //   * a circle marker at the node centre          -> select; drag = move the node up/down
        //                                                    (vertical offset); wheel = taller/shorter
        //   * the section-type glyph just above            -> select; middle-click cycles Ellipse ->
        //                                                    Rectangle -> Mk2 -> Mk3
        //   * two tip handles at the section's left/right  -> B9-style: drag out/in = widen/narrow,
        //     edges                                          drag along the spine = move forward/back
        // Update() only runs while this shape is the active (enabled) one.
        private enum HoverKind { None, Node, Profile, TipLeft, TipRight }
        private readonly List<GameObject> _nodeHandles = new List<GameObject>();   // circle marker
        private readonly List<GameObject> _profIcons = new List<GameObject>();     // section glyph (above)
        private readonly List<GameObject> _leftTips = new List<GameObject>();      // left mesh-edge handle
        private readonly List<GameObject> _rightTips = new List<GameObject>();     // right mesh-edge handle
        private int _dragIndex = -1;     // node being centre-offset dragged
        private bool _dragMoved;         // this drag changed something: one undo step on release
        private ConfigNode _dragPre;     // the ship before it, if the undo list wants that (SpineUndo)
        private int _dragAxis;           // 0 = vertical (left-drag), 1 = horizontal (right-drag)
        private int _tipDragIndex = -1;  // node whose tip is being dragged
        private int _tipSide;            // -1 = left tip, +1 = right tip
        // Offset between the grabbed value and the cursor at mouse-down, so a drag moves the value by the
        // cursor delta instead of snapping it to wherever the (possibly off-centre) grab landed.
        private float _grabDeltaV, _grabDeltaH, _grabDeltaW, _grabDeltaP;
        private bool _inputLocked;
        private const ControlTypes GizmoLockMask = ControlTypes.EDITOR_PAD_PICK_PLACE | ControlTypes.CAMERACONTROLS;
        private string GizmoLockId => "SpineGizmo_" + GetInstanceID();

        public void Update()
        {
            // Only the active Spine shape on a selected part drives gizmos. A disabled shape module stops
            // getting Update() (so it would leave its handles orphaned); guarding here also covers a stale
            // active shape and hides the gizmos once the part's PAW closes (part deselected).
            bool selected = part.PartActionWindow != null && part.PartActionWindow.isActiveAndEnabled;
            UpdateOutlines(selected);
            if (!HighLogic.LoadedSceneIsEditor || nodes.Count == 0 || PPart == null || PPart.CurrentShape != this || !selected)
            {
                FinishDrag();
                CleanupGizmo();
                return;
            }

            SyncHandleCount(_nodeHandles, "SpineNodeHandle");
            SyncHandleCount(_profIcons, "SpineProfileIcon");
            SyncHandleCount(_leftTips, "SpineTipL");
            SyncHandleCount(_rightTips, "SpineTipR");

            Camera cam = GizmoCam;
            int sel = SelIndex;
            HoverKind hoverKind = HoverKind.None;
            int hover = -1;
            if (_dragIndex >= 0) { hover = _dragIndex; hoverKind = HoverKind.Node; }
            else if (_tipDragIndex >= 0) { hover = _tipDragIndex; hoverKind = (_tipSide < 0) ? HoverKind.TipLeft : HoverKind.TipRight; }
            else if (cam != null && !CrossSectionEditor.MouseOverWindow && !SpineOptionsWindow.MouseOverWindow && !PointerOverUi()) hover = ComputeHover(cam, out hoverKind);

            Quaternion rot = (cam != null) ? cam.transform.rotation : Quaternion.identity;
            Vector3 up = (cam != null) ? cam.transform.up : Vector3.up;
            SpineHandleVis vis = SpineSettings.HandleVis;

            for (int i = 0; i < nodes.Count; i++)
            {
                bool isSel = (i == sel);
                // All = every node's edge tips, Active = the selected node's only, None = no tips and no
                // centre marker either (it swamps a small section), leaving just the section glyph above
                // the node -- which still selects the node on click and cycles the profile on middle-click.
                bool showTips = (vis == SpineHandleVis.All) || (vis == SpineHandleVis.Active && isSel);
                bool showNode = vis != SpineHandleVis.None;

                float y = (nodes[i].position - 0.5f) * length;
                float rH = Erh(nodes[i]), off = Eoff(nodes[i]), offX = EoffH(nodes[i]);
                Vector3 baseW = part.transform.TransformPoint(new Vector3(offX, y, off));   // node centre (incl. offsets)

                bool nodeHot = (i == hover && hoverKind == HoverKind.Node);
                float s = (isSel || nodeHot) ? 0.30f : 0.22f;
                Material nodeMat = nodeHot ? HandleMat(2) : (isSel ? HandleMat(0) : HandleMat(1));
                // End nodes are drawn as arrows pointing fore/aft along the spine (they can't move
                // forward/back); interior nodes are circles.
                bool isLast = (i == nodes.Count - 1);
                Mesh nodeMesh = IsEndNode(i) ? (_meshTriangle ??= BuildTriangle()) : (_meshCircle ??= BuildCircle());
                Quaternion nodeRot = IsEndNode(i) ? SpineBillboard(cam, rot, isLast) : rot;
                if (_nodeHandles[i] != null && _nodeHandles[i].activeSelf != showNode) _nodeHandles[i].SetActive(showNode);
                if (showNode) PlaceIcon(_nodeHandles[i], baseW, nodeRot, s, nodeMesh, nodeMat, true, cam);

                bool profHot = (i == hover && hoverKind == HoverKind.Profile);
                PlaceIcon(_profIcons[i], baseW + up * 0.32f, rot, profHot ? 0.22f : 0.16f,
                          ProfileIconMesh(nodes[i].Section), profHot ? HandleMat(2) : ProfileMat, true, cam);

                Vector3 lW = part.transform.TransformPoint(new Vector3(-rH + offX, y, off));
                Vector3 rW = part.transform.TransformPoint(new Vector3(rH + offX, y, off));
                bool lHot = (i == hover && hoverKind == HoverKind.TipLeft);
                bool rHot = (i == hover && hoverKind == HoverKind.TipRight);
                PlaceTip(_leftTips[i], lW, rot, showTips, lHot, cam);
                PlaceTip(_rightTips[i], rW, rot, showTips, rHot, cam);
            }

            if (cam != null) HandleGizmoInput(cam, hoverKind, hover);
            else SetInputLock(false);
        }

        private void SyncHandleCount(List<GameObject> list, string name)
        {
            while (list.Count < nodes.Count) list.Add(CreateHandle(name));
            while (list.Count > nodes.Count)
            {
                GameObject extra = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                if (extra != null) Destroy(extra);
            }
        }

        private void PlaceIcon(GameObject go, Vector3 worldPos, Quaternion rot, float scale, Mesh mesh, Material mat, bool outline, Camera cam)
        {
            if (go == null) return;
            go.transform.SetParent(null, false);
            go.transform.position = worldPos;
            go.transform.rotation = rot;                       // billboard
            go.transform.localScale = Vector3.one * scale;
            go.layer = part.gameObject.layer;
            if (go.GetComponent<MeshFilter>() is MeshFilter mf) mf.sharedMesh = mesh;
            if (go.GetComponent<Renderer>() is Renderer r) r.sharedMaterial = mat;
            if (outline) PlaceOutline(go, mesh, scale, cam);
        }

        // A black copy of the icon, one outline-pixel larger, drawn first (lower render queue) so it
        // shows as a thin outline behind the coloured icon. The child scale is solved each frame so the
        // ring is a constant ~1 px wide regardless of zoom. Parented to the icon so it billboards with it.
        private const float OutlinePixels = 1f;
        private void PlaceOutline(GameObject go, Mesh mesh, float parentScale, Camera cam)
        {
            Transform ot = go.transform.Find("outline");
            GameObject o = (ot != null) ? ot.gameObject : new GameObject("outline", typeof(MeshFilter), typeof(MeshRenderer));
            o.transform.SetParent(go.transform, false);
            o.transform.localPosition = Vector3.zero;
            o.transform.localRotation = Quaternion.identity;
            o.layer = go.layer;

            float child = 1.1f;
            if (cam != null)
            {
                Vector3 c = go.transform.position;
                float pxPerWorld = Vector2.Distance(cam.WorldToScreenPoint(c), cam.WorldToScreenPoint(c + cam.transform.right));
                float coloredR = 0.5f * parentScale;          // world radius of the coloured disc
                if (pxPerWorld > 1e-4f && coloredR > 1e-5f)
                    child = (coloredR + OutlinePixels / pxPerWorld) / coloredR;
            }
            o.transform.localScale = Vector3.one * child;
            o.GetComponent<MeshFilter>().sharedMesh = mesh;
            o.GetComponent<Renderer>().sharedMaterial = OutlineMat;
        }

        // A tip handle (diamond) at a section edge; shown only for the selected node.
        private void PlaceTip(GameObject go, Vector3 worldPos, Quaternion rot, bool show, bool hot, Camera cam)
        {
            if (go == null) return;
            if (go.activeSelf != show) go.SetActive(show);
            if (!show) return;
            PlaceIcon(go, worldPos, rot, hot ? 0.26f : 0.20f, _meshDiamond ??= BuildDiamond(),
                      hot ? HandleMat(2) : TipMat, true, cam);
        }

        // Cursor-projected width and spine position from a tip-drag ray, in the section's width/length
        // plane. Returns false if the ray misses the plane.
        private bool RawTip(int i, Ray ray, out float rawW, out float rawP)
        {
            SpineNode n = nodes[i];
            rawW = n.sizeH; rawP = n.position;
            Vector3 center = part.transform.TransformPoint(new Vector3(0f, NodeY(n), Eoff(n)));
            Plane plane = new Plane(part.transform.forward, center);   // local-Z normal -> the X/Y plane
            if (!plane.Raycast(ray, out float enter)) return false;
            Vector3 local = part.transform.InverseTransformPoint(ray.GetPoint(enter));
            rawW = 2f * Mathf.Abs(local.x - EoffH(n)) / Mathf.Max(0.01f, hScale);   // width about the (possibly offset) centre
            rawP = local.y / Mathf.Max(0.01f, length) + 0.5f;
            return true;
        }

        // Drag a node's edge tip in the section's width/length plane: |x| sets the width, the spine (y)
        // coordinate repositions the node (clamped between neighbours). Either move alone or together.
        private void DragTip(int i, int side, Ray ray)
        {
            SpineNode n = nodes[i];
            if (!RawTip(i, ray, out float rawW, out float rawP)) return;

            float newH = Mathf.Clamp(rawW + _grabDeltaW, MinSizeFor(i), MaxSize);
            // End nodes are pinned fore/aft -> width only.
            float pos = n.position;
            if (!IsEndNode(i))
            {
                pos = rawP + _grabDeltaP;
                float lo = (i > 0) ? nodes[i - 1].position + 0.01f : 0f;
                float hi = (i < nodes.Count - 1) ? nodes[i + 1].position - 0.01f : 1f;
                if (lo > hi) lo = hi = 0.5f * (lo + hi);
                pos = Mathf.Clamp(pos, lo, hi);
            }

            if (Mathf.Approximately(newH, n.sizeH) && Mathf.Approximately(pos, n.position)) return;   // no change
            n.sizeH = newH;
            n.position = pos;
            _dragMoved = true;
            if (i == SelIndex) { nodeWidth = n.sizeH; nodePos = pos; }   // live slider update
            RebuildLive();   // full sync deferred to drag end
        }

        // The pointer is on the game's UI (the PAW, parts list, toolbars): a click there is the UI's, not a handle's
        // behind it. ClickThroughBlocker only covers IMGUI windows; the PAW is uGUI, so ask its event system, as the
        // editor does before picking a part.
        private static bool PointerOverUi() =>
            UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        private void HandleGizmoInput(Camera cam, HoverKind hoverKind, int hover)
        {
            if (_dragIndex >= 0)
            {
                int btn = (_dragAxis == 1) ? 1 : 0;   // left-drag = vertical, right-drag = horizontal
                if (Input.GetMouseButton(btn))
                {
                    SetInputLock(true);
                    Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                    if (_dragAxis == 1) DragNodeHorizontal(_dragIndex, ray);
                    else DragNodeVertical(_dragIndex, ray);
                }
                else
                {
                    _dragIndex = -1;
                    RebuildAndPropagate();   // one full interop + symmetry sync on release
                    EndDragUndo();
                    if (_dragAxis == 1) nodeOffsetH = nodes[SelIndex].offsetH;
                    else nodeOffsetV = nodes[SelIndex].offsetV;
                    _dragAxis = 0;
                    MonoUtilities.RefreshPartContextWindow(part);
                }
                return;
            }

            if (_tipDragIndex >= 0)
            {
                if (Input.GetMouseButton(0))
                {
                    SetInputLock(true);
                    DragTip(_tipDragIndex, _tipSide, cam.ScreenPointToRay(Input.mousePosition));
                }
                else
                {
                    _tipDragIndex = -1;
                    RebuildAndPropagate();   // one full interop + symmetry sync on release
                    EndDragUndo();
                    SpineNode sn = nodes[SelIndex];
                    nodeWidth = sn.sizeH;
                    nodePos = sn.position;
                    MonoUtilities.RefreshPartContextWindow(part);
                }
                return;
            }

            SetInputLock(hover >= 0);
            if (hover < 0) return;

            switch (hoverKind)
            {
                case HoverKind.Profile:
                    if (Input.GetMouseButtonDown(0)) SelectNode(hover);
                    else if (Input.GetMouseButtonDown(2)) CycleProfile(hover);
                    return;

                case HoverKind.TipLeft:
                case HoverKind.TipRight:
                    // Tip handle: drag out/in widens/narrows, drag along the spine repositions.
                    if (Input.GetMouseButtonDown(0))
                    {
                        SelectNode(hover);
                        _tipDragIndex = hover;
                        _tipSide = (hoverKind == HoverKind.TipLeft) ? -1 : 1;
                        _dragSelf = SelfSnap(_built);
                        StartDragUndo();
                        // Capture the grab offset so the drag moves by the cursor delta, not snapping the
                        // value to the (possibly off-tip) grab point.
                        if (RawTip(hover, cam.ScreenPointToRay(Input.mousePosition), out float rw, out float rp))
                        { _grabDeltaW = nodes[hover].sizeH - rw; _grabDeltaP = nodes[hover].position - rp; }
                        else { _grabDeltaW = _grabDeltaP = 0f; }
                    }
                    return;

                default:
                    // Node circle. With "must select before dragging" on, the first click only selects
                    // (so you can click a node without moving it) and only the already-selected node
                    // drags; with it off, any node drags immediately. Left-drag = vertical offset,
                    // right-drag = horizontal offset, wheel = height.
                    if (Input.GetMouseButtonDown(0))
                    {
                        bool canDrag = !SpineSettings.ClickBeforeDrag || hover == SelIndex;
                        SelectNode(hover);
                        if (canDrag)
                        {
                            _dragIndex = hover;
                            _dragAxis = 0;
                            _grabDeltaV = nodes[hover].offsetV - RawVertical(hover, cam.ScreenPointToRay(Input.mousePosition));
                            _dragSelf = SelfSnap(_built);
                            StartDragUndo();
                        }
                    }
                    else if (Input.GetMouseButtonDown(1))
                    {
                        bool canDrag = !SpineSettings.ClickBeforeDrag || hover == SelIndex;
                        SelectNode(hover);
                        if (canDrag)
                        {
                            _dragIndex = hover;
                            _dragAxis = 1;
                            _grabDeltaH = nodes[hover].offsetH - RawHorizontal(hover, cam.ScreenPointToRay(Input.mousePosition));
                            _dragSelf = SelfSnap(_built);
                            StartDragUndo();
                        }
                    }
                    else if (!Mathf.Approximately(Input.mouseScrollDelta.y, 0f)) ResizeNode(hover, Input.mouseScrollDelta.y);
                    return;
            }
        }

        // Nearest icon whose rendered disc the mouse is actually inside; the hit radius is the icon's
        // own on-screen size (incl. outline) so the hotzone matches what you see. The kind tells the
        // caller which control the mouse is over.
        private int ComputeHover(Camera cam, out HoverKind kind)
        {
            Vector2 mouse = Input.mousePosition;
            int hover = -1;
            kind = HoverKind.None;
            float best = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i < _nodeHandles.Count && _nodeHandles[i] != null && _nodeHandles[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _nodeHandles[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _nodeHandles[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.Node; }
                }
                if (i < _profIcons.Count && _profIcons[i] != null && _profIcons[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _profIcons[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _profIcons[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.Profile; }
                }
                if (i < _leftTips.Count && _leftTips[i] != null && _leftTips[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _leftTips[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _leftTips[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.TipLeft; }
                }
                if (i < _rightTips.Count && _rightTips[i] != null && _rightTips[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _rightTips[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _rightTips[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.TipRight; }
                }
            }
            return hover;
        }

        private static float ScreenDist(Camera cam, Vector3 world, Vector2 mouse)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f) return -1f;   // behind camera
            return Vector2.Distance(new Vector2(sp.x, sp.y), mouse);
        }

        // On-screen pixel radius of an icon disc (mesh radius 0.5 * its outline scale 1.25), plus a few
        // pixels of slack so the very edge is still grabbable.
        private static float IconScreenRadius(Camera cam, GameObject go)
        {
            Vector3 c = go.transform.position;
            Vector3 e = c + cam.transform.right * (0.5f * go.transform.localScale.x);
            Vector3 sc = cam.WorldToScreenPoint(c);
            Vector3 se = cam.WorldToScreenPoint(e);
            return Vector2.Distance(new Vector2(sc.x, sc.y), new Vector2(se.x, se.y)) + 3f;
        }

        // A handle drag is one undo step, recorded on release if it changed anything.
        private void StartDragUndo()
        {
            _dragMoved = false;
            _dragPre = SpineUndo.Before(this, null);
        }

        private void EndDragUndo()
        {
            if (_dragMoved) SpineUndo.After(this, null, _dragPre);
            _dragMoved = false;
            _dragPre = null;
        }

        private void SelectNode(int i)
        {
            selectedNode = Mathf.Clamp(i, 0, nodes.Count - 1);
            LoadProxyFromNode();
            FollowSelected();
            MonoUtilities.RefreshPartContextWindow(part);
        }

        private void CycleProfile(int i)
        {
            SpineNode n = nodes[i];
            string key = "profile#" + i;   // clicks on through the shapes: one step
            ConfigNode pre = SpineUndo.Before(this, key);
            List<CrossSection> all = AvailableSections();           // built-ins, the craft's, then shared A-Z
            int at = Math.Max(0, all.FindIndex(s => s.name == n.Section.name));
            AssignSection(n, all[(at + 1) % all.Count]);
            if (i == SelIndex) FollowSelected();
            RebuildAndPropagate();                                  // section type changes geometry
            SpineUndo.After(this, key, pre);
            if (i == SelIndex) { LoadProxyFromNode(); MonoUtilities.RefreshPartContextWindow(part); }
        }

        // Mouse wheel on a node makes it taller / shorter (its vertical size).
        private void ResizeNode(int i, float scroll)
        {
            SpineNode n = nodes[i];
            float step = PPart.diameterSmallStep * Mathf.Sign(scroll);
            float to = Mathf.Clamp(n.sizeV + step, MinSizeFor(i), MaxSize);
            if (Mathf.Approximately(to, n.sizeV)) return;
            string key = "wheel#" + i;   // a turn of the wheel: one step
            ConfigNode pre = SpineUndo.Before(this, key);
            n.sizeV = to;
            RebuildAndPropagate();
            SpineUndo.After(this, key, pre);
            if (i == SelIndex) { LoadProxyFromNode(); MonoUtilities.RefreshPartContextWindow(part); }
        }

        // Cursor-projected vertical offset (m, pre-vScale) from a drag ray along the part's local Z axis.
        private float RawVertical(int i, Ray ray)
        {
            Vector3 axisPoint = part.transform.TransformPoint(new Vector3(0f, NodeY(nodes[i]), 0f));   // un-offset centre
            float zWorld = ClosestParamOnAxis(axisPoint, part.transform.forward, ray);                 // metres along local Z
            return (vScale > 0f) ? zWorld / vScale : zWorld;
        }

        // Dragging a node moves its cross-section up/down (vertical centre offset), tracking the mouse
        // along the part's local vertical (Z) axis.
        private void DragNodeVertical(int i, Ray ray)
        {
            SpineNode n = nodes[i];
            float newOffset = Mathf.Clamp(RawVertical(i, ray) + _grabDeltaV, -10f, 10f);
            if (Mathf.Approximately(newOffset, n.offsetV)) return;   // no change -> skip rebuild
            n.offsetV = newOffset;
            _dragMoved = true;
            if (i == SelIndex) nodeOffsetV = newOffset;   // PAW value updates; refresh on drag end
            RebuildLive();   // full sync deferred to drag end
        }

        // Cursor-projected horizontal offset (m, pre-hScale) from a drag ray along the part's local X axis.
        private float RawHorizontal(int i, Ray ray)
        {
            Vector3 axisPoint = part.transform.TransformPoint(new Vector3(0f, NodeY(nodes[i]), 0f));   // un-offset centre
            float xWorld = ClosestParamOnAxis(axisPoint, part.transform.right, ray);                   // metres along local X
            return (hScale > 0f) ? xWorld / hScale : xWorld;
        }

        // Right-dragging a node moves its cross-section left/right (horizontal centre offset), tracking
        // the mouse along the part's local horizontal (X) axis.
        private void DragNodeHorizontal(int i, Ray ray)
        {
            SpineNode n = nodes[i];
            float newOffset = Mathf.Clamp(RawHorizontal(i, ray) + _grabDeltaH, -10f, 10f);
            if (Mathf.Approximately(newOffset, n.offsetH)) return;   // no change -> skip rebuild
            n.offsetH = newOffset;
            _dragMoved = true;
            if (i == SelIndex) nodeOffsetH = newOffset;   // PAW value updates; refresh on drag end
            RebuildLive();   // full sync deferred to drag end
        }

        // Parameter (signed metres along axisDir from axisPoint) of the point on the axis line closest
        // to the mouse ray.
        private static float ClosestParamOnAxis(Vector3 axisPoint, Vector3 axisDir, Ray ray)
        {
            Vector3 d1 = axisDir.normalized;
            Vector3 d2 = ray.direction.normalized;
            Vector3 r = axisPoint - ray.origin;
            float b = Vector3.Dot(d1, d2);
            float denom = 1f - b * b;            // a = d1.d1 = 1, e = d2.d2 = 1
            if (denom < 1e-6f) return Vector3.Dot(d1, r);   // parallel
            float c = Vector3.Dot(d1, r);
            float f = Vector3.Dot(d2, r);
            return (b * f - c) / denom;
        }

        private void SetInputLock(bool on)
        {
            if (on && !_inputLocked) { InputLockManager.SetControlLock(GizmoLockMask, GizmoLockId); _inputLocked = true; }
            else if (!on && _inputLocked) { InputLockManager.RemoveControlLock(GizmoLockId); _inputLocked = false; }
        }

        private static Camera GizmoCam =>
            (EditorCamera.Instance != null && EditorCamera.Instance.cam != null) ? EditorCamera.Instance.cam : Camera.main;

        private GameObject CreateHandle(string name) => new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));

        // Flat billboarded icon meshes (white vertex colours, tinted by the handle material).
        private static Mesh _meshCircle;

        // Section-type glyph drawn above the node circle: the cross-section's own silhouette so it
        // reads at a glance (ellipse squished, Mk3 round, rectangle square, Mk2 lifting-body).
        private static Mesh _iconEllipse, _iconRect, _iconMk2, _iconMk3;
        // Custom-section glyphs by name, rebuilt when the section's geometry key changes.
        private static readonly Dictionary<string, KeyValuePair<string, Mesh>> _customIcons = new Dictionary<string, KeyValuePair<string, Mesh>>();
        private static void DropCustomIcon(string name)
        {
            if (!_customIcons.TryGetValue(name, out var old)) return;
            if (old.Value != null) Destroy(old.Value);
            _customIcons.Remove(name);
        }

        private static Mesh ProfileIconMesh(CrossSection s)
        {
            if (!s.IsBuiltIn)
            {
                if (_customIcons.TryGetValue(s.name, out var hit) && hit.Key == s.Key && hit.Value != null) return hit.Value;
                DropCustomIcon(s.name);
                Mesh m = PolyMesh(s.UnitOutline, "Custom", new Vector2(1f, 0.62f));
                _customIcons[s.name] = new KeyValuePair<string, Mesh>(s.Key, m);
                return m;
            }
            switch (s.builtIn.Value)
            {
                case SpineProfile.Rectangle: return _iconRect ??= PolyMesh(s.UnitOutline, "Rect", new Vector2(1f, 0.6f));
                case SpineProfile.Mk2: return _iconMk2 ??= PolyMesh(s.UnitOutline, "Mk2", new Vector2(1f, 0.6f));
                case SpineProfile.Mk3: return _iconMk3 ??= PolyMesh(BuildMk3Outline(0.43f, 0.5f, MinSides), "Mk3", Vector2.one);
                default: return _iconEllipse ??= PolyMesh(UnitCircle(MinSides), "Ellipse", new Vector2(1f, 0.62f));
            }
        }

        private static Mesh BuildCircle() => PolyMesh(UnitCircle(MinSides), "Circle", Vector2.one);

        private static Mesh _meshDiamond;
        private static Mesh BuildDiamond() => PolyMesh(new[] { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f) }, "Diamond", Vector2.one);

        // Triangle pointing +Y (oriented along the spine by SpineBillboard for the end nodes).
        private static Mesh _meshTriangle;
        private static Mesh BuildTriangle()
        {
            var v = new[] { new Vector3(0f, 0.62f, 0f), new Vector3(-0.54f, -0.42f, 0f), new Vector3(0.54f, -0.42f, 0f) };
            var col = new[] { Color.white, Color.white, Color.white };
            var tris = new[] { 0, 1, 2, 0, 2, 1 };   // double-sided
            return new Mesh { name = "SpineTriangle", vertices = v, colors = col, triangles = tris };
        }

        // Billboard that keeps the glyph facing the camera but points its +Y along the spine (fore/aft).
        private Quaternion SpineBillboard(Camera cam, Quaternion fallback, bool front)
        {
            if (cam == null) return fallback;
            Vector3 spine = part.transform.up;                  // world dir toward the top node
            Vector3 dir = front ? spine : -spine;
            Vector3 fwd = cam.transform.forward;
            Vector3 proj = dir - Vector3.Dot(dir, fwd) * fwd;   // onto the camera plane
            if (proj.sqrMagnitude < 1e-6f) return fallback;     // looking straight down the axis
            return Quaternion.LookRotation(fwd, proj.normalized);
        }

        // Filled polygon of any simple (possibly concave) unit outline, fit into the icon box.
        private static Mesh PolyMesh(Vector2[] outline, string name, Vector2 scale)
        {
            int n = outline.Length;
            var v = new Vector3[n];
            var col = new Color[n];
            for (int i = 0; i < n; i++) { v[i] = new Vector3(outline[i].x * scale.x * 0.5f, outline[i].y * scale.y * 0.5f, 0f); col[i] = Color.white; }
            int[] tri = SectionGeometry.Triangulate(outline);
            var tris = new int[tri.Length * 2];   // double-sided: the icons billboard either way round
            for (int i = 0; i < tri.Length; i += 3)
            {
                tris[2 * i] = tri[i]; tris[2 * i + 1] = tri[i + 1]; tris[2 * i + 2] = tri[i + 2];
                tris[2 * i + 3] = tri[i]; tris[2 * i + 4] = tri[i + 2]; tris[2 * i + 5] = tri[i + 1];
            }
            return new Mesh { name = "SpineProf" + name, vertices = v, colors = col, triangles = tris };
        }

        private static Material _matSel, _matNorm, _matHover, _matProf;
        // 0 = selected (yellow), 1 = normal (cyan), 2 = hovered (white).
        private static Material HandleMat(int kind)
        {
            switch (kind)
            {
                case 0: return _matSel ??= MakeMat(Color.yellow);
                case 2: return _matHover ??= MakeMat(Color.white);
                default: return _matNorm ??= MakeMat(new Color(0.2f, 0.8f, 1f));
            }
        }

        // Section glyph idle tint (green), so it reads as a distinct control from the node circle.
        private static Material ProfileMat => _matProf ??= MakeMat(new Color(0.5f, 1f, 0.55f));

        // Edge tip handle idle tint (orange).
        private static Material _matTip;
        private static Material TipMat => _matTip ??= MakeMat(new Color(1f, 0.6f, 0.15f));

        // Black outline drawn one queue earlier (behind) the coloured icons so they stand out.
        private static Material _matOutline;
        private static Material OutlineMat
        {
            get { if (_matOutline == null) { _matOutline = MakeMat(Color.black); _matOutline.renderQueue = 4999; } return _matOutline; }
        }

        // Unlit, solid, always-on-top material (renders through the mesh).
        private static Material MakeMat(Color col)
        {
            Material m = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_Color", col);
            m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.renderQueue = 5000;
            return m;
        }

        // ---- cross-section outlines (Spine Options) ----
        // A line loop per node on the part's own transform (so it moves with the part), its points the ring's
        // own, a hair proud of the skin; the selected node's thicker and orange. Each loop is drawn twice: in front
        // of the skin as is, and behind it (where the part or anything else hides it) faint, so the far side of a
        // section reads as hidden. Through each, a faint vertical line from the section's bottom to its top across
        // the node's centre, where its handle sits, drawn through the skin as the handles are, so a handle reads as
        // its outline's. Redrawn when the shape, the selection or the option changes.
        private readonly List<LineRenderer> _outlines = new List<LineRenderer>();
        private int _shapeVersion, _outlineKey = int.MinValue;
        private static Material _outlineMat, _hiddenMat, _chordMat;
        private static readonly Color OutlineColor = new Color(0.35f, 0.85f, 1f, 0.85f);
        private static readonly Color OutlineSelected = new Color(1f, 0.70f, 0.15f, 1f);
        private const float HiddenAlpha = 0.25f;                 // an outline's hidden part, of its own alpha
        private const float ChordAlpha = 0.45f, ChordSelectedAlpha = 0.70f;
        private const string HiddenName = "SpineSectionOutlineHidden", ChordName = "SpineSectionChord";

        private static Material LineMaterial(string name, UnityEngine.Rendering.CompareFunction zTest)
        {
            var m = new Material(Shader.Find("Hidden/Internal-Colored")) { name = name };
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)zTest);
            return m;
        }

        private static LineRenderer NewLine(Transform parent, string name, Material mat, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private static LineRenderer Child(LineRenderer lr, string name)
        {
            Transform t = lr.transform.Find(name);
            return t != null ? t.GetComponent<LineRenderer>() : null;
        }

        // Outlines on the selected part only (its PAW open, as for the gizmo), so other Spines stay uncluttered.
        private void UpdateOutlines(bool selected)
        {
            bool on = HighLogic.LoadedSceneIsEditor && SpineSettings.ShowOutlines && selected && nodes.Count > 0 && PPart != null && PPart.CurrentShape == this;
            if (!on) { ClearOutlines(); return; }
            int key = (_shapeVersion * 397 ^ SelIndex) * 31 + nodes.Count;
            if (key == _outlineKey && _outlines.Count == nodes.Count && _outlines.TrueForAll(l => l != null)) return;
            _outlineKey = key;
            if (_outlineMat == null) _outlineMat = LineMaterial("SpineSectionOutline", UnityEngine.Rendering.CompareFunction.LessEqual);
            if (_hiddenMat == null) _hiddenMat = LineMaterial(HiddenName, UnityEngine.Rendering.CompareFunction.Greater);
            if (_chordMat == null) _chordMat = LineMaterial(ChordName, UnityEngine.Rendering.CompareFunction.Always);
            while (_outlines.Count < nodes.Count)
            {
                LineRenderer lr = NewLine(part.transform, "SpineSectionOutline", _outlineMat, true);
                NewLine(lr.transform, HiddenName, _hiddenMat, true);   // these go with their outline
                NewLine(lr.transform, ChordName, _chordMat, false);
                _outlines.Add(lr);
            }
            while (_outlines.Count > nodes.Count)
            {
                LineRenderer extra = _outlines[_outlines.Count - 1];
                _outlines.RemoveAt(_outlines.Count - 1);
                if (extra != null) Destroy(extra.gameObject);
            }
            float size = 0f;
            foreach (SpineNode n in nodes) size = Mathf.Max(size, Erh(n), Erv(n));
            float width = Mathf.Clamp(0.008f * size, 0.01f, 0.05f), proud = 0.4f * width + 0.005f;
            int sel = SelIndex;
            for (int i = 0; i < nodes.Count; i++)
            {
                LineRenderer lr = _outlines[i];
                if (lr == null) continue;
                Ring ring = NodeRing(nodes[i]);
                int n = ring.outline.Length;
                var pts = new Vector3[n];
                Vector3 c = Vector3.zero;
                for (int j = 0; j < n; j++) c += pts[j] = RingV(ring, j);
                c /= n;
                for (int j = 0; j < n; j++) pts[j] += (pts[j] - c).normalized * proud;
                bool hot = i == sel;
                Color col = hot ? OutlineSelected : OutlineColor;
                float w = hot ? 2f * width : width;
                lr.positionCount = n;
                lr.SetPositions(pts);
                lr.startColor = lr.endColor = col;
                lr.widthMultiplier = w;

                LineRenderer hidden = Child(lr, HiddenName);
                if (hidden != null)
                {
                    hidden.positionCount = n;
                    hidden.SetPositions(pts);
                    hidden.startColor = hidden.endColor = new Color(col.r, col.g, col.b, col.a * HiddenAlpha);
                    hidden.widthMultiplier = w;
                }

                // the vertical: where the section crosses its own centre line (x = 0), bottom to top, through the
                // centre (the handle), sheared by the tilt as the ring is
                LineRenderer chord = Child(lr, ChordName);
                if (chord == null) continue;
                float lo = 0f, hi = 0f;
                for (int j = 0; j < n; j++)
                {
                    Vector2 a = ring.outline[j], b = ring.outline[(j + 1) % n];
                    if ((a.x <= 0f) == (b.x <= 0f)) continue;
                    float t = a.x / (a.x - b.x), y = a.y + t * (b.y - a.y);
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
                Vector3 At(float y) => RingV(new Ring { y = ring.y, offsetX = ring.offsetX, offsetZ = ring.offsetZ, tanTiltV = ring.tanTiltV,
                                                        tanTiltH = ring.tanTiltH, outline = new[] { new Vector2(0f, y) } }, 0);
                chord.positionCount = 3;
                chord.SetPositions(new[] { At(lo - proud), At(0f), At(hi + proud) });
                chord.startColor = chord.endColor = new Color(col.r, col.g, col.b, hot ? ChordSelectedAlpha : ChordAlpha);
                chord.widthMultiplier = 0.8f * w;
            }
        }

        private void ClearOutlines()
        {
            foreach (LineRenderer lr in _outlines) if (lr != null) Destroy(lr.gameObject);
            _outlines.Clear();
            _outlineKey = int.MinValue;
        }

        // A drag cut off before the button came up (the part deselected): synced and recorded as a release would.
        private void FinishDrag()
        {
            if (_dragIndex < 0 && _tipDragIndex < 0) return;
            _dragIndex = -1;
            _tipDragIndex = -1;
            _dragAxis = 0;
            if (HighLogic.LoadedSceneIsEditor && nodes.Count > 0)
            {
                RebuildAndPropagate();
                EndDragUndo();
            }
        }

        private void CleanupGizmo()
        {
            _dragIndex = -1;
            _tipDragIndex = -1;
            _dragMoved = false;
            _dragPre = null;
            SetInputLock(false);
            DestroyAll(_nodeHandles);
            DestroyAll(_profIcons);
            DestroyAll(_leftTips);
            DestroyAll(_rightTips);
        }

        private void DestroyAll(List<GameObject> list)
        {
            foreach (GameObject h in list) if (h != null) Destroy(h);
            list.Clear();
        }

        public void OnDisable()
        {
            CleanupGizmo();
            ClearOutlines();
        }

        public void OnDestroy()
        {
            CleanupGizmo();
            ClearOutlines();
            if (_subscribed) { CrossSectionLibrary.Changed -= OnSectionLibraryChanged; _subscribed = false; }
        }

        #endregion

        #region Meshes

        private static void WriteToAppropriateMesh(UncheckedMesh mesh, Mesh iconMesh, Mesh normalMesh)
        {
            Mesh target = (HighLogic.LoadedScene == GameScenes.LOADING) ? iconMesh : normalMesh;
            // A Unity mesh indexes its vertices with 16 bits unless told otherwise, and Procedural Parts' WriteTo
            // doesn't: past 65535 vertices (a shell of many sections at many points) the triangles wrap round onto
            // the wrong vertices and the skin shatters. Clear first: the format can't change under existing triangles.
            target.Clear();
            target.indexFormat = mesh.vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.WriteTo(target);
        }

        private void GenerateSideMesh(List<Ring> rings, List<Ring> inner = null)
        {
            int rc = rings.Count;

            // Shell: the inner wall is the outline inset by the shell thickness (not coincident), so the
            // wall has real depth. Built once and reused for every ring's inner-skin vertex.
            if (shellMode) inner ??= BuildInnerRings(rings); else inner = null;

            // Hard-edge columns: outline indices where the section turns sharply (Rectangle/Mk2
            // corners). Detected globally (union over rings) so the loft topology stays a clean
            // grid; a corner that rounds out along a profile morph keeps its column but the two
            // duplicated normals converge, so the crease fades smoothly into the rounded section
            // instead of cutting across it.
            bool[] hard = ComputeHardColumns(rings);

            // Expand each ring into columns, duplicating every hard column so the two flats it
            // joins carry their own one-sided normals -> a crisp lighting break at the corner.
            var cols = new List<int>();    // original outline index of each emitted column
            var side = new List<int>();    // -1 = left copy, +1 = right copy, 0 = smooth/centred
            for (int j = 0; j < Sides; j++)
            {
                if (hard[j]) { cols.Add(j); side.Add(-1); cols.Add(j); side.Add(+1); }
                else { cols.Add(j); side.Add(0); }
            }
            int K = cols.Count;
            int per = K + 1;                 // +1 wrap column for a clean UV seam

            // A shell also gets an inner skin: the same vertices with flipped normals and reversed
            // winding, so the interior renders and lights correctly (useful for shrouds/shields).
            int vCount = shellMode ? 2 * rc * per : rc * per;
            int triCount = shellMode ? 2 * (rc - 1) * K * 2 : (rc - 1) * K * 2;
            int innerBase = rc * per;
            var mesh = new UncheckedMesh(vCount, triCount);

            float span = rings[rc - 1].y - rings[0].y;
            if (span <= 0f) span = 1f;

            for (int r = 0; r < rc; r++)
            {
                float vCoord = (rings[r].y - rings[0].y) / span;
                Ring below = rings[Mathf.Max(0, r - 1)];
                Ring above = rings[Mathf.Min(rc - 1, r + 1)];
                Ring ring = rings[r];
                float[] arcFrac = RingArcFractions(ring);   // cumulative perimeter fraction -> even texel density
                for (int k = 0; k <= K; k++)
                {
                    int c = k % K;
                    int j = cols[c];
                    int sd = side[c];
                    int idx = r * per + k;
                    Vector3 pos = RingV(ring, j);
                    mesh.vertices[idx] = pos;

                    Vector3 tSpine = RingV(above, j) - RingV(below, j);
                    // One-sided ring tangent on a hard column gives each flat its own face normal;
                    // the centred difference keeps curved sections smooth.
                    Vector3 tRing = (sd < 0) ? RingV(ring, j) - RingV(ring, j - 1)
                                  : (sd > 0) ? RingV(ring, j + 1) - RingV(ring, j)
                                             : RingV(ring, j + 1) - RingV(ring, j - 1);
                    // Outlines run counter-clockwise, so spine x ring is the outward side the triangles below
                    // face (they are wound from the same two edges). No flip toward the ring centre: where the
                    // skin leans back over itself (a raked, narrowing bow) that test points normals inward.
                    Vector3 normal = Vector3.Cross(tSpine, tRing).normalized;
                    if (normal == Vector3.zero)
                    {
                        Vector3 radial = new Vector3(pos.x - ring.offsetX, 0f, pos.z - ring.offsetZ);
                        normal = (radial.sqrMagnitude > 0f) ? radial.normalized : Vector3.right;
                    }
                    mesh.normals[idx] = normal;
                    Vector3 tan = (tRing.sqrMagnitude > 0f) ? tRing.normalized : Vector3.right;
                    mesh.tangents[idx] = new Vector4(tan.x, tan.y, tan.z, 1f);
                    mesh.uv[idx] = new Vector2((k == K) ? 1f : arcFrac[j], vCoord);

                    if (shellMode)   // inner skin: inset inward by the wall thickness, normal flipped
                    {
                        int iidx = innerBase + idx;
                        mesh.vertices[iidx] = RingV(inner[r], j);
                        mesh.normals[iidx] = -normal;
                        mesh.tangents[iidx] = new Vector4(-tan.x, -tan.y, -tan.z, 1f);
                        mesh.uv[iidx] = mesh.uv[idx];
                    }
                }
            }

            // A ring of zero width or height (a wedge's stem, or a point) has no outline tangent of its own, and
            // the strip into it is twisted: the ring before runs across, this one along a line. Normals of its
            // own would differ from the ring before's, and each quad of that strip would crease along its
            // diagonal (a sawtooth down the stem). Copy the nearest full ring's normals instead, so the strip
            // shades as one smooth piece.
            for (int r = 0; r < rc; r++)
            {
                if (!Collapsed(rings[r])) continue;
                int from = -1;
                for (int step = 1; step < rc && from < 0; step++)
                {
                    int lo = r - step, hi = r + step;
                    // an end ring looks inward; a middle one prefers the ring below, as the triangles do
                    if (lo >= 0 && !Collapsed(rings[lo])) from = lo;
                    else if (hi < rc && !Collapsed(rings[hi])) from = hi;
                }
                if (from < 0) break;   // every ring collapsed: nothing better to copy
                for (int k = 0; k <= K; k++)
                {
                    int idx = r * per + k, src = from * per + k;
                    mesh.normals[idx] = mesh.normals[src];
                    mesh.tangents[idx] = mesh.tangents[src];
                    if (shellMode)
                    {
                        mesh.normals[innerBase + idx] = mesh.normals[innerBase + src];
                        mesh.tangents[innerBase + idx] = mesh.tangents[innerBase + src];
                    }
                }
            }

            int t = 0;
            for (int r = 0; r < rc - 1; r++)
            {
                // The strip into a stem fans a wide ring onto a line, so its quads are badly twisted and a split on
                // the long diagonal turns a thin triangle inside out. Split those on the short one.
                bool stem = Collapsed(rings[r]) || Collapsed(rings[r + 1]);
                for (int k = 0; k < K; k++)
                {
                    int a = r * per + k;
                    int b = r * per + k + 1;
                    int cc = (r + 1) * per + k;
                    int d = (r + 1) * per + k + 1;
                    bool ad = stem && (mesh.vertices[d] - mesh.vertices[a]).sqrMagnitude < (mesh.vertices[cc] - mesh.vertices[b]).sqrMagnitude;
                    if (ad)
                    {
                        mesh.triangles[t++] = a; mesh.triangles[t++] = cc; mesh.triangles[t++] = d;
                        mesh.triangles[t++] = a; mesh.triangles[t++] = d; mesh.triangles[t++] = b;
                    }
                    else
                    {
                        mesh.triangles[t++] = a; mesh.triangles[t++] = cc; mesh.triangles[t++] = b;
                        mesh.triangles[t++] = b; mesh.triangles[t++] = cc; mesh.triangles[t++] = d;
                    }
                    if (shellMode)   // inner skin: reversed winding
                    {
                        int ia = innerBase + a, ib = innerBase + b, ic = innerBase + cc, id = innerBase + d;
                        if (ad)
                        {
                            mesh.triangles[t++] = ia; mesh.triangles[t++] = id; mesh.triangles[t++] = ic;
                            mesh.triangles[t++] = ia; mesh.triangles[t++] = ib; mesh.triangles[t++] = id;
                        }
                        else
                        {
                            mesh.triangles[t++] = ia; mesh.triangles[t++] = ib; mesh.triangles[t++] = ic;
                            mesh.triangles[t++] = ib; mesh.triangles[t++] = id; mesh.triangles[t++] = ic;
                        }
                    }
                }
            }

            float avgCirc = 0f;
            foreach (Ring r in rings) avgCirc += OutlinePerimeter(r.outline);
            avgCirc /= rc;
            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(avgCirc, length));
            WriteToAppropriateMesh(mesh, PPart.SidesIconMesh, SidesMesh);
        }

        private static bool Collapsed(Ring ring) => ring.rH < 1e-4f || ring.rV < 1e-4f;

        // Cumulative perimeter fraction at each outline index (length Sides+1, [0]=0 .. [Sides]=1), so the
        // side texture U maps by arc length -> uniform texel density on flats and curves alike.
        private static float[] RingArcFractions(Ring ring)
        {
            Vector2[] o = ring.outline;
            int n = o.Length;
            var cum = new float[n + 1];
            for (int j = 0; j < n; j++) cum[j + 1] = cum[j] + (o[(j + 1) % n] - o[j]).magnitude;
            float total = (cum[n] > 1e-6f) ? cum[n] : 1f;
            for (int j = 0; j <= n; j++) cum[j] /= total;
            return cum;
        }

        // A column is "hard" if the outline turns sharply there in any ring (a Rectangle/Mk2
        // corner). Curved profiles (~15 deg/step at Sides=24) stay under the threshold and remain
        // smoothly shaded.
        private static bool[] ComputeHardColumns(List<Ring> rings)
        {
            const float thresholdDeg = 30f;
            int sides = rings.Count > 0 ? rings[0].outline.Length : MinSides;
            var hard = new bool[sides];
            foreach (Ring ring in rings)
            {
                for (int j = 0; j < sides; j++)
                {
                    if (hard[j]) continue;
                    Vector3 a = RingV(ring, j) - RingV(ring, j - 1);
                    Vector3 b = RingV(ring, j + 1) - RingV(ring, j);
                    Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z);
                    if (a2.sqrMagnitude < 1e-10f || b2.sqrMagnitude < 1e-10f) continue;
                    if (Vector2.Angle(a2, b2) > thresholdDeg) hard[j] = true;
                }
            }
            return hard;
        }

        private void GenerateCapMesh(List<Ring> rings)
        {
            if (shellMode)   // open ends: close the wall edge with a thin rim so it doesn't look paper-thin
            {
                var rim = new UncheckedMesh(4 * Sides, 8 * Sides);
                WriteRim(rim, rings[0], 0, 0, false);                              // bottom rim
                WriteRim(rim, rings[rings.Count - 1], 2 * Sides, 4 * Sides, true); // top rim
                WriteToAppropriateMesh(rim, PPart.EndsIconMesh, EndsMesh);
                return;
            }
            var mesh = new UncheckedMesh(Sides * 2, (Sides - 2) * 2);
            WriteCapVertices(mesh, rings[0], 0, false);
            WriteCapVertices(mesh, rings[rings.Count - 1], Sides, true);
            // best-fit rings aren't the sections' own samplings: triangulate the end rings themselves (unit, so a
            // zero-size end still has a shape to cut)
            int[] lo = _alignUnits != null ? SectionGeometry.Triangulate(_alignUnits[0]) : nodes[0].Section.CapTriangles(Sides);
            int[] hi = _alignUnits != null ? SectionGeometry.Triangulate(_alignUnits[_alignUnits.Length - 1])
                                           : nodes[nodes.Count - 1].Section.CapTriangles(Sides);
            WriteCapTriangles(mesh, lo, false, 0, 0);
            WriteCapTriangles(mesh, hi, true, Sides, Sides - 2);
            WriteToAppropriateMesh(mesh, PPart.EndsIconMesh, EndsMesh);
        }

        // One open-end rim: a thin annulus from the outer outline to the inner (inset) outline, flat-shaded
        // along the spine axis so the wall reads solid at the mouth. vOff/triOff index into the shared mesh.
        // Emitted double-sided so it's visible from either side regardless of the winding convention.
        private void WriteRim(UncheckedMesh mesh, Ring outer, int vOff, int triOff, bool up)
        {
            Ring inner = InnerRing(outer);
            Vector3 nrm = new Vector3(0f, up ? 1f : -1f, 0f);
            for (int j = 0; j < Sides; j++)
            {
                int oi = vOff + j, ii = vOff + Sides + j;
                mesh.vertices[oi] = RingV(outer, j); mesh.normals[oi] = nrm; mesh.tangents[oi] = new Vector4(1f, 0f, 0f, 1f);
                mesh.uv[oi] = new Vector2((float)j / Sides, 1f);
                mesh.vertices[ii] = RingV(inner, j); mesh.normals[ii] = nrm; mesh.tangents[ii] = new Vector4(1f, 0f, 0f, 1f);
                mesh.uv[ii] = new Vector2((float)j / Sides, 0f);
            }
            int t = triOff * 3;
            for (int j = 0; j < Sides; j++)
            {
                int jn = (j + 1) % Sides;
                int o = vOff + j, on = vOff + jn, ic = vOff + Sides + j, inn = vOff + Sides + jn;
                mesh.triangles[t++] = o; mesh.triangles[t++] = on; mesh.triangles[t++] = inn;
                mesh.triangles[t++] = o; mesh.triangles[t++] = inn; mesh.triangles[t++] = ic;
                mesh.triangles[t++] = o; mesh.triangles[t++] = inn; mesh.triangles[t++] = on;
                mesh.triangles[t++] = o; mesh.triangles[t++] = ic; mesh.triangles[t++] = inn;
            }
        }

        private void WriteCapVertices(UncheckedMesh mesh, Ring ring, int offset, bool up)
        {
            float denom = Mathf.Max(0.01f, 2f * Mathf.Max(ring.rH, ring.rV));
            for (int j = 0; j < Sides; j++)
            {
                int idx = offset + j;
                Vector3 pos = RingV(ring, j);
                mesh.vertices[idx] = pos;
                mesh.normals[idx] = new Vector3(0f, up ? 1f : -1f, 0f);
                mesh.tangents[idx] = new Vector4(1f, 0f, 0f, 1f);
                mesh.uv[idx] = new Vector2(pos.x / denom + 0.5f, pos.z / denom + 0.5f);
            }
        }

        // Ear-clipped rather than a fan from vertex 0, so concave custom sections cap correctly. The end ring
        // is its node's section scaled, so the section's cached triangulation fits. It's CCW in outline
        // space; the top cap reverses it to face +Y.
        private void WriteCapTriangles(UncheckedMesh mesh, int[] tri, bool up, int vertexOffset, int triOffset)
        {
            int tio = triOffset * 3;
            for (int i = 0; i < Sides - 2; i++)
            {
                mesh.triangles[i * 3 + tio] = tri[i * 3] + vertexOffset;
                mesh.triangles[i * 3 + 1 + tio] = (up ? tri[i * 3 + 2] : tri[i * 3 + 1]) + vertexOffset;
                mesh.triangles[i * 3 + 2 + tio] = (up ? tri[i * 3 + 1] : tri[i * 3 + 2]) + vertexOffset;
            }
        }

        // The outline points every ring gives the collider: the same ones on each ring (the rings' outlines are
        // aligned point for point, so ring point k is the same feature all along and the collider's faces follow
        // the loft's). The section's own shape, concave or not: the collider's pieces are cut into convex parts
        // along it, and whatever reads its triangles (FAR's aero and water forces) sees the hollow of a V, a step or
        // a notch rather than a hull bridged across it. Every point when the outline has no more than ColliderSides;
        // otherwise its corners (ComputeHardColumns) are kept and the rest of ColliderSides spread evenly by arc
        // length (the rings' mean) over the stretches between them, each stretch its share by length -- a round
        // section (no corners: point 0 anchors it) thins to evenly spaced points, mirror-symmetric about point 0
        // when the section is.
        // Null when the rings don't share a point count (then each ring gives its own hull).
        private List<int> SharedColliderPoints(List<Ring> rings)
        {
            if (rings.Count == 0 || rings[0].outline == null) return null;
            int n = rings[0].outline.Length;
            if (rings.Any(r => r.outline == null || r.outline.Length != n)) return null;
            if (n <= ColliderSides) return Enumerable.Range(0, n).ToList();   // every point

            // arc length position of each point, 0..1 round the outline ([n] = 1), the rings' mean
            var s = new float[n + 1];
            int counted = 0;
            foreach (Ring r in rings)
            {
                float perimeter = 0f;
                for (int j = 0; j < n; j++) perimeter += (r.outline[(j + 1) % n] - r.outline[j]).magnitude;
                if (perimeter < 1e-6f) continue;   // a zero-size end has no shape to weigh
                float[] f = RingArcFractions(r);
                for (int j = 0; j <= n; j++) s[j] += f[j];
                counted++;
            }
            for (int j = 0; j <= n; j++) s[j] = counted > 0 ? s[j] / counted : (float)j / n;

            List<int> corners = ColliderCorners(rings, n);
            if (corners.Count == 0) corners.Add(0);
            if (corners.Count >= ColliderSides)
                return ThinByArea(rings, corners);

            // each stretch (corner i to i+1, wrapping) its share of the rest: one at a time to the stretch whose
            // spacing would stay the widest (its length over its points + 1), while it has outline points left
            int nc = corners.Count;
            var length = new float[nc];
            var room = new int[nc];
            var given = new int[nc];
            for (int i = 0; i < nc; i++)
            {
                int a = corners[i], b = i + 1 < nc ? corners[i + 1] : corners[0] + n;
                length[i] = s[b % n] + (b >= n ? 1f : 0f) - s[a];
                room[i] = b - a - 1;
            }
            for (int left = ColliderSides - nc; left > 0; left--)
            {
                int best = -1;
                for (int i = 0; i < nc; i++)
                    if (given[i] < room[i] && (best < 0 || length[i] / (given[i] + 1) > length[best] / (given[best] + 1) + 1e-6f))
                        best = i;
                given[best]++;
            }

            var idx = new List<int>(ColliderSides);
            for (int i = 0; i < nc; i++)
            {
                int a = corners[i], b = i + 1 < nc ? corners[i + 1] : corners[0] + n, m = given[i];
                idx.Add(a);
                if (m == 0) continue;
                float U(int j) => s[j % n] + (j >= n ? 1f : 0f);
                // each point nearest its even arc position (a tie to the nearer corner, so a symmetric stretch
                // stays symmetric), then kept in order and apart
                var pick = new int[m];
                for (int q = 1; q <= m; q++)
                {
                    float target = s[a] + length[i] * q / (m + 1);
                    bool firstHalf = 2 * q <= m + 1;
                    int bestJ = -1;
                    float bestD = float.MaxValue;
                    for (int step = 0; step < b - a - 1; step++)
                    {
                        int j = firstHalf ? a + 1 + step : b - 1 - step;
                        float d = Mathf.Abs(U(j) - target);
                        if (d < bestD - 1e-6f) { bestD = d; bestJ = j; }
                    }
                    pick[q - 1] = bestJ;
                }
                for (int q = 0; q < m; q++) pick[q] = Math.Max(pick[q], q > 0 ? pick[q - 1] + 1 : a + 1);
                for (int q = m - 1; q >= 0; q--) pick[q] = Math.Min(pick[q], q < m - 1 ? pick[q + 1] - 1 : b - 1);
                foreach (int j in pick) idx.Add(j % n);
            }
            // in outline order from the lowest index (corner 0's stretch may wrap past n)
            int lowest = idx.IndexOf(idx.Min());
            return idx.Skip(lowest).Concat(idx.Take(lowest)).ToList();
        }

        // The outline points where any ring turns sharply (as ComputeHardColumns, 30 deg), each against the nearest
        // points either side that differ from it: a resampled outline can hold a corner twice (ResampleWithCorners
        // puts a filler on it), and the copies' zero-length edge hides the turn from a neighbour-to-neighbour test.
        // Of a point repeated, the first copy counts.
        private static List<int> ColliderCorners(List<Ring> rings, int n)
        {
            const float eps2 = 1e-10f, thresholdDeg = 30f;
            var hard = new bool[n];
            foreach (Ring r in rings)
            {
                Vector2[] o = r.outline;
                for (int j = 0; j < n; j++)
                {
                    if (hard[j] || (o[j] - o[(j + n - 1) % n]).sqrMagnitude < eps2) continue;
                    int p = j - 1, q = j + 1;
                    while (p > j - n && (o[((p % n) + n) % n] - o[j]).sqrMagnitude < eps2) p--;
                    while (q < j + n && (o[q % n] - o[j]).sqrMagnitude < eps2) q++;
                    Vector2 a = o[j] - o[((p % n) + n) % n], b = o[q % n] - o[j];
                    if (a.sqrMagnitude < eps2 || b.sqrMagnitude < eps2) continue;
                    if (Vector2.Angle(a, b) > thresholdDeg) hard[j] = true;
                }
            }
            var corners = new List<int>();
            for (int j = 0; j < n; j++) if (hard[j]) corners.Add(j);
            return corners;
        }

        // More corners than ColliderSides: drop the corner whose triangle with its neighbours costs the least area
        // summed over the rings, till ColliderSides are left
        private List<int> ThinByArea(List<Ring> rings, List<int> idx)
        {
            idx = new List<int>(idx);
            float Cost(int k)
            {
                int c = idx.Count, a = idx[(k + c - 1) % c], b = idx[k], d = idx[(k + 1) % c];
                float sum = 0f;
                foreach (Ring r in rings)
                {
                    Vector2 pa = r.outline[a], pb = r.outline[b], pd = r.outline[d];
                    sum += Mathf.Abs((pb.x - pa.x) * (pd.y - pa.y) - (pb.y - pa.y) * (pd.x - pa.x));
                }
                return sum;
            }
            while (idx.Count > ColliderSides)
            {
                int drop = 0;
                float least = float.MaxValue;
                for (int k = 0; k < idx.Count; k++)
                {
                    float c = Cost(k);
                    if (c < least) { least = c; drop = k; }
                }
                idx.RemoveAt(drop);
            }
            return idx;
        }

        // One ring's points for the collider when the rings don't share a point count: its outline's hull,
        // thinned to ColliderSides by dropping the hull points that cost the least area -- corners and tips of a
        // custom section survive, wherever they fall in the outline.
        private List<int> ColliderPoints(Vector2[] o)
        {
            var idx = Enumerable.Range(0, o.Length).OrderBy(i => o[i].x).ThenBy(i => o[i].y).ToList();
            var hull = new List<int>(2 * o.Length);
            float Turn(int a, int b, int c) => (o[b].x - o[a].x) * (o[c].y - o[a].y) - (o[b].y - o[a].y) * (o[c].x - o[a].x);
            for (int pass = 0; pass < 2; pass++)   // monotone chain: lower hull, then upper
            {
                int floor = hull.Count + 1;
                foreach (int i in idx)
                {
                    while (hull.Count > floor && Turn(hull[hull.Count - 2], hull[hull.Count - 1], i) <= 1e-9f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(i);
                }
                hull.RemoveAt(hull.Count - 1);
                idx.Reverse();
            }
            if (hull.Count == 0) hull.Add(0);
            while (hull.Count > ColliderSides)
            {
                int drop = 0;
                float least = float.MaxValue;
                for (int k = 0; k < hull.Count; k++)
                {
                    float area = Mathf.Abs(Turn(hull[(k + hull.Count - 1) % hull.Count], hull[k], hull[(k + 1) % hull.Count]));
                    if (area < least) { least = area; drop = k; }
                }
                hull.RemoveAt(drop);
            }
            return hull;
        }

        // The collider, as convex pieces: physics takes each MeshCollider's convex hull, and KSP surface-attaches to
        // the collider (a part goes where a ray hits it, turned to the face's normal), so one hull of the whole part
        // floats over every waist, hollow and narrowing end. A piece is a run of rings whose collider points stay
        // near straight lines from the run's first ring to its last (a linear segment is one run, a curved one a
        // few), cut along the section into convex parts where the section is concave. The pieces' triangles are
        // the loft's sides and end caps between them, each once, so whatever reads them (FAR's aero and water
        // forces) sees one skin, as before.
        private void GenerateColliderMesh(List<Ring> rings)
        {
            int rc = rings.Count;
            if (rc < 2) return;
            List<int> shared = SharedColliderPoints(rings);
            int per = shared?.Count ?? ColliderSides;
            var pts = new Vector3[rc][];
            var flat = new Vector2[rc][];
            for (int r = 0; r < rc; r++)
            {
                Ring ring = rings[r];
                List<int> idx = shared ?? ColliderPoints(ring.outline);
                pts[r] = new Vector3[per];
                flat[r] = new Vector2[per];
                for (int j = 0; j < per; j++)
                {
                    // a short list repeats points in order, so the ring still goes round once (the padding makes
                    // zero-area triangles); wrapping round it again would fold the mesh over itself
                    int k = shared != null ? idx[j] : idx[j * idx.Count / per];
                    pts[r][j] = RingV(ring, k);
                    flat[r][j] = ring.outline[k];
                }
            }

            var meshes = new List<Mesh>();
            var todo = new Stack<(int a, int b)>(ColliderRuns(pts).AsEnumerable().Reverse());
            while (todo.Count > 0)
            {
                (int a, int b) = todo.Pop();
                // a ring's own hull points (no shared list) are convex already; a section's convex parts must be
                // convex on every ring of the run (a morph changes the shape along it), else the run is halved
                List<List<int>> parts;
                if (shared == null) parts = new List<List<int>> { Enumerable.Range(0, per).ToList() };
                else
                {
                    parts = ConvexParts(flat[(a + b) / 2]);
                    if (parts.Count > 1 && b - a > 1 && !parts.All(q => ConvexOnRings(flat, a, b, q)))
                    {
                        int mid = (a + b) / 2;
                        todo.Push((mid, b));
                        todo.Push((a, mid));
                        continue;
                    }
                }
                foreach (List<int> poly in parts)
                    if (ColliderPiece(pts, a, b, poly, per, a == 0 && !shellMode, b == rc - 1 && !shellMode) is Mesh m)
                        meshes.Add(m);
            }
            SetColliders(meshes);
        }

        // Runs of rings [a, b] for the pieces: a run grows while every collider point of the rings inside it stays
        // within a little of the straight line between the run's end rings' points.
        private static List<(int a, int b)> ColliderRuns(Vector3[][] pts)
        {
            var runs = new List<(int, int)>();
            int rc = pts.Length, a = 0;
            while (a < rc - 1)
            {
                int b = a + 1;
                while (b + 1 < rc && Straight(pts, a, b + 1)) b++;
                runs.Add((a, b));
                a = b;
            }
            return runs;
        }

        private static bool Straight(Vector3[][] pts, int a, int b)
        {
            for (int j = 0; j < pts[a].Length; j++)
            {
                Vector3 p = pts[a][j], q = pts[b][j], d = q - p;
                float len2 = d.sqrMagnitude;
                float tol = Mathf.Max(0.005f, 0.004f * Mathf.Sqrt(len2));
                for (int m = a + 1; m < b; m++)
                {
                    Vector3 v = pts[m][j] - p;
                    float t = len2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(v, d) / len2) : 0f;
                    if ((v - t * d).sqrMagnitude > tol * tol) return false;
                }
            }
            return true;
        }

        // One piece: the outline points `poly` (convex, in outline order) on rings a..b; its triangles the loft's
        // side faces along the outline edges it owns (consecutive in the outline and in `poly`) and, at an end of the
        // part, its share of the end cap. A flat piece gets a point lifted off its plane, so physics can make a hull
        // of it; one with no extent at all is none.
        private static Mesh ColliderPiece(Vector3[][] pts, int a, int b, List<int> poly, int per, bool capLo, bool capHi)
        {
            int np = poly.Count, rings = b - a + 1;
            if (np < 2) return null;
            var verts = new List<Vector3>(np * rings + 1);
            for (int r = a; r <= b; r++)
                foreach (int j in poly) verts.Add(pts[r][j]);
            var tris = new List<int>();
            for (int k = 0; k < np; k++)
            {
                int j = poly[k], j1 = poly[(k + 1) % np];
                if (j1 != (j + 1) % per && !(np == per)) continue;   // a cut across the section, not an outline edge
                for (int r = 0; r < rings - 1; r++)
                {
                    int va = r * np + k, vb = r * np + (k + 1) % np, vc = (r + 1) * np + k, vd = (r + 1) * np + (k + 1) % np;
                    tris.Add(va); tris.Add(vc); tris.Add(vb);
                    tris.Add(vb); tris.Add(vc); tris.Add(vd);
                }
            }
            if (capLo)
                for (int k = 1; k < np - 1; k++) { tris.Add(0); tris.Add(k); tris.Add(k + 1); }
            if (capHi)
            {
                int top = (rings - 1) * np;
                for (int k = 1; k < np - 1; k++) { tris.Add(top); tris.Add(top + k + 1); tris.Add(top + k); }
            }

            // flat? (all the points within a millimetre of one plane)
            Vector3 v0 = verts[0], v1 = v0;
            foreach (Vector3 v in verts) if ((v - v0).sqrMagnitude > (v1 - v0).sqrMagnitude) v1 = v;
            Vector3 axis = v1 - v0;
            if (axis.sqrMagnitude < 1e-8f) return null;
            Vector3 v2 = v0;
            float far = 0f;
            foreach (Vector3 v in verts)
            {
                float d = Vector3.Cross(axis, v - v0).sqrMagnitude;
                if (d > far) { far = d; v2 = v; }
            }
            Vector3 nrm = Vector3.Cross(axis, v2 - v0);
            if (nrm.sqrMagnitude < 1e-12f) return null;
            nrm.Normalize();
            float thick = 0f;
            foreach (Vector3 v in verts) thick = Mathf.Max(thick, Mathf.Abs(Vector3.Dot(v - v0, nrm)));
            if (thick < 1e-3f)
            {
                Vector3 c = Vector3.zero;
                foreach (Vector3 v in verts) c += v;
                verts.Add(c / verts.Count + 2e-3f * nrm);
            }

            var mesh = new Mesh { name = "SpineCollider" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        // A section outline (collider points, in order) cut into convex parts: ear-clipped into triangles, then
        // neighbouring parts merged while the merge stays convex (Hertel-Mehlhorn). Points repeated in a row count
        // once; a convex or degenerate outline is one part.
        private static List<List<int>> ConvexParts(Vector2[] o)
        {
            int n = o.Length;
            var all = Enumerable.Range(0, n).ToList();
            var u = new List<int>(n);
            for (int j = 0; j < n; j++)
                if (u.Count == 0 || (o[j] - o[u[u.Count - 1]]).sqrMagnitude > 1e-10f) u.Add(j);
            while (u.Count > 1 && (o[u[0]] - o[u[u.Count - 1]]).sqrMagnitude <= 1e-10f) u.RemoveAt(u.Count - 1);
            if (u.Count < 4) return new List<List<int>> { all };
            var repeat = Enumerable.Repeat(true, n).ToArray();
            foreach (int j in u) repeat[j] = false;
            float area = 0f;
            for (int k = 0; k < u.Count; k++)
            {
                Vector2 p = o[u[k]], q = o[u[(k + 1) % u.Count]];
                area += p.x * q.y - q.x * p.y;
            }
            if (Mathf.Abs(area) < 1e-8f) return new List<List<int>> { all };
            float sgn = Mathf.Sign(area);
            float Cross(int i, int j, int k) => sgn * ((o[j].x - o[i].x) * (o[k].y - o[i].y) - (o[j].y - o[i].y) * (o[k].x - o[i].x));
            bool Convex(List<int> poly)
            {
                for (int k = 0; k < poly.Count; k++)
                    if (Cross(poly[k], poly[(k + 1) % poly.Count], poly[(k + 2) % poly.Count]) < -1e-7f) return false;
                return true;
            }
            if (Convex(u)) return new List<List<int>> { all };

            // ear clipping
            var rest = new List<int>(u);
            var parts = new List<List<int>>();
            int guard = 0;
            while (rest.Count > 3 && guard++ < 10 * n)
            {
                bool clipped = false;
                for (int k = 0; k < rest.Count; k++)
                {
                    int i = rest[(k + rest.Count - 1) % rest.Count], j = rest[k], l = rest[(k + 1) % rest.Count];
                    if (Cross(i, j, l) <= 1e-9f) continue;
                    bool inside = false;
                    foreach (int m in rest)
                    {
                        if (m == i || m == j || m == l) continue;
                        if (Cross(i, j, m) > 1e-9f && Cross(j, l, m) > 1e-9f && Cross(l, i, m) > 1e-9f) { inside = true; break; }
                    }
                    if (inside) continue;
                    parts.Add(new List<int> { i, j, l });
                    rest.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) return new List<List<int>> { all };   // not a simple polygon: one part
            }
            parts.Add(rest);

            // merge across shared edges while convex
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int x = 0; x < parts.Count && !merged; x++)
                    for (int y = x + 1; y < parts.Count && !merged; y++)
                    {
                        List<int> m = Merge(parts[x], parts[y]);
                        if (m == null || !Convex(m)) continue;
                        parts[x] = m;
                        parts.RemoveAt(y);
                        merged = true;
                    }
            }
            // each part in outline order (as u runs), with the repeats of its points
            var order = new int[n];
            for (int k = 0; k < u.Count; k++) order[u[k]] = k;
            return parts.Select(p => WithRepeats(p.OrderBy(j => order[j]).ToList(), repeat)).ToList();
        }

        // Whether outline points `poly` make a convex polygon on every ring a..b (each ring's own turning sense).
        private static bool ConvexOnRings(Vector2[][] flat, int a, int b, List<int> poly)
        {
            for (int r = a; r <= b; r++)
            {
                Vector2[] o = flat[r];
                float area = 0f;
                for (int k = 0; k < o.Length; k++) area += o[k].x * o[(k + 1) % o.Length].y - o[(k + 1) % o.Length].x * o[k].y;
                if (Mathf.Abs(area) < 1e-8f) continue;   // a closed end: no shape to bend
                float sgn = Mathf.Sign(area);
                float span = 0f;
                foreach (int j in poly) span = Mathf.Max(span, o[j].magnitude);
                float eps = 1e-4f * span * span;
                // the part's distinct points, in order
                var q = new List<Vector2>(poly.Count);
                foreach (int j in poly)
                    if (q.Count == 0 || (o[j] - q[q.Count - 1]).sqrMagnitude > 1e-10f) q.Add(o[j]);
                if (q.Count > 1 && (q[0] - q[q.Count - 1]).sqrMagnitude <= 1e-10f) q.RemoveAt(q.Count - 1);
                for (int k = 0; k < q.Count; k++)
                {
                    Vector2 p0 = q[k], p1 = q[(k + 1) % q.Count], p2 = q[(k + 2) % q.Count];
                    if (sgn * ((p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x)) < -eps) return false;
                }
            }
            return true;
        }

        // Two polygons sharing an edge (one's i->j, the other's j->i) joined across it; null if they share none.
        private static List<int> Merge(List<int> p, List<int> q)
        {
            for (int a = 0; a < p.Count; a++)
            {
                int i = p[a], j = p[(a + 1) % p.Count];
                int b = q.IndexOf(j);
                if (b < 0 || q[(b + 1) % q.Count] != i) continue;
                var m = new List<int>(p.Count + q.Count - 2);
                for (int k = 0; k < p.Count; k++) m.Add(p[(a + 1 + k) % p.Count]);   // j ... i
                for (int k = 2; k < q.Count; k++) m.Add(q[(b + k) % q.Count]);         // q's others, i to j
                return m;
            }
            return null;
        }

        // A part's points with the outline's repeats of each (the ones skipped as duplicates) put back after it, so
        // its outline edges are the outline's own, zero-length ones included.
        private static List<int> WithRepeats(List<int> part, bool[] repeat)
        {
            int n = repeat.Length;
            var r = new List<int>(part.Count);
            foreach (int j in part)
            {
                r.Add(j);
                for (int k = (j + 1) % n; k != j && repeat[k]; k = (k + 1) % n) r.Add(k);
            }
            return r;
        }

        // The pieces onto the part's collider holder: its MeshColliders reused while their count holds (only the
        // meshes change), remade when it changes.
        private List<MeshCollider> _pieces;

        private void SetColliders(List<Mesh> meshes)
        {
            Transform holder = PPart.ColliderHolder.transform;
            bool reuse = _pieces != null && _pieces.Count == meshes.Count && _pieces.All(c => c != null && c.transform.parent == holder)
                         && holder.childCount == meshes.Count;
            if (!reuse)
            {
                foreach (Transform child in holder.Cast<Transform>().ToList())
                {
                    child.SetParent(null, false);
                    Destroy(child.gameObject);
                }
                _pieces = new List<MeshCollider>(meshes.Count);
                for (int i = 0; i < meshes.Count; i++)
                {
                    var go = new GameObject("collisionMesh");
                    go.transform.SetParent(holder, false);
                    go.layer = holder.gameObject.layer;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = true;
                    _pieces.Add(mc);
                }
            }
            for (int i = 0; i < meshes.Count; i++)
            {
                Mesh old = _pieces[i].sharedMesh;
                _pieces[i].sharedMesh = meshes[i];
                if (old != null && old != meshes[i] && old.name == "SpineCollider") Destroy(old);
            }
        }

        private void UpdateNodeSize(string nodeName, float diameter)
        {
            AttachNode node = part.attachNodes.Find(n => n.id == nodeName);
            if (node == null) return;
            node.size = Math.Min((int)(diameter / PPart.diameterLargeStep), 3);
            node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);
            RaiseChangeAttachNodeSize(node, diameter, Mathf.PI * diameter * diameter * 0.25f);
            RaiseChangeTextureScale(nodeName, PPart.legacyTextureHandler.EndsMaterial, new Vector2(diameter, diameter));
        }

        private static float OutlinePerimeter(Vector2[] o)
        {
            float p = 0f;
            for (int j = 0; j < o.Length; j++) p += (o[(j + 1) % o.Length] - o[j]).magnitude;
            return p;
        }

        #endregion
    }
}

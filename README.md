# CustomProceduralParts

Extra shapes for [ProceduralParts](https://github.com/KSP-RO/ProceduralParts), shipped as a
separate mod. Requires ProceduralParts and ModuleManager.

## Install

From CKAN (installs ProceduralParts and ModuleManager with it), or unzip a
[release](https://github.com/chambem42/CustomProceduralParts/releases) into the KSP folder so you
get `GameData/CustomProceduralParts/`. Needs ProceduralParts 2.8 or later and KSP 1.12.

## Shapes

### Spine

A fuselage-like body lofted from a series of cross-section nodes along an axis, edited with an
in-scene gizmo. Added to every ProceduralParts part (pick it from the Shape selector).

- Per-node cross-section: built-in Ellipse (with stretch), Rectangle (top/bottom fillets), Mk2,
  Mk3, or any custom section. Sections morph point-for-point between adjacent nodes.
- Custom cross-sections: **Edit Cross Sections** in the PAW opens an editor. Pick a section, drag
  its vertices on the grid (snap and grid step optional; double-click an edge to add a vertex,
  right-click one to remove it), then Save, or Save As a new name. Drag the window's corner to
  stretch the grid, and turn Plot opacity down to see the scene through it (to trace a part's
  cross-section, say). Mirror left-to-right and/or top-to-bottom: edit one side (or the top-left
  quadrant) and the rest follows; turning a mirror on replaces the far side, and Revert undoes it.
  With the dialog open and no unsaved edits, it shows whichever node is selected. Built-ins can't
  be overwritten, only saved as a copy. Sections are polygons of up to 96 vertices that don't
  self-intersect (concave is fine), fitted to the node's width x height.
- Shared and craft sections: your shared sections are stored in
  `GameData/CustomProceduralParts/PluginData/CrossSections.cfg` and offered on every craft. Each
  craft also embeds the sections it uses, so it loads anywhere, and loading one never adds to your
  shared list: its nodes keep the craft's own copies (listed as "craft" in the dialog, whose
  "Show sections from" toggles pick built-in / shared / craft). Save on a craft section updates
  that craft; Save As puts it in your shared list, and the craft then uses the shared one. A craft
  section that is one of your shared sections, now or before a later edit, uses the shared one,
  so the craft follows the edit. Vessels outside the editor keep the geometry they were built with.
- Per-node width, height, vertical/horizontal offset, tilt, and position; per-segment slope
  (linear/concave/convex/waisted). First/last nodes are pinned to the ends.
- Gizmo: circle handles select / offset a node, edge-tip handles resize and reposition, the
  section glyph cycles the profile. Right-drag for the alternate axis.
- **Spine Options** in the PAW opens a window. For all Spine parts (saved in
  `PluginData/SpineOptions.cfg`): which nodes show handles (all, the selected one, none), click a
  node before dragging it, and cross-section outlines (every node's section drawn on the part, its
  hidden side faint, with a faint vertical line through each node's handle; the selected node's in
  orange). For this part (saved with the craft): points per section, 24 to 96, for smoother curves.
- Airfoil toggle (drives a stock ModuleLiftingSurface the patch adds).
- Hollow shell mode with variable thickness and closable ends.
- Volume, drag cubes, symmetry, and FAR/TestFlight interop.

Known limitations: surface-attach coordinates ignore per-node vertical offset; open shell ends
still have solid stack nodes and a closed FAR voxelization.

## Tests

`KspTests/` holds in-game tests for the KspTest harness: `dotnet test
KspTests/CustomProceduralParts.KspTests.Run`. They run in a KSP instance KspTest makes with
ProceduralParts from CKAN, the ProceduralParts build this mod compiles against over it
(`PPGameData`, default a sibling `ProceduralParts` checkout) and this mod. Set `KspDir` (or
`KSPDIR`) to the KSP install to build against, and `KspTestRoot` (or `KSPTEST_ROOT`) if KspTest isn't
checked out beside this repo; `KspTests/Directory.Build.props.user` holds local values.

Showcase videos are recorded the same way but kept apart (each wants an instance with extra mods):
`dotnet test KspTests/CustomProceduralParts.Showcase.Run --filter FullyQualifiedName~Showcase_B2Fuselage`
saves every frame (to `CPP_SHOWCASE_DIR` if set) for ffmpeg to assemble; see `B2Showcase.cs`.

## Building

ProceduralParts keeps the shape API's `UpdateShape` / `InitializeAttachmentNodes` `internal`, so
this project compiles against a publicized ProceduralParts (Krafs.Publicizer).

Stage these into `.refs/` (flat): `Assembly-CSharp`, `UnityEngine`, `UnityEngine.CoreModule`,
`UnityEngine.IMGUIModule`, `UnityEngine.InputLegacyModule`, `UnityEngine.PhysicsModule`,
`UnityEngine.TextRenderingModule` (from `KSP_x64_Data/Managed`) and `ProceduralParts.dll`. Then:

```
MSBuild Source/CustomProceduralParts.csproj -restore -p:Configuration=Release
```

Output goes to `GameData/CustomProceduralParts/Plugins/`. Pass `-p:RefsDir=<dir>` to use a
different reference folder.

## Releasing

1. Bump `<Version>` in `Source/CustomProceduralParts.csproj`, the `KSPAssembly` version in
   `Source/AssemblyAttributes.cs`, and `VERSION` in
   `GameData/CustomProceduralParts/CustomProceduralParts.version`.
2. Build Release, then `powershell -File tools/package.ps1` (checks the three versions agree and
   writes `dist/CustomProceduralParts-<version>.zip`).
3. Push, then `gh release create v<version> dist/CustomProceduralParts-<version>.zip`.

CKAN picks up new releases on its own. Its metadata is `CustomProceduralParts.netkan` in this repo
(the NetKAN repo points at it), so dependency or tag changes go there.

## License

CC BY-SA 3.0, same as ProceduralParts. See `LICENSE.txt`.

# CustomProceduralParts

Extra shapes for [ProceduralParts](https://github.com/KSP-RO/ProceduralParts), shipped as a
separate mod. Requires ProceduralParts and ModuleManager.



https://github.com/user-attachments/assets/7f5f5706-9512-4fa2-8f38-de1a7377e339


## Install

From CKAN (installs ProceduralParts and ModuleManager with it), or unzip a
[release](https://github.com/chambem42/CustomProceduralParts/releases) into the KSP folder so you
get `GameData/CustomProceduralParts/`. Needs ProceduralParts 2.8 or later and KSP 1.12.

## Shapes

### Spine

A fuselage-like body lofted from a series of cross-section nodes along an axis, edited with an
in-scene gizmo. Added to every ProceduralParts part (pick it from the Shape selector).

- Per-node cross-section: built-in Ellipse, Rectangle, Mk2, Mk3, or any custom section. Sections morph point-for-point between adjacent nodes. With custom
  sections in the spine, the points pair by best fit, whatever each section's vertex count: matching
  corners and edges morph into each other instead of twisting, every vertex stays exact (unless a
  section has more than the part's points per section), and neighbours with the same vertex count
  morph vertex to vertex.
- Custom cross-sections: **Edit Cross Sections** in the PAW opens an editor. Pick a section, drag
  its vertices on the grid (snap and grid step optional; double-click an edge to add a vertex,
  right-click one to remove it), then Save, or Save As a new name. Drag the window's corner to
  stretch the grid, and turn Plot opacity down to see the scene through it (to trace a part's
  cross-section, say). Rest the pointer on a control for a tooltip. Mirror left-to-right and/or top-to-bottom: edit one side (or the top-left
  quadrant) and the rest follows; turning a mirror on replaces the far side, and Revert undoes it.
  The mirrors stay ticked (in `PluginData/SpineOptions.cfg`) for every section you open that is
  already symmetric that way; one that isn't opens unmirrored until you tick it. Stretch pulls the
  section's halves apart with straight runs between, fitted back into the box: right for a flat top
  and bottom (a circle becomes a stadium), left for flat sides. With the dialog open and no unsaved
  edits, it shows whichever node is selected. Built-ins can't
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
  node before dragging it, and cross-section outlines (on the selected part, every node's section
  drawn on it, its hidden side faint, with a faint vertical line through each node's handle; the
  selected node's in orange). For this part (saved with the craft): points per section, 24 to 96, for smoother curves.
- Surface attachment follows the skin: the collider is convex pieces that track the visible
  surface (waists, hollows and concave sections included), so a part attaches where you point and
  turned to that face. Edit the Spine any way and what's attached stays on the skin at the same
  spot, without turning; a Spine surface-attached to something stays touching it.
- Hollow shell mode with variable thickness and closable ends.
- Volume, drag cubes, symmetry, and FAR/TestFlight interop.

Known limitations: open shell ends still have solid stack nodes and a closed FAR voxelization.

## RP-1

RP-1 tools a Spine tank, structural part, nose cone or crew tube as two bounding cylinders, its
narrowest and its widest section, both at the part's length (one tooling when the section doesn't
change). The part menu lists both cylinders and whether each is tooled, updated as you reshape the
part. Procedural avionics are tooled with the widest. That's RP-1's own code (its
`ModuleToolingPTank`, from the Spine's `MinBoundingDiameter`, `MaxDiameter` and `Length`), so this mod
has nothing RP-1 specific. An RP-1 without it tools a Spine by the size of the part's hidden Cylinder
shape.

## Tests

`KspTests/` holds in-game tests for the KspTest harness: `dotnet test
KspTests/CustomProceduralParts.KspTests.Run`. They run in a KSP instance KspTest makes with
ProceduralParts from CKAN, the ProceduralParts build this mod compiles against over it
(`PPGameData`, default a sibling `ProceduralParts` checkout) and this mod. Set `KspDir` (or
`KSPDIR`) to the KSP install to build against, and `KspTestRoot` (or `KSPTEST_ROOT`) if KspTest isn't
checked out beside this repo; `KspTests/Directory.Build.props.user` holds local values.

RP-1's tooling of Spines has its own: `dotnet test KspTests/CustomProceduralParts.RP1Tests.Run`, in
an instance with RP-1 and its dependencies from CKAN (several GB, made once from the CKAN download
cache). KspTest builds RP-1 itself from source over CKAN's copy: until a release tools Spines, the
branch that does. `-p:Rp1Repo=<folder or GitHub owner/name>` with `-p:Rp1Ref=<branch>` or
`-p:Rp1Pr=<number>` picks another (a folder alone: its checkout as it is), `-p:Rp1Repo=ckan` CKAN's
release. Building it needs Visual Studio's MSBuild.

Showcase videos are recorded the same way but kept apart (each wants an instance with extra mods):
`dotnet test KspTests/CustomProceduralParts.Showcase.Run --filter FullyQualifiedName~Showcase_B2Fuselage`
saves every frame (to `CPP_SHOWCASE_DIR` if set) for ffmpeg to assemble; see `B2Showcase.cs`.
`Showcase_SpineBasics` (`SpineBasicsShowcase.cs`) is the PAW, the handles and a boat hull. Each
take is a couple of GB of frames.

## Building

ProceduralParts keeps the shape API's `UpdateShape` / `InitializeAttachmentNodes` `internal`, so
this project compiles against a publicized ProceduralParts (Krafs.Publicizer).

Stage these into `.refs/` (flat): `Assembly-CSharp`, `UnityEngine`, `UnityEngine.CoreModule`,
`UnityEngine.IMGUIModule`, `UnityEngine.InputLegacyModule`, `UnityEngine.PhysicsModule`,
`UnityEngine.TextRenderingModule` (from `KSP_x64_Data/Managed`), `ProceduralParts.dll`, and for
the RP-1 tests `RP0.dll` and `RealFuels.dll`. Then:

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

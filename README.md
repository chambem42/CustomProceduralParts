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

- Per-node cross-section: built-in Ellipse (with stretch), Rectangle (top/bottom fillets), Mk2,
  Mk3, or any custom section. Sections morph point-for-point between adjacent nodes.
- Custom cross-sections: **Edit Cross Sections** in the PAW opens an editor. Pick a section, drag
  its vertices on the grid (snap and grid step optional; double-click an edge to add a vertex,
  right-click one to remove it), then Save, or Save As a new name. Built-ins can't be overwritten,
  only saved as a copy. Sections are polygons of up to 24 vertices that don't self-intersect
  (concave is fine), fitted to the node's width x height. They're stored in
  `GameData/CustomProceduralParts/PluginData/CrossSections.cfg`, and each craft embeds the ones it
  uses, so it loads anywhere. On import, a name clash with a different local section is renamed
  `Name (2)`.
- Per-node width, height, vertical/horizontal offset, tilt, and position; per-segment slope
  (linear/concave/convex/waisted). First/last nodes are pinned to the ends.
- Gizmo: circle handles select / offset a node, edge-tip handles resize and reposition, the
  section glyph cycles the profile. Right-drag for the alternate axis.
- Airfoil toggle (drives a stock ModuleLiftingSurface the patch adds).
- Hollow shell mode with variable thickness and closable ends.
- Volume, drag cubes, symmetry, and FAR/TestFlight interop.

Known limitations: surface-attach coordinates ignore per-node vertical offset; open shell ends
still have solid stack nodes and a closed FAR voxelization.

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

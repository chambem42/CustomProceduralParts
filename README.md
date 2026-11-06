# CustomProceduralParts

Extra shapes for [ProceduralParts](https://github.com/KSP-RO/ProceduralParts), shipped as a
separate mod. Requires ProceduralParts and ModuleManager.

## Shapes

### Spine

A fuselage-like body lofted from a series of cross-section nodes along an axis, edited with an
in-scene gizmo. Added to every ProceduralParts part (pick it from the Shape selector).

- Per-node cross-section: Ellipse (with stretch), Rectangle (top/bottom fillets), Mk2, Mk3.
  Profiles morph point-for-point between adjacent nodes.
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

## License

CC BY-SA 3.0, same as ProceduralParts. See `LICENSE.txt`.

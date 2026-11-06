// KSP loads plugin DLLs in GameData folder order; the dependency makes it load ProceduralParts first.
[assembly: KSPAssembly("CustomProceduralParts", 0, 1)]
[assembly: KSPAssemblyDependency("ProceduralParts", 2, 0)]

#!/usr/bin/env bash
# .refs/ for the build (see README's Building) from the game the CI set up: KSP's Managed assemblies and the
# ProceduralParts CKAN installed into it.
set -euo pipefail
m="$KSPTEST_GAME/KSP_x64_Data/Managed"
[ -d "$m" ] || m="$KSPTEST_GAME/KSP_Data/Managed"
mkdir -p .refs
cp "$m"/Assembly-CSharp.dll "$m"/UnityEngine.dll "$m"/UnityEngine.*.dll .refs/
cp "$KSPTEST_GAME"/GameData/ProceduralParts/Plugins/ProceduralParts.dll .refs/
ls .refs

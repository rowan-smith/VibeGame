#!/usr/bin/env bash
# Compile terrain MGFX shaders. On Windows use compile-terrain-shaders.ps1 directly.
# On Linux, run scripts/setup-mgfxc-wine.sh once, then set MGFXC_WINE_PATH.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/Veilborne.Core/assets/shaders"
if [[ "$(uname -s)" == "Linux" ]]; then
  export MGFXC_WINE_PATH="${MGFXC_WINE_PATH:-$HOME/.winemonogame}"
  if [[ ! -f "$MGFXC_WINE_PATH/drive_c/Program Files/dotnet/dotnet.exe" ]]; then
    echo "Wine MGFXC prefix not found. Run: $ROOT/scripts/setup-mgfxc-wine.sh" >&2
    exit 1
  fi
  xvfb-run -a mgfxc terrain_biome_merge.fx terrain_biome_merge.mgfxo /Profile:OpenGL
else
  mgfxc terrain_biome_merge.fx terrain_biome_merge.mgfxo /Profile:OpenGL
fi
echo "Compiled terrain_biome_merge.mgfxo"

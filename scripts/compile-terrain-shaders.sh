#!/usr/bin/env bash
# Compile terrain MGFX shaders. On Windows use compile-terrain-shaders.ps1 directly.
# On Linux, MGFXC requires Wine + .NET 8 in the wine prefix (see MonoGame docs).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/Veilborne.Core/assets/shaders"
mgfxc terrain_biome_merge.fx terrain_biome_merge.mgfxo /Profile:OpenGL
echo "Compiled terrain_biome_merge.mgfxo"

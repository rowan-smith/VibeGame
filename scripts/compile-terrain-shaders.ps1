# Compile terrain MGFX shaders for MonoGame DesktopGL (run on Windows or with Wine+MGFXC configured).
$ErrorActionPreference = "Stop"
$shaderDir = Join-Path $PSScriptRoot "..\Veilborne.Core\assets\shaders"
Push-Location $shaderDir
try {
    mgfxc terrain_biome_merge.fx terrain_biome_merge.mgfxo /Profile:OpenGL
    Write-Host "Compiled terrain_biome_merge.mgfxo"
}
finally {
    Pop-Location
}

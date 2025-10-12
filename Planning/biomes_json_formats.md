Case A:
```json
"Textures": {
    // Diffuse / Albedo (strongly prefer Albedo)
    "Albedo": "textures/terrain/foo/foo_diff.png",
    // Normal map (GL or DX format)
    "Normal": "textures/terrain/foo/foo_nor_gl.png",
    // Packed AO/Rough/Metal texture
    // R = AO, G = Roughness, B = Metalness
    "ARM": "textures/terrain/foo/foo_arm.png"
    // Optional height/displacement map for parallax or tessellation
    "Displacement": "textures/terrain/foo/foo_disp.png"
}
```

Case B:
```json
"Textures": {
// Diffuse / Albedo (strongly prefer Albedo)
"Albedo": "textures/terrain/foo/foo_diff.png",
// Normal map (GL or DX format)
"Normal": "textures/terrain/foo/foo_nor_gl.png",
// Individual channels instead of a packed map
"AO": "textures/terrain/foo/foo_ao.png",
"Rough": "textures/terrain/foo/foo_rough.png",
// Optional if your material is fully non-metallic
"Metal": "textures/terrain/foo/foo_metal.png"
// Optional height/displacement map for parallax or tessellation
"Displacement": "textures/terrain/foo/foo_disp.png"
}
```

Case C:
```json
"Textures": {
// Diffuse / Albedo (strongly prefer Albedo)
"Albedo": "textures/terrain/foo/foo_diff.png",
// Normal map (GL or DX format)
"Normal": "textures/terrain/foo/foo_nor_gl.png",
// Packed AO/Rough texture
// R = AO, G = Roughness
"AOR": "textures/terrain/foo/foo_ao.png",
// Metal (Optional)
"Metal": "textures/terrain/foo/foo_metal.png"
// Optional height/displacement map for parallax or tessellation
"Displacement": "textures/terrain/foo/foo_disp.png"
}
```
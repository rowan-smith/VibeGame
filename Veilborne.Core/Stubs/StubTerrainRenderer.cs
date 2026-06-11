using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Stubs
{
    /// <summary>
    /// Stub terrain renderer for DI setup. Real implementation will be provided by ECS manager.
    /// </summary>
    public class StubTerrainRenderer : ITerrainRenderer
    {
        public void ApplyBiomeTextures(BiomeData biome) { }
        public void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend) { }
        public void Render(float[,] heights, float tileSize, CameraComponent camera, Vector3 baseColor) { }
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera) { }
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig) { }
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig, Vector4[,]? splatmap) { }
        public void SetColorTint(Vector4 tint) { }
        public void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld) { }
        public void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld) { }
        public void ProcessBuildQueue(int maxBuilds = 1) { }
        public void MarkOriginDirty(Vector2 originWorld) { }
        public void PatchRegion(float[,] heights, float tileSize, Vector2 originWorld, int startX, int startZ, int endX, int endZ) { }
        public bool IsChunkVisibleForRender(
            Vector2 chunkOrigin, float tileSize, int gridWidth, int gridHeight, CameraComponent camera) => true;
        public void Flush() { }
        public void SetWarmupMode(bool enabled) { }
    }
}

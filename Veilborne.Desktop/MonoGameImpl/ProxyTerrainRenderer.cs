using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Stubs;

namespace Veilborne.MonoGameImpl
{
    /// <summary>
    /// Proxy ITerrainRenderer registered in DI. Delegates to a stub until the real
    /// MonoGameTerrainRenderer is ready, then swaps transparently.
    /// </summary>
    public class ProxyTerrainRenderer : ITerrainRenderer
    {
        private ITerrainRenderer _inner = new StubTerrainRenderer();

        public void SetInner(ITerrainRenderer inner) => _inner = inner;

        public void Render(float[,] heights, float tileSize, CameraComponent camera, Vector3 baseColor) =>
            _inner.Render(heights, tileSize, camera, baseColor);

        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera) =>
            _inner.RenderAt(heights, tileSize, originWorld, camera);
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig) =>
            _inner.RenderAt(heights, tileSize, originWorld, camera, baseHeights, layerConfig);
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig, Vector4[,]? splatmap) =>
            _inner.RenderAt(heights, tileSize, originWorld, camera, baseHeights, layerConfig, splatmap);

        public void ApplyBiomeTextures(BiomeData biome) => _inner.ApplyBiomeTextures(biome);
        public void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend) => _inner.ApplyBiomeBlendTextures(primary, secondary, secondaryBlend);
        public void SetColorTint(Vector4 color) => _inner.SetColorTint(color);
        public void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld) => _inner.BuildChunks(heights, tileSize, originWorld);
        public void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld) => _inner.EnqueueBuild(heights, tileSize, originWorld);
        public void ProcessBuildQueue(int maxPerFrame) => _inner.ProcessBuildQueue(maxPerFrame);
        public void MarkOriginDirty(Vector2 originWorld) => _inner.MarkOriginDirty(originWorld);
        public void PatchRegion(float[,] heights, float tileSize, Vector2 originWorld, int x0, int z0, int x1, int z1) =>
            _inner.PatchRegion(heights, tileSize, originWorld, x0, z0, x1, z1);

        public bool IsChunkVisibleForRender(
            Vector2 chunkOrigin, float tileSize, int gridWidth, int gridHeight, CameraComponent camera) =>
            _inner.IsChunkVisibleForRender(chunkOrigin, tileSize, gridWidth, gridHeight, camera);
        public void Flush() => _inner.Flush();
        public void SetWarmupMode(bool enabled) => _inner.SetWarmupMode(enabled);
    }
}

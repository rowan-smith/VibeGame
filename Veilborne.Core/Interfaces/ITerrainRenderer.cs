using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;

namespace Veilborne.Interfaces
{
    public interface ITerrainRenderer
    {
        void Render(float[,] heights, float tileSize, CameraComponent camera, Vector3 baseColor);

        // Render a heightmap positioned with its (0,0) corner at originWorld (bottom-left), no centering
        void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera);
        void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig);
        void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig, Vector4[,]? splatmap);

        // Apply the biome-specific surface texture set prior to rendering
        void ApplyBiomeTextures(BiomeData biome);
        void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend);

        void SetColorTint(Vector4 color);

        // Synchronous build (CPU + GPU upload) — kept for compatibility
        void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld);

        // Queue a CPU-side mesh data build to run off the main thread; GPU upload occurs later on ProcessBuildQueue
        void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld);

        // Upload up to maxPerFrame prepared meshes to GPU; should be called from main thread each frame
        void ProcessBuildQueue(int maxPerFrame);

        // Mark a cached origin as dirty so the next EnqueueBuild/BuildChunks will rebuild it
        void MarkOriginDirty(Vector2 originWorld);

        // Partially update an already built chunk at originWorld by patching a sub-rectangle [x0..x1], [z0..z1]
        // Coordinates are in local grid indices within the provided heights array.
        void PatchRegion(float[,] heights, float tileSize, Vector2 originWorld, int x0, int z0, int x1, int z1);

        // Issue the actual GPU draw for all queued RenderAt calls. Call once per frame after all RenderAt calls.
        void Flush();
    }
}

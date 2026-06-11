using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using Veilborne.Biomes;
using Veilborne.Interfaces;

namespace Veilborne.MonoGameImpl
{
    using XnaColor = Microsoft.Xna.Framework.Color;
    using XnaVector2 = Microsoft.Xna.Framework.Vector2;
    using XnaVector3 = Microsoft.Xna.Framework.Vector3;

    /// <summary>
    /// CPU-side terrain mesh generation. Safe to run off the main thread.
    /// GPU buffer upload happens later on the graphics device thread.
    /// </summary>
    internal enum TerrainChunkLayerBlendMode : byte
    {
        None = 0,
        SurfaceToSubsurface = 1,
        SubsurfaceToDeep = 2
    }

    internal static class TerrainMeshCpuBuilder
    {
        internal const float BiomeMergeCornerThreshold = 0.015f;
        private const float TexWorldRepeat = 8f;

        internal readonly record struct LayerSnapshot(
            float[,]? BaseHeights,
            Vector4[,]? Splatmap,
            TerrainLayerConfig? LayerConfig,
            bool UseSplatLayering,
            byte LayerMode);

        internal sealed class CpuBuildResult
        {
            public required (float X, float Z, float Tile) Key { get; init; }
            public required VertexPositionColorTexture[] Vertices { get; init; }
            public required int Width { get; init; }
            public required int Depth { get; init; }
            public required float TileSize { get; init; }
            public required Vector2 Origin { get; init; }
            public required float MinY { get; init; }
            public required float MaxY { get; init; }
            public required float LayerBlendCoverage { get; init; }
            public required float BiomeBlendCoverage { get; init; }
            public required float CachedMaxMerge { get; init; }
            public required string PrimaryBiomeId { get; init; }
            public required string MergeBiomeId { get; init; }
            public required LayerSnapshot Layer { get; init; }
            public required float[,] Heights { get; init; }
        }

        internal static CpuBuildResult Build(
            float[,] heights,
            float tileSize,
            Vector2 origin,
            LayerSnapshot layer,
            IBiomeProvider? biomeProvider,
            bool biomeCrossfadeEnabled,
            bool fastMeshBuild)
        {
            int width = heights.GetLength(0);
            int depth = heights.GetLength(1);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            float[,]? mergeMap = null;
            string primaryBiomeId = string.Empty;
            string mergeBiomeId = string.Empty;
            float maxBoundaryBlend = 0f;
            if (biomeCrossfadeEnabled && biomeProvider is SimpleBiomeProvider simple)
            {
                (primaryBiomeId, mergeBiomeId, maxBoundaryBlend) = BiomeSampling.ResolveChunkBiomePair(
                    simple, null, origin, width, depth, tileSize, 4f);
                if (!string.IsNullOrEmpty(mergeBiomeId))
                {
                    int stride = fastMeshBuild ? 4 : 2;
                    (mergeMap, float mapMax) = BiomeSampling.BuildChunkPairBlendMap(
                        simple, null, origin, width, depth, tileSize, primaryBiomeId, mergeBiomeId, stride);
                    if (mapMax > maxBoundaryBlend)
                        maxBoundaryBlend = mapMax;
                }
            }

            bool hasBiomeMerge = mergeMap != null;
            var layerMode = (TerrainChunkLayerBlendMode)layer.LayerMode;
            bool useSplatLayering = layer.UseSplatLayering;

            int vertexCount = width * depth;
            var vertices = new VertexPositionColorTexture[vertexCount];
            int blendSamples = 0;
            int blendNonZero = 0;
            int biomeBlendSamples = 0;
            int biomeBlendNonZero = 0;

            for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                float y = heights[x, z];
                float worldX = origin.X + x * tileSize;
                float worldZ = origin.Y + z * tileSize;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                float layerAlpha = 0f;
                if (useSplatLayering && layer.Splatmap != null &&
                    x < layer.Splatmap.GetLength(0) && z < layer.Splatmap.GetLength(1))
                {
                    var sw = layer.Splatmap[x, z];
                    layerAlpha = ComputeLayerBlendAlphaFromSplat(sw, layerMode);
                    blendSamples++;
                    if (layerAlpha > 0.02f) blendNonZero++;
                }
                else if (layer.BaseHeights != null && layer.LayerConfig != null)
                {
                    float depthDelta = MathF.Max(0f, layer.BaseHeights[x, z] - y);
                    layerAlpha = ComputeLayerBlendAlpha(depthDelta, layer.LayerConfig, layerMode);
                }

                float biomeAlpha = 0f;
                if (hasBiomeMerge && mergeMap != null)
                {
                    biomeAlpha = mergeMap[x, z];
                    biomeBlendSamples++;
                    if (biomeAlpha > 0.02f) biomeBlendNonZero++;
                }

                float alpha = hasBiomeMerge && biomeAlpha > 0.001f ? biomeAlpha : layerAlpha;
                byte blendAlpha = (byte)Math.Clamp((int)(alpha * 255f), 0, 255);
                var vertexColor = new XnaColor((byte)255, (byte)255, (byte)255, blendAlpha);
                var uv = new XnaVector2(worldX / TexWorldRepeat, worldZ / TexWorldRepeat);
                vertices[z * width + x] = new VertexPositionColorTexture(
                    new XnaVector3(worldX, y, worldZ),
                    vertexColor, uv);
            }

            return new CpuBuildResult
            {
                Key = (origin.X, origin.Y, tileSize),
                Vertices = vertices,
                Width = width,
                Depth = depth,
                TileSize = tileSize,
                Origin = origin,
                MinY = float.IsFinite(minY) ? minY : 0f,
                MaxY = float.IsFinite(maxY) ? maxY : 0f,
                LayerBlendCoverage = blendSamples > 0 ? blendNonZero / (float)blendSamples : 0f,
                BiomeBlendCoverage = biomeBlendSamples > 0 ? biomeBlendNonZero / (float)biomeBlendSamples : 0f,
                CachedMaxMerge = maxBoundaryBlend,
                PrimaryBiomeId = primaryBiomeId,
                MergeBiomeId = mergeBiomeId,
                Layer = layer,
                Heights = heights
            };
        }

        private static float ComputeLayerBlendAlphaFromSplat(Vector4 splat, TerrainChunkLayerBlendMode mode)
        {
            if (mode == TerrainChunkLayerBlendMode.SubsurfaceToDeep)
            {
                float deep = MathF.Max(0f, splat.Z + splat.W);
                float sub = MathF.Max(0f, splat.Y);
                float denom = deep + sub;
                return denom > 1e-5f ? Math.Clamp(deep / denom, 0f, 1f) : 0f;
            }

            float surface = MathF.Max(0f, splat.X);
            float exposed = MathF.Max(0f, splat.Y + splat.Z + splat.W);
            float total = surface + exposed;
            return total > 1e-5f ? Math.Clamp(exposed / total, 0f, 1f) : 0f;
        }

        private static float ComputeLayerBlendAlpha(
            float depth, TerrainLayerConfig config, TerrainChunkLayerBlendMode mode)
        {
            if (mode == TerrainChunkLayerBlendMode.SubsurfaceToDeep)
            {
                float denom = MathF.Max(0.05f, config.DeepDepth - config.SubsurfaceDepth);
                return Math.Clamp((depth - config.SubsurfaceDepth) / denom, 0f, 1f);
            }

            return Math.Clamp(depth / MathF.Max(0.05f, config.SubsurfaceDepth), 0f, 1f);
        }
    }
}

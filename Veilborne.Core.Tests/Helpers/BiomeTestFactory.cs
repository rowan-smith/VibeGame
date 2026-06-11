using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Interfaces;
using Veilborne.Objects;

namespace Veilborne.Core.Tests.Helpers;

public static class BiomeTestFactory
{
    public static SimpleBiomeProvider CreateProvider(
        int seed = 1337,
        float cellSize = 180f,
        float blendWidth = 120f,
        params (string id, float temp, float moist)[] biomes)
    {
        var list = new List<IBiome>(biomes.Length);
        foreach (var biome in biomes)
        {
            list.Add(new ConfigBiomeFromData(
                new BiomeData
                {
                    Id = biome.id,
                    ProceduralData = new ProceduralData
                    {
                        Base = new ProceduralBase
                        {
                            Temperature = biome.temp,
                            Moisture = biome.moist,
                            Altitude = 0.5f,
                            Fertility = 0.5f
                        },
                        Weights = new ProceduralWeights
                        {
                            WtTemp = 1f,
                            WtMoisture = 1f,
                            WtElevation = 0.1f,
                            WtFertility = 0.1f
                        }
                    },
                    TerrainLayers = new TerrainLayerConfig
                    {
                        SurfaceTextureId = $"tex_{biome.id}_surface",
                        SubsurfaceTextureId = $"tex_{biome.id}_sub"
                    }
                },
                EmptySpawner.Instance));
        }

        return new SimpleBiomeProvider(
            list,
            averageCellSize: cellSize,
            seed: seed,
            blendWidthWorld: blendWidth);
    }

    public static (Vector2 worldPos, string primaryId, string mergeId, float blend) FindTransitionSample(
        SimpleBiomeProvider provider,
        float searchRadius = 4000f,
        float step = 40f)
    {
        for (float z = -searchRadius; z <= searchRadius; z += step)
        for (float x = -searchRadius; x <= searchRadius; x += step)
        {
            var pos = new Vector2(x, z);
            var (primary, secondary, blend) = provider.GetBiomeBlendAt(pos, null);
            if (secondary is null || blend <= 0.08f || blend >= 0.92f)
                continue;
            return (pos, primary.Id, secondary.Id, blend);
        }

        throw new InvalidOperationException("No biome transition found in search area.");
    }

    private sealed class EmptySpawner : IWorldObjectSpawner
    {
        public static readonly EmptySpawner Instance = new();
        public List<SpawnedObject> GenerateObjects(
            string biomeId, ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count) => new();
    }
}

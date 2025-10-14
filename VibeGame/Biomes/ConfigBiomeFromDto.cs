using System.Drawing;
using System.Numerics;
using System.Collections.Generic;
using VibeGame.Objects;
using VibeGame.Terrain;
using VibeGame.Biomes.Spawners;

namespace VibeGame.Biomes.Environment
{
    public sealed class ConfigBiomeFromData : IBiome
    {
        public string Id => Data.Id;
        public BiomeData Data { get; }
        public IWorldObjectSpawner ObjectSpawner { get; }

        public ConfigBiomeFromData(BiomeData data, IWorldObjectSpawner spawner)
        {
            Data = data;
            ObjectSpawner = spawner;
        }

        public bool Contains(Vector2 worldPos, ITerrainGenerator terrain) => true;
        public float GetBaseHeight(Vector2 worldPos, ITerrainGenerator terrain) => Data.BaseHeight;
        public float GetHeightMultiplier(Vector2 worldPos, ITerrainGenerator terrain) => Data.HeightMultiplier;
        public Color GetColor(Vector2 worldPos) => Data.Color;

        public List<SpawnedObject> GenerateObjects(
            ITerrainGenerator terrain,
            float[,] heights,
            Vector2 originWorld,
            int count)
        {
            return ObjectSpawner.GenerateObjects(Id, terrain, heights, originWorld, count);
        }
    }
}

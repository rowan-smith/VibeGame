using System.Drawing;
using System.Numerics;
using Veilborne.Terrain;
using System.Collections.Generic;
using Veilborne.Interfaces;
using Veilborne.Objects;

namespace Veilborne.Biomes
{
    public sealed class ConfigBiome : IBiome
    {
        public string Id { get; }
        public BiomeData Data { get; }
        public IWorldObjectSpawner ObjectSpawner { get; }

        public ConfigBiome(string id, BiomeData data, IWorldObjectSpawner spawner)
        {
            Id = id;
            Data = data;
            ObjectSpawner = spawner;
        }

        public bool Contains(Vector2 worldPos, ITerrainGenerator terrain) => true;
        public float GetBaseHeight(Vector2 worldPos, ITerrainGenerator terrain) => Data.BaseHeight;
        public float GetHeightMultiplier(Vector2 worldPos, ITerrainGenerator terrain) => Data.HeightMultiplier;
        public Color GetColor(Vector2 worldPos) => Data.Color;

        public List<SpawnedObject> GenerateObjects(ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count)
        {
            return ObjectSpawner.GenerateObjects(Id, terrain, heights, originWorld, count);
        }
    }
}

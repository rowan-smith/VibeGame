using System.Drawing;
using System.Numerics;
using VibeGame.Objects;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    public interface IBiome
    {
        string Id { get; }

        BiomeData Data { get; }

        IWorldObjectSpawner ObjectSpawner { get; }

        bool Contains(Vector2 worldPos, ITerrainGenerator terrain);

        float GetBaseHeight(Vector2 worldPos, ITerrainGenerator terrain);

        float GetHeightMultiplier(Vector2 worldPos, ITerrainGenerator terrain);

        Color GetColor(Vector2 worldPos);

        List<SpawnedObject> GenerateObjects(ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count);
    }
}

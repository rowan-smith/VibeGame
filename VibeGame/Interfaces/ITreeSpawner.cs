using System.Numerics;
using VibeGame.Terrain;
using VibeGame.Objects;
using System.Collections.Generic;

namespace VibeGame.Biomes.Spawners
{
    public interface ITreeSpawner : IWorldObjectSpawner
    {
        List<SpawnedObject> SpawnTrees(Vector2 origin, ITerrainGenerator terrain, float[,] heights, int count);
    }
}

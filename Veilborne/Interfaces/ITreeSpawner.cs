using System.Numerics;
using Veilborne.Objects;

namespace Veilborne.Interfaces
{
    public interface ITreeSpawner : IWorldObjectSpawner
    {
        List<SpawnedObject> SpawnTrees(Vector2 origin, ITerrainGenerator terrain, float[,] heights, int count);
    }
}

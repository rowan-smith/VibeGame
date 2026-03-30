using System.Numerics;
using Veilborne.Core.Objects;

namespace Veilborne.Core.Interfaces
{
    public interface ITreeSpawner : IWorldObjectSpawner
    {
        List<SpawnedObject> SpawnTrees(Vector2 origin, ITerrainGenerator terrain, float[,] heights, int count);
    }
}

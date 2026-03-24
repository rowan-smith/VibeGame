using System.Numerics;
using Veilborne.Biomes.Environment;

namespace Veilborne.Interfaces
{
    public interface IEnvironmentSampler
    {
        EnvironmentSample Sample(Vector2 worldPos, ITerrainGenerator terrain);
    }
}

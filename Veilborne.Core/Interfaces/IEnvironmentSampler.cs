using System.Numerics;
using Veilborne.Core.Biomes.Environment;

namespace Veilborne.Core.Interfaces
{
    public interface IEnvironmentSampler
    {
        EnvironmentSample Sample(Vector2 worldPos, ITerrainGenerator terrain);
    }
}

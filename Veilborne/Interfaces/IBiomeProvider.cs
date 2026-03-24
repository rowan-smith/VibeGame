using System.Numerics;

namespace Veilborne.Interfaces
{
    public interface IBiomeProvider
    {
        IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain);
    }
}

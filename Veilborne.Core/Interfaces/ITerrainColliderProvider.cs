using System.Numerics;

namespace Veilborne.Interfaces
{
    public interface ITerrainColliderProvider
    {
        IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range);
    }
}

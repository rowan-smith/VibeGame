using System.Collections.Generic;
using System.Numerics;

namespace VibeGame.Terrain
{
    public interface ITerrainColliderProvider
    {
        IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range);
    }
}

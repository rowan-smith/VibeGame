using System.Numerics;
using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Uniform-grid spatial index for static world objects to keep collision queries local.
    /// </summary>
    public sealed class WorldObjectSpatialIndex
    {
        private const float CellSize = 10f;
        private readonly Dictionary<(int x, int z), List<Entity>> _cells = new();
        private readonly List<(int x, int z)> _activeCells = new();
        private readonly HashSet<int> _dedupe = new();

        public void Rebuild(EntityRegistry entities)
        {
            Clear();

            entities.ForEachWith<WorldObjectComponent, TransformComponent>(entity =>
            {
                if (!entity.TryGetComponent<ColliderComponent>(out var collider))
                    return;

                var filter = entity.TryGetComponent<CollisionFilterComponent>(out var customFilter)
                    ? customFilter
                    : new CollisionFilterComponent
                    {
                        Layer = CollisionLayer.WorldStatic,
                        CollidesWith = CollisionLayer.Player
                    };
                if ((filter.CollidesWith & CollisionLayer.Player) == 0)
                    return;

                var transform = entity.GetComponent<TransformComponent>();
                float radius = MathF.Max(0.001f, collider.Radius);
                int minX = FloorCell(transform.Position.X - radius);
                int maxX = FloorCell(transform.Position.X + radius);
                int minZ = FloorCell(transform.Position.Z - radius);
                int maxZ = FloorCell(transform.Position.Z + radius);

                for (int cz = minZ; cz <= maxZ; cz++)
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var key = (cx, cz);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<Entity>(8);
                        _cells[key] = list;
                    }
                    if (list.Count == 0)
                        _activeCells.Add(key);
                    list.Add(entity);
                }
            });
        }

        public void Query(Vector3 center, float radius, List<Entity> results)
        {
            results.Clear();
            _dedupe.Clear();

            int minX = FloorCell(center.X - radius);
            int maxX = FloorCell(center.X + radius);
            int minZ = FloorCell(center.Z - radius);
            int maxZ = FloorCell(center.Z + radius);

            for (int cz = minZ; cz <= maxZ; cz++)
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (!_cells.TryGetValue((cx, cz), out var list))
                    continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var entity = list[i];
                    if (_dedupe.Add(entity.Id))
                        results.Add(entity);
                }
            }
        }

        private void Clear()
        {
            for (int i = 0; i < _activeCells.Count; i++)
            {
                var key = _activeCells[i];
                if (_cells.TryGetValue(key, out var list))
                    list.Clear();
            }
            _activeCells.Clear();
            _dedupe.Clear();
        }

        private static int FloorCell(float value) => (int)MathF.Floor(value / CellSize);
    }
}

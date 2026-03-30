using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Resolves Parent/Children relationship metadata each frame.
    /// </summary>
    public class DependencySystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly List<Entity> _parentsList = new();
        private readonly HashSet<int> _knownParentIds = new();
        private readonly Dictionary<int, List<int>> _childIdsByParent = new();
        private static readonly int[] EmptyIds = [];

        public DependencySystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            _parentsList.Clear();
            _knownParentIds.Clear();
            _childIdsByParent.Clear();

            foreach (var e in _entities.GetEntitiesWith<ChildrenComponent>())
            {
                _parentsList.Add(e);
                _knownParentIds.Add(e.Id);
            }

            if (_parentsList.Count == 0)
                return;

            foreach (var entity in _entities.GetEntitiesWith<ParentComponent>())
            {
                var parent = entity.GetComponent<ParentComponent>();
                if (parent.EntityId < 0)
                    continue;

                if (!_knownParentIds.Contains(parent.EntityId))
                    continue;

                if (!_childIdsByParent.TryGetValue(parent.EntityId, out var childIds))
                {
                    childIds = new List<int>();
                    _childIdsByParent[parent.EntityId] = childIds;
                }

                childIds.Add(entity.Id);
            }

            foreach (var parentEntity in _parentsList)
            {
                var children = parentEntity.GetComponent<ChildrenComponent>();
                children.EntityIds = _childIdsByParent.TryGetValue(parentEntity.Id, out var ids)
                    ? ids.ToArray()
                    : EmptyIds;
                parentEntity.SetComponent(children);
            }
        }
    }
}

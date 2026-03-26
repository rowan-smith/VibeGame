using Veilborne.Core.Ecs.Components;
using System.Collections.Generic;
using System.Linq;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Resolves Parent/Children relationship metadata each frame.
    /// </summary>
    public class DependencySystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public DependencySystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            var parents = _entities.GetEntitiesWith<ChildrenComponent>().ToList();
            if (parents.Count == 0)
                return;

            var childIdsByParent = new Dictionary<int, List<int>>(parents.Count);
            var knownParentIds = new HashSet<int>(parents.Select(p => p.Id));

            foreach (var entity in _entities.GetEntitiesWith<ParentComponent>())
            {
                var parent = entity.GetComponent<ParentComponent>();
                if (parent.EntityId < 0)
                    continue;

                if (!knownParentIds.Contains(parent.EntityId))
                    continue;

                if (!childIdsByParent.TryGetValue(parent.EntityId, out var childIds))
                {
                    childIds = new List<int>();
                    childIdsByParent[parent.EntityId] = childIds;
                }

                childIds.Add(entity.Id);
            }

            foreach (var parentEntity in parents)
            {
                var children = parentEntity.GetComponent<ChildrenComponent>();
                children.EntityIds = childIdsByParent.TryGetValue(parentEntity.Id, out var ids)
                    ? ids.ToArray()
                    : [];
                parentEntity.SetComponent(children);
            }
        }
    }
}

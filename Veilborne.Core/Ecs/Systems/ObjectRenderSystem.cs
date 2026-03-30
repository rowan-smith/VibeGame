using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Objects;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Render-phase adapter for world object mesh drawing.
    /// </summary>
    public class ObjectRenderSystem : IRenderSystem
    {
        private readonly EntityRegistry _entities;
        private readonly IWorldObjectRenderer _renderer;

        public ObjectRenderSystem(EntityRegistry entities, IWorldObjectRenderer renderer)
        {
            _entities = entities;
            _renderer = renderer;
        }

        public void Draw()
        {
            CameraComponent cam = default;
            bool hasCamera = false;
            _entities.ForEachWith<CameraComponent>(entity =>
            {
                if (hasCamera) return;
                cam = entity.GetComponent<CameraComponent>();
                hasCamera = true;
            });
            if (!hasCamera) return;
            _renderer.Render(cam);
        }
    }
}


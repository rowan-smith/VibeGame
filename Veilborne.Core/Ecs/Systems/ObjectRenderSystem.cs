using Veilborne.Objects;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Render-phase adapter for world object mesh drawing.
    /// </summary>
    public class ObjectRenderSystem : IRenderSystem
    {
        private readonly IWorldObjectRenderer _renderer;

        public ObjectRenderSystem(IWorldObjectRenderer renderer)
        {
            _renderer = renderer;
        }

        public void Draw()
        {
            if (_renderer is IRenderSystem renderSystem)
                renderSystem.Draw();
        }
    }
}


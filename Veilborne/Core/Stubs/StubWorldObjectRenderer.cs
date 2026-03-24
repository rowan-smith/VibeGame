using Veilborne.Core.Ecs.Components;
using Veilborne.Objects;

namespace Veilborne.Core.Stubs
{
    /// <summary>
    /// Stub world object renderer for DI setup. Real implementation will be provided by ECS manager.
    /// </summary>
    public class StubWorldObjectRenderer : IWorldObjectRenderer
    {
        public void Draw() { }
        public void Render(CameraComponent camera) { }
        public void DrawWorldObject(SpawnedObject obj) { }
    }
}
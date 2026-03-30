using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Objects;

namespace Veilborne.Core.Interfaces
{
    public interface IEcsRuntime
    {
        void Initialize(EntityRegistry entityRegistry, IInfiniteTerrain terrain);
        void UpdateSystems(float deltaTime);
        void RenderSystems(float deltaTime, CameraComponent camera);
        IUiProvider GetUiProvider();
        ITerrainRenderer GetTerrainRenderer();
        IWorldObjectRenderer GetWorldObjectRenderer();
    }
}

using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Objects;

namespace Veilborne.Interfaces
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

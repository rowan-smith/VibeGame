using Veilborne.Core.Ecs;
using Veilborne.Objects;

namespace Veilborne.Interfaces
{
    public interface IEcsRuntime
    {
        void Initialize(EntityRegistry entityRegistry, IInfiniteTerrain terrain);
        void UpdateSystems(float deltaTime);
        void RenderSystems(float deltaTime, Veilborne.Core.Ecs.Components.CameraComponent camera);
        IUiProvider GetUiProvider();
        ITerrainRenderer GetTerrainRenderer();
        IWorldObjectRenderer GetWorldObjectRenderer();
    }
}

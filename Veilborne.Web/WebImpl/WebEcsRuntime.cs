using Microsoft.JSInterop;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Objects;
using Microsoft.Extensions.Logging;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebEcsRuntime : IEcsRuntime
    {
        private readonly IJSRuntime _js;
        private readonly IUiProvider _uiProvider;
        public WebEcsRuntime(IJSRuntime js, IUiProvider uiProvider)
        {
            _js = js;
            _uiProvider = uiProvider;
        }
        public void Initialize(EntityRegistry entityRegistry, IInfiniteTerrain terrain) { }
        public void UpdateSystems(float deltaTime) { }
        public void RenderSystems(float deltaTime, CameraComponent camera) { }
        public IUiProvider GetUiProvider() => _uiProvider;
        public ITerrainRenderer GetTerrainRenderer() => null!; // TODO: Implement
        public IWorldObjectRenderer GetWorldObjectRenderer() => null!; // TODO: Implement
    }
}

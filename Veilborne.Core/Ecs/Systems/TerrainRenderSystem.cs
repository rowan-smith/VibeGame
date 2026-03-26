using Serilog;
using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    public class TerrainRenderSystem : IRenderSystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;
        private readonly ITerrainRenderer _renderer;
        private readonly ILogger _log = Log.ForContext<TerrainRenderSystem>();
        private bool _loggedMissingCamera;
        private bool _loggedInvalidCamera;

        public TerrainRenderSystem(EntityRegistry entities, IInfiniteTerrain terrain, ITerrainRenderer renderer)
        {
            _entities = entities;
            _terrain = terrain;
            _renderer = renderer;
        }

        public void Draw()
        {
            bool anyCamera = false;
            _entities.ForEachWith<CameraComponent>(entity =>
            {
                anyCamera = true;
                var cam = entity.GetComponent<CameraComponent>();
                if (Vector3.DistanceSquared(cam.Target, cam.Position) < 1e-6f || cam.Up.LengthSquared() < 1e-6f)
                {
                    if (!_loggedInvalidCamera)
                    {
                        _log.Warning("TerrainRenderSystem: skipping render for invalid camera vectors.");
                        _loggedInvalidCamera = true;
                    }
                    return;
                }
                _terrain.Render(cam);
            });
            _renderer.Flush();

            if (!anyCamera && !_loggedMissingCamera)
            {
                _log.Warning("TerrainRenderSystem: no entities matched CameraComponent query.");
                _loggedMissingCamera = true;
            }
        }
    }
}

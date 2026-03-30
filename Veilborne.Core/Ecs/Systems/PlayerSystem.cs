using Serilog;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    public class PlayerSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly ICameraController _cameraController;
        private readonly ILogger _log = Log.ForContext<PlayerSystem>();
        private bool _loggedMissingCamera;

        public PlayerSystem(EntityRegistry entities, ICameraController cameraController)
        {
            _entities = entities;
            _cameraController = cameraController;
        }

        public void Update(float dt)
        {
            bool anyCamera = false;
            _entities.ForEachWith<CameraComponent>(entity =>
            {
                anyCamera = true;
                var cam = entity.GetComponent<CameraComponent>();
                _cameraController.UpdateAndGetHorizontalMove(ref cam, dt);
                entity.SetComponent(cam);
            });

            if (!anyCamera && !_loggedMissingCamera)
            {
                _log.Warning("PlayerSystem: no entities matched CameraComponent query.");
                _loggedMissingCamera = true;
            }
        }
    }
}

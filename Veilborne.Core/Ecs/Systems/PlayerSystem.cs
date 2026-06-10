using Serilog;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Ecs.Systems
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
            _entities.ForEachWith<CameraComponent>((Entity entity, ref CameraComponent cam) =>
            {
                anyCamera = true;
                _cameraController.UpdateAndGetHorizontalMove(ref cam, dt);
            });

            if (!anyCamera && !_loggedMissingCamera)
            {
                _log.Warning("PlayerSystem: no entities matched CameraComponent query.");
                _loggedMissingCamera = true;
            }
        }
    }
}

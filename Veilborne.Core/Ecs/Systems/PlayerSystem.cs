using System.Numerics;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    public class PlayerSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly ICameraController _cameraController;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _terrain;

        public PlayerSystem(EntityRegistry entities, ICameraController cameraController, IPhysicsController physics, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _cameraController = cameraController;
            _physics = physics;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<PlayerComponent, CameraComponent>())
            {
                var cam = entity.GetComponent<CameraComponent>();
                Vector3 horizMove = _cameraController.UpdateAndGetHorizontalMove(cam, dt);
                _physics.Integrate(cam, dt, horizMove, (x, z) => _terrain.SampleHeight(new Vector3(x, 0, z)));
            }
        }
    }
}

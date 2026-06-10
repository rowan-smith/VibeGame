using System.Numerics;
using Serilog;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Integrates camera/player motion and writes resulting velocity state.
    /// </summary>
    public class IntegrationSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _terrain;
        private readonly ILogger _log = Log.ForContext<IntegrationSystem>();
        private bool _loggedMissingCamera;
        private bool _loggedFirstMotion;
        private bool _loggedInvalidCameraUp;

        public IntegrationSystem(EntityRegistry entities, IPhysicsController physics, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _physics = physics;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            bool anyCamera = false;
            _entities.ForEachWith<CameraComponent>((Entity entity, ref CameraComponent cam) =>
            {
                anyCamera = true;
                if (cam.Up.LengthSquared() < 1e-6f)
                {
                    cam.Up = Vector3.UnitY;
                    if (!_loggedInvalidCameraUp)
                    {
                        _log.Warning("IntegrationSystem: corrected invalid camera up vector");
                        _loggedInvalidCameraUp = true;
                    }
                }

                var startPos = cam.Position;
                var moveInput = entity.TryGetComponent<MoveInputComponent>(out var input)
                    ? input.HorizontalDisplacement
                    : Vector3.Zero;
                var jump = entity.TryGetComponent<JumpComponent>(out var jumpComponent)
                    ? jumpComponent
                    : new JumpComponent();
                var verticalVelocity = entity.TryGetComponent<VerticalVelocityComponent>(out var verticalVelocityComponent)
                    ? verticalVelocityComponent
                    : new VerticalVelocityComponent();
                var gravityY = entity.TryGetComponent<GravityComponent>(out var gravity)
                    ? gravity.Direction.Y
                    : -20f;

                if (entity.TryGetComponent<AccelerationComponent>(out var acceleration))
                {
                    moveInput += acceleration.Value * dt * dt;
                }

                _physics.Integrate(ref cam, ref jump, ref verticalVelocity, dt, moveInput, gravityY,
                    (x, z) => _terrain.SampleHeight(new Vector3(x, 0, z)));

                entity.SetComponent(cam);
                entity.SetComponent(jump);
                entity.SetComponent(verticalVelocity);

                if (entity.TryGetComponent<TransformComponent>(out var transform))
                {
                    transform.Position = cam.Position;
                    entity.SetComponent(transform);
                }

                if (entity.TryGetComponent<VelocityComponent>(out var velocity) && dt > 0.0001f)
                {
                    velocity.Linear = (cam.Position - startPos) / dt;
                    entity.SetComponent(velocity);
                    if (!_loggedFirstMotion)
                    {
                        _log.Debug("IntegrationSystem: first motion sample pos={Position} vel={Velocity}", cam.Position, velocity.Linear);
                        _loggedFirstMotion = true;
                    }
                }

                if (entity.TryGetComponent<ForceComponent>(out var force) && force.Value != Vector3.Zero)
                {
                    force.Value = Vector3.Zero;
                    entity.SetComponent(force);
                }
            });

            if (!anyCamera && !_loggedMissingCamera)
            {
                _log.Warning("IntegrationSystem: no entities matched CameraComponent query");
                _loggedMissingCamera = true;
            }
        }
    }
}

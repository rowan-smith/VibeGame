using System.Numerics;
using Serilog;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Maintains valid camera vectors before input/physics phases execute.
    /// </summary>
    public class CameraSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly ILogger _log = Log.ForContext<CameraSystem>();
        private bool _loggedMissingCamera;
        private bool _loggedCameraCorrection;
        private bool _loggedUpCorrection;

        public CameraSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            bool anyCamera = false;
            foreach (var entity in _entities.GetEntitiesWith<CameraComponent>())
            {
                anyCamera = true;
                var cam = entity.GetComponent<CameraComponent>();

                if (Vector3.DistanceSquared(cam.Target, cam.Position) < 1e-6f)
                {
                    cam.Target = cam.Position + new Vector3(0, 0, 1);
                    if (!_loggedCameraCorrection)
                    {
                        _log.Warning("CameraSystem: corrected degenerate camera target vector.");
                        _loggedCameraCorrection = true;
                    }
                }

                if (cam.Up.LengthSquared() < 1e-6f)
                {
                    cam.Up = Vector3.UnitY;
                    if (!_loggedUpCorrection)
                    {
                        _log.Warning("CameraSystem: corrected invalid camera up vector.");
                        _loggedUpCorrection = true;
                    }
                }

                entity.SetComponent(cam);
            }

            if (!anyCamera && !_loggedMissingCamera)
            {
                _log.Warning("CameraSystem: no entities matched CameraComponent query.");
                _loggedMissingCamera = true;
            }
        }
    }
}

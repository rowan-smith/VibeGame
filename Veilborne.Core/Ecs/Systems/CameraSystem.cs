using System.Numerics;
using Serilog;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Maintains valid camera vectors before input/physics phases execute.
    /// </summary>
    public class CameraSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly EcsFrameContext _frameContext;
        private readonly ILogger _log = Log.ForContext<CameraSystem>();
        private bool _loggedMissingCamera;
        private bool _loggedCameraCorrection;
        private bool _loggedUpCorrection;

        public CameraSystem(EntityRegistry entities, EcsFrameContext frameContext)
        {
            _entities = entities;
            _frameContext = frameContext;
        }

        public void Update(float dt)
        {
            _frameContext.BeginFrame();
            bool anyCamera = false;

            _entities.ForEachWith<CameraComponent>((Entity entity, ref CameraComponent cam) =>
            {
                anyCamera = true;
                if (!_frameContext.HasPrimaryCamera)
                    _frameContext.SetPrimaryCamera(cam.Position);

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
            });

            if (!anyCamera && !_loggedMissingCamera)
            {
                _log.Warning("CameraSystem: no entities matched CameraComponent query.");
                _loggedMissingCamera = true;
            }
        }
    }
}

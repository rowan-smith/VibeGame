using System.Drawing;
using System.Numerics;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.GameWorlds.Active.Components;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Systems.Core;

public class RenderSystem : IRenderSystem
{
    private readonly IRenderer _renderer;
    private readonly ICameraFactory _cameraFactory;
    private readonly List<Vector3> _testCubes = new();
    private readonly Random _random = new();

    public RenderSystem(IRenderer renderer, ICameraFactory cameraFactory)
    {
        _renderer = renderer;
        _cameraFactory = cameraFactory;
    }

    public int Priority => 200;
    public SystemCategory Category => SystemCategory.Rendering;
    public bool RunsWhenPaused => true;

    public void Initialize()
    {
        for (int i = 0; i < 10; i++)
        {
            float x = (float)(_random.NextDouble() * 50 - 25);
            float z = (float)(_random.NextDouble() * 50 - 25);
            float y = 0.5f;
            _testCubes.Add(new Vector3(x, y, z));
        }
    }

    public void Render(GameTime time, GameState state)
    {
        var cameras = state.EntitiesWith<CameraComponent, TransformComponent>();

        foreach (var entity in cameras)
        {
            var cameraComp = entity.GetComponent<CameraComponent>();
            if (cameraComp == null) continue;

            var camera = _cameraFactory.CreateFrom(cameraComp);
            _renderer.Begin3D(camera);

            // Draw the ground plane
            _renderer.DrawPlane(new Vector3(0, -0.01f, 0), new Vector2(50, 50), Color.LightGray);

            // Draw your test cubes (always red)
            foreach (var pos in _testCubes)
            {
                var aboveGround = pos + new Vector3(0, 0.5f, 0); // half cube height
                _renderer.DrawCube(aboveGround, new Vector3(1,1,1), Color.Red);
            }

            // Draw all other entities that are not test cubes (blue)
            foreach (var e in state.EntitiesWith<TransformComponent>())
            {
                var t = e.Transform;

                // Skip positions that are already in _testCubes
                if (_testCubes.Contains(t.Position)) 
                    continue;

                _renderer.DrawCube(t.Position, new Vector3(1, 2, 1), Color.Blue);
            }

            _renderer.End3D();
        }

        // Overlay text if paused
        if (time.State == EngineState.Paused)
        {
            _renderer.DrawText("Paused", 600, 340, 20, Color.Red);
        }
    }

    public void Shutdown() { }
}

using System.Numerics;
using Raylib_CsLo;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active.Components;
using Veilborne.GameWorlds.Active.Entities;
using Veilborne.Interfaces;
using Veilborne.Utility;

namespace Veilborne.Systems.Core;

public class RenderSystem : IRenderSystem
{
    private readonly Player _player;
    private readonly CameraSystem _cameraSystem;

    public int Priority => 200; // Render after camera update
    public SystemCategory Category => SystemCategory.Rendering;
    public bool RunsWhenPaused => true;
    
    private List<Vector3> _testCubes;
    private Random _random = new Random();

    public RenderSystem(Player player, CameraSystem cameraSystem)
    {
        _player = player;
        _cameraSystem = cameraSystem;
    }

    public void Initialize()
    {
        _testCubes = new List<Vector3>();

        // Generate 10 random cubes
        for (int i = 0; i < 10; i++)
        {
            float x = (float)(_random.NextDouble() * 50 - 25); // -25 to 25
            float z = (float)(_random.NextDouble() * 50 - 25); // -25 to 25
            float y = 0.5f; // cube sits on plane, half height
            _testCubes.Add(new Vector3(x, y, z));
        }
    }

    public void Update(GameTime time, GameState state)
    {
    }

    public void Render(GameTime time, GameState state)
    {
        Raylib.BeginMode3D(_cameraSystem.Camera);

        // Draw ground
        Raylib.DrawPlane(Vector3.Zero, new Vector2(50, 50), Raylib.LIGHTGRAY);

        // Draw test cubes
        foreach (var pos in _testCubes)
        {
            Raylib.DrawCube(pos, 1f, 1f, 1f, Raylib.RED);
            Raylib.DrawCubeWires(pos, 1f, 1f, 1f, Raylib.BLACK);
        }

        // Draw entities
        foreach (var entity in state.EntitiesWith<TransformComponent>())
        {
            var t = entity.Transform;
            Raylib.DrawCube(t.Position, 1f, 2f, 1f, Raylib.BLUE);
        }

        Raylib.EndMode3D();
        
        // --- 2D overlays ---
        if (time.State == EngineState.Paused)
        {
            Raylib.DrawText("Paused", 600, 340, 20, Raylib.RED);
        }
    }

    public void Shutdown()
    {
        // Free textures, shaders, buffers, etc.
        // Do NOT close the window here
    }
}

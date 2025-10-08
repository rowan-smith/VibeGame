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

    public RenderSystem(Player player, CameraSystem cameraSystem)
    {
        _player = player;
        _cameraSystem = cameraSystem;
    }

    public void Initialize()
    {
        // Only initialize render-related resources
        // No window creation here
    }

    public void Update(GameTime time, GameState state)
    {
    }

    public void Render(GameTime time, GameState state)
    {
        Raylib.BeginMode3D(_cameraSystem.Camera);

        // Simple ground
        Raylib.DrawPlane(Vector3.Zero, new Vector2(50, 50), Raylib.LIGHTGRAY);

        // Draw entities
        // Render all entities with TransformComponent
        foreach (var entity in state.EntitiesWith<TransformComponent>())
        {
            var t = entity.Transform;

            // Default: simple red cube
            Raylib.DrawCube(t.Position, 1f, 2f, 1f, Raylib.RED);

            // Optional: Add a gizmo / debug indicator for player
            if (entity is Player)
            {
                Raylib.DrawCubeWires(t.Position, 1f, 2f, 1f, Raylib.RED);
            }
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

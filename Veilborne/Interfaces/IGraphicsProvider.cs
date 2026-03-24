using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Interfaces
{
    public interface IGraphicsProvider
    {
        int ScreenWidth { get; }
        int ScreenHeight { get; }
        void InitializeWindow(int width, int height, string title);
        void CloseWindow();
        bool ShouldClose();
        void SetTargetFps(int fps);
        float GetFrameTime();
        void SetWindowIcon(string relativeSvgPath);
        void BeginDrawing();
        void EndDrawing();
        void ToggleBorderless();
        void Begin3D(CameraComponent camera);
        void End3D();
        void Clear(Vector3 color);
        void DrawCube(Vector3 position, Vector3 size, Vector3 color);
        void DrawCubeWires(Vector3 position, Vector3 size, Vector3 color);
        // Add more drawing methods as needed
    }
}

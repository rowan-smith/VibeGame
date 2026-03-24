using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;
using ZeroElectric.Vinculum;

namespace Veilborne.Core.RaylibImpl
{
    public class RaylibGraphicsProvider : IGraphicsProvider
    {
        public int ScreenWidth => Raylib.GetScreenWidth();
        public int ScreenHeight => Raylib.GetScreenHeight();

        public void InitializeWindow(int width, int height, string title)
        {
            Raylib.InitWindow(width, height, title);
            Raylib.SetExitKey(KeyboardKey.KEY_NULL);
        }

        public void CloseWindow() => Raylib.CloseWindow();

        public bool ShouldClose() => Raylib.WindowShouldClose();

        public void SetTargetFps(int fps) => Raylib.SetTargetFPS(fps);

        public float GetFrameTime() => Raylib.GetFrameTime();

        public void SetWindowIcon(string relativeSvgPath)
        {
            if (SvgTextureLoader.TryGetIconImage(relativeSvgPath, 256, out var iconImg))
            {
                try
                {
                    Raylib.SetWindowIcon(iconImg);
                }
                finally
                {
                    Raylib.UnloadImage(iconImg);
                }
            }
        }

        public void BeginDrawing() => Raylib.BeginDrawing();

        public void EndDrawing() => Raylib.EndDrawing();

        private bool _isBorderless = false;
        private int _windowedWidth = 1280;
        private int _windowedHeight = 720;

        public void ToggleBorderless()
        {
            if (!_isBorderless)
            {
                _windowedWidth = Raylib.GetScreenWidth();
                _windowedHeight = Raylib.GetScreenHeight();
                Raylib.ClearWindowState(ConfigFlags.FLAG_FULLSCREEN_MODE);
                Raylib.SetWindowState(ConfigFlags.FLAG_WINDOW_UNDECORATED);
                int monitor = Raylib.GetCurrentMonitor();
                int monWidth = Raylib.GetMonitorWidth(monitor);
                int monHeight = Raylib.GetMonitorHeight(monitor);
                Vector2 monPos = Raylib.GetMonitorPosition(monitor);
                Raylib.SetWindowSize(monWidth, monHeight);
                Raylib.SetWindowPosition((int)monPos.X, (int)monPos.Y);
                _isBorderless = true;
            }
            else
            {
                Raylib.ClearWindowState(ConfigFlags.FLAG_WINDOW_UNDECORATED);
                Raylib.SetWindowSize(_windowedWidth, _windowedHeight);
                int monitor = Raylib.GetCurrentMonitor();
                int monWidth = Raylib.GetMonitorWidth(monitor);
                int monHeight = Raylib.GetMonitorHeight(monitor);
                Vector2 monPos = Raylib.GetMonitorPosition(monitor);
                int x = (int)monPos.X + (monWidth - _windowedWidth) / 2;
                int y = (int)monPos.Y + (monHeight - _windowedHeight) / 2;
                Raylib.SetWindowPosition(x, y);
                _isBorderless = false;
            }
        }

        public void Begin3D(CameraComponent camera)
        {
            var rCamera = new Camera3D(
                camera.Position,
                camera.Target,
                camera.Up,
                camera.FovY,
                camera.IsPerspective ? CameraProjection.CAMERA_PERSPECTIVE : CameraProjection.CAMERA_ORTHOGRAPHIC
            );
            Raylib.BeginMode3D(rCamera);
        }

        public void End3D()
        {
            Raylib.EndMode3D();
        }

        public void Clear(Vector3 color)
        {
            Color c = new Color();
            c.r = (byte)(color.X * 255);
            c.g = (byte)(color.Y * 255);
            c.b = (byte)(color.Z * 255);
            c.a = 255;
            Raylib.ClearBackground(c);
        }

        public void DrawCube(Vector3 position, Vector3 size, Vector3 color)
        {
            Color c = new Color();
            c.r = (byte)(color.X * 255);
            c.g = (byte)(color.Y * 255);
            c.b = (byte)(color.Z * 255);
            c.a = 255;
            Raylib.DrawCube(position, size.X, size.Y, size.Z, c);
        }

        public void DrawCubeWires(Vector3 position, Vector3 size, Vector3 color)
        {
            Color c = new Color();
            c.r = (byte)(color.X * 255);
            c.g = (byte)(color.Y * 255);
            c.b = (byte)(color.Z * 255);
            c.a = 255;
            Raylib.DrawCubeWires(position, size.X, size.Y, size.Z, c);
        }
    }
}

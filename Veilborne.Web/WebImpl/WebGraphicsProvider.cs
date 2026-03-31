using System.Numerics;
using Microsoft.JSInterop;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebGraphicsProvider : IGraphicsProvider
    {
        private readonly IJSRuntime _js;
        private readonly IJSInProcessRuntime _jsSync;
        public WebGraphicsProvider(IJSRuntime js)
        {
            _js = js;
            _jsSync = (IJSInProcessRuntime)js;
        }
        public int ScreenWidth => 1280;
        public int ScreenHeight => 720;
        public void InitializeWindow(int width, int height, string title) { }
        public void CloseWindow() { }
        public bool ShouldClose() => false;
        public void SetTargetFps(int fps) { }
        public float GetFrameTime() => 1f / 60f;
        public void SetWindowIcon(string relativeSvgPath) { }
        public void BeginDrawing() { }
        public void EndDrawing() { }
        public void ToggleBorderless() { }
        public void Begin3D(CameraComponent camera) { }
        public void End3D() { }
        public void Clear(Vector3 color)
        {
            // Use PixiJS to clear the stage/background
            string cssColor = $"#{((int)(color.X*255)):X2}{((int)(color.Y*255)):X2}{((int)(color.Z*255)):X2}";
            _jsSync.InvokeVoid("veilborne.pixi.clear", cssColor);
        }
        public void SetSkyClearColor(Vector3 color) { }
        public void DrawCube(Vector3 position, Vector3 size, Vector3 color) { }
        public void DrawCubeWires(Vector3 position, Vector3 size, Vector3 color) { }
        public void ToggleFullscreen() { }
        public void RequestScreenshot() { }
    }
}

using System.Numerics;
using Veilborne.Interfaces;
using ZeroElectric.Vinculum;

namespace Veilborne.Core.RaylibImpl
{
    public class RaylibInputProvider : IInputProvider
    {
        public Vector2 GetMousePosition() => Raylib.GetMousePosition();
        public Vector2 GetMouseDelta() => Raylib.GetMouseDelta();
        public float GetMouseWheelMove() => Raylib.GetMouseWheelMove();
        public bool IsKeyDown(int key) => Raylib.IsKeyDown((KeyboardKey)key);
        public bool IsKeyPressed(int key) => Raylib.IsKeyPressed((KeyboardKey)key);
        public bool IsMouseButtonDown(int button) => Raylib.IsMouseButtonDown((MouseButton)button);
        public bool IsMouseButtonPressed(int button) => Raylib.IsMouseButtonPressed((MouseButton)button);
        public bool IsMouseButtonReleased(int button) => Raylib.IsMouseButtonReleased((MouseButton)button);
        public void ShowCursor() => Raylib.EnableCursor();
        public void HideCursor() => Raylib.DisableCursor();
    }
}

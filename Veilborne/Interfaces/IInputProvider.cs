using System.Collections.Generic;
using System.Numerics;

namespace Veilborne.Interfaces
{
    public interface IInputProvider
    {
        void UpdateStates();
        Vector2 GetMousePosition();
        Vector2 GetMouseDelta();
        float GetMouseWheelMove();
        bool IsKeyDown(int key);
        bool IsKeyPressed(int key);
        bool IsMouseButtonDown(int button);
        bool IsMouseButtonPressed(int button);
        bool IsMouseButtonReleased(int button);
        IReadOnlyList<int> GetPressedKeys();
        IReadOnlyList<int> GetPressedMouseButtons();
        void ShowCursor();
        void HideCursor();
    }

    public static class InputKeys
    {
        // MonoGame Keys enum values (XNA/WinForms virtual key codes)
        public const int KEY_W = 87;
        public const int KEY_S = 83;
        public const int KEY_A = 65;
        public const int KEY_D = 68;
        public const int KEY_SPACE = 32;
        public const int KEY_ENTER = 13;
        public const int KEY_BACKSPACE = 8;
        public const int KEY_DELETE = 46;
        public const int KEY_ESCAPE = 27;   // MonoGame Keys.Escape = 27
        public const int KEY_UP = 38;
        public const int KEY_DOWN = 40;
        public const int KEY_LEFT = 37;
        public const int KEY_RIGHT = 39;
        public const int KEY_F1 = 112;      // MonoGame Keys.F1 = 112
        public const int KEY_F2 = 113;
        public const int KEY_F3 = 114;
        public const int KEY_F12 = 123;     // MonoGame Keys.F12 = 123
        public const int KEY_LEFT_SHIFT = 160;  // MonoGame Keys.LeftShift = 160
        public const int KEY_ONE = 49;
        public const int KEY_TWO = 50;
        public const int KEY_THREE = 51;
        public const int KEY_FOUR = 52;
        public const int KEY_FIVE = 53;
        public const int KEY_SIX = 54;
        public const int KEY_SEVEN = 55;
        public const int KEY_EIGHT = 56;
        public const int KEY_NINE = 57;

        public const int MOUSE_BUTTON_LEFT = 0;
        public const int MOUSE_BUTTON_RIGHT = 1;
        public const int MOUSE_BUTTON_MIDDLE = 2;
    }
}

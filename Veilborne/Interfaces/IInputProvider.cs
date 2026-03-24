using System.Numerics;

namespace Veilborne.Interfaces
{
    public interface IInputProvider
    {
        Vector2 GetMousePosition();
        Vector2 GetMouseDelta();
        float GetMouseWheelMove();
        bool IsKeyDown(int key);
        bool IsKeyPressed(int key);
        bool IsMouseButtonDown(int button);
        bool IsMouseButtonPressed(int button);
        bool IsMouseButtonReleased(int button);
        void ShowCursor();
        void HideCursor();
    }

    public static class InputKeys
    {
        public const int KEY_W = 87;
        public const int KEY_S = 83;
        public const int KEY_A = 65;
        public const int KEY_D = 68;
        public const int KEY_SPACE = 32;
        public const int KEY_ESCAPE = 256;
        public const int KEY_F1 = 290;
        public const int KEY_F2 = 291;
        public const int KEY_F12 = 301;
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
    }
}

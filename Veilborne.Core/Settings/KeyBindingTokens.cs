using Veilborne.Interfaces;

namespace Veilborne.Settings
{
    public static class KeyBindingTokens
    {
        public const string None = "none";
        public const string KeyW = "key.w";
        public const string KeyA = "key.a";
        public const string KeyS = "key.s";
        public const string KeyD = "key.d";
        public const string KeyUp = "key.up";
        public const string KeyDown = "key.down";
        public const string KeyLeft = "key.left";
        public const string KeyRight = "key.right";
        public const string KeySpace = "key.space";
        public const string KeyF1 = "key.f1";
        public const string KeyF12 = "key.f12";
        public const string Key1 = "key.1";
        public const string Key2 = "key.2";
        public const string Key3 = "key.3";
        public const string Key4 = "key.4";
        public const string Key5 = "key.5";
        public const string Key6 = "key.6";
        public const string Key7 = "key.7";
        public const string Key8 = "key.8";
        public const string Key9 = "key.9";
        public const string MouseLeft = "mouse.left";
        public const string MouseRight = "mouse.right";
        public const string MouseMiddle = "mouse.middle";

        public static string Normalize(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return None;
            return token.Trim().ToLowerInvariant();
        }

        public static bool TryGetKeyCode(string token, out int keyCode)
        {
            switch (Normalize(token))
            {
                case KeyW: keyCode = InputKeys.KEY_W; return true;
                case KeyA: keyCode = InputKeys.KEY_A; return true;
                case KeyS: keyCode = InputKeys.KEY_S; return true;
                case KeyD: keyCode = InputKeys.KEY_D; return true;
                case KeyUp: keyCode = InputKeys.KEY_UP; return true;
                case KeyDown: keyCode = InputKeys.KEY_DOWN; return true;
                case KeyLeft: keyCode = InputKeys.KEY_LEFT; return true;
                case KeyRight: keyCode = InputKeys.KEY_RIGHT; return true;
                case KeySpace: keyCode = InputKeys.KEY_SPACE; return true;
                case KeyF1: keyCode = InputKeys.KEY_F1; return true;
                case KeyF12: keyCode = InputKeys.KEY_F12; return true;
                case Key1: keyCode = InputKeys.KEY_ONE; return true;
                case Key2: keyCode = InputKeys.KEY_TWO; return true;
                case Key3: keyCode = InputKeys.KEY_THREE; return true;
                case Key4: keyCode = InputKeys.KEY_FOUR; return true;
                case Key5: keyCode = InputKeys.KEY_FIVE; return true;
                case Key6: keyCode = InputKeys.KEY_SIX; return true;
                case Key7: keyCode = InputKeys.KEY_SEVEN; return true;
                case Key8: keyCode = InputKeys.KEY_EIGHT; return true;
                case Key9: keyCode = InputKeys.KEY_NINE; return true;
                default:
                    keyCode = 0;
                    return false;
            }
        }

        public static bool TryGetMouseButton(string token, out int button)
        {
            switch (Normalize(token))
            {
                case MouseLeft: button = InputKeys.MOUSE_BUTTON_LEFT; return true;
                case MouseRight: button = InputKeys.MOUSE_BUTTON_RIGHT; return true;
                case MouseMiddle: button = InputKeys.MOUSE_BUTTON_MIDDLE; return true;
                default:
                    button = -1;
                    return false;
            }
        }

        public static bool IsDown(IInputProvider input, InputBindingSettings binding)
            => IsTokenDown(input, binding.Primary) || IsTokenDown(input, binding.Secondary);

        public static bool IsPressed(IInputProvider input, InputBindingSettings binding)
            => IsTokenPressed(input, binding.Primary) || IsTokenPressed(input, binding.Secondary);

        public static bool IsTokenDown(IInputProvider input, string token)
        {
            if (TryGetKeyCode(token, out int key))
                return input.IsKeyDown(key);
            if (TryGetMouseButton(token, out int button))
                return input.IsMouseButtonDown(button);
            return false;
        }

        public static bool IsTokenPressed(IInputProvider input, string token)
        {
            if (TryGetKeyCode(token, out int key))
                return input.IsKeyPressed(key);
            if (TryGetMouseButton(token, out int button))
                return input.IsMouseButtonPressed(button);
            return false;
        }

        public static string ToDisplay(string token)
        {
            switch (Normalize(token))
            {
                case None: return "-";
                case KeyW: return "W";
                case KeyA: return "A";
                case KeyS: return "S";
                case KeyD: return "D";
                case KeyUp: return "Up";
                case KeyDown: return "Down";
                case KeyLeft: return "Left";
                case KeyRight: return "Right";
                case KeySpace: return "Space";
                case KeyF1: return "F1";
                case KeyF12: return "F12";
                case Key1: return "1";
                case Key2: return "2";
                case Key3: return "3";
                case Key4: return "4";
                case Key5: return "5";
                case Key6: return "6";
                case Key7: return "7";
                case Key8: return "8";
                case Key9: return "9";
                case MouseLeft: return "Mouse 1";
                case MouseRight: return "Mouse 2";
                case MouseMiddle: return "Mouse 3";
                default:
                    return token;
            }
        }

        public static string FromKeyCode(int keyCode)
        {
            return keyCode switch
            {
                InputKeys.KEY_W => KeyW,
                InputKeys.KEY_A => KeyA,
                InputKeys.KEY_S => KeyS,
                InputKeys.KEY_D => KeyD,
                InputKeys.KEY_UP => KeyUp,
                InputKeys.KEY_DOWN => KeyDown,
                InputKeys.KEY_LEFT => KeyLeft,
                InputKeys.KEY_RIGHT => KeyRight,
                InputKeys.KEY_SPACE => KeySpace,
                InputKeys.KEY_F1 => KeyF1,
                InputKeys.KEY_F12 => KeyF12,
                InputKeys.KEY_ONE => Key1,
                InputKeys.KEY_TWO => Key2,
                InputKeys.KEY_THREE => Key3,
                InputKeys.KEY_FOUR => Key4,
                InputKeys.KEY_FIVE => Key5,
                InputKeys.KEY_SIX => Key6,
                InputKeys.KEY_SEVEN => Key7,
                InputKeys.KEY_EIGHT => Key8,
                InputKeys.KEY_NINE => Key9,
                _ => None
            };
        }

        public static string FromMouseButton(int button)
        {
            return button switch
            {
                InputKeys.MOUSE_BUTTON_LEFT => MouseLeft,
                InputKeys.MOUSE_BUTTON_RIGHT => MouseRight,
                InputKeys.MOUSE_BUTTON_MIDDLE => MouseMiddle,
                _ => None
            };
        }
    }
}

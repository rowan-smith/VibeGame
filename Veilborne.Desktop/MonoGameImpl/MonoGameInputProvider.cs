using System.Numerics;
using Microsoft.Xna.Framework.Input;
using Veilborne.Core.Interfaces;

namespace Veilborne.Desktop.MonoGameImpl
{
    public class MonoGameInputProvider : IInputProvider
    {
        private static readonly int[] SupportedMouseButtons =
        {
            InputKeys.MOUSE_BUTTON_LEFT,
            InputKeys.MOUSE_BUTTON_RIGHT,
            InputKeys.MOUSE_BUTTON_MIDDLE
        };

        private Microsoft.Xna.Framework.Input.KeyboardState _currKeyboardState;
        private Microsoft.Xna.Framework.Input.KeyboardState _prevKeyboardState;
        private Microsoft.Xna.Framework.Input.MouseState _currMouseState;
        private Microsoft.Xna.Framework.Input.MouseState _prevMouseState;
        private VeilborneGame? _game;
        private bool _firstLockedDelta = true;
        private bool _forceLockedRecentering = true;

        /// <summary>Wire up so ShowCursor/HideCursor and mouse locking work.</summary>
        public void SetGame(VeilborneGame game) => _game = game;

        public void UpdateStates()
        {
            _prevKeyboardState = _currKeyboardState;
            _prevMouseState = _currMouseState;
            _currKeyboardState = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            _currMouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
        }

        public Vector2 GetMousePosition()
        {
            var state = _currMouseState;
            return new Vector2(state.X, state.Y);
        }

        public Vector2 GetMouseDelta()
        {
            var state = _currMouseState;
            if (_game != null && _game.MouseLocked)
            {
                if (!_game.IsWindowActive)
                {
                    _firstLockedDelta = true;
                    _forceLockedRecentering = true;
                    return Vector2.Zero;
                }

                int cx = _game.GraphicsDevice.Viewport.Width / 2;
                int cy = _game.GraphicsDevice.Viewport.Height / 2;

                if (_forceLockedRecentering)
                {
                    Microsoft.Xna.Framework.Input.Mouse.SetPosition(cx, cy);
                    _forceLockedRecentering = false;
                    _firstLockedDelta = false;
                    return Vector2.Zero;
                }

                var delta = new Vector2(state.X - cx, state.Y - cy);
                Microsoft.Xna.Framework.Input.Mouse.SetPosition(cx, cy);
                if (_firstLockedDelta)
                {
                    _firstLockedDelta = false;
                    return Vector2.Zero;
                }
                return delta;
            }
            _firstLockedDelta = true;
            _forceLockedRecentering = true;
            // Free cursor mode — delta from previous position
            var prev = new Vector2(_prevMouseState.X, _prevMouseState.Y);
            return new Vector2(state.X, state.Y) - prev;
        }

        public float GetMouseWheelMove()
        {
            var state = _currMouseState;
            return state.ScrollWheelValue - _prevMouseState.ScrollWheelValue;
        }

        public bool IsKeyDown(int key) => _currKeyboardState.IsKeyDown((Microsoft.Xna.Framework.Input.Keys)key);

        public bool IsKeyPressed(int key)
        {
            var k = (Microsoft.Xna.Framework.Input.Keys)key;
            return _currKeyboardState.IsKeyDown(k) && !_prevKeyboardState.IsKeyDown(k);
        }

        public bool IsMouseButtonDown(int button)
        {
            var state = _currMouseState;
            return button switch
            {
                0 => state.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                1 => state.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                2 => state.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                _ => false
            };
        }

        public bool IsMouseButtonPressed(int button)
        {
            var curr = _currMouseState;
            var prev = _prevMouseState;
            return button switch
            {
                0 => curr.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed && prev.LeftButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                1 => curr.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed && prev.RightButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                2 => curr.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed && prev.MiddleButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                _ => false
            };
        }

        public bool IsMouseButtonReleased(int button)
        {
            var curr = _currMouseState;
            var prev = _prevMouseState;
            return button switch
            {
                0 => curr.LeftButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed && prev.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                1 => curr.RightButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed && prev.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                2 => curr.MiddleButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed && prev.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                _ => false
            };
        }

        public IReadOnlyList<int> GetPressedKeys()
        {
            var currentlyDown = _currKeyboardState.GetPressedKeys();
            var previouslyDown = _prevKeyboardState.GetPressedKeys();
            var previousSet = new HashSet<Keys>(previouslyDown);
            var pressed = new List<int>(currentlyDown.Length);
            foreach (var key in currentlyDown)
            {
                if (!previousSet.Contains(key))
                    pressed.Add((int)key);
            }
            return pressed;
        }

        public IReadOnlyList<int> GetPressedMouseButtons()
        {
            var pressed = new List<int>(3);
            foreach (int button in SupportedMouseButtons)
            {
                if (IsMouseButtonPressed(button))
                    pressed.Add(button);
            }
            return pressed;
        }

        public void ShowCursor()
        {
            if (_game != null) { _game.IsMouseVisible = true; _game.MouseLocked = false; }
            _firstLockedDelta = true;
            _forceLockedRecentering = true;
            UpdateStates();
        }

        public void HideCursor()
        {
            if (_game != null)
            {
                _game.IsMouseVisible = false;
                _game.MouseLocked = true;
                int cx = _game.GraphicsDevice.Viewport.Width / 2;
                int cy = _game.GraphicsDevice.Viewport.Height / 2;
                Microsoft.Xna.Framework.Input.Mouse.SetPosition(cx, cy);
            }
            _firstLockedDelta = true;
            _forceLockedRecentering = true;
            UpdateStates();
        }
    }
}

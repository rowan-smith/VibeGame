using Microsoft.JSInterop;
using System.Numerics;
using Veilborne.Interfaces;
using System.Collections.Generic;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebInputProvider : IInputProvider
    {
        private readonly IJSRuntime _js;
        private DotNetObjectReference<WebInputProvider>? _dotNetRef;
        private Vector2 _mousePosition;
        private Vector2 _mouseDelta;
        private float _mouseWheel;
        private HashSet<int> _keysDown = new();
        private HashSet<int> _keysPressed = new();
        private HashSet<int> _mouseButtonsDown = new();
        private HashSet<int> _mouseButtonsPressed = new();
        private HashSet<int> _mouseButtonsReleased = new();

        private HashSet<int> _pendingKeysPressed = new();
        private HashSet<int> _pendingMouseButtonsPressed = new();
        private HashSet<int> _pendingMouseButtonsReleased = new();
        private float _pendingMouseWheel = 0;

        private Vector2 _lastMousePosition;
        private bool _initialized;

        public WebInputProvider(IJSRuntime js)
        {
            _js = js;
            _dotNetRef = DotNetObjectReference.Create(this);
            _initialized = false;
        }

        public void UpdateStates()
        {
            if (!_initialized)
            {
                _js.InvokeVoidAsync("veilborne.addInputListeners", _dotNetRef);
                _initialized = true;
            }
            _mouseDelta = _mousePosition - _lastMousePosition;
            _lastMousePosition = _mousePosition;

            // Clear previous frame "one-shot" states
            _keysPressed.Clear();
            _mouseButtonsPressed.Clear();
            _mouseButtonsReleased.Clear();

            // Transfer pending states from JS callbacks to the current frame state
            lock (_pendingKeysPressed)
            {
                foreach (var k in _pendingKeysPressed) _keysPressed.Add(k);
                _pendingKeysPressed.Clear();
            }

            lock (_pendingMouseButtonsPressed)
            {
                foreach (var b in _pendingMouseButtonsPressed) _mouseButtonsPressed.Add(b);
                _pendingMouseButtonsPressed.Clear();
            }

            lock (_pendingMouseButtonsReleased)
            {
                foreach (var b in _pendingMouseButtonsReleased) _mouseButtonsReleased.Add(b);
                _pendingMouseButtonsReleased.Clear();
            }

            _mouseWheel = _pendingMouseWheel;
            _pendingMouseWheel = 0;
        }

        public Vector2 GetMousePosition() => _mousePosition;
        public Vector2 GetMouseDelta() => _mouseDelta;
        public float GetMouseWheelMove() => _mouseWheel;
        public bool IsKeyDown(int key) => _keysDown.Contains(key);
        public bool IsKeyPressed(int key) => _keysPressed.Contains(key);
        public bool IsMouseButtonDown(int button) => _mouseButtonsDown.Contains(button);
        public bool IsMouseButtonPressed(int button) => _mouseButtonsPressed.Contains(button);
        public bool IsMouseButtonReleased(int button) => _mouseButtonsReleased.Contains(button);
        public IReadOnlyList<int> GetPressedKeys() => new List<int>(_keysDown);
        public IReadOnlyList<int> GetPressedMouseButtons() => new List<int>(_mouseButtonsDown);
        public void ShowCursor() { }
        public void HideCursor() { }

        [JSInvokable]
        public void OnKeyDown(int key)
        {
            if (_keysDown.Add(key))
            {
                lock (_pendingKeysPressed) _pendingKeysPressed.Add(key);
            }
        }
        [JSInvokable]
        public void OnKeyUp(int key)
        {
            _keysDown.Remove(key);
        }
        [JSInvokable]
        public void OnMouseMove(int x, int y)
        {
            _mousePosition = new Vector2(x, y);
        }
        [JSInvokable]
        public void OnMouseDown(int button)
        {
            if (_mouseButtonsDown.Add(button))
            {
                lock (_pendingMouseButtonsPressed) _pendingMouseButtonsPressed.Add(button);
            }
        }
        [JSInvokable]
        public void OnMouseUp(int button)
        {
            if (_mouseButtonsDown.Remove(button))
            {
                lock (_pendingMouseButtonsReleased) _pendingMouseButtonsReleased.Add(button);
            }
        }
        [JSInvokable]
        public void OnMouseWheel(float delta)
        {
            _pendingMouseWheel = delta;
        }
    }
}

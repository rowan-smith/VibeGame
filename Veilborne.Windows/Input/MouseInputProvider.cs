using Microsoft.Xna.Framework.Input;
using Veilborne.Core.Interfaces;
using Vector2 = System.Numerics.Vector2;

namespace Veilborne.Windows.Input;

public class MouseInputProvider : IMouseInput
{
    private MouseState _previous;
    private MouseState _current;
    private bool _locked;

    public MouseInputProvider()
    {
        _previous = Mouse.GetState();
        _current = _previous;
    }

    public Vector2 GetDelta()
    {
        _previous = _current;
        _current = Mouse.GetState();
        return new Vector2(_current.X - _previous.X, _current.Y - _previous.Y);
    }

    public void LockCursor(bool locked)
    {
        _locked = locked;
        // In MonoGame, you can control this via Game.IsMouseVisible
        // and recenter manually each frame if needed.
        // You could inject a callback to the main Game class to handle that.
    }
}

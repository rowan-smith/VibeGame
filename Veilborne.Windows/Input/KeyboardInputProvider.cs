using Microsoft.Xna.Framework.Input;
using Veilborne.Core.Enums;
using Veilborne.Core.Interfaces;

namespace Veilborne.Windows.Input;

public class KeyboardInputProvider : IInputProvider
{
    private KeyboardState _current;
    private KeyboardState _previous;

    public KeyboardInputProvider()
    {
        _current = Keyboard.GetState();
        _previous = _current;
    }

    public void Update()
    {
        _previous = _current;
        _current = Keyboard.GetState();
    }

    public bool IsKeyDown(Key key)
    {
        return _current.IsKeyDown(ToMonoKey(key));
    }

    public bool IsKeyPressed(Key key)
    {
        var k = ToMonoKey(key);
        return _current.IsKeyDown(k) && !_previous.IsKeyDown(k);
    }

    private static Keys ToMonoKey(Key key) => key switch
    {
        Key.W => Keys.W,
        Key.A => Keys.A,
        Key.S => Keys.S,
        Key.D => Keys.D,
        Key.Space => Keys.Space,
        Key.P => Keys.P,
        _ => Keys.None
    };
}

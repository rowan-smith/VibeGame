using Veilborne.Core.Enums;

namespace Veilborne.Core.Interfaces;

public interface IInputProvider
{
    bool IsKeyDown(Key key);

    bool IsKeyPressed(Key key);
}

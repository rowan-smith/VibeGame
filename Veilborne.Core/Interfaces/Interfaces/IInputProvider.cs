using Veilborne.Core.Enums;

namespace Veilborne.Core.Interfaces;

public interface IInputProvider
{
    // Called once per frame to refresh input state
    void Update();

    bool IsKeyDown(Key key);

    bool IsKeyPressed(Key key);
}

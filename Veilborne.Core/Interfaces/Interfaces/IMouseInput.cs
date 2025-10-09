using System.Numerics;

namespace Veilborne.Core.Interfaces;

public interface IMouseInput
{
    Vector2 GetDelta();

    void LockCursor(bool locked);
}

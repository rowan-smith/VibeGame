using Veilborne.GameWorlds;
using Veilborne.Utility;

namespace Veilborne.Interfaces;

public interface IRenderSystem : ISystem
{
    void Render(GameTime time, GameState state);
}

using Veilborne.Core.GameWorlds;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Interfaces;

public interface IRenderSystem : ISystem
{
    void Render(GameTime time, GameState state);
}

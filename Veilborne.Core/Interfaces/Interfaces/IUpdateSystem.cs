using Veilborne.Core.GameWorlds;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Interfaces
{
    public interface IUpdateSystem : ISystem
    {
        void Update(GameTime time, GameState state); // Called each frame
    }
}

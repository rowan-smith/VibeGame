using Veilborne.GameWorlds;
using Veilborne.Utility;

namespace Veilborne.Interfaces;

public interface ISystem
{
    int Priority { get; } // Determines update order
    
    SystemCategory Category { get; } 

    bool RunsWhenPaused { get; }  

    void Initialize(); // Called once on startup with world context

    void Update(GameTime time, GameState state); // Called each frame

    void Shutdown(); // Called on exit
}

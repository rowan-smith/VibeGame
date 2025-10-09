using Veilborne.Core.Utility;

namespace Veilborne.Core.Interfaces;

public interface ISystem
{
    int Priority { get; } // Determines update order
    
    SystemCategory Category { get; } 

    bool RunsWhenPaused { get; }  

    void Initialize(); // Called once on startup with world context

    void Shutdown(); // Called on exit
}

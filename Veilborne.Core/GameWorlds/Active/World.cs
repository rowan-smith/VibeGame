using Veilborne.Core.Interfaces;
using Veilborne.Core.Systems;
using Veilborne.Core.Utility;

namespace Veilborne.Core.GameWorlds.Active
{
    public class World
    {
        public GameState State { get; }
        public SystemManager SystemManager { get; set; }

        public World(GameState state, IEnumerable<ISystem> systems)
        {
            State = state;
            SystemManager = new SystemManager(systems);
        }

        public void Initialize()
        {
            SystemManager.InitializeAll();
        }

        public void Update(GameTime time)
        {
            // Update systems
            SystemManager.Update(time, State);
        }

        public void Render(GameTime time)
        {
            SystemManager.Render(time, State); // Only RenderSystems
        }

        public void Shutdown()
        {
            SystemManager.ShutdownAll();
        }
    }
}

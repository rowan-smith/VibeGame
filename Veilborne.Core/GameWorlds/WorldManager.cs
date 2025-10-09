using Veilborne.Core.Utility;
using Veilborne.Core.GameWorlds.Active;

namespace Veilborne.Core.GameWorlds
{
    public class WorldManager
    {
        private readonly List<World> _worlds = new();
        private World? _activeWorld;

        public void AddWorld(World world)
        {
            _worlds.Add(world);
            if (_activeWorld == null) _activeWorld = world;
            world.Initialize();
        }

        public void Initialize()
        {
            foreach (var world in _worlds) world.Initialize();
        }

        public void Update(GameTime time)
        {
            foreach (var world in _worlds) world.Update(time);
        }

        public void Render(GameTime time)
        {
            if (_activeWorld == null) return;
            _activeWorld.Render(time);
        }

        public void Shutdown()
        {
            foreach (var world in _worlds) world.Shutdown();
        }

        public void SetActiveWorld(World world)
        {
            if (!_worlds.Contains(world)) throw new InvalidOperationException("World not added yet.");
            _activeWorld = world;
        }

        public World? GetActiveWorld() => _activeWorld;
    }
}

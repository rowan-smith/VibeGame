using Raylib_CsLo;
using Veilborne.GameWorlds.Active;
using Veilborne.Utility;

namespace Veilborne.GameWorlds
{
    public class WorldManager
    {
        private readonly List<World> _worlds = new();
        private World? _activeWorld;

        public void AddWorld(World world)
        {
            _worlds.Add(world);
            if (_activeWorld == null)
            {
                _activeWorld = world;
            }

            // Ensure systems are initialized for newly added worlds
            world.Initialize();
        }

        public void Initialize()
        {
            foreach (var world in _worlds)
            {
                world.Initialize();
            }
        }

        public void Update(GameTime time)
        {
            // Update logic for all worlds
            foreach (var w in _worlds)
            {
                w.Update(time); // Logic only
            }
        }

        public void Render(GameTime time)
        {
            // Only render if we have an active world
            if (_activeWorld == null)
            {
                return;
            }

            // Begin frame
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Raylib.SKYBLUE);

            // Delegate drawing to the active world
            _activeWorld.Render(time);

            // End frame
            Raylib.EndDrawing();
        }

        public void Shutdown()
        {
            foreach (var world in _worlds)
            {
                world.Shutdown();
            }
        }

        public void SetActiveWorld(World world)
        {
            if (!_worlds.Contains(world))
                throw new InvalidOperationException("World not added yet.");

            _activeWorld = world;
        }

        public World? GetActiveWorld()
        {
            return _activeWorld;
        }
    }
}

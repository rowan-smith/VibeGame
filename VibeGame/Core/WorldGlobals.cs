using System.Text.Json;

namespace VibeGame.Core
{
    /// Global world settings including the deterministic seed used across all procedural systems.
    public static class WorldGlobals
    {
        public static int Seed { get; private set; }
        public static WorldConfig? Config { get; private set; }

        static WorldGlobals()
        {
            Initialize();
        }

        public static void Initialize()
        {
            // Attempt to load seed from optional config; otherwise generate once per process.
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var path1 = Path.Combine(baseDir, "assets", "config", "world.json");
                var path2 = Path.Combine(baseDir, "assets", "config", "terrain", "world.json");
                string? path = File.Exists(path1) ? path1 : (File.Exists(path2) ? path2 : null);
                if (path != null)
                {
                    Config = JsonModelLoader.LoadFile<WorldConfig>(path);
                    Seed = Config.WorldSeed != 0 ? Config.WorldSeed : Random.Shared.Next();
                }
                else
                {
                    Seed = Random.Shared.Next();
                }
            }
            catch
            {
                Seed = Random.Shared.Next();
            }

            Console.WriteLine($"World Seed: {Seed}");
        }

        // Simple deterministic hash utility for coordinate-based randomness combined with the world seed.
        public static int Hash(params int[] values)
        {
            unchecked
            {
                int h = 216613626; // FNV-like start
                h = (h * 16777619) ^ Seed;
                foreach (var v in values)
                    h = (h * 16777619) ^ v;
                return h;
            }
        }

        public static float Random01(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                return (x % 10000) / 10000f; // [0,1)
            }
        }
    }
}

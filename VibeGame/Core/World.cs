using System.Numerics;
using VibeGame.Biomes;
using VibeGame.Objects;
using VibeGame.Terrain;

namespace VibeGame.Core
{
    // New instance-based World coordinator; does not replace WorldGlobals (legacy static seed holder).
    public class World
    {
        // Core Determinism
        public int Seed { get; init; }

        // Subsystems
        public TerrainManager Terrain { get; init; }
        public BiomeManager Biomes { get; init; }
        public ObjectSpawner Spawner { get; init; }
        public Player Player { get; init; }

        // Active chunks and async queue
        public Dictionary<Vector3, Chunk> ActiveChunks { get; } = new();
        public AsyncTaskQueue AsyncQueue { get; } = new();

        // Require fully-constructed subsystems to avoid DI churn in this refactor step
        public World(int seed, Player player, TerrainManager terrain, BiomeManager biomes, ObjectSpawner spawner)
        {
            Seed = seed;
            Player = player;
            Terrain = terrain;
            Biomes = biomes;
            Spawner = spawner;
        }

        public void Update(Vector3 playerPos)
        {
            // Update terrain rings around player
            Terrain.UpdateAround(playerPos, 0);
            // Prewarm biome cache and spawn objects asynchronously
            Biomes.EnsureChunks(playerPos, AsyncQueue);
            Spawner.EnsureObjects(playerPos, ActiveChunks, AsyncQueue);
        }

        public void PumpAsyncJobs()
        {
            Terrain.PumpAsyncJobs();
            // Additional queues could be pumped here if needed
        }

        // Convenience world queries
        public float SampleHeight(float x, float z) => Terrain.SampleHeight(x, z);
        public Biomes.IBiome GetBiomeAt(float x, float z) => Terrain.GetBiomeAt(x, z);
    }
}

using System.Numerics;
using VibeGame.Biomes;
using VibeGame.Objects;
using VibeGame.Terrain;

namespace VibeGame.Core
{
    /// <summary>
    /// Core runtime world.
    /// </summary>
    public class World
    {
        // Core determinism
        public int Seed { get; init; }

        // Subsystems
        public TerrainManager Terrain { get; init; }
        public ITerrainGenerator TerrainAdapter { get; init; } // Adapter for ObjectSpawner
        public IBiomeProvider Biomes { get; init; }
        public ObjectSpawner Spawner { get; init; }
        public Player Player { get; init; }

        // Active chunks and async queue
        public Dictionary<Vector3, Chunk> ActiveChunks { get; } = new();
        public AsyncTaskQueue AsyncQueue { get; } = new();

        // Constructor
        public World(int seed, Player player, TerrainManager terrain, IBiomeProvider biomes, ObjectSpawner spawner)
        {
            Seed = seed;
            Player = player;
            Terrain = terrain;
            Biomes = biomes;
            TerrainAdapter = new TerrainManagerAdapter(terrain); // wrap TerrainManager
            Spawner = spawner;
        }

        /// <summary>
        /// Update world around player position.
        /// </summary>
        public void Update(Vector3 playerPos)
        {
            // Update terrain rings around player
            Terrain.UpdateAround(playerPos, 0);

            // Spawn objects asynchronously using adapted terrain
            Spawner.EnsureObjects(playerPos, ActiveChunks, AsyncQueue);
        }

        /// <summary>
        /// Pump any async jobs (terrain / object generation)
        /// </summary>
        public async Task PumpAsyncJobs()
        {
            await Terrain.PumpAsyncJobs();
            // Additional queues could be pumped here if needed
        }

        // Convenience methods
        public float SampleHeight(float x, float z) => Terrain.SampleHeight(new Vector3(x, 0, z));

        public IBiome GetBiomeAt(float x, float z) => Biomes.GetBiomeAt(new Vector2(x, z), TerrainAdapter);
    }
}

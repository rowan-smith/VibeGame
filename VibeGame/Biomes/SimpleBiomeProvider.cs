using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    // Simple, maintainable provider with composable list of biomes, in priority order.
    public class SimpleBiomeProvider : IBiomeProvider
    {
        private readonly IReadOnlyList<IBiome> _biomes;

        public SimpleBiomeProvider(IEnumerable<IBiome> biomes)
        {
            _biomes = biomes.ToList();
        }

        public IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain)
        {
            // No fallbacks: return the first biome whose Contains() matches. If none, return a special EmptyBiome (no trees).
            foreach (var biome in _biomes)
            {
                if (biome.Contains(worldPos, terrain)) return biome;
            }
            return EmptyBiome.Instance;
        }

        private sealed class EmptyBiome : IBiome
        {
            public static readonly EmptyBiome Instance = new EmptyBiome();
            public string Id => "None";
            public ITreeSpawner TreeSpawner { get; } = new EmptyTreeSpawner();
            public bool Contains(Vector2 worldPos, ITerrainGenerator terrain) => true; // catch-all when explicitly chosen
        }

        private sealed class EmptyTreeSpawner : ITreeSpawner
        {
            public List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
                ITerrainGenerator terrain, Vector2 originWorld, int chunkSize, int targetCount)
            {
                return new List<(Vector3, float, float, float)>();
            }
        }
    }
}

using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    /// <summary>
    /// Simple biome provider that assigns biomes deterministically by world position.
    /// </summary>
    public class SimpleBiomeProvider : IBiomeProvider
    {
        private readonly List<IBiome> _biomes;
        private readonly int _seed;

        public SimpleBiomeProvider(IEnumerable<IBiome> biomes, int seed = 1337)
        {
            _biomes = new List<IBiome>(biomes);
            _seed = seed;
        }

        public IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain)
        {
            if (_biomes.Count == 0)
            {
                throw new InvalidOperationException("No biomes registered");
            }

            // Deterministic selection based on position
            int index = HashPosition(worldPos) % _biomes.Count;
            return _biomes[index];
        }

        private int HashPosition(Vector2 pos)
        {
            unchecked
            {
                int hash = _seed;
                hash = hash * 397 ^ pos.X.GetHashCode();
                hash = hash * 397 ^ pos.Y.GetHashCode();
                return Math.Abs(hash);
            }
        }
    }
}

using System.Numerics;
using Serilog;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    /// <summary>
    /// Biome provider with smooth blending between biome blocks.
    /// </summary>
    public class SimpleBiomeProvider : IBiomeProvider
    {
        private readonly List<IBiome> _biomes;
        private readonly int _seed;
        private readonly float _biomeBlockSize; // size of biome blocks in world units

        private readonly ILogger _logger = Log.ForContext<SimpleBiomeProvider>();

        public SimpleBiomeProvider(IEnumerable<IBiome> biomes, float biomeBlockSize = 64f, int seed = 1337)
        {
            _biomes = new List<IBiome>(biomes);
            _seed = seed;
            _biomeBlockSize = Math.Max(1f, biomeBlockSize);

            _logger.Debug("Registered {BiomeCount} biomes with block size {BlockSize}", _biomes.Count, _biomeBlockSize);
        }

        public IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain)
        {
            if (_biomes.Count == 0)
                throw new InvalidOperationException("No biomes registered");

            // Determine the four nearest block centers
            float fx = worldPos.X / _biomeBlockSize;
            float fy = worldPos.Y / _biomeBlockSize;

            int x0 = (int)MathF.Floor(fx);
            int x1 = x0 + 1;
            int y0 = (int)MathF.Floor(fy);
            int y1 = y0 + 1;

            float tx = fx - x0; // normalized position in block [0,1]
            float ty = fy - y0;

            // Get biome indices for the four corners
            IBiome b00 = _biomes[HashBlock(x0, y0) % _biomes.Count];
            IBiome b10 = _biomes[HashBlock(x1, y0) % _biomes.Count];
            IBiome b01 = _biomes[HashBlock(x0, y1) % _biomes.Count];
            IBiome b11 = _biomes[HashBlock(x1, y1) % _biomes.Count];

            // Use bilinear interpolation for a smooth weighting
            float w00 = (1 - tx) * (1 - ty);
            float w10 = tx * (1 - ty);
            float w01 = (1 - tx) * ty;
            float w11 = tx * ty;

            // Select the dominant biome based on weights (for simplicity)
            IBiome dominant = b00;
            float maxWeight = w00;

            if (w10 > maxWeight) { maxWeight = w10; dominant = b10; }
            if (w01 > maxWeight) { maxWeight = w01; dominant = b01; }
            if (w11 > maxWeight) { maxWeight = w11; dominant = b11; }

            return dominant;
        }

        private int HashBlock(int bx, int by)
        {
            unchecked
            {
                int hash = _seed;
                hash = hash * 397 ^ bx;
                hash = hash * 397 ^ by;
                return Math.Abs(hash);
            }
        }
    }
}

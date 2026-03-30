using System.Numerics;

namespace Veilborne.Core.Interfaces
{
    /// <summary>
    /// A single biome with its normalized blend weight at a world position.
    /// </summary>
    public readonly record struct BiomeWeight(IBiome Biome, float Weight);

    public interface IBiomeProvider
    {
        IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain);

        /// <summary>
        /// Returns up to <paramref name="maxResults"/> nearest biomes with normalized blend
        /// weights that sum to 1.0.  Default implementation returns the single primary biome.
        /// </summary>
        void GetBlendWeightsAt(Vector2 worldPos, ITerrainGenerator terrain, Span<BiomeWeight> buffer, out int count, int maxResults = 4)
        {
            buffer[0] = new BiomeWeight(GetBiomeAt(worldPos, terrain), 1f);
            count = 1;
        }
    }
}

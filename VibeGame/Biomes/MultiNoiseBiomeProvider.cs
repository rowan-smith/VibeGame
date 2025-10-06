using System.Numerics;
using VibeGame.Terrain;
using VibeGame.Biomes.Environment;

namespace VibeGame.Biomes
{
    /// Biome provider that classifies world positions using multi-noise environment sampling
    /// and a set of biome cluster profiles in (Temp, Moisture, Elevation, Fertility) space.
    public sealed class MultiNoiseBiomeProvider : IBiomeProvider
    {
        private readonly IEnvironmentSampler _sampler;
        private readonly Dictionary<string, IBiome> _biomesById;
        private readonly List<BiomeClusterProfile> _profiles;

        public MultiNoiseBiomeProvider(IEnumerable<IBiome> biomes,
                                       IEnvironmentSampler sampler,
                                       IEnumerable<BiomeClusterProfile>? profiles = null)
        {
            _sampler = sampler;
            _biomesById = biomes.ToDictionary(b => b.Id, b => b);

            if (profiles == null)
                throw new InvalidOperationException("Biome profiles are required from configuration. No fallback defaults are provided.");

            _profiles = profiles.ToList();
            if (_profiles.Count == 0)
                throw new InvalidOperationException("Biome profiles list is empty. Provide at least one profile in configuration.");

            // Validate that all profiles reference known biomes
            var unknown = _profiles.Select(p => p.BiomeId).Where(id => !_biomesById.ContainsKey(id)).Distinct().ToList();
            if (unknown.Count > 0)
                throw new InvalidOperationException($"Profiles reference unknown biome ids: {string.Join(", ", unknown)}");

            // Ensure every provided biome has at least one profile
            var covered = new HashSet<string>(_profiles.Select(p => p.BiomeId));
            var notCovered = _biomesById.Keys.Where(id => !covered.Contains(id)).ToList();
            if (notCovered.Count > 0)
                throw new InvalidOperationException($"Missing profiles for biomes: {string.Join(", ", notCovered)}");
        }

        public IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain)
        {
            var env = _sampler.Sample(worldPos, terrain);

            // Choose nearest cluster center by weighted distance
            float bestDist = float.MaxValue;
            IBiome? best = null;
            foreach (var p in _profiles)
            {
                float d2 = p.DistanceSquared(env);
                if (d2 < bestDist && _biomesById.TryGetValue(p.BiomeId, out var biome))
                {
                    bestDist = d2;
                    best = biome;
                }
            }

            if (best != null) return best;
            throw new InvalidOperationException("No matching biome was found for the sampled environment. Check configured biome profiles.");
        }


    }
}

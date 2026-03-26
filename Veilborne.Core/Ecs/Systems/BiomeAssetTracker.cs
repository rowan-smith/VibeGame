using System.Collections.Generic;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Tracks biome asset request/load state to avoid duplicate work.
    /// </summary>
    public class BiomeAssetTracker
    {
        public HashSet<string> Requested { get; } = new(System.StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Loaded { get; } = new(System.StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ActiveChunkRefs { get; } = new(System.StringComparer.OrdinalIgnoreCase);
    }
}


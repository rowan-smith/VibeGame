using System.Numerics;

namespace Veilborne.Terrain
{
    public enum ResourceBlockType
    {
        None = 0,
        Grass = 1,
        Dirt = 2,
        Rock = 3,
        Coal = 4,
        Iron = 5,
        Copper = 6
    }

    public sealed class ResourceVoxel
    {
        public Vector3 LocalPosition { get; set; }
        public ResourceBlockType Type { get; set; } = ResourceBlockType.None;
        public float Density { get; set; } = 1f;
        public string BiomeId { get; set; } = string.Empty;
    }
}

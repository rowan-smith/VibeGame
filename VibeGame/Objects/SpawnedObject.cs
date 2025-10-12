using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Objects
{
    public sealed class SpawnedObject
    {
        public string ObjectId { get; set; } = string.Empty;
        public string ModelPath { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public Vector3 Scale { get; set; } = Vector3.One;
        public float CollisionRadius { get; set; } = 0f;
        // Nullable rotation degrees from config: null = auto orient, value (including 0) = manual override
        public float? ConfigRotationDegrees { get; set; }
    }

    public interface IWorldObjectSpawner
    {
        List<SpawnedObject> GenerateObjects(string biomeId, ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count);
    }

    public interface IWorldObjectRenderer
    {
        void DrawWorldObject(SpawnedObject obj);
    }
}

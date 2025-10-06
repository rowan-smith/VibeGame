using System.Numerics;

namespace VibeGame.Objects
{
    public sealed class SpawnedObject
    {
        public string ObjectId { get; set; } = string.Empty;   // e.g., "maple_tree"
        public string ModelPath { get; set; } = string.Empty;  // e.g., "assets/models/world/trees/maple_tree.glb"
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public Vector3 Scale { get; set; } = new Vector3(1f, 1f, 1f);
        
        // Optional XZ collision radius (meters). If > 0, treat as solid cylinder for simple collision.
        public float CollisionRadius { get; set; } = 0f;
    }

    public interface IWorldObjectSpawner
    {
        List<SpawnedObject> GenerateObjects(
            string biomeId,
            Terrain.ITerrainGenerator terrain,
            float[,] heights,
            Vector2 originWorld,
            int count);
    }

    public interface IWorldObjectRenderer
    {
        void DrawWorldObject(SpawnedObject obj);
    }
}
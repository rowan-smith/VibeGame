using System.Numerics;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.Objects
{
    public sealed class SpawnedObject
    {
        public string ObjectId { get; set; } = string.Empty;
        public string ObjectDisplayName { get; set; } = string.Empty;
        public string ModelPath { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public Vector3 Scale { get; set; } = Vector3.One;
        public float CollisionRadius { get; set; } = 0f;
    }

    public interface IWorldObjectSpawner
    {
        List<SpawnedObject> GenerateObjects(string biomeId, ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count);
    }

    public interface IWorldObjectRenderer
    {
        void Render(CameraComponent camera);
        void DrawWorldObject(SpawnedObject obj);
    }
}

using System.Numerics;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Performs lightweight player-vs-world-object broadphase/narrowphase checks.
    /// </summary>
    public class CollisionDetectionSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly CollisionFrameBuffer _collisionBuffer;

        public CollisionDetectionSystem(EntityRegistry entities, CollisionFrameBuffer collisionBuffer)
        {
            _entities = entities;
            _collisionBuffer = collisionBuffer;
        }

        public void Update(float dt)
        {
            _collisionBuffer.PlayerPush = Vector3.Zero;

            foreach (var player in _entities.GetEntitiesWith<PlayerComponent, CameraComponent>())
            {
                if (!player.TryGetComponent<ColliderComponent>(out var playerCollider))
                    continue;

                var playerCamera = player.GetComponent<CameraComponent>();
                var playerPos = playerCamera.Position;
                var playerRadius = MathF.Max(0.001f, playerCollider.Radius);
                var playerVelocity = player.TryGetComponent<VelocityComponent>(out var velocity)
                    ? velocity.Linear
                    : Vector3.Zero;
                var playerFilter = player.TryGetComponent<CollisionFilterComponent>(out var pf)
                    ? pf
                    : new CollisionFilterComponent
                    {
                        Layer = CollisionLayer.Player,
                        CollidesWith = CollisionLayer.WorldStatic | CollisionLayer.Foliage
                    };

                Vector3 accumulatedPush = Vector3.Zero;
                foreach (var worldObject in _entities.GetEntitiesWith<WorldObjectComponent, TransformComponent>())
                {
                    if (!worldObject.TryGetComponent<ColliderComponent>(out var objectCollider))
                        continue;
                    var objectFilter = worldObject.TryGetComponent<CollisionFilterComponent>(out var of)
                        ? of
                        : new CollisionFilterComponent
                        {
                            Layer = CollisionLayer.WorldStatic,
                            CollidesWith = CollisionLayer.Player
                        };

                    if ((playerFilter.CollidesWith & objectFilter.Layer) == 0 ||
                        (objectFilter.CollidesWith & playerFilter.Layer) == 0)
                        continue;

                    var objectTransform = worldObject.GetComponent<TransformComponent>();
                    // Use horizontal (XZ) collision so trees with ground-level pivots still block the player camera.
                    var delta = playerPos - objectTransform.Position;
                    delta.Y = 0f;
                    var distanceSq = delta.LengthSquared();
                    var targetDistance = playerRadius + MathF.Max(0.001f, objectCollider.Radius);
                    var targetDistanceSq = targetDistance * targetDistance;

                    if (distanceSq >= targetDistanceSq)
                    {
                        // Swept XZ test prevents tunneling when player moves fast enough to cross a tree in one frame.
                        if (dt > 1e-5f &&
                            TryComputeSweptPush(playerPos - playerVelocity * dt, playerPos, objectTransform.Position, targetDistance, out var sweptPush))
                        {
                            accumulatedPush += sweptPush;
                        }
                        continue;
                    }

                    if (distanceSq < 1e-6f)
                        delta = Vector3.UnitX;

                    var distance = MathF.Max(0.001f, MathF.Sqrt(delta.LengthSquared()));
                    var normal = delta / distance;
                    normal.Y = 0f;
                    var penetration = targetDistance - distance;
                    accumulatedPush += normal * penetration;
                }

                accumulatedPush.Y = 0f;
                _collisionBuffer.PlayerPush = accumulatedPush;
                break;
            }
        }

        private static bool TryComputeSweptPush(Vector3 start3, Vector3 end3, Vector3 center3, float radius, out Vector3 push)
        {
            var start = new Vector2(start3.X, start3.Z);
            var end = new Vector2(end3.X, end3.Z);
            var center = new Vector2(center3.X, center3.Z);
            var d = end - start;
            var m = start - center;

            float a = Vector2.Dot(d, d);
            if (a < 1e-8f)
            {
                push = Vector3.Zero;
                return false;
            }

            float c = Vector2.Dot(m, m) - radius * radius;
            if (c <= 0f)
            {
                push = Vector3.Zero;
                return false;
            }

            float b = Vector2.Dot(m, d);
            float disc = b * b - a * c;
            if (disc < 0f)
            {
                push = Vector3.Zero;
                return false;
            }

            float t = (-b - MathF.Sqrt(disc)) / a;
            if (t < 0f || t > 1f)
            {
                push = Vector3.Zero;
                return false;
            }

            var hit = start + d * t;
            var n = hit - center;
            if (n.LengthSquared() < 1e-8f)
                n = Vector2.UnitX;
            else
                n = Vector2.Normalize(n);

            const float skin = 0.01f;
            var desired = center + n * (radius + skin);
            var planarPush = desired - end;
            if (planarPush.LengthSquared() < 1e-8f)
            {
                push = Vector3.Zero;
                return false;
            }

            push = new Vector3(planarPush.X, 0f, planarPush.Y);
            return true;
        }
    }
}

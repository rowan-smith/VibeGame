using System.Numerics;
using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Performs lightweight player-vs-world-object broadphase/narrowphase checks.
    /// </summary>
    public class CollisionDetectionSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly CollisionFrameBuffer _collisionBuffer;
        private readonly WorldObjectSpatialIndex _spatialIndex;
        private readonly List<Entity> _candidateObjects = new(128);

        public CollisionDetectionSystem(EntityRegistry entities, CollisionFrameBuffer collisionBuffer, WorldObjectSpatialIndex spatialIndex)
        {
            _entities = entities;
            _collisionBuffer = collisionBuffer;
            _spatialIndex = spatialIndex;
        }

        public void Update(float dt)
        {
            _collisionBuffer.PlayerPush = Vector3.Zero;

            bool playerHandled = false;
            _entities.ForEachWith<PlayerComponent, CameraComponent>(player =>
            {
                if (playerHandled) return;
                if (!player.TryGetComponent<ColliderComponent>(out var playerCollider))
                    return;

                var playerCamera = player.GetComponent<CameraComponent>();
                var playerPos = playerCamera.Position;
                var playerRadius = MathF.Max(0.001f, playerCollider.Radius);
                var playerVelocity = player.TryGetComponent<VelocityComponent>(out var velocity)
                    ? velocity.Linear
                    : Vector3.Zero;
                var playerPrevPos = playerPos - playerVelocity * MathF.Max(0f, dt);
                float playerMoveDistance = Vector3.Distance(playerPos, playerPrevPos);
                var playerFilter = player.TryGetComponent<CollisionFilterComponent>(out var pf)
                    ? pf
                    : new CollisionFilterComponent
                    {
                        Layer = CollisionLayer.Player,
                        CollidesWith = CollisionLayer.WorldStatic | CollisionLayer.Foliage
                    };

                Vector3 accumulatedPush = Vector3.Zero;
                float queryRadius = MathF.Max(2f, playerRadius + playerMoveDistance + 8f);
                _spatialIndex.Query(playerPos, queryRadius, _candidateObjects);
                for (int candidateIndex = 0; candidateIndex < _candidateObjects.Count; candidateIndex++)
                {
                    var worldObject = _candidateObjects[candidateIndex];
                    if (!worldObject.HasComponent<WorldObjectComponent>())
                        continue;
                    if (!worldObject.TryGetComponent<TransformComponent>(out _))
                        continue;
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
                    float broadphaseRadius = targetDistance + playerMoveDistance + 1.0f;
                    float broadphaseRadiusSq = broadphaseRadius * broadphaseRadius;
                    var fromStart = playerPrevPos - objectTransform.Position;
                    fromStart.Y = 0f;
                    if (distanceSq > broadphaseRadiusSq && fromStart.LengthSquared() > broadphaseRadiusSq)
                        continue;

                    if (distanceSq >= targetDistanceSq)
                    {
                        // Swept XZ test prevents tunneling when player moves fast enough to cross a tree in one frame.
                        if (dt > 1e-5f &&
                            TryComputeSweptPush(playerPrevPos, playerPos, objectTransform.Position, targetDistance, out var sweptPush))
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
                playerHandled = true;
            });
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

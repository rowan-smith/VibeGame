using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Objects;

namespace Veilborne.Core.Terrain
{
    internal static class WorldObjectCollisionRules
    {
        public static bool IsFoliage(SpawnedObject obj)
        {
            // Keep classification conservative to avoid accidentally marking tree assets as foliage.
            return ContainsAny(obj.ObjectId, "grass_", "bush_", "shrub_", "fern_") ||
                   ContainsAny(obj.ObjectId, "_grass", "_bush", "_shrub", "_fern") ||
                   ContainsAny(obj.ObjectId, "surface_grass", "grass");
        }

        public static float ComputeColliderRadius(SpawnedObject obj)
        {
            if (obj.CollisionRadius <= 0f)
                return 0f;

            float baseRadius = MathF.Max(0.05f, obj.CollisionRadius);
            if (IsFoliage(obj))
                return Math.Clamp(baseRadius * 0.5f, 0.05f, 0.45f);

            // Trees should block a bit wider than trunk, but keep a sane cap to avoid walling the player in.
            bool looksLikeTree = ContainsAny(obj.ObjectId, "tree", "oak", "pine", "maple", "birch");
            if (looksLikeTree)
            {
                float canopyBoost = baseRadius * 1.2f;
                return Math.Clamp(canopyBoost, 0.75f, 1.75f);
            }

            return baseRadius;
        }

        public static CollisionFilterComponent GetFilter(SpawnedObject obj)
        {
            if (IsFoliage(obj))
            {
                return new CollisionFilterComponent
                {
                    Layer = CollisionLayer.Foliage,
                    CollidesWith = CollisionLayer.None
                };
            }

            return new CollisionFilterComponent
            {
                Layer = CollisionLayer.WorldStatic,
                CollidesWith = CollisionLayer.Player
            };
        }

        private static bool ContainsAny(string source, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (!string.IsNullOrWhiteSpace(source) &&
                    source.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}


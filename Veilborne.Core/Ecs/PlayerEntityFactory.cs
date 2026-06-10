using System.Numerics;
using Veilborne.Ecs.Components;

namespace Veilborne.Ecs
{
    /// <summary>
    /// Creates the default player entity with all gameplay components attached.
    /// </summary>
    public static class PlayerEntityFactory
    {
        public static Entity CreateDefault(EntityRegistry registry, Vector3 spawnPosition)
        {
            var player = registry.CreateEntity();
            player.AddComponent(new PlayerComponent());
            player.AddComponent(new TransformComponent
            {
                Position = spawnPosition,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            });
            player.AddComponent(new ColliderComponent { Radius = 0.5f });
            player.AddComponent(new CollisionFilterComponent
            {
                Layer = CollisionLayer.Player,
                CollidesWith = CollisionLayer.WorldStatic
            });
            player.AddComponent(new VelocityComponent { Linear = Vector3.Zero });
            player.AddComponent(new VerticalVelocityComponent { Value = 0f });
            player.AddComponent(new AccelerationComponent { Value = Vector3.Zero });
            player.AddComponent(new ForceComponent { Value = Vector3.Zero });
            player.AddComponent(new DragComponent { Linear = 0f, Angular = 0f });
            player.AddComponent(new MassComponent { Value = 1f, IsKinematic = false });
            player.AddComponent(new RigidbodyComponent { IsKinematic = false, IsSleeping = false });
            player.AddComponent(new GravityComponent { Direction = new Vector3(0f, -20f, 0f) });
            player.AddComponent(new HealthComponent { Current = 100f, Max = 100f });
            player.AddComponent(new TeamComponent { Id = 1 });
            player.AddComponent(new NameComponent { Value = "Player" });
            player.AddComponent(new TagComponent { Name = "Player" });
            player.AddComponent(new ParentComponent { EntityId = -1 });
            player.AddComponent(new ChildrenComponent { EntityIds = [] });
            player.AddComponent(new LifetimeComponent { RemainingSeconds = 0f });
            player.AddComponent(new DirtyComponent { NeedsUpdate = false });
            player.AddComponent(new BillboardComponent { FaceCamera = false });
            player.AddComponent(new ShadowCasterComponent { CastsShadows = true });
            player.AddComponent(new MaterialComponent { ShaderId = string.Empty, Tint = Vector4.One });
            player.AddComponent(new JumpComponent
            {
                JumpSpeed = 8.5f,
                JumpBufferSeconds = 0.12f,
                CoyoteSeconds = 0.10f,
                JumpBufferTimer = 0f,
                CoyoteTimer = 0f,
                IsGrounded = false
            });
            player.AddComponent(new MoveInputComponent { HorizontalDisplacement = Vector3.Zero });
            player.AddComponent(new HotbarSelectionComponent { SelectedSlot = 0 });
            player.AddComponent(new DigInteractionComponent
            {
                IsDigHeld = false,
                HasGroundHit = false,
                GroundHit = Vector3.Zero,
                ProbeMaxDistance = 6f,
                ProbeStep = 0.25f,
                ProbeEpsilon = 0.05f,
                ToolBreakSpeedMultiplier = 1f,
                ToolStaminaCost = 0
            });
            player.AddComponent(new MiningHitComponent
            {
                HasHit = false,
                HitPosition = Vector3.Zero,
                BlockType = Terrain.ResourceBlockType.None
            });
            player.AddComponent(new CameraComponent
            {
                Position = spawnPosition,
                Target = Vector3.Zero,
                Up = Vector3.UnitY,
                FovY = 45.0f
            });

            return player;
        }
    }
}

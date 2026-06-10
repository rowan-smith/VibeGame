using Veilborne.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Ecs
{
    /// <summary>
    /// Creates HUD UI entities linked to a target camera/player entity.
    /// </summary>
    public static class UiEntityFactory
    {
        public sealed record HudUiEntities(Entity Canvas, Entity Crosshair);

        public static HudUiEntities CreateHudUi(EntityRegistry registry, int targetCameraEntityId)
        {
            var canvas = registry.CreateEntity();
            canvas.AddComponent(new CanvasComponent
            {
                TargetCameraEntityId = targetCameraEntityId,
                Visible = true
            });
            canvas.AddComponent(new ChildrenComponent { EntityIds = [] });

            var crosshair = registry.CreateEntity();
            crosshair.AddComponent(new ParentComponent { EntityId = canvas.Id });
            crosshair.AddComponent(new UIElementKindComponent { Kind = "Crosshair" });
            crosshair.AddComponent(new UIElementComponent
            {
                Bounds = new Rect(0, 0, 0, 0),
                Text = "idle"
            });
            crosshair.AddComponent(new RenderComponent
            {
                Visible = true,
                ModelPath = string.Empty
            });

            return new HudUiEntities(canvas, crosshair);
        }
    }
}

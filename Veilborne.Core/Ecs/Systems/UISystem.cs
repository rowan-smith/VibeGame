using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Keeps UI element visibility coherent with canvas visibility.
    /// </summary>
    public class UISystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public UISystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            bool anyCanvasVisible = false;
            int targetCameraId = -1;
            _entities.ForEachWith<CanvasComponent>(canvasEntity =>
            {
                if (anyCanvasVisible)
                    return;
                var canvas = canvasEntity.GetComponent<CanvasComponent>();
                if (!canvas.Visible)
                    return;
                anyCanvasVisible = true;
                targetCameraId = canvas.TargetCameraEntityId;
            });

            bool hasGroundHit = false;
            if (targetCameraId >= 0)
            {
                _entities.ForEachWith<PlayerComponent, DigInteractionComponent>(player =>
                {
                    if (hasGroundHit || player.Id != targetCameraId)
                        return;
                    hasGroundHit = player.GetComponent<DigInteractionComponent>().HasGroundHit;
                });
            }

            _entities.ForEachWith<UIElementComponent, UIElementKindComponent>(uiEntity =>
            {
                var kind = uiEntity.GetComponent<UIElementKindComponent>();
                if (kind.Kind != "Crosshair")
                    return;
                var ui = uiEntity.GetComponent<UIElementComponent>();
                ui.Text = hasGroundHit ? "hit" : "idle";
                uiEntity.SetComponent(ui);
            });

            _entities.ForEachWith<UIElementComponent, RenderComponent>(uiEntity =>
            {
                var render = uiEntity.GetComponent<RenderComponent>();
                if (render.Visible == anyCanvasVisible)
                    return;
                render.Visible = anyCanvasVisible;
                uiEntity.SetComponent(render);
            });
        }
    }
}


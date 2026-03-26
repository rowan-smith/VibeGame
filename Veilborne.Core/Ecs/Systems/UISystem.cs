using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
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
            foreach (var canvasEntity in _entities.GetEntitiesWith<CanvasComponent>())
            {
                var canvas = canvasEntity.GetComponent<CanvasComponent>();
                if (canvas.Visible)
                {
                    anyCanvasVisible = true;
                    targetCameraId = canvas.TargetCameraEntityId;
                    break;
                }
            }

            bool hasGroundHit = false;
            if (targetCameraId >= 0)
            {
                foreach (var player in _entities.GetEntitiesWith<PlayerComponent, DigInteractionComponent>())
                {
                    if (player.Id != targetCameraId)
                        continue;
                    hasGroundHit = player.GetComponent<DigInteractionComponent>().HasGroundHit;
                    break;
                }
            }

            foreach (var uiEntity in _entities.GetEntitiesWith<UIElementComponent, UIElementKindComponent>())
            {
                var ui = uiEntity.GetComponent<UIElementComponent>();
                var kind = uiEntity.GetComponent<UIElementKindComponent>();

                if (kind.Kind == "Crosshair")
                {
                    ui.Text = hasGroundHit ? "hit" : "idle";
                    uiEntity.SetComponent(ui);
                }
            }

            foreach (var uiEntity in _entities.GetEntitiesWith<UIElementComponent, RenderComponent>())
            {
                var render = uiEntity.GetComponent<RenderComponent>();
                if (render.Visible != anyCanvasVisible)
                {
                    render.Visible = anyCanvasVisible;
                    uiEntity.SetComponent(render);
                }
            }
        }
    }
}


namespace Veilborne.Ecs.Components
{
/// <summary>
/// Marks a UI canvas target entity and basic visibility state.
/// </summary>
 public struct CanvasComponent : IComponent
    {
        public CanvasComponent() { }

        public int TargetCameraEntityId { get; set; } = -1;

        public bool Visible { get; set; } = true;
    }
}


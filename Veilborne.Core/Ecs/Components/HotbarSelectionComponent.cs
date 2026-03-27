namespace Veilborne.Core.Ecs.Components
{
    /// <summary>
    /// Tracks currently selected hotbar slot for an entity.
    /// </summary>
    public struct HotbarSelectionComponent : IComponent
    {
        public HotbarSelectionComponent() { }

        public int SelectedSlot { get; set; } = 0;
    }
}

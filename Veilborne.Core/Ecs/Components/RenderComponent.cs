namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores model rendering metadata for world objects.
/// </summary>
 public struct RenderComponent : IComponent
    {
        public RenderComponent() { }

        public string ModelPath { get; set; } = string.Empty;

        public bool Visible { get; set; } = true;

        public bool IsFoliage { get; set; } = false;
    }
}



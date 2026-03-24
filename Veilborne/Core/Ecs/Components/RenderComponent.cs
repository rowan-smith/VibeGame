namespace Veilborne.Core.Ecs.Components
{
    public class RenderComponent : IComponent
    {
        public string ModelPath { get; set; } = string.Empty;

        public bool Visible { get; set; } = true;

        public float? ConfigRotationDegrees { get; set; }
    }
}

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Classifies UI element entities by role.
/// </summary>
 public struct UIElementKindComponent : IComponent
    {
        public UIElementKindComponent() { }

        public string Kind { get; set; } = string.Empty;
    }
}


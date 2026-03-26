using Veilborne.Interfaces;

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores UI element bounds and label text.
/// </summary>
 public struct UIElementComponent : IComponent
    {
        public UIElementComponent() { }

        public Rect Bounds { get; set; } = new Rect(0, 0, 0, 0);

        public string Text { get; set; } = string.Empty;
    }
}


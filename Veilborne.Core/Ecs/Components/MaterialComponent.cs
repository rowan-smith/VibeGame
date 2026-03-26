using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores material shader key and tint override for rendering.
/// </summary>
 public struct MaterialComponent : IComponent
    {
        public MaterialComponent() { }

        public string ShaderId { get; set; } = string.Empty;

        public Vector4 Tint { get; set; } = Vector4.One;
    }
}



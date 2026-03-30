using System.Numerics;

namespace Veilborne.Ecs.Components
{
/// <summary>
/// Defines supported light source categories.
/// </summary>
 public enum LightType
    {
        Directional = 0,
        Point = 1,
        Spot = 2
    }
/// <summary>
/// Stores light source parameters used by lighting systems.
/// </summary>
 public struct LightComponent : IComponent
    {
        public LightComponent() { }

        public LightType Type { get; set; } = LightType.Point;

        public Vector3 Position { get; set; } = Vector3.Zero;

        public Vector3 Color { get; set; } = Vector3.One;

        public float Intensity { get; set; } = 1f;
    }
}



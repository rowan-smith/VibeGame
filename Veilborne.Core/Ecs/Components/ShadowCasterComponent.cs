namespace Veilborne.Ecs.Components
{
/// <summary>
/// Marks whether an entity should cast shadows.
/// </summary>
 public struct ShadowCasterComponent : IComponent
    {
        public ShadowCasterComponent() { }

        public bool CastsShadows { get; set; } = true;
    }
}


